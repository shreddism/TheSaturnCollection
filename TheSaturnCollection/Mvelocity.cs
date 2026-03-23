using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;       
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Multifilter (Velocity Interpolation)")]
    public class MultifilterVI : AsyncPositionedPipelineElement<IDeviceReport>
    {
        public MultifilterVI() : base()
        {
        }

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        [Property("Velocity Trajectory Limiter (Hover Over The Textbox)"), DefaultPropertyValue(3.0f), ToolTip
        (
            "Important: This uses an unusual form of interpolation that makes it easier to add experimental pet project velocity features.\n" +
            "This works just fine on a Wacom PTK-470, but you may prefer the position interpolated multifilter better,\n" +
            "as it just uses Temporal Resampler's interpolation and prediction.\n\n" +

            "Possible range: 2.0 - 3.0, default 3.0\n\n" +

            "2 = zero prediction, only interpolation, 3 = only prediction under sufficient situations.\n" +
            "If on a Intuos Pro (200hz or 300hz), put this to 3.\n" +
            "At least try positional interpolation otherwise."
        )]
        public float vtlimiter { 
            set => _vtlimiter = Math.Clamp(value, 2.0f, 3.0f);
            get => _vtlimiter;
        }
        public float _vtlimiter;

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
        float adjDacOuter;

        [BooleanProperty("Wire Mode", ""), DefaultPropertyValue(true), ToolTip
        (
            "Equivalent to 'extraFrames' from Temporal Resampler.\n" +
            "You should probably leave this enabled unless your specific situation requires otherwise.\n" +
            "Some fixes prevent most distance smoothing from racketing under normal scenarios when this is enabled,\n" +
            "which are not present in any other filter. This changes behavior but keeps it working well."
        )]
        public bool wire { set; get; }
        float updateTime;
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

        [BooleanProperty("Wacom PTK-x70 Series Toggle", ""), DefaultPropertyValue(true), ToolTip
        (
            "Enables behavioral tweaks that improve the experience on a Wacom PTK-x70 tablet, like not bugging out on press/lift.\n" +
            "May be applicable on a PTH-x60 tablet, but this is untested."
        )]
        public bool hcToggle { set; get; }
        Vector2 emPos;
        bool eflag;

        [Property("Correction - Accel Adjustment"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0\n\n" +

            "May modify feel slightly on sharp acceleration.\n" +
            "If you are unsure, keep at 0."
        )]
        public float dCorrect { 
            set => _dCorrect = Math.Clamp(value, 0.0f, 1.0f);
            get => _dCorrect;
        }
        public float _dCorrect;

        [Property("Correction - Decel Adjustment"), DefaultPropertyValue(5.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 5.0\n\n" +

            "Higher = less chance of bad overshoot on deceleration.\n" +
            "If you are unsure, keep at 5."
        )]
        public float tv2 { 
            set => _tv2 = Math.Max(value, 0.0f);
            get => _tv2;
        }
        public float _tv2;

        protected override void ConsumeState()
        {
            if (!init) {
                Initialize();
                init = true;
                emergency = 5;
                eflag = false;
            }
            if (State is ITabletReport report)
            {
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
                if (reportTime < 25f && reportTime > 0.01f) {
                    if (msOverride == 0) {
                        reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                        expectC = reportMsAvg / expect;
                        correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                    }
                    if (emergency > 0)
                    emergency--;
                }
                else {
                    emergency = 5;
                    eflag = false;
                }

                moveOk = false;
                consume = true;

                StatUpdate(report);
                
                bottom = -1 * Math.Max(alpha0 - vtlimiter, 0);
                if (top > 1f || bottom > 1f) {
                    top = 0;
                    bottom = 0;
                }

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
                    report.Pressure = pressure[0];
                    
                    if (eflag) {
                        startOutput = pos[0];
                        if (rInner > 0f) RF();
                        else {
                            moveOk = true;
                            clampOutput = startOutput;      
                        }
                        AEMA();
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
                    top = 0;
                    OnEmit();
                    return;
                }

                if (consume) {
                    if ((preserveTime > 1) && (top < 1)) {
                        top = (preserveTime - 1);            
                        bottom = 0;
                    }
                    else top = 0;
                } 

                cTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                alpha0 = ((1 - top) * cTime) + top;
                top *= 0.99f;

                preserveTime = alpha0 + (expect / reportMsAvg);
                alpha0 += (vtlimiter - 1);

                if (hcToggle && pps == 3)
                alpha0 = Math.Clamp(alpha0, (vtlimiter - 1), 4);
                else {
                    alpha0 = Math.Clamp(alpha0, (vtlimiter - 1), pps);
                }

                trDir = Trajectory(stdir[0], stdir[1], stdir[2], alpha0);
                sdirt1 = Trajectory(a1stdir[0], a1stdir[1], a1stdir[2], alpha0 + 0.5f);
                trDir = Vector2.Lerp(trDir, sdirt1, pps4);

                useDir = WireMultAdjust(trDir / expectC, expect, updateTime, wire);

                startOutput += useDir;

                if (rInner > 0f) RF();
                else {
                    moveOk = true;
                    clampOutput = startOutput;
                }

                if (moveOk) {
                    Vector2 hard = smpos[0];
                    if (hcToggle) {
                        Vector2 cDir = (trDir - (trDir - (stdir[1] / reportMsAvg))) * Math.Max(cTime + (vtlimiter - 3), 0.0f) * expectC;
                        Vector2 hDir = cDir * cmod1;
                        hard += hDir;
                    }
                    float cWeight = Math.Min(1.0f, WireMultAdjust(adjdWeight, expect, updateTime, wire)) / (1 + dscale);    // Corrective weights get multiplied by time.
                    float dWeight = tv2 * cWeight * Math.Min(dscale + dscalebonus, 1);
                    startOutput = Vector2.Lerp(startOutput, hard, cWeight * cmod1);
                    startOutput = Vector2.Lerp(startOutput, smpos[0], dWeight);
                    clampOutput = Vector2.Lerp(clampOutput, hard, cWeight * cmod1);
                    clampOutput = Vector2.Lerp(clampOutput, smpos[0], dWeight);
                } 

                AEMA();

                outputInternal = adaptOutput;

                emPos = outputInternal;

                report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
                dirOfOutput = (report.Position - lastOutputPos) / updateTime;
                lastOutputPos = report.Position;

                report.Pressure = pressure[0];

                if (!vec2IsFinite(report.Position + startOutput + clampOutput + smoothOutput + adaptOutput + outputInternal)) {
                    ERefresh();
                    emPos = pos[0];
                    InsertAtFirst(smpos, pos[0]);

                    eflag = false;
                    emergency = 5;
                    report.Position = new Vector2(adaptOutput.X / xMod, adaptOutput.Y);
                }
                
                consume = false;

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

            if (emergency == 0) {
                InsertAtFirst(pathdiffs, PathDiff(new Vector2(pos[1].X / xMod, pos[1].Y), new Vector2(pos[0].X / xMod, pos[0].Y), lastOutputPos));
            }

            DAC();

            if (dir[0] == pos[0]) {
                emergency = 5;
                dir[0] = Vector2.Zero;
                eflag = false;
            }

            if ((hcToggle) && ((pressure[0] > 0 && pressure[1] == 0) || (pressure[0] == 0 && pressure[1] > 0))) {
                if (emergency == 0) eflag = true;
                emergency = 5;
            }

            dscale = Smoothstep(accel[0] - Math.Max(0, jerk[0]), -10 * areaScale, -200 * areaScale);
            dscalebonus = Smoothstep(pathdiffs[0].X, 0, 25) * Smoothstep(accel[0], 0, -25);
            vascale = Smoothstep(vel[0] + accel[0], 25 * areaScale, 100 * areaScale);

            float bonus = Smoothstep((accel[0] + jerk[0]), 10, 200);
            cmod1 = (1 - dCorrect) + (dCorrect) * (MathF.Pow(Smoothstep((accel[0] + jerk[0]), -200, -10), 2) + (2 * bonus - bonus * bonus));

            pps = Math.Min(Math.Min(stdir[0].Length(), stdir[1].Length()), stdir[2].Length());
            pps = Smoothstep(pps, 2, 20);
            pps2Dir = (stdir[0] + stdir[1]) - (stdir[2] + stdir[2]);
            pps2 = Smoothstep(pps2Dir.Length(), 1, 15);
            pps3 = Smoothstep(Vector2.Distance(stdir[0], stdir[1]), dacInner, adjDacOuter);
            pps = 2 + (vtlimiter - 2) * Math.Min(Math.Min(pps, pps2), pps3);
            pps4 = Smoothstep(stdir[3].Length() - stdir[0].Length(), -15, -3) - Smoothstep(stdir[3].Length() - stdir[0].Length(), 3, 15);

            if (hcToggle && pressure[0] == 0) {
                pps = Math.Min(pps, 3 - Smoothstep(Vector2.Distance(ddir[0], ddir[1]), 70, 100));
            }
        }

        void DAC() {
            if (dacInner + dacOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + adjDacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], dir[0], scale); 
                InsertAtFirst(stdir, stabilized);
                InsertAtFirst(a1stdir, (stdir[1] + stdir[0]) / 2);
            }
            else {
                InsertAtFirst(stdir, dir[0]);
                InsertAtFirst(a1stdir, (stdir[1] + stdir[0]) / 2);
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
                clampOutput = Vector2.Lerp(clampOutput, startOutput, WireWeightAdjust(Smoothstep(ringDirLength, -0.01f, rInner), expect, updateTime, wire));
                clampOutput = Vector2.Lerp(clampOutput, startOutput, WireWeightAdjust(Smoothstep(accel[0], -10 * areaScale, -150 * areaScale), expect, updateTime, wire));
            }
        }

        void AEMA() {
            Vector2 dist = clampOutput - smoothHold;
            float distLength = dist.Length();
            float mLength = DSFunction(distLength);
            float wcon = WireWeightAdjust(stockWeight * Default(mLength / distLength, 0), expect, updateTime, wire);
            smoothHold += wcon * dist;
            smoothOutput = smoothHold;
            if (sepMult > 0 && mLength > 0) {
                if (!(wire) || updateTime / expect > 0.99f) {
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
            adaptOutput = Vector2.Lerp(adaptOutput, smoothOutput, WireWeightAdjust(weight, expect, updateTime, wire));
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
                expectC = reportMsAvg / expect;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                if (dacInner + dacOuter == 0f) {
                    adjdWeight = correctWeight;
                }
            }
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
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

        Vector2[] a1stdir = new Vector2[HMAX];
        Vector2[] pathdiffs = new Vector2[HMAX];
        
        Vector2 startOutput, outputInternal;
        Vector2 lastOutputPos, dirOfOutput;
        float reportTime;
        float adjdWeight;
        float correctWeight;
        bool init = false;
        int emergency;

        float reportMsAvg;
        float sepScale;
        
        bool consume;
        float dscalebonus;

        bool moveOk;
        
        const float startCorrectWeight = 0.01f;    
        const float msStandard = 3.302466f;
        float expect => 1000 / Frequency;
        
        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();

        Vector2 sdirt1, useDir, pps2Dir, trDir;
        float dscale, vascale;
        float expectC; 
        float alpha0, preserveTime;
        float top, bottom;
        float pps, pps2, pps3, pps4;
        float cTime;
        float cmod1 = 1;
    }
}