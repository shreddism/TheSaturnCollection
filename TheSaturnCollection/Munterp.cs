using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;       

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

        [Property("Velocity 'Outer Range'"), DefaultPropertyValue(2.0f), ToolTip
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
            "Multiplies every area-subjective threshold."
        )]
        public float areaScale { 
            set => _areaScale = Math.Clamp(value, 0.01f, 5f);
            get => _areaScale;
        }
        public float _areaScale;

        [Property("Expect Wire"), DefaultPropertyValue(false), ToolTip
        (
            "Default: false\n\n" +
            "Keep this disabled unless you know what you are doing."
        )]
        public bool wire { set; get; }

        [Property("Expect Time"), DefaultPropertyValue(1f), ToolTip
        (
            "Default: 1\n\n" +
            "Don't touch this unless you know what you are doing."
        )]
        public float expect { 
            set => _expect = Math.Max(value, 0.0f);
            get => _expect;
        }
        public float _expect;

        public event Action<IDeviceReport> Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {    
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
                if (reportTime < 25) {
                  //  if (msOverride == 0)
                  //  reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                 //   else reportMsAvg = msOverride;
                    if (emergency > 0)
                    emergency--;
                }
                else {
                    emergency = 4;
                }
            //    Console.WriteLine(reportTime);
                moveOk = false;
                consume = true;

            //    Console.WriteLine("Consume---------");
                      
                StatUpdate(report);
            //    ConditionalUpdate();
                
              //  bottom = -1 * Math.Max(alpha0 - vtlimiter, 0);

                startOutput += stdir[0];

                if (rInner > 0f) RF();
                else {
                    moveOk = true;
                    ringOutput = startOutput;
                }

                if (moveOk && emergency == 0 && !liftorpress) {
                float cWeight = WireMultAdjust(dumbWeight, expect, reportTime, wire);
                float dWeight = cWeight * FSmoothstep(accel[0], -10 * areaScale, -200 * areaScale);
                startOutput = Vector2.Lerp(startOutput, pos[0], cWeight);
                startOutput = Vector2.Lerp(startOutput, pos[0], dWeight);
                }

                AEMA();

                report.Position = aemaOutput;

                if (!vec2IsFinite(report.Position + ringOutput + iRingPos0 + startOutput)) {
                    emergency = 4;
                }

             //   Console.WriteLine(Vector2.Distance(report.Position, pos[0]));

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

/*
        protected override void UpdateState()
        {
            if (State is ITabletReport report && PenIsInRange())
            {
                
                updateTime = (float)updateStopwatch.Restart().TotalMilliseconds;

                if (consume) {
                    alpha1 = 0;
                    if ((alpha0PreservationSociety > 1) && (top < 1)) {
                        top = 0.9f * (alpha0PreservationSociety - 1);
                        bottom = 0;
                    }
                    else top = 0;
                }

            //    Console.WriteLine(top);
           //     Console.WriteLine(bottom);
                    
                ohmygodbruh = (float)(reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);

                alpha0 = ((1 - top) * ohmygodbruh) + top;

                alpha0PreservationSociety = alpha0 + (expect / reportMsAvg);
                
                alpha0 += (vtlimiter - 1);

                alpha0 = Math.Clamp(alpha0, (vtlimiter - 1), pathpreservationsociety);

             //   Console.WriteLine(alpha0);

                trDir = Trajectory(stdir[0], stdir[1], stdir[2], alpha0);
                sdirt1 = Trajectory(a1stdir[0], a1stdir[1], a1stdir[2], alpha0 + 0.5f);
                trDir = Vector2.Lerp(trDir, sdirt1, pps4);
                LD();
                ldDir = WireAdjust(ldDir / (reportMsAvg / (expect)), expect, updateTime, wire);
    
                ldOutput += ldDir;

                RF();

                if (moveOk && emergency == 0 && !liftorpress) {
                    Vector2 hard = pos[0] + (trDir - (trDir - (stdir[1] / reportMsAvg))) * Math.Max(0, alpha0 - (vtlimiter - 1)) * (reportMsAvg / expect);
                    
                }
              
                AEMA();

                report.Position = aemaOutput;
                dirOfOutput = (report.Position - lastOutputPos) / updateTime;
                report.Pressure = pressure[0];

                if (!vec2IsFinite(report.Position + ringOutput + iRingPos0 + ldOutput) | liftorpress) {
                    report.Position = pos[0];
                    aemaOutput = pos[0];
                    ldOutput = pos[0];
                    ringOutput = pos[0];
                    iRingPos0 = pos[0];
                    emergency = 5;
                    OnEmit();
                    return;
                }

                if (emergency > 0) {
                    report.Position = pos[0];
                    ldOutput = pos[0];
                    aemaOutput = pos[0];
                    ringOutput = pos[0];
                    iRingPos0 = pos[0];
                    OnEmit();
                    return;
                }

                consume = false;

             //   Console.WriteLine(report.Position - pos[0]);

                OnEmit();
            }
        }
        */

        void StatUpdate(ITabletReport report) {
            InsertAtFirst(pos, report.Position);
            Vector2 smoothed = pos[0];
            if (reverseSmoothing < 1f && reverseSmoothing > 0f)
                smoothed = pos[1] + (pos[0] - pos[1]) / reverseSmoothing;
            InsertAtFirst(smpos, smoothed);
            InsertAtFirst(pressure, report.Pressure);
            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(vel, dir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());
            DAC();
            
        //    Console.WriteLine(dir[0]);

            if ((pressure[0] > 0 && pressure[1] == 0) || (pressure[0] == 0 && pressure[1] > 0))
            liftorpress = true;
             liftorpress = false;

            if (dir[0] == pos[0]) {
                emergency = 3;
            }

      //      pathpreservationsociety = Math.Min(Math.Min(stdir[0].Length(), stdir[1].Length()), stdir[2].Length());
     //       pathpreservationsociety = FSmoothstep(pathpreservationsociety, 0, 20);
    // //       pps2Dir = (stdir[0] + stdir[1]) - (stdir[2] + stdir[2]);
   //         pps2 = FSmoothstep(pps2Dir.Length(), 0, 15);
    //        pps3 = FSmoothstep(Vector2.Distance(stdir[0], stdir[1]), dacInner, dacOuter);
    //        pathpreservationsociety = 2 + (vtlimiter - 2) * Math.Min(Math.Min(pathpreservationsociety, pps2), pps3);
    //        pps4 = FSmoothstep(stdir[3].Length() - stdir[0].Length(), -15, 0) - FSmoothstep(stdir[3].Length() - stdir[0].Length(), 0, 15);
        //    Console.WriteLine(pathpreservationsociety);

  //          if (pressure[0] == 0)
    //            pathpreservationsociety = Math.Min(pathpreservationsociety, 3 - FSmoothstep(Vector2.Distance(ddir[0], ddir[1]), 30, 69));   
        }

        void DAC() {
            if (dacInner + dacOuter > 0f) {
                float vscale = FSmoothstep(vel[0], 5, 15 + dacOuter);
                float scale = MathF.Pow(FSmoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), Math.Max(0, vscale * dacInner), 0.01f + (vscale * dacOuter)), 3);
                Vector2 stabilized = Vector2.Lerp(stdir[0], dir[0], scale);
                if (vel[0] >= 1 && vel[1] >= 1 && vel[0] < 100 * areaScale && stabilized.Length() > 1) {
                    float ascale = Math.Max(Math.Abs(accel[0]), Math.Abs(vel[0] - stdir[0].Length()));
                    stabilized = Vector2.Lerp(stabilized, stdir[0].Length() * Vector2.Normalize(stabilized), vscale * (1 - scale) * (FSmoothstep(ascale, 0, dacOuter)));
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
                ringOutput = Vector2.Lerp(ringOutput, startOutput, FSmoothstep(ringDir.Length(), -0.01f, moddist));
                ringOutput = Vector2.Lerp(ringOutput, startOutput, FSmoothstep(accel[0], -10 * areaScale, -200 * areaScale));
                moveOk = true;
            }
            else moveOk = false;
        }

        void AEMA() {
            float weight = 1;

            if (stockWeight < 1f || moddist > 0f || aResponse > 0f) {
                weight = stockWeight;
                float dist = Vector2.Distance((aemaHold), ringOutput); 
                if (wire) dist *= MathF.Pow(MathF.Pow(Math.Min(updateTime / expect, 1.5f), 1 / MathF.Pow(stockWeight, 1 + FSmoothstep(vel[0], 0, moddist))), 1 / (modPow)); // I HAVE NO IDEA HOW OR WHY THIS WORKS BUT THIS REDUCES MOST RACKET UNDER NORMAL SCENARIOS!!!!!!
                float mod4 = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * stockWeight * MathF.Pow(FSmoothstep(dist, 5000 * aResponse * areaScale, (500 * aResponse * areaScale) - 1.0f) * FSmoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 30 * areaScale), modPow) * DotNorm(ddir[0], dir[0], 0);
                float mod5 = FSmoothstep(dist + vel[0] - (Math.Min(2.0f, aResponse) * accel[0]) + accel[0], -0.01f, moddist);
                weight -= (mod4);
                weight = Math.Clamp(weight, 0, 1);
                weight *= MathF.Pow(mod5, modPow);
            }
           
            aemaHold = Vector2.Lerp(aemaHold, ringOutput, weight);
            aemaDir = aemaHold - lastAemaHold;
            lastAemaHold = aemaHold;
            aemaOutput += aemaDir;

            if (dirSeparation > 0) {
                aemaOutput = Vector2.Lerp(aemaOutput, ringOutput, dirSeparation * WireMultAdjust(MathF.Pow(weight, 3 - dirSeparation), expect, updateTime, wire) * stockWeight);
            }
        }

        float WireMultAdjust(float a, float be, float br, bool w) => w ? a * (br / be) : a;

        Vector2 WireMultAdjust(Vector2 a, float be, float br, bool w) => w ? a * (br / be) : a;

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
        float[] vel = new float[HMAX];
        float[] accel = new float[HMAX];
        float[] jerk = new float[HMAX];
        float[] pointaccel = new float[HMAX];
        uint[] pressure = new uint[HMAX];
        
        Vector2 startOutput;
        float dumbWeight = 0.01f;
        Vector2 aemaDir, aemaHold, lastAemaHold;
        float peakMag, planeMag;
        float ohmygodbruh;
        Vector2 clusterpos0, clusterpos1;
        Vector2 clusterdir0, clusterdir1;
        bool clusterjumping, magclusterjumping;
        float stmag0, stmag1;
        float reportTime;
        float reportMsAvg;
        Vector2 trueOutput, trueDir;
        Vector2 ldDir, ldOutput;
        Vector2 aemaOutput;
        Vector2 iRingPos0, iRingPos1, ringDir, ringOutput, ringInputPos1, ringInputPos0, ringInputDir;
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
        bool liftorpress;
        bool moveOk;
        Vector2 dirOfOutput, lastOutputPos;
        float pps3;
        float adjdWeight;

       // float expect => 1000 / Frequency;
    }
}