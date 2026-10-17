using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;  
using static Saturn.Utils;     

namespace Saturn
{
    [PluginName("Saturn - High Report Rate Noise Compensation")]
    public class HRRNC : IPositionedPipelineElement<IDeviceReport>
    {
        public HRRNC() : base()
        {
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        private HPETDeltaStopwatch perfStopwatch = new HPETDeltaStopwatch();

        [Property("Noise Amount (Warning: Hover Over the Textbox and Read This Tooltip)"), DefaultPropertyValue(6f), ToolTip
        (
            "This probably should be the very first filter that your input passes through. However, this may not be the case.\n" +
            "To make sure, save your OTD settings as a file, edit the file so that this filter is at the top, then load the settings file and save.\n\n" +

            "This filter is primarily designed for high report-rate tablets which could use a little noise filtering.\n" +
            "It attempts to target report-to-report signal noise, even under motion.\n" +
            "This was tested on a PTK-470 that runs at 1000hz. The default settings are good in this use case.\n" +
            "You will likely not need to touch any other setting.\n\n" +

            "Possible range: 0.0 - any, default 5.0\n\n" +

            "Most of the filter's internal behavior is dictated by this value. It can consider deviation within this amount negligible unless it accumulates."
        )]
        public float noiseAmount { 
            set => _noiseAmount = Math.Max(value, 0f);
            get => _noiseAmount;
        }
        public float _noiseAmount;

        [Property("Accel/Velocity Multiplier"), DefaultPropertyValue(1f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 1.0\n\n" +

            "This filter should not overshoot, but if it does, this should be lowered very slightly.\n" +
            "In any normal case, this should not go below 1."
        )]
        public float vMult { 
            set => _vMult = Math.Clamp(value, 0f, 1f);
            get => _vMult;
        }
        public float _vMult;

        [Property("Time Scale"), DefaultPropertyValue(1f), ToolTip
        (
            "Possible range: 0.0 - any, default 1.0\n\n" +

            "There are baseline weights that expect a certain scale of time. Change this as you see fit."
        )]
        public float timeScale { 
            set => _timeScale = Math.Max(value, 0f);
            get => _timeScale;
        }
        public float _timeScale;

        [Property("Position Slack"), DefaultPropertyValue(1.5f), ToolTip
        (
            "Possible range: 1.0 - any, default 1.5\n\n" +

            "Position correction has to have a little extra slack for the function of the filter.\n" +
            "This value multiplies the confidence thresholds for position."
        )]
        public float posSlack { 
            set => _posSlack = Math.Max(value, 1f);
            get => _posSlack;
        }
        public float _posSlack;

        [Property("Correction Strength"), DefaultPropertyValue(0.5f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.5\n\n" +

            "Multiplies how strong correction can be for every report and cycle of the filter.\n" +
            "This can be changed to preference, if you can tell the difference."
        )]
        public float cStrength { 
            set => _cStrength = Math.Clamp(value, 0f, 1f);
            get => _cStrength;
        }
        public float _cStrength;

        [BooleanProperty("Jerk Response Increase", ""), DefaultPropertyValue(true), ToolTip
        (
            "If enabled, motion over a safe timeframe that assumes 1000Hz will be tracked and will\n" +
            "increase sensitivity when acceleration is changing at a rate far past what noise could cause."
        )]
        public bool ajResponse { 
            set => _ajResponse = value;
            get => _ajResponse;
        }
        public bool _ajResponse;

        public event Action<IDeviceReport>? Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                float reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;

                if (!init || reportTime > 25f || noiseAmount == 0f) { 
                    outputPos = report.Position;
                    usedDir = Vector2.Zero;
                    usedAccel = Vector2.Zero;
                    lastDirection = Vector2.Zero;
                    lastPos = report.Position;
                    accelProb = Vector2.Zero;
                    dirProb = Vector2.Zero;
                    positionProb = Vector2.Zero;
                    init = true;
                    Emit?.Invoke(value);
                    return;
                }

                if (ajResponse) {
                    UpdateTable(report.Position);
                    float responseUrgency = fSmoothstep(simplej[7].Length(), (noiseAmount * 2f) + 50f, (noiseAmount * 4f) + 100f);
                    conf = noiseAmount * (1.0f - 0.5f * responseUrgency);
                    cAmount = fInversion(cStrength, 1.0f + 0.5f * responseUrgency);
                }
                else {
                    conf = noiseAmount;
                    cAmount = cStrength;
                }

                currDirection = report.Position - lastPos;

                currAccel = currDirection - lastDirection;   

                usedAccel = Vector2.Lerp(usedAccel, currAccel, fInversion(0.1f, timeScale));

                accelProb += usedAccel - currAccel;

                accelProb *= fInversion(0.65f, timeScale);

                Vector2 aDiff = (Math.Max(fSmoothstep(currAccel.Length(), conf * 4f, conf * 6f), fSmoothstep(accelProb.Length(), conf * 2f, conf * 3f))) * Math.Max(Vector2.Distance(usedAccel, currAccel) - conf * 2f, 0f) * Normalize(currAccel - usedAccel);
                
                usedAccel += cAmount * aDiff;
                
                usedDir += vMult * usedAccel * fSmoothstep(usedAccel.Length(), conf * 0.25f, conf);

                usedDir = Vector2.Lerp(usedDir, currDirection, fInversion(0.15f, timeScale));

                dirProb += usedDir - currDirection;

                dirProb *= fInversion(0.65f, timeScale);

                Vector2 vDiff = Math.Max(fSmoothstep(currDirection.Length(), conf * 2f, conf * 3f), fSmoothstep(dirProb.Length(), conf, conf * 1.5f)) * Math.Max(Vector2.Distance(usedDir, currDirection) - 1f * conf, 0f) * Normalize(currDirection - usedDir);
            
                usedDir += cAmount * vDiff;
                
                outputPos += fSmoothstep(Vector2.Distance(outputPos, report.Position), 0, conf * 0.25f) * vMult * fSmoothstep(usedDir.Length(), conf * 0.05f, conf * 0.33f) * usedDir;

                outputPos = Vector2.Lerp(outputPos, report.Position, fInversion(0.1f * fSmoothstep(usedDir.Length(), conf * 5f, 0f), timeScale));

                positionProb += outputPos - report.Position;
                
                positionProb *= fInversion(0.65f, timeScale);

                Vector2 pDiff = Math.Max(fSmoothstep(Vector2.Distance(outputPos, report.Position), conf * 1f * fSmoothstep(usedDir.Length(), conf, 0f), conf * posSlack), fSmoothstep(positionProb.Length(), conf * 0.5f * posSlack, conf * posSlack)) * Math.Max(Vector2.Distance(outputPos, report.Position) - conf * 0.5f, 0f) * Normalize(report.Position - outputPos);

                outputPos += (cAmount) * pDiff;
            
                lastPos = report.Position;
                lastDirection = currDirection;

                init = true;
                if (!float.IsFinite(outputPos.X) || !float.IsFinite(outputPos.Y)) {
                    outputPos = report.Position;
                    init = false;
                }

                report.Position = outputPos;        
            }
            Emit?.Invoke(value);
        }

