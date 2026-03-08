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

        [Property("Reverse EMA"), DefaultPropertyValue(1f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +
            "Equivalent to what is seen in Reconstructor and Temporal Resampler.\n" +
            "ONLY touch this IF your tablet has hardware smoothing!\n" +
            "Follow the instructions from the wiki."
        )]
        public float reverseSmoothing
        {
           set => _reverseSmoothing = Math.Clamp(value, 0.001f, 1);
           get => _reverseSmoothing;
        }
        public float _reverseSmoothing;

        [Property("Directional Antichatter 'Inner Radius'"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +
            "Works like Devocub antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 2 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly."
        )]
        public float dacInner { 
            set => _dacInner = Math.Clamp(value, 0, _dacOuter);
            get => _dacInner;
        }
        public float _dacInner;

        [Property("Directional Antichatter 'Outer Radius'"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +
            "Works like Devocub antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 5 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly."
        )]
        public float dacOuter { 
            set => _dacOuter = Math.Max(value, 0.0f);
            get => _dacOuter;
        }
        public float _dacOuter;

        [Property("Velocity 'Outer Range'"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +
            "Will act the same as the above, but for magnitude of direction."
        )]
        public float vOuter { 
            set => _vOuter = Math.Max(value, 0.0f);
            get => _vOuter;
        }
        public float _vOuter;

        [Property("Stock EMA Weight"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +
            "EMA weight, but it can change based on the current situation and internal thresholds.\n" +
            "This should hold for any reasonable area.\n" +
            "Setting this too low may cause racket if wire is enabled. If you want this low, you won't miss wire.\n" +
            "The below options control adaptivity."
        )]
        public float stockWeight { 
            set => _stockWeight = Math.Clamp(value, 0.001f, 1.0f);
            get => _stockWeight;
        }
        public float _stockWeight;

        [Property("Accel Response Aggressiveness"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +
            "Useful values range between 0 and 2, but can go higher.\n" +
            "Makes movement 'snappier' on sharp accel.\n" +
            "Do not put above 0 if you hover on a PTK-470, as reporting becomes buggy.\n" +
            "General users - don't put above 0."
        )]
        public float aResponse { 
            set => _aResponse = Math.Max(value, 0.0f);
            get => _aResponse;
        }
        public float _aResponse;

        [Property("Inner Radius"), DefaultPropertyValue(5.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 5.0\n\n" +
            "A full deadzone for movement. Unit is in raw tablet data.\n" +
            "Already directionally separated with position transition based on the below option, so don't make this too high with the below option being too low."
        )]
        public float rInner { 
            set => _rInner = Math.Max(value, 0.0f);
            get => _rInner;
        }
        public float _rInner;

        [Property("Additional Antichatter"), DefaultPropertyValue(100.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 100.0\n\n" +
            "Fairly self-explanatory.\n" +
            "Keep Directional Separation up for basically zero added latency on normal movements."
        )]
        public float moddist { 
            set => _moddist = Math.Max(value, 0.0f);
            get => _moddist;
        }
        public float _moddist;

        [Property("Antichatter Power"), DefaultPropertyValue(5.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 5.0\n\n" +
            "Raises the antichatter's weight to this value."
        )]
        public float modPow { 
            set => _modPow = Math.Max(value, 0.0f);
            get => _modPow;
        }
        public float _modPow;

        [Property("Directional Separation"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 1.0\n\n" +
            "Controls aggressiveness of antichatter's directional separation.\n" +
            "Setting at 0 will create added latency."
        )]
        public float dirSeparation {
            set => _dirSeparation = Math.Clamp(value, 0, 1.0f);
            get => _dirSeparation;
        }
        public float _dirSeparation;

        [Property("Wire"), DefaultPropertyValue(true), ToolTip
        (
            "Equivalent to 'extraFrames' from Temporal Resampler.\n" +
            "You should probably leave this enabled unless your specific situation requires otherwise.\n" +
            "Some people have reported this breaking things in the past, but for them it has been fixed.\n" +
            "Some fixes prevent antichatter from racketing under normal scenarios when this is enabled,\n" +
            "which are not present in any other filter. This changes behavior but keeps it working well."
        )]
        public bool wire { set; get; }

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
            "Multiplies every area-subjective threshold.\n" +
            "Increase if your area is large."
        )]
        public float areaScale { 
            set => _areaScale = Math.Clamp(value, 0.01f, 5f);
            get => _areaScale;
        }
        public float _areaScale;

        [Property("X Modifier"), DefaultPropertyValue(1f), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0\n\n" +
            "Acts as aspect ratio compensation.\n" +
            "If you want to make sure this is display-consistent,\n" +
            "divide your display area setting's aspect ratio (number in the middle)\n" +
            "by your tablet area setting's aspect ratio.\n" +
            "Enter the result here."
        )]
        public float xMod { 
            set => _xMod = Math.Clamp(value, 0.01f, 100f);
            get => _xMod;
        }
        public float _xMod;

        [Property("Wacom PTK-x70 Series Toggle"), DefaultPropertyValue(false), ToolTip
        (
            "Enables behavioral tweaks that improve the experience on a Wacom PTK-x70 tablet, like not bugging out on press/lift.\n" +
            "May be applicable on a PTH-x60 tablet, but this is untested."
        )]
        public bool hcToggle { set; get; }

        protected override void ConsumeState()
        {
            if (State is ITabletReport report)
            {   
                if (!init) {
                    ResetValues(report.Position);
                    Initialize();
                    init = true;
                    emergency = 5;
                    eflag = false;
                    aemaOutput = smpos[0];
                    startOutput = smpos[0];
                    aemaHold = smpos[0];
                    ringOutput = smpos[0];
                    iRingPos0 = smpos[0];
                    lastAemaHold = smpos[0];
                    lastOutputPos = smpos[0];
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
                    ResetValues(report.Position);
                }
                StatUpdate(report);
                moveOk = false;
                if (wire) {
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
                    report.Position = pos[0];
                    startOutput = pos[0];
                    report.Pressure = pressure[0];
                    InsertAtFirst(smpos, pos[0]);
                    InsertAtFirst(stpos, pos[0]);
                    InsertAtFirst(prpos, pos[0]);
                    
                    if (eflag) {
                        RF();
                        AEMA();
                        float eTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                        float scale = Math.Min((((float)(5 - emergency) + Math.Min(eTime, 1.0f)) * 0.2f), 1.0f);
                        report.Position = Vector2.Lerp(lastOutputPos, aemaOutput, scale);
                    }
                    else { 
                        aemaOutput = pos[0];
                        startOutput = pos[0];
                        aemaHold = pos[0];
                        ringOutput = pos[0];
                        iRingPos0 = pos[0];
                        lastAemaHold = pos[0];
                        lastOutputPos = pos[0];
                    }
                    OnEmit();
                    return;
                }

                float t = 1 + (float)(runningStopwatch.Elapsed - latestReport).TotalSeconds * rpsAvg;
                t = Math.Clamp(t, 0, 3);
    
                startOutput = RTrajectory(t, prpos[2], prpos[1], prpos[0]);

                if (rInner > 0f) RF();
                else {
                    moveOk = true;
                    ringOutput = startOutput;
                }
              
                AEMA();

                report.Position = aemaOutput;
                dirOfOutput = (report.Position - lastOutputPos) / updateTime;
                lastOutputPos = report.Position;
                report.Pressure = pressure[0];   

                if (!vec2IsFinite(report.Position + startOutput + ringOutput + aemaOutput)) {
                    report.Position = pos[0];
                    aemaOutput = pos[0];
                    startOutput = pos[0];
                    ringOutput = pos[0];
                    iRingPos0 = pos[0];
                    aemaHold = pos[0];
                    lastAemaHold = pos[0];
                    eflag = false;
                    emergency = 5;
                    ResetValues(pos[0]);
                    OnEmit();
                    return;
                }       

                OnEmit();
            }
        }

        void StatUpdate(ITabletReport report) {
            InsertAtFirst(pos, report.Position);
            
            Vector2 smoothed = pos[0];
            if (reverseSmoothing < 1f && reverseSmoothing > 0f)
                smoothed = pos[1] + (pos[0] - pos[1]) / reverseSmoothing;
            InsertAtFirst(smpos, smoothed);

            InsertAtFirst(pressure, report.Pressure);
            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(adjDir, new Vector2(dir[0].X * xMod, dir[0].Y));
            InsertAtFirst(vel, adjDir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(jerk, accel[0] - accel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());

            DAC();
            
            Vector2 predict = stpos[0];
            if (frameShift > 0f) {

                if (kf != null) predict = kf.Update(stpos[0], secAvg);
                else predict = pos[0];

                predict += (stpos[0] - predict) * (1f - frameShift);
            }

            tOffset += secAvg - consumeDelta;
            tOffset *= MathF.Exp(-5f * consumeDelta);
            tOffset = Math.Clamp(tOffset, -secAvg, secAvg);
            latestReport = runningStopwatch.Elapsed + TimeSpan.FromSeconds(tOffset);

            InsertAtFirst(prpos, predict);

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
            if (dacInner + dacOuter + vOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + dacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], dir[0], scale);
                if (vel[0] >= 1 && vel[1] >= 1 && vel[0] < 150 * areaScale && stabilized.Length() > 1) {
                    float ascale = Math.Max(Math.Abs(accel[0]), Math.Abs(vel[0] - stdir[0].Length()));
                    stabilized = Vector2.Lerp(stabilized, stdir[0].Length() * Vector2.Normalize(stabilized), vscale * (1 - scale) * (Smoothstep(ascale, 0, vOuter)));
                }
            InsertAtFirst(stdir, stabilized);
            Vector2 stpoint = stpos[0] + stdir[0];
            InsertAtFirst(stpos, stpoint);
        
            if (moveOk)
                stpos[0] = Vector2.Lerp(stpos[0], pos[0], MathF.Sqrt(adjdWeight));
            }
            else {
                InsertAtFirst(stdir, dir[0]);
                InsertAtFirst(stpos, pos[0]);
            }
        }

        void RF() {
            ringInputPos1 = ringInputPos0;
            ringInputPos0 = startOutput;
            ringInputDir = ringInputPos0 - ringInputPos1;
            Vector2 dist = startOutput - iRingPos0;
            iRingPos1 = iRingPos0;
            iRingPos0 += Math.Max(0, dist.Length() - (rInner)) * Default(Vector2.Normalize(dist), Vector2.Zero);
            ringDir = iRingPos0 - iRingPos1;
            ringOutput += ringDir;
            if (ringDir.Length() > 0 || dist.Length() > rInner || accel[0] < -10 * areaScale || vel[0] > 10 * rInner) {
                ringOutput = Vector2.Lerp(ringOutput, startOutput, Smoothstep(new Vector2(ringDir.X * xMod, ringDir.Y).Length(), -0.01f, 0.5f * moddist));
                ringOutput = Vector2.Lerp(ringOutput, startOutput, Smoothstep(accel[0], -10 * areaScale, -200 * areaScale));
                moveOk = true;
            }
            else moveOk = false;
        }

        void AEMA() {
            float weight = 1;
            float mod4 = 0;
            if (stockWeight < 1f || moddist > 0f || aResponse > 0f) {
                weight = stockWeight;
                Vector2 distVector = aemaHold - ringOutput;
                distVector.X *= xMod;
                float dist = distVector.Length();
                if (wire) dist *= MathF.Pow(MathF.Pow(Math.Min(updateTime / expect, 1.5f), 1 / MathF.Pow(stockWeight, 1 + Smoothstep(vel[0], 0, moddist))), 1 / (modPow)); // I HAVE NO IDEA HOW OR WHY THIS WORKS BUT THIS REDUCES MOST RACKET UNDER NORMAL SCENARIOS!!!!!!
                mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * stockWeight * MathF.Pow(Smoothstep(dist, 5000 * aResponse * areaScale, (500 * aResponse * areaScale) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 30 * areaScale), modPow) * DotNorm(ddir[0], dir[0], 0);
                float mod5 = Smoothstep(dist + vel[0], -0.01f, moddist);
                weight *= MathF.Pow(mod5, modPow);
            }
           
            aemaHold = Vector2.Lerp(aemaHold, ringOutput, weight);
            aemaDir = aemaHold - lastAemaHold;
            lastAemaHold = aemaHold;
            acOutput += aemaDir;

            if (dirSeparation > 0) {
                acOutput = Vector2.Lerp(acOutput, ringOutput, dirSeparation * Math.Min(1, WireMultAdjust(weight, expect, updateTime, wire)) * stockWeight);
            }
            
            weight = Math.Clamp(1 - mod4, 0, 1);
            aemaOutput = Vector2.Lerp(aemaOutput, acOutput, Math.Min(1, WireMultAdjust(weight, expect, updateTime, wire)));
        }

        void Initialize() {
            if (msOverride > 0) {
                reportMsAvg = msOverride;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                reportMsAvg = msAvg = msOverride;
                secAvg = reportMsAvg / 1000f;
                rpsAvg = 1f / secAvg;
                if (dacInner + dacOuter == 0f) {
                    adjdWeight = 0;
                }
            }
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
        }

        void ResetValues(Vector2 p) {
            kf = new KalmanVector2(4, p);
            stpos = Enumerable.Repeat(p, stpos.Length).ToArray();
            smpos = Enumerable.Repeat(p, smpos.Length).ToArray();
            prpos = Enumerable.Repeat(p, prpos.Length).ToArray();
            latestReport = runningStopwatch.Elapsed;
            tOffset = 0;
        }

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] stdir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        Vector2[] smpos = new Vector2[HMAX];
        Vector2[] adjDir = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];

        Vector2[] stpos = new Vector2[HMAX];
        Vector2[] prpos = new Vector2[HMAX];
        
        Vector2 startOutput;
        Vector2 ringInputPos0, ringInputPos1, ringInputDir, iRingPos0, iRingPos1, ringDir, ringOutput;
        Vector2 aemaHold, lastAemaHold, aemaDir, acOutput, aemaOutput;
        Vector2 lastOutputPos, dirOfOutput;
        float reportTime;
        float adjdWeight, adjDacOuter;
        float correctWeight;
        bool moveOk;
        bool init = false;
        int emergency;

        float reportMsAvg;
        float updateTime;
        bool eflag = false;

        const float startCorrectWeight = 0.01f;    
        const float msStandard = 3.302466f;
        float expect => 1000 / Frequency;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();

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

