using System;
using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Post-Processing")]
    public class PostProcessing : OutputModeAware
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

        [Property("Number"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Important: This should probably be the last Post-Transform plugin applied.\n" +
            "If you are planning to use Circular Area and this at the same time,\n" +
            "check console output to make sure this is the last Post-Transform plugin.\n" +
            "If it isn't, or you are unsure, see the instructions on the wiki.\n" +

            "Possible range: 0.0 - any, default 0.0\n" +
            "post transform absolute to relative"
        )]
        public float reset
        {
            set => _reset = Math.Max(0.0f, value);
            get => _reset;
        }
        public float _reset;

        [Property("Resolution Scale (Hover Over The Textbox Before Enabling For The First Time)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 1.0 - any, default 1.0\n" +
            "This filter truncates the pixel position of the cursor.\n" +
            "This multiplies the position by its value before truncating it, increasing resolution.\n" +
            "If set to 1.0 the cursor is set to whole pixels, if set to 2.0 the cursor is set to quadrants, and so on."
        )]
        public float gridMult
        {
            set => _gridMult = Math.Max(1.0f, value);
            get => _gridMult;
        }
        public float _gridMult;

        [BooleanProperty("Dynamic Mode", ""), DefaultPropertyValue(true), ToolTip
        (
            "The cursor won't move if the input position has not changed by one scaled pixel since the last move."
        )]
        public bool dynamicMode { set; get; }

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                HandleOutputMode(report.Position);

                if (!initFlag) initFlag = true;

                pxOutput = pos[0];
                
                pxOutput *= gridMult;
                pxOutput = new Vector2(MathF.Floor(pos[0].X), MathF.Floor(pos[0].Y));
                pxOutput /= gridMult;
                
                if ((!dynamicMode) || ((Vector2.Distance(checkPos, pos[0]) + (dir[0] + dir[1] + dir[2] + dir[3]).Length() >= 1 / gridMult) && (pxOutput != outputPos[0]))) {
                    checkPos = pos[0];
                    InsertAtFirst(outputPos, pxOutput);
                }
                else InsertAtFirst(outputPos, outputPos[0]);

                if (relativeFlag) {
                    report.Position = outputPos[0] - outputPos[1];
                }
                else report.Position = outputPos[0];

            }
            Emit?.Invoke(value);
        }

        void HandleOutputMode(Vector2 input) {
            float reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
            OutputModeType outputMode = GetOutputMode();
            if (outputMode == OutputModeType.absolute) {
                if (reset == 0f) {
                InsertAtFirst(pos, input);
                InsertAtFirst(dir, pos[0] - pos[1]);
                }
                else {
                    InsertAtFirst(dir, input - fRelPoint);
                    if (reportTime < reset || !initFlag) {
                        InsertAtFirst(pos, pos[0] + dir[0]);
                    }
                    else {
                        NonInsertAtFirst(pos);
                    }
                    fRelPoint = input;
                }
            }
            else if (outputMode == OutputModeType.relative) {
                InsertAtFirst(dir, input);
                InsertAtFirst(pos, pos[0] + dir[0]);
                relativeFlag = true;
            }
        }

        const int HMAX = 4;

        Vector2[] pos = new Vector2[HMAX];
        Vector2[] dir = new Vector2[HMAX];
        Vector2[] outputPos = new Vector2[HMAX];

        Vector2 fRelPoint;
        
        Vector2 checkPos;

        Vector2 pxOutput;

        bool relativeFlag = false;
        bool initFlag = false;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}