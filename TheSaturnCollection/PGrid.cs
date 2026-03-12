using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;       

namespace Saturn
{
    [PluginName("Saturn - Pixel Grid")]
    public class PixelGrid : IPositionedPipelineElement<IDeviceReport>
    {
        public PixelGrid() : base()
        {
        }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        [Property("Resolution Scale (Hover Over The Textbox Before Enabling For The First Time)"), DefaultPropertyValue(1f), ToolTip
        (
            "Important: Due to the internals of how filters work, this does not work on relative mode.\n" +
            "If you are planning to use Circular Area and this at the same time,\n" +
            "check console output to make sure this is the last Post-Transform plugin.\n" +
            "If it isn't, or you are unsure, see the instructions on the wiki.\n" +

            "Possible range: 1 - any, default 1\n" +
            "This filter truncates the pixel position of the cursor.\n" +
            "This multiplies the position by an integer value before truncating it, increasing resolution.\n" +
            "If set to 1 the cursor is set to whole pixels, if set to 2 the cursor is set to quadrants, and so on.\n" +
            "This setting is set to its ceiling, making it an integer internally."

        )]
        public float gridMult
        {
            set => _gridMult = MathF.Ceiling(Math.Max(1, value));
            get => _gridMult;
        }
        public float _gridMult;

        public event Action<IDeviceReport>? Emit;

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                report.Position *= gridMult;
                report.Position = new Vector2(MathF.Truncate(report.Position.X), MathF.Truncate(report.Position.Y));
                report.Position /= gridMult;
            }
            Emit?.Invoke(value);
        }

    }
}