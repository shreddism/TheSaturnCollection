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
    [PluginName("Saturn - Rounded Mapping")]
    public class RoundedMapping : OutputModeAware
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

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

        [Property("threshold"), DefaultPropertyValue(720.0f), ToolTip
        (
            ""
        )]
        public float threshold
        {
            set => _threshold = Math.Max(0.0f, value);
            get => _threshold;
        }
        public float _threshold;

        [BooleanProperty("Alternate Calculation", ""), DefaultPropertyValue(true), ToolTip
        (
            "Uses a different, gentler curve inside the radius. Recommended."
        )]
        public bool mode { set; get; }

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                HandleOutputMode(report.Position);

                if (!relativeFlag) {
                    Vector2 dist = pos[0] - displayCenter;
                    float ratio = dist.Length() / threshold;
                    if (!mode || ratio >= 1.0f) ratio = MathF.Pow(ratio, dPower);
                    else ratio = float.Lerp(1.0f - MathF.Pow(1.0f - ratio, (1.0f / dPower)), MathF.Pow(ratio, dPower), MathF.Min(ratio, 1.0f));
                    Vector2 output = (Default(Vector2.Normalize(dist), Vector2.Zero) * ratio * threshold) + displayCenter;
                    report.Position = output;
                    Console.WriteLine(output);
                }
            }
            Emit?.Invoke(value);
        }

        void HandleOutputMode(Vector2 input) {

            if (!initFlag) {
                outputMode = GetOutputMode();
                
                initFlag = true;
            }
            displayCenter = GetDisplayCenter();
            displayArea = GetDisplayArea();
            edgeLengths = displayArea * 0.5f;

            if (outputMode.Type == OutputType.absolute) {
                InsertAtFirst(pos, input);
            }
            else if (outputMode.Type == OutputType.relative) {
                relativeFlag = true;
            } 
        }

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] outputPos = new Vector2[HMAX];

        OutputMode outputMode;

        Vector2 displayCenter;
        Vector2 displayArea;
        Vector2 edgeLengths;

        bool relativeFlag = false;
        bool initFlag = false;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}