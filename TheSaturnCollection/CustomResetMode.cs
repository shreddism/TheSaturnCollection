using System;
using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using OpenTabletDriver.Plugin.DependencyInjection;      
using OpenTabletDriver.Plugin.Platform.Pointer; 
using System.Linq;
using System.Collections.Generic;
using static Saturn.Utils;

namespace Saturn;

[PluginName("Saturn - Custom Reset Mode")]
public class CustomResetMode : AbsoluteOutputMode
{

    [Resolved]
    public override IAbsolutePointer? Pointer { set; get; }

    public override void Read(IDeviceReport deviceReport)
    {   
        bool dropFlag = false;

        int maxDrops = CustomResetTool.tNearProximityDrops;

        if (maxDrops > 0 && deviceReport is IProximityReport proximityReport) {
            if (!proximityReport.NearProximity){
                dropsRemaining = maxDrops;
                dropFlag = true;
            }
        }

        if (dropFlag) {
            base.Read(null);
            return;
        }

        base.Read(deviceReport);
    }

    protected override IAbsolutePositionReport Transform(IAbsolutePositionReport report)
    {
        if (dropsRemaining > 0) {
            dropsRemaining--;
            report.Position = lastPostTransformPosition;
            if (dropsRemaining == 0) firstSinceDrops = true;
            return report;
        }

        if (!initFlag) {
            if (Tablet != null) {
                mmScale = new Vector2
                (
                    Tablet.Properties.Specifications.Digitizer.MaxX / Tablet.Properties.Specifications.Digitizer.Width, 
                    Tablet.Properties.Specifications.Digitizer.MaxY / Tablet.Properties.Specifications.Digitizer.Height
                );
                if (Input != null && Output != null) {
                    initFlag = true;
                }  
            }
        }

        int bResetCode = CustomResetBinding.bResetCode;

        int resetCode = bResetCode;

        if (initFlag) {
            if (resetCode == 1) {
                Vector2 newInputPosition = new Vector2
                (
                    report.Position.X / mmScale.X,
                    report.Position.Y / mmScale.Y
                );
                base.Input!.Position = newInputPosition; // lines 32, 33
                base.TransformationMatrix = base.CreateTransformationMatrix();
                draggingFlag = false;
                dragOffset = Vector2.Zero;
            }
            else if (resetCode == 2) {
                if (!draggingFlag) {
                    dragPos = lastPreTransformPosition;
                    dragOffset = report.Position - dragPos;
                    draggingFlag = true;
                }
                else {
                    dragOffset = report.Position - dragPos;
                }
            }
            else { 
                if (draggingFlag) {
                    Vector2 newInputPosition = new Vector2
                    (
                        dragOffset.X / mmScale.X,
                        dragOffset.Y / mmScale.Y
                    );
                    base.Input!.Position += newInputPosition; // lines 32, 33
                    base.TransformationMatrix = base.CreateTransformationMatrix();
                    draggingFlag = false;
                    dragOffset = Vector2.Zero;
                }
            }
        }

        lastPreTransformPosition = report.Position;
        
        base.Transform(report);             // Convert from tablet to display

        if (resetCode > 0) {       // Cursor annoyingly jitters if this doesn't exist.
            if (!holdingResetFlag) {
                if (resetCode == 1) {
                    holdPos = report.Position;
                }
                else if (resetCode == 2) {
                    holdPos = lastPostTransformPosition;
                    if (firstSinceDrops) { 
                        report.Position = holdPos;
                        firstSinceDrops = false;
                    }
                }
                holdingResetFlag = true;
            }
            else {
                report.Position = holdPos;
            }
        }
        else { 
            holdingResetFlag = false;
        }

        lastPostTransformPosition = report.Position;    
        
        return report; 
    }

    bool draggingFlag = false;
    Vector2 dragPos;    
    Vector2 dragOffset;
    Vector2 lastPreTransformPosition;
    Vector2 lastPostTransformPosition;

    int dropsRemaining;

    bool firstSinceDrops;

    bool initFlag = false;
    bool holdingResetFlag = false;
    Vector2 holdPos;
    Vector2 mmScale;
    private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
}

[PluginName("Saturn - Custom Reset Binding")]
public class CustomResetBinding : IStateBinding
{
    [Property("Reset Mode"), DefaultPropertyValue("Center"), PropertyValidated(nameof(resetModes)), ToolTip
    (
        "aaaa"
    )]
    public string resetMode { get; set; } = string.Empty;
    public static IEnumerable<string> resetModes { get; set; } = new List<string> { "Center", "Drag" };
    internal static int bResetCode;
    int resetCodeSetting = 0;
    

    public void Initialize() {
        if (resetMode == "Drag") resetCodeSetting = 2;
        else resetCodeSetting = 1;
        initFlag = true;
    }

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (!initFlag) {
            Initialize();
        }
        bResetCode = resetCodeSetting;
    }

    public void Release(TabletReference tablet, IDeviceReport report)
    {
        if (!initFlag) {
            Initialize();
        }
        bResetCode = 0;
    }

    bool initFlag = false;
}

[PluginName("Saturn - Custom Reset Tool")]
public class CustomResetTool : ITool
{
    [Property("Reset Time"), DefaultPropertyValue(0f), ToolTip
    (
        "Only takes effect if the output mode is Saturn - Custom Reset Mode.\n" +
        "Bindings can be used for this."
    )]
    public float resetTime
    {
        set => _resetTime = Math.Max(value, 0f);
        get => _resetTime;
    }
    public float _resetTime;
    internal static float tResetTime = 0;

    [Property("Near Proximity Extra Position Drops"), DefaultPropertyValue(3), ToolTip
        (
            "Some tablets send reports with this confidence flag.\n" +
            "This amount of 'valid' tablet reports will be thrown out after the last untrustworthy report.\n" +
            "This is because pen buttons can be pressed down but show up as released in this situation.\n" +
            "If using an interpolator, increase this setting."
        )]
    public int nearProximityDrops { set; get; }
    internal static int tNearProximityDrops = 3;

    public bool Initialize() {
        tResetTime = resetTime;
        tNearProximityDrops = nearProximityDrops;
        return true;
    }

    public void Dispose() {
        tResetTime = 0;
        tNearProximityDrops = 3;
    }
}


