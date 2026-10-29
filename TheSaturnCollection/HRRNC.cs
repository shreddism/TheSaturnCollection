using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;  
using OpenTabletDriver.Plugin;
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

        [Property("Noise Amount (Warning: Hover Over the Textbox and Read This Tooltip)"), DefaultPropertyValue(5), ToolTip
        (
            "This probably should be the first that your input passes through, unless you know what you are doing. However, this may not be the case.\n" +
            "To make sure, save your OTD settings as a file, edit the file so that this filter is at the top, then load the settings file and save.\n\n" +

            "This filter is primarily designed for high report-rate tablets which could use a little noise filtering.\n" +
            "It attempts to target report-to-report signal noise, even under motion.\n" +

            "Possible range: 0.0 - any, default 5.0\n\n" +

            "Most of the filter's internal behavior is dictated by this value. It can consider deviation within this amount negligible unless it accumulates.\n" +
            "It's fairly likely that you will not need to touch any other setting."
        )]
        public double noiseAmount { 
            set => _noiseAmount = Math.Max(value, 0);
            get => _noiseAmount;
        }
        public double _noiseAmount;

        [Property("Position Slack"), DefaultPropertyValue(1.25), ToolTip
        (
            "Possible range: 1.0 - any, default 1.25\n\n" +

            "Position correction has to have a little extra slack for the function of the filter.\n" +
            "This value multiplies the confidence thresholds for position."
        )]
        public double posSlack { 
            set => _posSlack = Math.Max(value, 1);
            get => _posSlack;
        }
        public double _posSlack;

        [Property("Correction Strength"), DefaultPropertyValue(0.5), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.5\n\n" +

            "Multiplies how strong correction can be for every report and cycle of the filter.\n" +
            "This can be changed to preference, if you can tell the difference."
        )]
        public double cStrength { 
            set => _cStrength = Math.Clamp(value, 0, 1);
            get => _cStrength;
        }
        public double _cStrength;

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

        [BooleanProperty("Chained Correction", ""), DefaultPropertyValue(true), ToolTip
        (
            "If enabled, 1/2 of position's correction will be added to expected velocity, and 1/4 to expected acceleration.\n" +
            "This should increase accuracy in most cases, but will not function well if there are outliers in input, like skips or drops."
        )]
        public bool cChain { 
            set => _cChain = value;
            get => _cChain;
        }
        public bool _cChain;

        [Property("Timing Variance Severity"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Sometimes, the magnitude of position change may vary much more than anything else.\n" +
            "This can happen if pressure is turned on in some tablets, like a PTK-470.\n" +
            "If you don't know whether this occurs, do not change this from 0.\n" +
            "If you do know, first a value ranging from 1 to 3."
        )]
        public double TimingSeverity { 
            set => _TimingSeverity = Math.Max(0.0, value);
            get => _TimingSeverity;
        }
        public double _TimingSeverity;

        [Property("Cross Pattern Severity"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "In rare cases, noise may appear in a cross pattern.\n" +
            "Raising this will internally divide any deviation that is on the axes.\n" +
            "If you know this happens, increase your noise amount according the maximum reached on the edges,\n" +
            "then raise this so that your maximum diagonal noise is equal to (noise / (1 + this setting))."
        )]
        public double crossSeverity { 
            set => _crossSeverity = Math.Max(0.0, value);
            get => _crossSeverity;
        }
        public double _crossSeverity;

        [Property("Accel/Velocity Multiplier"), DefaultPropertyValue(1), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 1.0\n\n" +

            "This filter should not overshoot, but if it does, this should be lowered very slightly.\n" +
            "In any normal case, this should not go below 1."
        )]
        public double vMult { 
            set => _vMult = Math.Clamp(value, 0, 1);
            get => _vMult;
        }
        public double _vMult;

        [Property("Time Scale"), DefaultPropertyValue(1), ToolTip
        (
            "Possible range: 0.0 - any, default 1.0\n\n" +

            "There are baseline weights that expect a certain scale of time. Change this as you see fit."
        )]
        public double timeScale { 
            set => _timeScale = Math.Max(value, 0);
            get => _timeScale;
        }
        public double _timeScale;

        public const string HOVER_TOOLTIP =
            "If the hover settings are non-default and non-equal, the noise amount value\n" +
            "will be multiplied from 1 to the maximum multiplier value, going from\n" +
            "the dragging distance to the maximum distance.\n" +
            "You can look at these values in the Tablet Debugger."
        ;

        [Property("Dragging Hover Distance"), DefaultPropertyValue(0.0), ToolTip
        (
            HOVER_TOOLTIP
        )]
        public double dragDist { 
            set => _dragDist = Math.Max(value, 0);
            get => _dragDist;
        }
        public double _dragDist;

        [Property("Maximum Hover Distance"), DefaultPropertyValue(0.0), ToolTip
        (
            HOVER_TOOLTIP
        )]
        public double maxDist { 
            set => _maxDist = Math.Max(value, 0);
            get => _maxDist;
        }
        public double _maxDist;

        [Property("Maximum Hover Multiplier"), DefaultPropertyValue(1.0), ToolTip
        (
            HOVER_TOOLTIP
        )]
        public double maxHMult { 
            set => _maxHMult = Math.Max(value, 1);
            get => _maxHMult;
        }
        public double _maxHMult;

        [Property("Dragging Confidence"), DefaultPropertyValue(1.0), ToolTip
        (
            "Sometimes, tablets can end up not reporting a changed hover distance\n" +
            "until the pen is lifted by a large amount. This divides the noise amount value specifically\n" +
            "when pressure is above 0. This applies even when not adjusting for hover."
        )]
        public double dragConf { 
            set => _dragConf = Math.Max(value, 1);
            get => _dragConf;
        }
        public double _dragConf;

        public event Action<IDeviceReport>? Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is not ITabletReport report) { 
                Emit?.Invoke(value);
                return;
            }

            if (value is IProximityReport proximityReport && (dragDist + maxDist > 0.0) && (maxHMult != 1.0) && (dragDist != maxDist)) {
                hoverAdjust = true;
                hoverMultiplier = 1.0 + (maxHMult - 1.0) * (Step((double)proximityReport.HoverDistance, dragDist, maxDist));
            }
            else {
                hoverAdjust = false;
            }
            
            double reportTime = (double)reportStopwatch.Restart().TotalMilliseconds;

            if (tick < long.MaxValue) tick++;

            if (reportTime > 25) tick = 0;

            if (tick < 5 || noiseAmount == 0) { 
                adaptiveStretch = 0;
                moveAngle = 0;
                lastMoveAngle = 0;
                currPos = new Double2(report.Position);
                lastPos = currPos;
                outputPos = currPos;
                currAccel = Double2.Zero;
                currDirection = Double2.Zero;
                usedAccel = Double2.Zero;
                usedDir = Double2.Zero;
                lastDirection = Double2.Zero;
                Emit?.Invoke(value);
                return;
            }

            currPos = new Double2(report.Position);

            if (ajResponse) {
                UpdateTable(currPos);
                double responseUrgency = Smoothstep(simplej[7].Length(), (noiseAmount * 2) + 25, (noiseAmount * 4) + 50);
                conf = noiseAmount * (1.0 - 0.5 * responseUrgency);
                cAmount = UAdjust(cStrength, 1.0 + 0.5 * responseUrgency);
            }
            else {
                conf = noiseAmount;
                cAmount = cStrength;
            }

            if (hoverAdjust) {
                conf *= hoverMultiplier;
            }

            if ((dragConf > 1.0)) {
                if (report.Pressure > 0) {
                    conf /= dragConf;
                }
            }

            if (TimingSeverity > 0.0) {
                adaptiveStretch = Math.Max(((TimingSeverity * usedDir.Length()) / (2 * conf)) - 1, 0);
            }
            else {
                adaptiveStretch = 0.0;
            }

            veldyn = (TimingSeverity > 0.0) && (moveAngle != 0.0) && (adaptiveStretch != 0.0);

            currDirection = (currPos - lastPos);

            currAccel = currDirection - lastDirection;            

            usedAccel = Double2.Lerp(usedAccel, currAccel, UAdjust(0.1, timeScale));

            Double2 ahold = PatternAdjust(usedAccel - currAccel);

            accelProb += ahold;

            accelProb *= 0.6;

            Double2 aDiff = (Math.Max(Smoothstep(ahold.Length(), conf * 4, conf * 8), Smoothstep(accelProb.Length(), conf * 1.5, conf * 5))) * Math.Max(ahold.Length() - conf * 2, 0) * (currAccel - usedAccel).Normalize();
            
            usedAccel += cAmount * aDiff;
            
            usedDir += vMult * usedAccel * Smoothstep(usedAccel.Length(), conf * 0.25, conf);

            usedDir = Double2.Lerp(usedDir, currDirection, UAdjust(0.1, timeScale));

            Double2 dhold = PatternAdjust(usedDir - currDirection);

            dirProb += dhold;

            dirProb *= 0.65;

            Double2 vDiff = Math.Max(Smoothstep(currDirection.Length(), conf * 2, conf * 4), Smoothstep(dirProb.Length(), conf * 0.75, conf * 2.5)) * Math.Max(dhold.Length() - 1 * conf, 0) * (currDirection - usedDir).Normalize();
        
            usedDir += cAmount * vDiff;

            outputPos += Smoothstep(Double2.Distance(outputPos, currPos), 0, conf * 0.25) * vMult * Smoothstep(usedDir.Length(), conf * 0.05, conf * 0.33) * usedDir;

            outputPos = Double2.Lerp(outputPos, currPos, UAdjust(0.05 * Smoothstep(usedDir.Length(), conf * 5, 0), timeScale));
            
            Double2 phold = PatternAdjust(outputPos - currPos);

            positionProb += phold;
            
            positionProb *= 0.7;

            Double2 pDiff = Math.Max(Smoothstep(phold.Length(), conf * 1 * Smoothstep(usedDir.Length(), conf, 0), conf * 2 * posSlack), Smoothstep(positionProb.Length(), conf * 0.25 * posSlack, conf * posSlack + 1.0)) * Math.Max(Double2.Distance(outputPos, currPos) - conf * 0.5, 0) * (currPos - outputPos).Normalize();

            outputPos += (cAmount) * pDiff;

            if (cChain){
                usedAccel += (cAmount * 0.25) * pDiff;
                usedDir += (cAmount * 0.5) * pDiff;
            }

            lastPos = currPos;
            lastDirection = currDirection;

            if (TimingSeverity > 0.0 && tick > 10 && usedDir.Length() > 5) {
                    if (lastMoveAngle != 0) {
                        moveAngle = 0.5 * moveAngle + 0.5 * usedDir.Angle();
                        crossForce = 0.9 * crossForce + 0.1 * (moveAngle - lastMoveAngle);
                        if (Math.Abs(crossForce) > 0.005) {
                            moveAngle += crossForce;

                        }
                    }
                    else {
                        moveAngle = usedDir.Angle();
                    }
                    lastMoveAngle = moveAngle;
            }
            else {
                moveAngle = 0;
                lastMoveAngle = 0;
                crossForce = 0;
            }

            if (!outputPos.IsFinite()) {
                outputPos = new Double2(report.Position);
                tick = 0;
                Log.Write("HRRNC", "A position value is not finite. Setting all filter outputs to raw position", LogLevel.Error, false, false);
            } 

            report.Position = outputPos.AsVector2();     
            
            Emit?.Invoke(value);
        }

        public Double2 PatternAdjust(Double2 i) 
        {
            Double2 output = i;

            if (crossSeverity > 0.0) {
                double axisness = Math.Cos(2.0 * output.Angle());
                axisness *= axisness;

                if (crossSeverity > 1.0)
                    axisness = Math.Pow(axisness, crossSeverity);
                
                axisness *= crossSeverity;
                output /= 1.0 + axisness;
            }

            if (veldyn) {
                output = Double2.Rotate(output, -moveAngle);
                output.X /= 1.0 + adaptiveStretch;
                output = Double2.Rotate(output, moveAngle);
            }

            return output;
        }

        Double2 outputPos, lastPos, lastDirection, currAccel, usedAccel;
        Double2 currDirection, usedDir;
        Double2 positionProb, dirProb, accelProb;
        Double2 currPos;
        double conf, cAmount;
        long tick;
        double crossForce;
        double moveAngle = 0;
        double lastMoveAngle = 0;
        double adaptiveStretch = 0.0;
        double hoverMultiplier = 1.0;
        bool veldyn;
        bool hoverAdjust;

        Double2[] pos = new Double2[33];
        const int force_table_history = 8;
        Double2[] simplev = new Double2[force_table_history];
        Double2[] simplea = new Double2[force_table_history];
        Double2[] simplej = new Double2[force_table_history];
        public void UpdateTable(Double2 position) {
            InsertAtFirst(pos, position);
            for (int i = 0; i < force_table_history; i++) {
                int t1 = i + 1;
                double div = (double)t1;
                simplev[i] = (pos[0] - pos[t1]);
                simplea[i] = ((simplev[i]) - (pos[t1] - pos[t1 * 2]));
                simplej[i] = ((simplea[i]) - ((pos[t1 * 2] - pos[t1 * 3]) - (pos[t1 * 3] - pos[t1 * 4])));
            }
        }
    }
}