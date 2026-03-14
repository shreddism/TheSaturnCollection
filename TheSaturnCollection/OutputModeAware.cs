using System;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace Saturn;
public abstract class OutputModeAware : IPositionedPipelineElement<IDeviceReport>
{ 
    public OutputModeType GetOutputMode() {
        TryResolveOutputMode();
        return outputModeType;
    }

    [Resolved]
    public IDriver? driver;
    private OutputModeType outputModeType;
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
                outputModeType = OutputModeType.absolute;
                return;
            }
            if (output is RelativeOutputMode relOutput) {
                relativeOutputMode = relOutput;
                outputModeType = OutputModeType.relative;
                return;
            }
            outputModeType = OutputModeType.unknown;
        }
    }

    public abstract event Action<IDeviceReport> Emit;
    public abstract void Consume(IDeviceReport value);
    public abstract PipelinePosition Position { get; }
}

public enum OutputModeType {
    absolute,
    relative,
    unknown
}