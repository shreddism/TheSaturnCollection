using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;   
using static Saturn.Utils;    

namespace Saturn
{
    [PluginName("Saturn - Multifilter (Non-Interpolated)")]
    public class MultifilterU : IPositionedPipelineElement<IDeviceReport>
    {
        public MultifilterU() : base()
        {
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        [Property("Reverse EMA (Hover Over The Textbox)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Important: This multifilter is suitable for users who have tablet report rates extremely close to a multiple of their display refresh rate\n" +
            "or users with alien technology. Otherwise, the position interpolated version should work better.\n\n" +
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
            "Only takes effect if Adaptive EMA is enabled\n" +
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
            "Possible range: 0.01 - 100, default 1.0\n\n" +
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

        public event Action<IDeviceReport>? Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {    
                if (!init) {
                    Initialize();
                    init = true;
                    emergency = 5;
                }
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
                if (reportTime < 25) {
                    if (emergency > 0)
                    emergency--;
                }
                else {
                    emergency = 5;
                }
                moveOk = false;
                      
                StatUpdate(report);

                startOutput += stdir[0];

                if (rInner > 0f) RF();
                else {
                    moveOk = true;
                    ringOutput = startOutput;
                }

                if (moveOk && emergency == 0) {
                float cWeight = adjdWeight;
                float dWeight = cWeight * Smoothstep(accel[0], -10 * areaScale, -200 * areaScale);
                startOutput = Vector2.Lerp(startOutput, smpos[0], cWeight);
                startOutput = Vector2.Lerp(startOutput, smpos[0], dWeight);
                }

                AEMA();

                report.Position = aemaOutput;

                if (!vec2IsFinite(report.Position + startOutput + ringOutput + aemaOutput)) {
                    emergency = 5;
                }

                if (emergency > 0) {
                    report.Position = pos[0];
                    startOutput = pos[0];
                    aemaOutput = pos[0];
                    ringOutput = pos[0];
                    iRingPos0 = pos[0];
                    aemaHold = pos[0];
                    lastAemaHold = pos[0];
                }
            }
            Emit?.Invoke(value);
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
            InsertAtFirst(pointaccel, ddir[0].Length());

            DAC();

            if (dir[0] == pos[0]) {
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
                    stabilized = Vector2.Lerp(stabilized, stdir[0].Length() * Vector2.Normalize(stabilized), vscale * (1 - scale) * (Smoothstep(ascale, -0.01f, vOuter)));
                }
            InsertAtFirst(stdir, stabilized);
            }
            else {
                InsertAtFirst(stdir, dir[0]);
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

            if (stockWeight < 1f || moddist > 0f || aResponse > 0f) {
                weight = stockWeight;
                Vector2 distVector = aemaHold - ringOutput;
                distVector.X *= xMod;
                float dist = distVector.Length();
                float mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * stockWeight * MathF.Pow(Smoothstep(dist, 5000 * aResponse * areaScale, (500 * aResponse * areaScale) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 30 * areaScale), modPow) * DotNorm(ddir[0], dir[0], 0);
                float mod5 = Smoothstep(dist + vel[0], -0.01f, moddist);
                weight -= (mod4);
                weight = Math.Clamp(weight, 0, 1);
                weight *= MathF.Pow(mod5, modPow);
            }
           
            aemaHold = Vector2.Lerp(aemaHold, ringOutput, weight);
            aemaDir = aemaHold - lastAemaHold;
            lastAemaHold = aemaHold;
            aemaOutput += aemaDir;

            if (dirSeparation > 0) {
                aemaOutput = Vector2.Lerp(aemaOutput, ringOutput, dirSeparation * weight * stockWeight);
            }
        }

        void Initialize() {
            if (dacInner + dacOuter == 0f) {
                adjdWeight = 0;
            }
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
            correctWeight = startCorrectWeight;
        }

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] stdir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        Vector2[] a1stdir = new Vector2[HMAX];
        Vector2[] smpos = new Vector2[HMAX];
        Vector2[] adjDir = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];
        
        Vector2 startOutput;
        Vector2 ringInputPos0, ringInputPos1, ringInputDir, iRingPos0, iRingPos1, ringDir, ringOutput;
        Vector2 aemaHold, lastAemaHold, aemaDir, aemaOutput;
        float reportTime;
        float adjdWeight, adjDacOuter;
        float correctWeight;
        bool moveOk;
        bool init = false;
        int emergency;
        
        const float startCorrectWeight = 0.01f;
        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}