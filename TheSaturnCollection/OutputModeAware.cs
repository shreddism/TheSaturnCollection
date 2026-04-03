using System;
using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace Saturn;
public abstract class OutputModeAware : IPositionedPipelineElement<IDeviceReport>
{ 
    public OutputMode GetOutputMode() {
        TryResolveOutputMode();
        return outputMode;
    }

    public Vector2 GetAreaScalingFactor() {
        if (outputMode.Type == OutputType.absolute && absoluteOutputMode != null) {
            float digitizerWidth = absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.Width;
            float digitizerHeight = absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.Height;
            float digitizerMaxX = absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.MaxX;
            float digitizerMaxY = absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.MaxY;
            Vector2 lpmm = new Vector2(digitizerMaxX / digitizerWidth, digitizerMaxY / digitizerHeight);

            float areaWidth = absoluteOutputMode.Input.Width;
            float areaHeight = absoluteOutputMode.Input.Height;
            float monitorWidth = absoluteOutputMode.Output.Width;
            float monitorHeight = absoluteOutputMode.Output.Height;
            Vector2 pxpmm = new Vector2(monitorWidth / areaWidth, monitorHeight / areaHeight);

            return new Vector2(lpmm.X / pxpmm.X, lpmm.Y / pxpmm.Y);
        }

        if (outputMode.Type == OutputType.relative && relativeOutputMode != null) {
            float digitizerWidth = relativeOutputMode.Tablet.Properties.Specifications.Digitizer.Width;
            float digitizerHeight = relativeOutputMode.Tablet.Properties.Specifications.Digitizer.Height;
            float digitizerMaxX = relativeOutputMode.Tablet.Properties.Specifications.Digitizer.MaxX;
            float digitizerMaxY = relativeOutputMode.Tablet.Properties.Specifications.Digitizer.MaxY;
            Vector2 lpmm = new Vector2(digitizerMaxX / digitizerWidth, digitizerMaxY / digitizerHeight);

            float sensX = relativeOutputMode.Sensitivity.X;
            float sensY = relativeOutputMode.Sensitivity.Y;
            Vector2 pxpmm = new Vector2(sensX, sensX);

            return new Vector2(lpmm.X / pxpmm.X, lpmm.Y / pxpmm.Y);
        }

        TryResolveOutputMode();
        return default;
    }

    public Vector2 GetMaxCoords() {
        if (outputMode.Type == OutputType.absolute && absoluteOutputMode != null) {
            return new Vector2(absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.MaxX, absoluteOutputMode.Tablet.Properties.Specifications.Digitizer.MaxY);
        }

        if (outputMode.Type == OutputType.relative && relativeOutputMode != null) {
            return new Vector2(relativeOutputMode.Tablet.Properties.Specifications.Digitizer.MaxX, relativeOutputMode.Tablet.Properties.Specifications.Digitizer.MaxY);
        }

        TryResolveOutputMode();
        return default;
    }

    public Vector2 GetDisplayArea() {
        if (outputMode.Type == OutputType.absolute && absoluteOutputMode != null) {
            return new Vector2(absoluteOutputMode.Output.Width, absoluteOutputMode.Output.Height);
        }

        TryResolveOutputMode();
        return default;
    }

    public Vector2 GetDisplayCenter() {
        if (outputMode.Type == OutputType.absolute && absoluteOutputMode != null) {
            return absoluteOutputMode.Output.Position;
        }

        TryResolveOutputMode();
        return default;
    }

    [Resolved]
    public IDriver? driver;
    private OutputMode outputMode;
    private AbsoluteOutputMode? absoluteOutputMode;
    private RelativeOutputMode? relativeOutputMode;
    private void TryResolveOutputMode()
    {
        if (driver is Driver drv)
        {
            IOutputMode? output = drv.InputDevices
                .Where(dev => dev?.OutputMode?.Elements?.Contains(this) ?? false)
                .Select(dev => dev?.OutputMode).FirstOrDefault();

            if (output is AbsoluteOutputMode absOutput) {
                absoluteOutputMode = absOutput;
                outputMode.Type = OutputType.absolute;
                return;
            }
            if (output is RelativeOutputMode relOutput) {
                relativeOutputMode = relOutput;
                outputMode.Type = OutputType.relative;
                return;
            }
            outputMode.Type = OutputType.unknown;
        }
    }

    public abstract event Action<IDeviceReport> Emit;
    public abstract void Consume(IDeviceReport value);
    public abstract PipelinePosition Position { get; }
}

public enum OutputType {
    absolute,
    relative,
    unknown
}

public struct OutputMode {
    public OutputType Type;
}