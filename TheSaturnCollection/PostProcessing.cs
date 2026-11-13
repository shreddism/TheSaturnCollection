using System;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using OpenTabletDriver.Plugin.DependencyInjection;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Post-Processing")]
    public class PostProcessing : OutputModeAware
    {
        public PostProcessing() : base()
        {        
        }

        [Property("Lock Mode"), DefaultPropertyValue("Distance - Radial"), PropertyValidated(nameof(lockModes)), ToolTip
        (
            "Decides behavior of locked position.\n" +
            "Radial is similar to Radial Follow, Instant is similar to Chatter Exterminator.\n" +
            "Distance works off comparing points, while Axis separates the axes."
        )]
        public string lockMode { get; set; } = "Distance";
        public static IEnumerable<string> lockModes { get; set; } = new List<string> { "Distance - Radial", "Axis - Radial", "Distance - Instant", "Axis - Instant" };
    
        [Property("Lock Threshold"), DefaultPropertyValue(0.0), Unit("px")]
        public double lockThreshold { 
            set => _lockThreshold = Math.Max(0.0, value);
            get => _lockThreshold;
        }
        public double _lockThreshold = 0.0;

        [BooleanProperty("Lock Compensation", ""), DefaultPropertyValue(false), ToolTip
        (
            "Attempts to compensate for the area change that is brought on by locking.\n" +
            "On the axis radial setting, the area is usually effectively shrunk at the borders by the threshold value.\n" +
            "This multiplies input so that on that setting the input's range is brought back. Extends area on other settings."
        )]
        public bool compensation { set; get; } = false;

        [Property("Position Rounding"), DefaultPropertyValue(0.0), Unit("px"), ToolTip(
            "Rounds the position to the nearest value set."
        )]
        public double rounding { 
            set => _rounding = Math.Max(0.0, value);
            get => _rounding;
        }
        public double _rounding = 0.0;

        public override PipelinePosition Position => PipelinePosition.PostTransform;

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value) 
        {
            if (value is ITabletReport report) {
                outputMode = GetOutputMode();
                if (outputMode.Type == OutputType.absolute) {
                    currPosition = new Double2(report.Position);
                    displayCenter = new Double2(GetDisplayCenter());
                    displayArea = new Double2(GetDisplayArea());
                    minPoint = displayCenter - (displayArea * 0.5);
                    maxPoint = displayCenter + (displayArea * 0.5);
                    
                    if (lockThreshold != 0.0) {
                        switch (lockCode) {
                            case 1:
                                Double2 diff = (currPosition - lockPosition);
                                double length = diff.Length();
                                if (length > lockThreshold) {
                                    lockPosition += (length - lockThreshold) * diff.Normalize();
                                }
                            break;
                            case 2:
                                double dx = currPosition.X - lockPosition.X;
                                double dy = currPosition.Y - lockPosition.Y;
                                lockPosition.X += double.Sign(dx) * Math.Max(Math.Abs(dx) - lockThreshold, 0);
                                lockPosition.Y += double.Sign(dy) * Math.Max(Math.Abs(dy) - lockThreshold, 0);
                            break;
                            case 3:
                                if (Double2.Distance(currPosition, lockPosition) > lockThreshold) 
                                    lockPosition = currPosition;        
                            break;
                            case 4:
                                if (Math.Abs(currPosition.X - lockPosition.X) > lockThreshold)
                                    lockPosition.X = currPosition.X;
                                if (Math.Abs(currPosition.Y - lockPosition.Y) > lockThreshold)
                                    lockPosition.Y = currPosition.Y;
                            break;
                            default:
                            break;
                        }
                    }
                    else lockPosition = currPosition;

                    if (compensation) {
                        compPosition = lockPosition - displayCenter;
                        compPosition.X *= (displayCenter.X) / (displayCenter.X - lockThreshold);
                        compPosition.Y *= (displayCenter.Y) / (displayCenter.Y - lockThreshold);
                        compPosition += displayCenter;
                    }
                    else compPosition = lockPosition;

                    if (rounding != 0.0) {
                        outputPosition = Double2.Round(compPosition / rounding, 1) * rounding;
                    }
                    else outputPosition = compPosition;

                    outputPosition = Double2.Clamp(outputPosition, minPoint, maxPoint);

                    report.Position = outputPosition.AsVector2();
                }
            }
            Emit.Invoke(value);
        }

        Double2 currPosition, lockPosition, compPosition, outputPosition;
        int lockCode = 1;

        Double2 displayCenter, displayArea;

        Double2 minPoint, maxPoint;

        OutputMode outputMode;

        [OnDependencyLoad]
        public void Initialize()
        {
            lockCode = lockMode switch {
                "Axis - Radial" => 2,
                "Distance - Instant" => 3,
                "Axis - Instant" => 4,
                _ => 1
            };
        }
    }
}