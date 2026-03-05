using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;       

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
            "This works just fine on a PTK-470, but you may prefer the position interpolated multifilter better, as it just uses Temporal Resampler's interpolation and prediction.\n\n" +
            
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
            "Equivalent to what is seen in Temporal Resampler."
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

        [Property("Area Scale"), DefaultPropertyValue(0.75f), ToolTip
        (
            "Possible range: 0.01f - 5f, default 0.75\n\n" +
            "Multiplies every area-subjective threshold, like the ones used in Accel Response Aggressiveness.\n"
        )]
        public float areaScale { 
            set => _areaScale = Math.Clamp(value, 0.01f, 5f);
            get => _areaScale;
        }
        public float _areaScale;

        [Property("Wacom PTK-x70 Series Toggle"), DefaultPropertyValue(true), ToolTip
        (
            "Enables behavioral tweaks that improve the experience on a Wacom PTK-x70 tablet, like not bugging out on press/lift.\n" +
            "May be applicable on a PTH-x60 tablet, but this is untested."
        )]
        public bool hcToggle { set; get; }

        [Property("Correction - Accel Adjustment"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0\n\n" +
            "May modify feel slightly on sharp acceleration."
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
            "Unsure - keep at 5."
        )]
        public float tv2 { 
            set => _tv2 = Math.Max(value, 0.0f);
            get => _tv2;
        }
        public float _tv2;

        [Property("X Modifier"), DefaultPropertyValue(1f), ToolTip
        (
            "Possible range: 0.01 - 100, default 1\n\n" +
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

        public event Action<IDeviceReport> Emit;

    
        protected override void ConsumeState()
        {
            if (!init) {
                Initialize();
                init = true;
                emergency = 6;
                eflag = false;
            }
            if (State is ITabletReport report)
            {
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
                if (reportTime < 25) {
                    if (msOverride == 0) {
                        reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                        expectC = reportMsAvg / expect;
                        correctWeight = MathF.Pow(startCorrectWeight, 1 / expect) * (msStandard / reportMsAvg);
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
                    report.Position = pos[0];
                    startOutput = pos[0];
                    InsertAtFirst(smpos, pos[0]);
                    
                    if (eflag) {
                        if (rInner > 0f) RF();
                        else {
                            moveOk = true;
                            ringOutput = startOutput;
                        }
    
                        if (moveOk) {
                            AEMA();
                            cTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                            float scale = Math.Min((((float)(5 - emergency) + Math.Min(cTime, 1.0f)) * 0.2f), 1.0f);
                            report.Position = Vector2.Lerp(lastOutputPos, aemaOutput, scale);
                            emPos = report.Position;
                        }
                        else {
                            aemaOutput = emPos;
                            report.Position = aemaOutput;
                        }
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
                    top = 0;
                    OnEmit();
                    return;
                }

                if (consume) {
                    alpha1 = 0;
                    if ((alpha0PreservationSociety > 1) && (top < 1)) {
                        top = (alpha0PreservationSociety - 1);            
                        bottom = 0;
                    }
                    else top = 0;
                } 
                cTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                alpha0 = ((1 - top) * cTime) + top;
                top *= 0.99f;

                alpha0PreservationSociety = alpha0 + (expect / reportMsAvg);
                alpha0 += (vtlimiter - 1);

                if (hcToggle && pathpreservationsociety == 3)
                alpha0 = Math.Clamp(alpha0, (vtlimiter - 1), 4);
                else {
                    alpha0 = Math.Clamp(alpha0, (vtlimiter - 1), pathpreservationsociety);
                }

                trDir = Trajectory(stdir[0], stdir[1], stdir[2], alpha0);
                sdirt1 = Trajectory(a1stdir[0], a1stdir[1], a1stdir[2], alpha0 + 0.5f);
                trDir = Vector2.Lerp(trDir, sdirt1, pps4);
                useDir = WireMultAdjust(trDir / expectC, expect, updateTime, wire);
    
                startOutput += useDir;

                if (rInner > 0f) RF();
                else {
                    moveOk = true;
                    ringOutput = startOutput;
                }

                if (moveOk) {
                    Vector2 hard = smpos[0];
                    if (hcToggle) {
                        Vector2 cDir = (trDir - (trDir - (stdir[1] / reportMsAvg))) * Math.Max(cTime + (vtlimiter - 3), 0.0f) * expectC;
                        Vector2 hDir = cDir * cmod1;
                        hard += hDir;
                    }
                    float cWeight = Math.Min(1.0f, WireMultAdjust(adjdWeight, expect, updateTime, wire)) / (1 + dscale);    // Corrective weights get multiplied by time.
                    float dWeight = tv2 * cWeight * (dscale + dscalebonus);
                    startOutput = Vector2.Lerp(startOutput, hard, cWeight * cmod1);
                    startOutput = Vector2.Lerp(startOutput, smpos[0], dWeight);
                    ringOutput = Vector2.Lerp(ringOutput, hard, cWeight * cmod1);
                    ringOutput = Vector2.Lerp(ringOutput, smpos[0], dWeight);
                } 

                AEMA();

                report.Position = aemaOutput;
                dirOfOutput = (report.Position - lastOutputPos);
                lastOutputPos = report.Position;
                report.Pressure = pressure[0];

                if (!vec2IsFinite(report.Position + startOutput + aemaOutput + ringOutput)) {
                    report.Position = pos[0];
                    aemaOutput = pos[0];
                    startOutput = pos[0];
                    aemaHold = pos[0];
                    lastAemaHold = pos[0];
                    ringOutput = pos[0];
                    iRingPos0 = pos[0];
                    InsertAtFirst(smpos, pos[0]);
                    lastOutputPos = pos[0];
                    emergency = 5;
                    OnEmit();
                    return;
                }
                else {
                    emPos = report.Position;
                }
                
                consume = false;
                
               // Plot();

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
            if (emergency == 0) {
                InsertAtFirst(pathdiffs, PathDiff(pos[1], pos[0], lastOutputPos));
            }

            if (dacInner + dacOuter + vOuter > 0f) DAC();
            else {
                InsertAtFirst(stdir, dir[0]);
                InsertAtFirst(a1stdir, (stdir[1] + stdir[0]) / 2);
            }

            if ((hcToggle) && ((pressure[0] > 0 && pressure[1] == 0) || (pressure[0] == 0 && pressure[1] > 0)) || (dir[0] == pos[0])) {
                if (emergency == 0) eflag = true;
                emergency = 5;
            }

            dscale = FSmoothstep(accel[0] - Math.Max(0, jerk[0]), -10 * areaScale, -200 * areaScale);
            dscalebonus = FSmoothstep(pathdiffs[0].X, 0, 25) * FSmoothstep(vel[0] + accel[0], 50, 0);
            vascale = FSmoothstep(vel[0] + accel[0], 25 * areaScale, 100 * areaScale);

            float bonus = FSmoothstep((accel[0] + jerk[0]), 10, 200);
            cmod1 = (1 - dCorrect) + (dCorrect) * (MathF.Pow(FSmoothstep((accel[0] + jerk[0]), -200, -10), 2) + (2 * bonus - bonus * bonus));

            pathpreservationsociety = Math.Min(Math.Min(stdir[0].Length(), stdir[1].Length()), stdir[2].Length());
            pathpreservationsociety = FSmoothstep(pathpreservationsociety, 2, 20);
            pps2Dir = (stdir[0] + stdir[1]) - (stdir[2] + stdir[2]);
            pps2 = FSmoothstep(pps2Dir.Length(), 1, 15);
            pps3 = FSmoothstep(Vector2.Distance(stdir[0], stdir[1]), dacInner, adjDacOuter);
            pathpreservationsociety = 2 + (vtlimiter - 2) * Math.Min(Math.Min(pathpreservationsociety, pps2), pps3);
            pps4 = FSmoothstep(stdir[3].Length() - stdir[0].Length(), -15, -3) - FSmoothstep(stdir[3].Length() - stdir[0].Length(), 3, 15);

            if (hcToggle && pressure[0] == 0) {
                pathpreservationsociety = Math.Min(pathpreservationsociety, 3 - FSmoothstep(Vector2.Distance(ddir[0], ddir[1]), 70, 100));
            }
        }

        void DAC() {
            float vscale = FSmoothstep(vel[0], 5, 10 + dacOuter);
            float scale = MathF.Pow(FSmoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
            adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
            Vector2 stabilized = Vector2.Lerp(stdir[0], dir[0], scale); 
            if (vel[0] >= 1 && vel[1] >= 1 && vel[0] < 150 * areaScale && stabilized.Length() > 1) {
                float ascale = Math.Max(Math.Abs(accel[0]), Math.Abs(vel[0] - stdir[0].Length()));
                stabilized = Vector2.Lerp(stabilized, stdir[0].Length() * Vector2.Normalize(stabilized), vscale * (1 - scale) * (FSmoothstep(ascale, -0.01f, vOuter)));
            }
            InsertAtFirst(stdir, stabilized);
            InsertAtFirst(a1stdir, (stdir[1] + stdir[0]) / 2);
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
                ringOutput = Vector2.Lerp(ringOutput, startOutput, FSmoothstep(new Vector2(ringDir.X * xMod, ringDir.Y).Length(), -0.01f, 0.5f * moddist));
                ringOutput = Vector2.Lerp(ringOutput, startOutput, FSmoothstep(accel[0], -10 * areaScale, -200 * areaScale));
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
                if (wire) dist *= MathF.Pow(MathF.Pow(Math.Min(updateTime / expect, 1.5f), 1 / MathF.Pow(stockWeight, 1 + FSmoothstep(vel[0], 0, moddist))), 1 / (modPow)); // I HAVE NO IDEA HOW OR WHY THIS WORKS BUT THIS REDUCES MOST RACKET UNDER NORMAL SCENARIOS!!!!!!
                float mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * stockWeight * MathF.Pow(FSmoothstep(dist, 5000 * aResponse * areaScale, (500 * aResponse * areaScale) - 1.0f) * FSmoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 30 * areaScale), modPow) * DotNorm(ddir[0], dir[0], 0);
                float mod5 = FSmoothstep(dist + vel[0], -0.01f, moddist);
                weight -= (mod4);
                weight = Math.Clamp(weight, 0, 1);
                weight *= MathF.Pow(mod5, modPow);
            }
           
            aemaHold = Vector2.Lerp(aemaHold, ringOutput, weight);
            aemaDir = aemaHold - lastAemaHold;
            lastAemaHold = aemaHold;
            aemaOutput += aemaDir;

            if (dirSeparation > 0) {
                aemaOutput = Vector2.Lerp(aemaOutput, ringOutput, dirSeparation * Math.Min(1, WireMultAdjust(weight, expect, updateTime, wire)) * stockWeight);
            }
        }

        void Initialize() {
            if (msOverride > 0) {
                reportMsAvg = msOverride;
                expectC = reportMsAvg / expect;
                correctWeight = MathF.Pow(startCorrectWeight, 1 / expect) * (msStandard / msOverride);
                if (dacInner + dacOuter == 0f) {
                    adjdWeight = correctWeight;
                }
            }
            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);
        }

        void Plot() {
            Console.Write("vx");
            Console.WriteLine(((updateTime * 100)));
            Console.Write("vy");
            Console.WriteLine(((dirOfOutput.Length() * 10)));
            Console.Write("ax");
            Console.WriteLine(((dirOfOutput).X));
            Console.Write("ay");
            Console.WriteLine(((dirOfOutput).Y) * -1);
            Console.WriteLine("xx");
            Console.WriteLine("dd");
        }

        float WireMultAdjust(float a, float be, float br, bool w) => w ? a * Math.Clamp(br / be, 0, 1.5f) : a;

        Vector2 WireMultAdjust(Vector2 a, float be, float br, bool w) => w ? a * Math.Clamp(br / be, 0, 1.5f): a;

        float WirePowAdjust(float a, float be, float br, bool w) => w ? MathF.Pow(a, be / br) : a;

        float DotNorm(Vector2 a, Vector2 b) => Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b));

        float DotNorm(Vector2 a, Vector2 b, float x) => (a != Vector2.Zero && b != Vector2.Zero) ? Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)) : x;

        float Default(float a, float b) => float.IsFinite(a) ? a : b;

        Vector2 Default(Vector2 a, Vector2 b) => vec2IsFinite(a) ? a : b;

        Vector2 MinLength(Vector2 a, Vector2 b) => a.LengthSquared() <= b.LengthSquared() ? a : b;

        Vector2 capDist(Vector2 a, Vector2 b, float d) => a + Math.Min(Vector2.Distance(b, a), d) * (vec2IsFinite(Vector2.Normalize(b - a)) ? Vector2.Normalize(b - a) : Vector2.Zero); 

        public static float FSmoothstep(float x, float start, float end)
        {
            x = Math.Clamp((x - start) / (end - start), 0.0f, 1.0f);
            return x * x * (3.0f - 2.0f * x);
        }

        public static float FSmootherstep(float x, float start, float end)
        {
            x = Math.Clamp((x - start) / (end - start), 0.0f, 1.0f);
            return (x * x * x * (x * (6.0f * x - 15.0f) + 10.0f));
        }

        public static float ClampedLerp(float start, float end, float scale)
        {
            return start + Math.Clamp(scale, 0, 1) * (end - start);
        }

        public static Vector2 Trajectory(Vector2 p0, Vector2 p1, Vector2 p2, float t) {
            Vector2 tMid = 0.5f * (p0 + p2);
            return p2 + t * ((2 * p1) - p2 - tMid) + 0.5f * t * t * (2 * (tMid - p1));
        } 

        public static Vector2 PathDiff(Vector2 s, Vector2 e, Vector2 p) {
            Vector2 mp = p - s;
            Vector2 me = e - s;
            float ca = -MathF.Atan2(me.Y, me.X);
            Vector2 rp = Rotate(mp, ca);
            Vector2 re = Rotate(me, ca);
            return rp - re;
        }

        public static Vector2 Rotate(Vector2 p, float a) {
            float cosine = MathF.Cos(a);
            float sine = MathF.Sin(a);
            return new Vector2((cosine * p.X) - (sine * p.Y), (sine * p.X) + (cosine * p.Y));
        }

        void InsertAtFirst<T>(T[] arr, T element)
        {
            for (int p = arr.Length - 1; p > 0; p--) arr[p] = arr[p - 1];
            arr[0] = element;
        }

        const int HMAX = 4;

        Vector2 planestart, planeend, peak;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] stdir = new Vector2[HMAX];
        Vector2[] ddir = new Vector2[HMAX];
        Vector2[] a1stdir = new Vector2[HMAX];
        Vector2[] smpos = new Vector2[HMAX];
        Vector2[] pathdiffs = new Vector2[HMAX];
        Vector2[] adjDir = new Vector2[HMAX];
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];

        Vector2 lastAemaHold;
        float dscalebonus;
        Vector2 unaccounted = Vector2.Zero;
        float expectC;
        float dscale, vascale, scscale;
        float peakMag, planeMag;
        float cTime;
        Vector2 clusterpos0, clusterpos1;
        Vector2 clusterdir0, clusterdir1;
        bool clusterjumping, magclusterjumping;
        float stmag0, stmag1;
        float reportTime;
        float reportMsAvg;
        Vector2 trueOutput, trueDir;
        Vector2 useDir;
        Vector2 aemaOutput;
        Vector2 iRingPos0, iRingPos1, ringDir, startOutput, ringInputPos1, ringInputPos0, ringInputDir, ringOutput;
        int emergency;
        int namelesstime0, namelesstime1;
        float linedrivetime;
        bool linedriving;
        Vector2 arc;
        float savetime;
        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();
        float updateTime;
        float alpha0, alpha1, alpha0PreservationSociety;
        float top, bottom;
        Vector2 sdirt1;
        Vector2 pps2Dir;
        float pathpreservationsociety, pps2, pps4;
        bool consume;
        Vector2 sense;
        float peakAccel0, peakAccel1;
        Vector2 trDir;
        private bool vec2IsFinite(Vector2 vec) => float.IsFinite(vec.X) & float.IsFinite(vec.Y);
        private HPETDeltaStopwatch perfStopwatch = new HPETDeltaStopwatch();
        double updatePerfTimeAvg;
        double updates = 0;
        bool moveOk;
        Vector2 dirOfOutput, lastOutputPos;
        float pps3;
        bool init = false;
        float adjdWeight;
        bool eflag;
        const float startCorrectWeight = 0.01f;
        const float msStandard = 3.302466f;
        float correctWeight;
        float adjDacOuter;
        float cmod1 = 1;
        Vector2 aemaDir, aemaHold;
        Vector2 emPos;


        float expect => 1000 / Frequency;
    }
}