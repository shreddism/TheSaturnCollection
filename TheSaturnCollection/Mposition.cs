using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Multifilter (Position Interpolation)")]
    public class MultifilterTR : AsyncPositionedPipelineElement<IDeviceReport>
    {
        public MultifilterTR() : base()
        {
        }

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        const int HMAX = 4;

        [Property("Prediction Ratio (Hover Over The Textbox)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Important: Only enable ONE multifilter, probably this one! That's why they're called multifilters!\n" +
            "Issues that arise from failing to read this are the user's fault.\n\n" +

            "Possible range: 0.0 - 1.0, default 0.0\n\n" +

            "Same interpolation and prediction methods as Temporal Resampler.\n" +
            "Higher RPS raw tablets with less noise get extremely low error on 1.0."
        )]
        public float frameShift { 
            set => _frameShift = Math.Clamp(value, 0.0f, 1.0f);
            get => _frameShift;
        }
        public float _frameShift;
        Vector2[] prpos = new Vector2[HMAX];
        Vector2[] prdir = new Vector2[HMAX];

        [Property("Reverse EMA"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "Equivalent to what is seen in Reconstructor and Temporal Resampler.\n" +
            "ONLY touch this IF your tablet has hardware smoothing!\n" +
            "Follow the instructions from the wiki."
        )]
        public float reverseSmoothing
        {
            set => _reverseSmoothing = Math.Clamp(value, 0.001f, 1.0f);
            get => _reverseSmoothing;
        }
        public float _reverseSmoothing;
        Vector2[] smpos = new Vector2[HMAX];

        [Property("Directional Antichatter Inner Threshold"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Works somewhat like Devocub Antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 2 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly.\n" +
            "If you are unsure, keep at 0."
        )]
        public float dacInner { 
            set => _dacInner = Math.Max(value, 0.0f);
            get => _dacInner;
        }
        public float _dacInner;

        [Property("Directional Antichatter Outer Threshold"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Works somewhat like Devocub Antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 5 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly.\n" +
            "If you are unsure, keep at 0."
        )]
        public float dacOuter { 
            set => _dacOuter = Math.Max(value, 0.0f);
            get => _dacOuter;
        }
        public float _dacOuter;
        Vector2[] stdir = new Vector2[HMAX];
        Vector2[] stpos = new Vector2[HMAX];
        float adjDacOuter;

        [Property("Wire - Filter Mode"), PropertyValidated(nameof(wireModes)), DefaultPropertyValue("Wire - Point"), ToolTip
        (
            "Controls ConsumeState calling UpdateState and when below filtering applies.\n" +
            "Check the wiki for more info."
        )]
        public string wireMode { 
            set => _wireMode = value; 
            get => (_wireMode != null) ? _wireMode : "Wire - Point"; 
        }
        public static string[] wireModes => new[] {
            "Non-Wire - Point",
            "Non-Wire - Interp",
            "Wire - Point",
            "Wire - Interp"
        }; 
        public string? _wireMode;
        public int wireCode;
        Vector2[] fipos = new Vector2[HMAX];
        float updateTime;
        bool wireFlag, pointFlag, wireAdjustFlag;
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();

        [Property("Inner Radius"), DefaultPropertyValue(25.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 25.0\n\n" +

            "A full deadzone for movement. Unit is in raw tablet data.\n" +
            "Directionally separated with smooth position transition to raw based on itself."
        )]
        public float rInner { 
            set => _rInner = Math.Max(value, 0.0f);
            get => _rInner;
        }
        public float _rInner;
        Vector2 clampHold, clampOutput;

        [Property("Stock EMA Weight"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "EMA weight, but it can change based on the current situation.\n" +
            "The below options control adaptivity."
        )]
        public float stockWeight { 
            set => _stockWeight = Math.Clamp(value, 0.001f, 1.0f);
            get => _stockWeight;
        }
        public float _stockWeight;

        [Property("Smoothed Antichatter"), DefaultPropertyValue(50.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0\n\n" +
            
            "Sets base behavior for distance smoothing. Unit is raw tablet data.\n" +
            "Goes to raw position based on the setting below."
        )]
        public float smoothDist { 
            set => _smoothDist = Math.Max(value, 0.0f);
            get => _smoothDist;
        }
        public float _smoothDist;
        float halfSmoothDist;
        Vector2 smoothHold;

        [Property("Separated Threshold Mult"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.5 - any, default 1.0\n\n" +

            "Lower values are more eager to send smoothed position to raw."
        )]
        public float sepMult {
            set => _sepMult = Math.Clamp(value, 0.5f, 100000.0f);
            get => _sepMult;
        }
        public float _sepMult;
        Vector2 smoothOutput;

        [Property("Accel Response Aggressiveness"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Some like using Devocub or Radial Follow for their more exaggerated snap effect that they bring.\n" +
            "This adaptively brings that to sharp acceleration, so your cursor won't lock up on a small movement.\n" +
            "Sensitivity is based on Area Scale and X modifier.\n" +
            "Putting this too high, like above ~2, will make the cursor far less readable, so it isn't recommended.\n" +
            "If hovering on a PTK-x70, this may be unreliable, as reporting becomes buggy on the hardware level.\n" +
            "This could also apply to a PTH-x60 tablet, but this is untested.\n" +
            "General users - don't put above 0."
        )]
        public float aResponse { 
            set => _aResponse = Math.Max(value, 0.0f);
            get => _aResponse;
        }
        public float _aResponse;
        Vector2 adaptOutput;

        [Property("Expected Milliseconds Per Report Override"), DefaultPropertyValue(0.0f), ToolTip
        (
            "You should know what you are doing if you change this from 0.\n" +
            "Wacom PTK-x70 - make this 3.302466 if using given pen, otherwise you are on your own."
        )]
        public float msOverride { 
            set => _msOverride = Math.Max(value, 0.0f);
            get => _msOverride;
        }
        public float _msOverride;

        [Property("Area Scale"), DefaultPropertyValue(0.5f), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5\n\n" +

            "Multiplies every area-subjective threshold, mostly failsafes.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public float areaScale { 
            set => _areaScale = Math.Clamp(value, 0.01f, 5f);
            get => _areaScale;
        }
        public float _areaScale;

        [Property("X Modifier"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0\n\n" +

            "Acts as aspect ratio compensation.\n" +
            "Divide display area ratio by tablet area ratio, then use that value.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public float xMod { 
            set => _xMod = Math.Clamp(value, 0.01f, 100f);
            get => _xMod;
        }
        public float _xMod;

        [BooleanProperty("Wacom PTK-x70 Series Toggle", ""), DefaultPropertyValue(false), ToolTip
        (
            "Enables behavioral tweaks that improve the experience on a Wacom PTK-x70 tablet, like not bugging out on press/lift.\n" +
            "May be applicable on a PTH-x60 tablet, but this is untested."
        )]
        public bool hcToggle { set; get; }
        Vector2 emPos;
        bool eflag;

        protected override void ConsumeState()
        {
            if (State is ITabletReport report)
            {   
                if (!init) {
                    ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));
                    Initialize();
                    init = true;
                    emergency = 5;
                    eflag = false;
                }
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
                consumeDelta = reportTime / 1000f;
                if (reportTime < 25f && reportTime > 0.01f) {
                    if (msOverride == 0) {
                        reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                        rpsAvg += (1f / (consumeDelta) - rpsAvg) * (1f - MathF.Exp(-2f * (consumeDelta)));
                        secAvg = 1f / rpsAvg;
                        msAvg = 1000f * secAvg;
                        correctWeight = startCorrectWeight * expect * (msStandard / reportMsAvg);
                    }
                    if (emergency > 0)
                    emergency--;    
                }
                else {
                    emergency = 5;
                    eflag = false;
                    ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));
                }
                StatUpdate(report);
                if (wireFlag) {
                    UpdateState();
                }
            }
            else {
                OnEmit();
            } 
        }

        protected override void UpdateState()
        {
            if (State is ITabletReport report && PenIsInRange())
            {    
                updateTime = (float)updateStopwatch.Restart().TotalMilliseconds;

                if (emergency > 0) {
                    report.Pressure = pressure[0];
                    
                    if (eflag) {
                        if (!pointFlag) {
                            startOutput = pos[0];
                            if (rInner > 0f) RF();
                            else {
                                clampOutput = startOutput;
                            }
                            AEMA();
                        }
                        float eTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                        float scale = Math.Min((((float)(5 - emergency) + Math.Min(eTime, 1.0f)) * 0.2f), 1.0f);
                        outputInternal = Vector2.Lerp(emPos, adaptOutput, scale); 
                        report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
                    }
                    else { 
                        ERefresh();
                        emPos = pos[0];
                        report.Position = new Vector2(adaptOutput.X / xMod, adaptOutput.Y);
                        lastOutputPos = report.Position;
                    }
                    OnEmit();
                    return;
                }

                float t = 1 + (float)(runningStopwatch.Elapsed - latestReport).TotalSeconds * rpsAvg;
                t = Math.Clamp(t, 0, 3);

                if (pointFlag) {
                    outputInternal = RTrajectory(t, fipos[2], fipos[1], fipos[0]);
                }
                else {
                    startOutput = RTrajectory(t, stpos[2], stpos[1], stpos[0]);
                    if (rInner > 0f) RF();
                    else {
                        clampOutput = startOutput;
                    }
                    AEMA();
                    outputInternal = adaptOutput;
                }

                emPos = outputInternal;

                report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
                dirOfOutput = (report.Position - lastOutputPos) / updateTime;
                lastOutputPos = report.Position;

                report.Pressure = pressure[0];   

                if (!vec2IsFinite(report.Position + startOutput + clampOutput + smoothOutput + adaptOutput + outputInternal)) {
                    ERefresh();
                    emPos = pos[0];
                    eflag = false;
                    emergency = 5;
                    ResetValues(pos[0]);
                    report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
                }       

                OnEmit();
            }
        }

        void StatUpdate(ITabletReport report) {
            InsertAtFirst(pos, report.Position);
            pos[0].X *= xMod;
            
            Vector2 smoothed = pos[0];
            if (reverseSmoothing < 1f && reverseSmoothing > 0f)
                smoothed = pos[1] + (pos[0] - pos[1]) / reverseSmoothing;
            InsertAtFirst(smpos, smoothed);

            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(vel, dir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(jerk, accel[0] - accel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());

            InsertAtFirst(pressure, report.Pressure);
            
            Vector2 predict = smpos[0];
            if (frameShift > 0f) {

                if (kf != null) predict = kf.Update(smpos[0], secAvg);
                else predict = pos[0];

                predict += (smpos[0] - predict) * (1f - frameShift);
            }

            tOffset += secAvg - consumeDelta;
            tOffset *= MathF.Exp(-5f * consumeDelta);
            tOffset = Math.Clamp(tOffset, -secAvg, secAvg);
            latestReport = runningStopwatch.Elapsed + TimeSpan.FromSeconds(tOffset);

            InsertAtFirst(prpos, predict);
            InsertAtFirst(prdir, prpos[0] - prpos[1]);

            DAC();

            if (pointFlag) {
                startOutput = stpos[0];
                if (rInner > 0f) RF();
                else {
                    clampOutput = startOutput;
                }
                AEMA();
                InsertAtFirst(fipos, adaptOutput);
            }

            if (dir[0] == pos[0]) {
                emergency = 5;
                dir[0] = Vector2.Zero;
                eflag = false;
            }

            else if ((hcToggle) && ((pressure[0] > 0 && pressure[1] == 0) || (pressure[0] == 0 && pressure[1] > 0))) {
                if (emergency == 0) eflag = true;
                emergency = 5;
            }
        }

        void DAC() {
            if (dacInner + dacOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + adjDacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], prdir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], prdir[0], scale);  
                InsertAtFirst(stdir, stabilized);
                Vector2 stpoint = stpos[0] + stdir[0];
                InsertAtFirst(stpos, stpoint);
                stpos[0] = Vector2.Lerp(stpos[0], prpos[0], adjdWeight);
            }
            else {
                InsertAtFirst(stdir, dir[0]);
                InsertAtFirst(stpos, pos[0]);
                stpos[0] = Vector2.Lerp(stpos[0], prpos[0], adjdWeight);
            }
        }

        void RF() {
            Vector2 dist = startOutput - clampHold;
            float distLength = dist.Length();
            Vector2 ringDir = Math.Max(0, distLength - (rInner)) * Default(Vector2.Normalize(dist), Vector2.Zero);
            float ringDirLength = ringDir.Length();
            clampHold += ringDir;
            clampOutput += ringDir;
            if (ringDirLength > 0 || distLength > rInner || accel[0] < -10 * areaScale || vel[0] > 10 * rInner) {
                float xwa = XWA(expect, updateTime, wireAdjustFlag, reportMsAvg, expect, pointFlag);
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(ringDirLength, -0.01f, rInner), xwa));
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(accel[0], -10 * areaScale, -150 * areaScale), xwa));
            }
        }

        void AEMA() {
            Vector2 dist = clampOutput - smoothHold;
            float distLength = dist.Length();
            float mLength = DSFunction(distLength);
            float wcon = WireWeightAdjust(stockWeight * Default(mLength / distLength, 0), expect, updateTime, wireAdjustFlag);
            smoothHold += wcon * dist;
            smoothOutput = smoothHold;
            if (sepMult > 0 && mLength > 0) {
                if (!(wireFlag) || updateTime / expect > 0.99f) {
                    sepScale = Smoothstep(distLength, -0.01f, smoothDist * sepMult);
                }       
                smoothOutput = Vector2.Lerp(smoothHold, Vector2.Lerp(smoothHold, clampOutput, stockWeight), sepScale);
            }

            float mod4 = 0;
            if (aResponse > 0f) {
                float aDist = Vector2.Distance(smoothOutput, adaptOutput);
                mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * MathF.Pow(Smoothstep(aDist, 2500 * aResponse, (500 * aResponse) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 25 * areaScale), 3.0f) * DotNorm(ddir[0], dir[0], 0);
            }
            float weight = Math.Clamp(1 - mod4, 0, 1);
            adaptOutput = Vector2.Lerp(adaptOutput, smoothOutput, WireWeightAdjust(weight, expect, updateTime, wireAdjustFlag));
        }

        float DSFunction(float dist) {
            if (dist >= smoothDist) return dist - halfSmoothDist;
            float x = (dist / smoothDist);
            return x * x * halfSmoothDist;
        }

        void Initialize() {
            halfSmoothDist = smoothDist * 0.5f;

            if (msOverride > 0) {
                reportMsAvg = msOverride;
                msAvg = msOverride;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                secAvg = reportMsAvg / 1000f;
                rpsAvg = 1f / secAvg;
                if (dacInner + dacOuter == 0f) {
                    adjdWeight = correctWeight * 0.01f;
                }
            }

            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);

            wireCode = wireMode switch {
                "Non-Wire - Point" => 1,
                "Non-Wire - Interp" => 2,
                "Wire - Point" => 3,
                "Wire - Interp" => 4,
                _ => 1
            };

            pointFlag = ((wireCode & 1) == 1);
            wireFlag = (wireCode > 2);
            wireAdjustFlag = (wireCode == 4);
        }

        void ResetValues(Vector2 p) {
            kf = new KalmanVector2(4, p);
            pos = Enumerable.Repeat(p, pos.Length).ToArray();
            stpos = Enumerable.Repeat(p, stpos.Length).ToArray();
            smpos = Enumerable.Repeat(p, smpos.Length).ToArray();
            prpos = Enumerable.Repeat(p, prpos.Length).ToArray();
            fipos = Enumerable.Repeat(p, fipos.Length).ToArray();
            latestReport = runningStopwatch.Elapsed;
            tOffset = 0;
        }

        void ERefresh() {
            startOutput = pos[0];
            clampHold = pos[0];
            clampOutput = pos[0];
            smoothHold = pos[0];
            smoothOutput = pos[0];
            adaptOutput = pos[0];
            outputInternal = pos[0];
        }

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];
        
        Vector2 startOutput, outputInternal;
        Vector2 lastOutputPos, dirOfOutput;
        float reportTime;
        float adjdWeight;
        float correctWeight;
        bool init = false;
        int emergency;

        float reportMsAvg;
        float sepScale;

        const float startCorrectWeight = 0.01f;    
        const float msStandard = 3.302466f;
        float expect => 1000 / Frequency;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();

        KalmanVector2? kf;
        TimeSpan latestReport = TimeSpan.Zero;
        float rpsAvg = 200f, tOffset;
        float msAvg = 5;
        float secAvg = 0.005f;
        float consumeDelta;
        HPETDeltaStopwatch runningStopwatch = new HPETDeltaStopwatch(true);

        private static readonly int steps = 256;
        private static readonly float dt = 1f / steps;
        private float[] arcArr = new float[steps];
        private float arcTar = 0;
        private Vector2 _v1, _v2, _v3;
        private int _floor;
        Vector2 RTrajectory(float t, Vector2 v3, Vector2 v2, Vector2 v1)
        {
            var mid = 0.5f * (v1 + v3);
            var accel = 2f * (mid - v2);
            var vel = 2f * v2 - v3 - mid;
            
            // if there is acceleration, then start spacing points evenly using integrals
            if (Vector2.Dot(accel, accel) > 0.001f)
            {
                int floor = (int)Math.Floor(t);
                var _vel = vel + accel * floor;

                // if any of the inputs have changed, recalculate arcArr
                if ((_floor != floor) || (_v1 != v1) || (_v2 != v2) || (_v3 != v3))
                {
                    _v1 = v1;
                    _v2 = v2;
                    _v3 = v3;
                    _floor = floor;
                    arcTar = 0;

                    for (int _t = 0; _t < steps; _t++)
                    {
                        arcArr[_t] = arcTar;
                        arcTar += (_vel + _t * dt * accel).Length();
                    }
                }

                float _arcTar = arcTar * (t - floor);

                for (int _t = 0; _t < steps; _t++)
                {
                    if (arcArr[_t] < _arcTar) continue;
                    t = _t * dt + floor;
                    break;
                }
            }
            return v3 + t * vel + 0.5f * t * t * accel;
        }
    }

    public class KalmanFilter
    {
        private readonly double[,] scale_const;
        private readonly int states;
        private double lastMeasuredPos;

        private Matrix x;
        private Matrix P;
        private Matrix Q;
        private Matrix R;
        private Matrix H;

        public KalmanFilter(uint statesNumber, double initialPosition)
        {
            states = (int)statesNumber + 2;

            scale_const = new double[states, states];
            for (int i = 0; i < states; i++)
            {
                int fac_n = 1;
                int fac_i = 0;
                for (int j = i; j < states; j++)
                {
                    scale_const[i, j] = 1d / fac_n;
                    fac_i++;
                    fac_n *= fac_i;
                }
            }

            lastMeasuredPos = initialPosition;
            double[,] xArr = new double[states, 1];
            xArr[0, 0] = initialPosition;

            x = Matrix.Build.DenseOfArray(xArr);
            P = Matrix.Build.DenseIdentity(states);
            Q = Matrix.Build.DenseIdentity(states) * 1;
            R = Matrix.Build.DenseDiagonal(2, 2, 0.0001);
            H = Matrix.Build.DenseDiagonal(2, states, 1);
        }

        public double Update(double measuredPos, double dt)
        {
            double measuredVel = (measuredPos - lastMeasuredPos) / dt;
            lastMeasuredPos = measuredPos;

            var z = Matrix.Build.DenseOfArray(new double[,] { { measuredPos }, { measuredVel } });

            double[,] Aarr = new double[states, states];
            for (int i = 0; i < states; i++) 
            {
                double time_pow = 1;
                for (int j = i; j < states; j++) 
                {
                    Aarr[i, j] = time_pow * scale_const[i, j];
                    time_pow *= dt;
                } 
            }

            /*
                vvvvvvvvvvv
            4 states should look like this
            double[,] Aarr = new double[,] {
                {          1,          dt^1/1!,    dt^2/2!,    dt^3/3!     },
                {          0,          1,          dt^1/1!,    dt^2/2!     },
                {          0,          0,          1,          dt^1/1!     },
                {          0,          0,          0,          1           }
            }
            */

            var A = Matrix.Build.DenseOfArray(Aarr);

            x = A * x;
            P = A * P * A.Transpose() + Q;

            var S = H * P * H.Transpose() + R;
            var K = P * H.Transpose() * S.Inverse();

            x = x + K * (z - H * x);
            P = (Matrix.Build.DenseIdentity(states) - K * H) * P;

            return (A * x)[0, 0];
        }
    }

    public class KalmanVector2
    {
        private KalmanFilter xFilter;
        private KalmanFilter yFilter;

        public KalmanVector2(uint states, Vector2 initialPosition)
        {
            xFilter = new KalmanFilter(states, initialPosition.X);
            yFilter = new KalmanFilter(states, initialPosition.Y);
        }

        public Vector2 Update(Vector2 measuredPosition, float dt)
        {
            float xState = (float)xFilter.Update(measuredPosition.X, dt);
            float yState = (float)yFilter.Update(measuredPosition.Y, dt);
            return new Vector2(xState, yState);
        }
    }

    public class Matrix
    {
        internal readonly double[,] data;

        public Matrix(double[,] data)
        {
            this.data = data;
        }

        public int Rows => data.GetLength(0);
        public int Cols => data.GetLength(1);

        public double this[int i, int j]
        {
            get => data[i, j];
            set => data[i, j] = value;
        }

        public static Matrix operator +(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] + b[i, j];
            return new Matrix(result);
        }

        public static Matrix operator -(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] - b[i, j];
            return new Matrix(result);
        }

        public static Matrix operator *(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, b.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < b.Cols; j++)
                    for (int k = 0; k < a.Cols; k++)
                        result[i, j] += a[i, k] * b[k, j];
            return new Matrix(result);
        }

        public static Matrix operator *(Matrix a, double scalar)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] * scalar;
            return new Matrix(result);
        }

        public Matrix Transpose()
        {
            var result = new double[Cols, Rows];
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    result[j, i] = data[i, j];
            return new Matrix(result);
        }

        public Matrix Inverse()
        {
            if (Rows != Cols) throw new InvalidOperationException("Matrix must be square to invert.");

            int n = Rows;
            var result = new double[n, n];
            var identity = Build.DenseIdentity(n).data;
            var copy = (double[,])data.Clone();

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    result[i, j] = identity[i, j];

            for (int i = 0; i < n; i++)
            {
                double diag = copy[i, i];
                if (diag == 0) throw new InvalidOperationException("Matrix is singular.");

                for (int j = 0; j < n; j++)
                {
                    copy[i, j] /= diag;
                    result[i, j] /= diag;
                }

                for (int k = 0; k < n; k++)
                {
                    if (k == i) continue;
                    double factor = copy[k, i];
                    for (int j = 0; j < n; j++)
                    {
                        copy[k, j] -= factor * copy[i, j];
                        result[k, j] -= factor * result[i, j];
                    }
                }
            }

            return new Matrix(result);
        }

        public static class Build
        {
            public static Matrix DenseOfArray(double[,] data) => new Matrix(data);

            public static Matrix DenseIdentity(int size)
            {
                var result = new double[size, size];
                for (int i = 0; i < size; i++) result[i, i] = 1;
                return new Matrix(result);
            }

            public static Matrix DenseDiagonal(int rows, int cols, Func<int, double> diagFunc)
            {
                var result = new double[rows, cols];
                for (int i = 0; i < Math.Min(rows, cols); i++)
                    result[i, i] = diagFunc(i);
                return new Matrix(result);
            }

            public static Matrix DenseDiagonal(int rows, int cols, double value)
            {
                return DenseDiagonal(rows, cols, _ => value);
            }
        }
    }
}

