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
    [PluginName("Saturn - Area Rounding (Distance From Center)")]
    public class AreaRoundingDFC : OutputModeAware
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

        public const string DFC_TOOLTIP =            
            "The cursor's distance from the center is divided by the axis values then divided by the radius value.\n" +
            "If over 1, that value is simply raised to the power value.\n" +
            "If under 1, a blended curve is used to avoid extremeness.\n" +
            "That value is then multiplied back by the radius value,\n" +
            "and the output's axes are multiplied by their respective values.\n\n";

        [Property("Radius"), DefaultPropertyValue(720.0f), ToolTip(
            DFC_TOOLTIP +
            "Possible range: 0.0 - any, default 720.0\n\n" +
            "A smaller radius generally amplifies the effect of the power value.\n" +
            "This controls where the output position would be the same as the input position."
        )]
        public float threshold
        {
            set => _threshold = Math.Max(0.0f, value);
            get => _threshold;
        }
        public float _threshold;

        [Property("Power"), DefaultPropertyValue(1.0f), ToolTip(
            DFC_TOOLTIP +
            "Possible range: 0.0 - any, default 1.0\n\n" +
            "This decides the behavior of the filter.\n" +
            "If above 1, it amplifies movement outside the radius and diminishes movement inside of it,\n" +
            "rounding the tablet area out.\n" + 
            "If below 1, it diminishes movement outside the radius and amplifies movement inside of it,\n" +
            "rounding the display area out.\n" +
            "Useful values probably won't be beyond the range of 0.5 - 2.0."
            )]
        public float dPower
        {
            set => _dPower = Math.Max(0.0f, value);
            get => _dPower;
        }
        public float _dPower;

        const string AXIS_TOOLTIP = 
            "Dividing the input on an axis by a value is different from multiplying the output by the inverse value.\n" +
            "If the values are the same, it effectively becomes a multiplier for the radius value on that axis.";

        [Property("Horizontal Input Divisor"), DefaultPropertyValue(1.0f), ToolTip(AXIS_TOOLTIP)]
        public float xDiv
        {
            set => _xDiv = Math.Max(0.0f, value);
            get => _xDiv;
        }
        public float _xDiv;

        [Property("Vertical Input Divisor"), DefaultPropertyValue(1.0f), ToolTip(AXIS_TOOLTIP)]
        public float yDiv
        {
            set => _yDiv = Math.Max(0.0f, value);
            get => _yDiv;
        }
        public float _yDiv;

        [Property("Horizontal Output Multiplier"), DefaultPropertyValue(1.0f), ToolTip(AXIS_TOOLTIP)]
        public float xMul
        {
            set => _xMul = Math.Max(0.0f, value);
            get => _xMul;
        }
        public float _xMul;

        [Property("Vertical Output Multiplier"), DefaultPropertyValue(1.0f), ToolTip(AXIS_TOOLTIP)]
        public float yMul
        {
            set => _yMul = Math.Max(0.0f, value);
            get => _yMul;
        }
        public float _yMul;

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report) {
                outputMode = GetOutputMode();
                if (outputMode.Type == OutputType.absolute) {
                    displayCenter = GetDisplayCenter();
                    displayArea = GetDisplayArea();
                    edgeLengths = displayArea * 0.5f;
                    Vector2 dist = report.Position - displayCenter;
                    dist.X /= xDiv;
                    dist.Y /= yDiv;
                    float ratio = dist.Length() / threshold;

                    if (ratio >= 1.0f)
                        ratio = MathF.Pow(ratio, dPower);
                    else
                        ratio = float.Lerp(1.0f - MathF.Pow(1.0f - ratio, (1.0f / dPower)), MathF.Pow(ratio, dPower), MathF.Min(ratio * MathF.Min(dPower, 1.0f), 1.0f));

                    Vector2 output = (Default(Vector2.Normalize(dist), Vector2.Zero) * ratio * threshold);
                    output.X *= xMul;
                    output.Y *= yMul;
                    output += displayCenter;
                    output = Vector2.Clamp(output, displayCenter - edgeLengths, displayCenter + edgeLengths);
                    report.Position = output;
                }
            }
            Emit?.Invoke(value);
        }

        OutputMode outputMode;
        Vector2 displayCenter, displayArea, edgeLengths;
    }
}