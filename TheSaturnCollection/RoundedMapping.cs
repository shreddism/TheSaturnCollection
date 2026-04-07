using System;
using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Area Rounding")]
    public class AreaRounding : OutputModeAware
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

        [Property("Radius"), DefaultPropertyValue(720.0f), ToolTip
        (
            ""
        )]
        public float threshold
        {
            set => _threshold = Math.Max(0.0f, value);
            get => _threshold;
        }
        public float _threshold;

        [Property("power"), DefaultPropertyValue(1.0f), ToolTip
        (
            ""
        )]
        public float dPower
        {
            set => _dPower = Math.Max(0.0f, value);
            get => _dPower;
        }
        public float _dPower;

        [BooleanProperty("Alt/Blend Mode", ""), DefaultPropertyValue(true), ToolTip
        (
            "Uses a different curve inside the radius that is less harsh. Recommended."
        )]
        public bool mode { set; get; }

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                outputMode = GetOutputMode();

                if (outputMode.Type == OutputType.absolute) {
                    displayCenter = GetDisplayCenter();
                    displayArea = GetDisplayArea();
                    edgeLengths = displayArea * 0.5f;
                    Vector2 dist = report.Position - displayCenter;
                    float ratio = dist.Length() / threshold;
                    if (!mode || ratio >= 1.0f) ratio = MathF.Pow(ratio, dPower);
                    else ratio = float.Lerp(1.0f - MathF.Pow(1.0f - ratio, (1.0f / dPower)), MathF.Pow(ratio, dPower), MathF.Min(ratio * MathF.Min(dPower, 1.0f), 1.0f));
                    Vector2 output = (Default(Vector2.Normalize(dist), Vector2.Zero) * ratio * threshold) + displayCenter;
                    report.Position = Vector2.Clamp(output, displayCenter - edgeLengths, displayCenter + edgeLengths);
                }
            }

            Emit?.Invoke(value);
        }
        OutputMode outputMode;
        Vector2 displayCenter, displayArea, edgeLengths;
    }
}