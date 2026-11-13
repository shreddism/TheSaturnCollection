using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;       
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Synchronous Pressure Interp")]
    public class PressureInterp : IPositionedPipelineElement<IDeviceReport>
    {
        public PressureInterp() : base()
        {
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        [Property("Relative Rate"), DefaultPropertyValue(1.0), ToolTip(
            "Change this setting to reflect how many times the tablet reports in total for every time the pressure changes."
        )]
        public double relRate { 
            set => _relRate = Math.Max(1.0, value);
            get => _relRate;
        }
        public double _relRate = 1.0;

        [Property("Boundary"), DefaultPropertyValue(1.0), ToolTip(
            "If the rate isn't perfectly even, you might find it useful to interpolate beyond the current pressure when it takes too long.\n" +
            "This sets the maximum lerp scale between the last and current pressure."
        )]
        public double boundary { 
            set => _boundary = Math.Max(1.0, value);
            get => _boundary;
        }
        public double _boundary = 1.0;

        [Property("Difference Threshold"), DefaultPropertyValue(0.0), ToolTip(
            "The used interpolation boundary extends from 1 to its setting value based on the difference between the last and current pressure.\n" +
            "Make this setting the needed difference for the maximum lerp scale to actually be used."
        )]
        public double difference { 
            set => _difference = Math.Max(0.0, value);
            get => _difference;
        }
        public double _difference = 0.0;

        [BooleanProperty("Instant Zero", ""), DefaultPropertyValue(false), ToolTip
        (
            "Usually, when pressure goes from 0 to non-zero, the pressure instantly goes above 0,\n" +
            "but when pressure goes from non-zero to 0, the output has to progressively lerp to 0.\n" +
            "Enable this to force the output pressure to 0 when the latest input pressure is 0."
        )]
        public bool instantZero { set; get; } = false;

        public event Action<IDeviceReport>? Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                if (report.Pressure != targetPressure) {
                    savedPressure = targetPressure;
                    targetPressure = report.Pressure;
                    tick = 0.0;
                    if (difference != 0.0) usedBoundary = double.Lerp(1.0, boundary, Step(Math.Abs(savedPressure - targetPressure), 0.0, difference));
                    else usedBoundary = boundary;
                }

                tick += 1.0;    // i'll call this jaaakb's law: if prediction is not being done, the most "correct" behavior is not setting interp scale to 0 on update, but 1 / expected rate, because that extends correctly to expected rate of 1 simply making output equal to input

                if (!(report.Pressure == 0 && instantZero)) {
                    outputPressure = (uint)double.Lerp(savedPressure, targetPressure, Math.Min(tick / relRate, usedBoundary));
                    if (outputPressure >= maxPressure) {
                        outputPressure = maxPressure;
                        targetPressure = maxPressure;
                        savedPressure = maxPressure;
                        usedBoundary = 1.0;
                    }
                }
                else {
                    outputPressure = 0;
                }

                report.Pressure = outputPressure;
            }
            Emit.Invoke(value);
        }

        uint targetPressure = 0;
        uint savedPressure = 0;
        uint maxPressure = 0;
        uint outputPressure = 0;
        double tick = 0.0;
        double usedBoundary = 1.0;

        [TabletReference]
        public TabletReference Tablet 
        {
            set 
            {
                maxPressure = value.Properties.Specifications.Pen.MaxPressure;
            }
        }
    }
}