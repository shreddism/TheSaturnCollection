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
    [PluginName("Saturn - Post-Processing")]
    public class PostProcessing : OutputModeAware
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

        [Property("Reset Time"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Important: This should probably be the last Post-Transform plugin applied.\n" +
            "If you are planning to use Circular Area and this at the same time,\n" +
            "check console output to make sure this is the last Post-Transform plugin.\n" +
            "If it isn't, or you are unsure, see the instructions on the wiki.\n" +

            "Possible range: 0.001 - any, default 0.0\n" +
            "Sets resetTime time for post-processing output mode converted from absolute.\n" +
            "If on relative mode, there is no effect."
        )]
        public float resetTime
        {
            set => _resetTime = Math.Max(0.0f, value);
            get => _resetTime;
        }
        public float _resetTime;

        [Property("Deadzone Time"), DefaultPropertyValue(500f), ToolTip
        (
            "Nothing will register for this many milliseconds after resetting."
        )]
        public float deadzoneTime
        {
            set => _deadzoneTime = Math.Max(value, 0f);
            get => _deadzoneTime;
        }
        public float _deadzoneTime;

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

        [BooleanProperty("Dynamic Mode", ""), DefaultPropertyValue(false), ToolTip
        (
            "The cursor won't move if the input position has not changed by one scaled pixel since the last move."
        )]
        public bool dynamicMode { set; get; }

        public override event Action<IDeviceReport>? Emit;

        public override void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report)
            {
                reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;

                if (dTimeRemaining == 0) resetFlag = false;

                if (reportTime > resetTime) {
                    dTimeRemaining = deadzoneTime;
                    resetFlag = true;
                }
                else dTimeRemaining = Math.Max(0, dTimeRemaining - reportTime);
                
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
            OutputMode outputMode = GetOutputMode();
            if (outputMode.Type == OutputType.absolute) {

                if (resetTime == 0f) {
                InsertAtFirst(pos, input);
                InsertAtFirst(dir, pos[0] - pos[1]);
                }
                else {
                    InsertAtFirst(dir, input - fRelPoint);
                    if (!resetFlag || !initFlag) {
                        InsertAtFirst(pos, pos[0] + dir[0]);
                        Console.WriteLine("---");
                    }
                    else {
                        Console.WriteLine("---=");
                        NonInsertAtFirst(pos);
                    }
                    fRelPoint = input;
                }
            }
            else if (outputMode.Type == OutputType.relative) {
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
        bool resetFlag = false;

        float reportTime;
        float dTimeRemaining;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
    }
}