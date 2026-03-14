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

        [Property("Inner Radius"), DefaultPropertyValue(20.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 20.0\n\n" +

            "A full deadzone for movement. Unit is in raw tablet data.\n" +
            "Directionally separated with smooth position transition to raw based on itself."
        )]
        public float rInner { 
            set => _rInner = Math.Max(value, 0.0f);
            get => _rInner;
        }
        public float _rInner;

        [Property("Smoothed Antichatter"), DefaultPropertyValue(50.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0\n\n" +
            
            "Fairly self-explanatory. This shouldn't go too high.\n" +
            "If you drag, consider setting this to 0 and just using an inner radius.\n" +
            "Directional Separation controls behavior."
        )]
        public float moddist { 
            set => _moddist = Math.Max(value, 0.0f);
            get => _moddist;
        }
        public float _moddist;

        [Property("Distance Smoothing Power"), DefaultPropertyValue(5.0f), ToolTip
        (
            "Possible range: 0.01 - any, default 5.0\n\n" +

            "Raises the weight of the above setting to this value. This shouldn't go too high."
        )]
        public float modPow { 
            set => _modPow = Math.Max(value, 0.01f);
            get => _modPow;
        }
        public float _modPow;

        [Property("Directional Separation"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 1.0\n\n" +

            "From 0.0 to 1.0, takes Smoothed Antichatter from extra smoothing to an extra transition to raw."
        )]
        public float dirSeparation {
            set => _dirSeparation = Math.Clamp(value, 0.0f, 1.0f);
            get => _dirSeparation;
        }
        public float _dirSeparation;

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
                if (reportTime < 25f && reportTime > 0.01f) {
                    if (emergency > 0)
                    emergency--;
                }
                else {
                    emergency = 5;
                }
                      
                StatUpdate(report);

                startOutput += stdir[0];

                if (emergency == 0) {
                float cWeight = adjdWeight;
                float dWeight = cWeight * Smoothstep(accel[0], -10 * areaScale, -150 * areaScale);
                startOutput = Vector2.Lerp(startOutput, smpos[0], cWeight);
                startOutput = Vector2.Lerp(startOutput, smpos[0], dWeight);
                }

                if (rInner > 0f) RF();
                else {
                    ringOutput = startOutput;
                }
                
                AEMA();

                report.Position = new Vector2(aemaOutput.X / xMod, aemaOutput.Y);
                dirOfOutput = (report.Position - lastOutputPos);
                lastOutputPos = report.Position;

                if (!vec2IsFinite(report.Position + startOutput + ringOutput + aemaOutput + acOutput)) {
                    emergency = 5;
                }

                if (emergency > 0) {
                    ERefresh();
                    report.Position = new Vector2(aemaOutput.X / xMod, aemaOutput.Y);
                }

            }
            Emit?.Invoke(value);
        }

        void StatUpdate(ITabletReport report) {
            InsertAtFirst(pos, report.Position);
            pos[0] *= xMod;

            Vector2 smoothed = pos[0];
            if (reverseSmoothing < 1f && reverseSmoothing > 0f)
                smoothed = pos[1] + (pos[0] - pos[1]) / reverseSmoothing;
            InsertAtFirst(smpos, smoothed);

            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(vel, dir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());

            if (emergency == 0) {
                InsertAtFirst(pathdiffs, PathDiff(new Vector2(pos[1].X / xMod, pos[1].Y), new Vector2(pos[0].X / xMod, pos[0].Y), lastOutputPos));
            }

            DAC();

            dscalebonus = Smoothstep(pathdiffs[0].X, 0, 25) * Smoothstep(accel[0], 0, -25);

            if (dir[0] == pos[0]) {
                emergency = 5;
            }
        }

        void DAC() {
            if (dacInner + dacOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + adjDacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], dir[0], scale);
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
                ringOutput = Vector2.Lerp(ringOutput, startOutput, Smoothstep(ringDir.Length(), -0.01f, rInner));
                ringOutput = Vector2.Lerp(ringOutput, startOutput, Smoothstep(accel[0], -10 * areaScale, -150 * areaScale));
            }
        }

       void AEMA() {
            float weight = stockWeight;
            float mod4 = 0;

            if (moddist > 0f) {
                float dist = Vector2.Distance(aemaHold, ringOutput);
                float mod5 = Smoothstep(dist + vel[0] - accel[0], -0.01f, moddist);
                weight *= MathF.Pow(mod5, modPow);
            }
           
            aemaHold = Vector2.Lerp(aemaHold, ringOutput, weight);
            aemaDir = aemaHold - lastAemaHold;
            lastAemaHold = aemaHold;
            acOutput += aemaDir;

            if (dirSeparation > 0) {
                acOutput = Vector2.Lerp(acOutput, Vector2.Lerp(aemaHold, ringOutput, dirSeparation), weight * stockWeight);
                acOutput = Vector2.Lerp(acOutput, Vector2.Lerp(aemaHold, ringOutput, dirSeparation), stockWeight * dscalebonus);
            }

            if (aResponse > 0f) {
                float dist = Vector2.Distance(acOutput, aemaOutput);
                mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * MathF.Pow(Smoothstep(dist, 2500 * aResponse, (500 * aResponse) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 25 * areaScale), 3.0f) * DotNorm(ddir[0], dir[0], 0);
            }
            
            weight = Math.Clamp(1 - mod4, 0, 1);
            aemaOutput = Vector2.Lerp(aemaOutput, acOutput, Math.Min(1, weight));
        }

        void Initialize() {
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
            correctWeight = startCorrectWeight;
            if (dacInner + dacOuter == 0f) {
                adjdWeight = correctWeight * 0.01f;
            }
        }
        
        void ERefresh() {
            startOutput = pos[0];
            ringInputPos1 = pos[0];
            ringInputPos0 = pos[0];
            iRingPos1 = pos[0];
            iRingPos0 = pos[0];
            ringOutput = pos[0];
            lastAemaHold = pos[0];
            aemaHold = pos[0];
            acOutput = pos[0];
            aemaOutput = pos[0];
        }

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] stdir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        Vector2[] smpos = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];

        Vector2[] pathdiffs = new Vector2[HMAX];
        
        Vector2 startOutput;
        Vector2 ringInputPos0, ringInputPos1, ringInputDir, iRingPos0, iRingPos1, ringDir, ringOutput;
        Vector2 aemaHold, lastAemaHold, aemaDir, acOutput, aemaOutput;
        Vector2 lastOutputPos, dirOfOutput;
        float reportTime;
        float adjdWeight, adjDacOuter;
        float correctWeight;
        float dscalebonus;
        bool init = false;
        int emergency;
        
        const float startCorrectWeight = 0.01f;
        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}