        Vector2 outputPos, lastPos, lastDirection, currAccel, usedAccel;
        Vector2 currDirection, usedDir;
        Vector2 positionProb, dirProb, accelProb;
        float conf, cAmount;
        bool init;

        public static float fSmoothstep(float x, float start, float end)
        {
            x = Math.Clamp((x - start) / (end - start), 0.0f, 1.0f);
            return x * x * (3.0f - 2.0f * x);
        }

        public static float fInversion(float x, float pow) {
            return 1.0f - MathF.Pow(1.0f - x, pow);
        }

        Vector2[] pos = new Vector2[force_table_history * 5];

        const int force_table_history = 8;
        Vector2[] simplev = new Vector2[force_table_history];
        Vector2[] simplea = new Vector2[force_table_history];
        Vector2[] simplej = new Vector2[force_table_history];
        public void UpdateTable(Vector2 position) {
            InsertAtFirst(pos, position);
            for (int i = 0; i < force_table_history; i++) {
                int t1 = i + 1;
                float div = (float)t1;
                simplev[i] = (pos[0] - pos[t1]) / div;
                simplea[i] = ((simplev[i]) - (pos[t1] - pos[t1 * 2])) / (div * div);
                simplej[i] = ((simplea[i]) - ((pos[t1 * 2] - pos[t1 * 3]) - (pos[t1 * 3] - pos[t1 * 4]))) / (div / div);
            }
        }

        public Vector2 Normalize(Vector2 a) => (a != Vector2.Zero) ? (a / a.Length()) : Vector2.Zero;
    }
}