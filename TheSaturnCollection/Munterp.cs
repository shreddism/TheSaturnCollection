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
        float adjDacOuter;

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
                    clampOutput = startOutput;
                }
                
                AEMA();

                report.Position = new Vector2(adaptOutput.X / xMod, adaptOutput.Y);
                dirOfOutput = (report.Position - lastOutputPos);
                lastOutputPos = report.Position;

                if (!vec2IsFinite(report.Position + startOutput + clampOutput + smoothOutput + adaptOutput + outputInternal)) {
                    emergency = 5;
                }

                if (emergency > 0) {
                    ERefresh();
                    report.Position = new Vector2(adaptOutput.X / xMod, adaptOutput.Y);
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

            DAC();

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
            Vector2 dist = startOutput - clampHold;
            float distLength = dist.Length();
            Vector2 ringDir = Math.Max(0, distLength - (rInner)) * Default(Vector2.Normalize(dist), Vector2.Zero);
            float ringDirLength = ringDir.Length();
            clampHold += ringDir;
            clampOutput += ringDir;
            if (ringDirLength > 0 || distLength > rInner || accel[0] < -10 * areaScale || vel[0] > 10 * rInner) {
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(ringDirLength, -0.01f, rInner), 0.3f));
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(accel[0], -10 * areaScale, -150 * areaScale), 0.3f));
            }
        }

        void AEMA() {
            Vector2 dist = clampOutput - smoothHold;
            float distLength = dist.Length();
            float mLength = DSFunction(distLength);
            float wcon = stockWeight * Default(mLength / distLength, 0);
            smoothHold += wcon * dist;
            smoothOutput = smoothHold;
            if (sepMult > 0 && mLength > 0) {
                sepScale = Smoothstep(distLength, -0.01f, smoothDist * sepMult);
            }       
            smoothOutput = Vector2.Lerp(smoothHold, Vector2.Lerp(smoothHold, clampOutput, stockWeight), sepScale);
            

            float mod4 = 0;
            if (aResponse > 0f) {
                float aDist = Vector2.Distance(smoothOutput, adaptOutput);
                mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * MathF.Pow(Smoothstep(aDist, 2500 * aResponse, (500 * aResponse) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 25 * areaScale), 3.0f) * DotNorm(ddir[0], dir[0], 0);
            }
            float weight = Math.Clamp(1 - mod4, 0, 1);
            adaptOutput = Vector2.Lerp(adaptOutput, smoothOutput, weight);
        }

        float DSFunction(float dist) {
            if (dist >= smoothDist) return dist - halfSmoothDist;
            float x = (dist / smoothDist);
            return x * x * halfSmoothDist;
        }

        void Initialize() {
            halfSmoothDist = smoothDist * 0.5f;
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
            correctWeight = startCorrectWeight;
            if (dacInner + dacOuter == 0f) {
                adjdWeight = correctWeight * 0.01f;
            }
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

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];

        float sepScale;
        
        Vector2 startOutput, outputInternal;
        Vector2 lastOutputPos, dirOfOutput;
        float reportTime;
        float adjdWeight;
        float correctWeight;
        bool init = false;
        int emergency;
        
        const float startCorrectWeight = 0.01f;
        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}