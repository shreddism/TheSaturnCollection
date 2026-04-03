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

[PluginName("Custom Reset Absolute Mode")]
public class CustomResetAbsoluteMode : AbsoluteOutputMode
{
    bool bPressFlag = false;
    bool bReleaseFlag = false;

    [Resolved]
    public override IAbsolutePointer? Pointer { set; get; }

    public override void Read(IDeviceReport deviceReport) {
        if (!initFlag) AttemptInitialization();

        bool dropFlag = false;
        if (initFlag) {
            if (deviceReport is ITabletReport report) {
                reportIsFirstAfterConsume = true;
                CustomResetAbsoluteModeBinding.bAuxFlag = false;
                float readTime = (float)readStopwatch.Restart().TotalMilliseconds;
                if (tTimeProximity && maxDrops > 0 && tResetTime > 0 && readTime > tResetTime) {
                    dropsRemaining = maxDrops + 1;
                    dropFlag = true;
                }
            }

            if (maxDrops > 0 && deviceReport is IProximityReport proximityReport) {
                if (proximityReport.NearProximity == false) {
                    dropsRemaining = maxDrops + 1;
                    dropFlag = true;
                }
            }

            if (dropFlag) {
                base.Read(null);
                return;
            }
            
            base.Read(deviceReport);
        }
    }

    protected override IAbsolutePositionReport Transform(IAbsolutePositionReport report) {
        if (!initFlag) AttemptInitialization();

        bReleaseFlag = CustomResetAbsoluteModeBinding.bReleaseFlag;
        bPressFlag = CustomResetAbsoluteModeBinding.bPressFlag;

        CustomResetAbsoluteModeBinding.bAuxFlag = false;
        bool firstSinceDrops = false;
        if (dropsRemaining > 0 && transformCompleteFlag) {
            if (reportIsFirstAfterConsume) dropsRemaining--;

            if (dropsRemaining > 0) {
                report.Position = lastPostTransformInput;
                return report;
            }
            else firstSinceDrops = true;
        } 

        float transformTime = (float)transformStopwatch.Restart().TotalMilliseconds;
        int tempCode = -3;
        if ((savedFlag) && (tResetTime > 0) && (transformTime > tResetTime) && (tResetCode != 3)) {
            tResetFlag = true;
            tempCode = tResetCode;
            resetCode = tResetCode;
        }
    
        bResetCode = CustomResetAbsoluteModeBinding.bResetCode;
        if ((!bInitFlag) && (((dropsRemaining == 0) && (bPressFlag || bReleaseFlag)))) bInitFlag = true;

        if (bInitFlag && bPressFlag) resetCode = bResetCode;

        if (bInitFlag && bReleaseFlag && !tResetFlag) resetCode = 0;

        if ((firstSinceDrops) && (resetCode == 0) && (lastResetCode > 0)) {
            resetCode = lastResetCode;
        }

        if (initPersistHandledFlag) { 
            initPersistHandledFlag = false;
            resetCode = 0;
            report.Position = lastPostTransformInput;
            return report;
        }
        else if ((!transformCompleteFlag) && (resetCode == 0) && (persistentResetCode > 0)) {
            resetCode = persistentResetCode;
            initPersistHandledFlag = true;
        }

        if (resetCode != tempCode) tResetFlag = false;

        persistentResetCode = resetCode;

        if (initFlag) {
            if (resetCode == 1) {
                
                UpdateInputPos(new Vector2(
                    report.Position.X / mmScale.X,
                    report.Position.Y / mmScale.Y
                ));
                if (lastResetCode != 1) TransformationMatrix = base.CreateTransformationMatrix();

                BaseTransform(report); 
                if (!holdingResetFlag ^ (lastResetCode == 2)) {
                    holdPos = report.Position;
                    holdingResetFlag = true;
                }
                else {
                    report.Position = holdPos;
                }

                if (lastResetCode != 1 && tLogInfo) {
                    Log.Write("CustomResetAbsoluteMode", "Setting the tablet area's center to the cursor's position...");
                    Log.Write("CustomResetAbsoluteMode", "This effectively sets the cursor to the display center.");
                }
            }

            if (resetCode == 2) {
                if (!draggingFlag) {
                    dragHold = Input!.Position;

                    if (tPersistenceCode == 2 || tResetFlag) dragPos = lastPreTransformOutput;      
                    else dragPos = report.Position;

                    dragOffset = report.Position - dragPos;
                    draggingFlag = true;
                    DragUpdate();
                    TransformationMatrix = base.CreateTransformationMatrix();
                }
                else {
                    dragOffset = report.Position - dragPos;                    
                    DragUpdate();    
                }

                BaseTransform(report); 
                if (!holdingResetFlag) {
                    holdPos = report.Position;
                    holdingResetFlag = true;
                }
                else {
                    report.Position = holdPos;
                }
 
                if (lastResetCode != 2 && tLogInfo) {
                    if (!tResetFlag) {
                        Log.Write("CustomResetAbsoluteMode", "Dragging the tablet area...");
                    }
                    else {
                        Log.Write("CustomResetAbsoluteMode", "Resetting the tablet area...");
                    }
                }
            }
            else {
                if (outputCompleteIgnores == 0 && transformCompleteFlag) {
                    if (draggingFlag) {
                        DragUpdate();
                        TransformationMatrix = base.CreateTransformationMatrix();
                    }
                    draggingFlag = false;
                    dragOffset = Vector2.Zero;
                    
                } 
            }

            if (resetCode == -1) {
                UpdateInputPos(new Vector2(
                    report.Position.X / mmScale.X,
                    report.Position.Y / mmScale.Y
                ));
                BaseTransform(report);
                UpdateOutputPos(report.Position);
                
                if (lastResetCode != -1 && tLogInfo) {
                    Log.Write("CustomResetAbsoluteMode", "Setting the centers of both areas to the cursor's position...");
                }
            }

            if (resetCode == -2) { 
                if (!stockResetFlag) {
                    UpdateInputPos(stockInput);
                    UpdateOutputPos(stockOutput);
                    TransformationMatrix = base.CreateTransformationMatrix();
                    stockResetFlag = true;
                    if (tLogInfo) {
                        Log.Write("CustomResetAbsoluteMode", "Resetting both areas to stock settings...");
                        Log.Write("CustomResetAbsoluteMode", "Display Area: " + Output);
                        Log.Write("CustomResetAbsoluteMode", "Tablet Area: " + Input);
                    }
                }

                BaseTransform(report);
            }
            else {
                if (stockResetFlag) {
                    lastResetCode = 0;
                    stockResetFlag = false;
                }
            }

            if (tResetFlag) {
                if (resetCode != 3) lastResetCode = -3;
                resetCode = 0;
            }

            if (resetCode == 0) { 
                if (holdingResetFlag || lastResetCode != 0) {
                    TransformationMatrix = base.CreateTransformationMatrix();
                    holdingResetFlag = false;
                }

                if (!tResetFlag) BaseTransform(report);
                tResetFlag = false;

                if (lastResetCode != 0 && lastResetCode != -2 && tLogInfo) {
                    Log.Write("CustomResetAbsoluteMode", "Display Area: " + Output);
                    Log.Write("CustomResetAbsoluteMode", "Tablet Area: " + Input);
                }
            }
        }

        if (transformCompleteFlag && outputCompleteIgnores == 0) {
            lastPostTransformInput = report.Position;
            savedFlag = true;
        }
        
        transformCompleteFlag = true;
        lastResetCode = resetCode;

        return report; 
    }

    protected override void OnOutput(IDeviceReport value) {
        if (!initFlag) AttemptInitialization();

        if (initFlag) {
            if ((value is IAuxReport auxReport) && (outputCompleteIgnores > 0)) {
                if (CustomResetAbsoluteModeBinding.bAuxFlag) {
                    bResetCode = CustomResetAbsoluteModeBinding.bResetCode;
                    bInitFlag = true;
                    TransformationMatrix = base.CreateTransformationMatrix();
                }
                CustomResetAbsoluteModeBinding.bAuxFlag = false;
            }

            if (value is ITabletReport report) {
                if (outputCompleteIgnores > 0) {
                    if (reportIsFirstAfterConsume) outputCompleteIgnores--;

                    if (bResetCode == 1 || bResetCode == -1) {
                        Input!.Position = saveInputPosition;
                        Output!.Position = saveOutputPosition;
                    }
                    report.Position = lastOutput; 
                }
                if (tPixelGridFlag && outputCompleteIgnores == 0 && transformCompleteFlag) {
                    PostProcessingStatUpdate(report.Position);
                    pxOutput = pos[0];
                    pxOutput *= tPixelGridMult;
                    pxOutput = new Vector2(MathF.Floor(pos[0].X), MathF.Floor(pos[0].Y));
                    pxOutput /= tPixelGridMult;
                    if ((!tDynamicMode) || ((Vector2.Distance(checkPos, pos[0]) + (dir[0] + dir[1] + dir[2] + dir[3]).Length() >= 1 / tPixelGridMult) && (pxOutput != outputPos[0]))) {
                        checkPos = pos[0];
                        InsertAtFirst(outputPos, pxOutput);
                    }  
                    report.Position = outputPos[0];
                }
                lastOutput = report.Position;
            }
            
            reportIsFirstAfterConsume = false;
            base.OnOutput(value);
        }
    }

    void PostProcessingStatUpdate(Vector2 input) {
        InsertAtFirst(pos, input);
        InsertAtFirst(dir, pos[0] - pos[1]);
    }

    const int HMAX = 4;
    Vector2[] pos = new Vector2[HMAX];
    Vector2[] dir = new Vector2[HMAX];
    Vector2[] outputPos = new Vector2[HMAX];
    Vector2 checkPos;
    Vector2 pxOutput;

    bool initPersistHandledFlag = false;

    bool draggingFlag = false;
    Vector2 dragPos;    
    Vector2 dragOffset;
    static Vector2 lastPreTransformOutput;
    static Vector2 lastPostTransformInput;
    static Vector2 lastOutput;
    Vector2 dragHold;

    int maxDrops;
    int dropsRemaining;
    int bResetCode;
    int resetCode;
    static int lastResetCode;
    static int persistentResetCode;

    int tPersistenceCode;

    Matrix3x2 StockMatrix;

    Vector2 stockInput;
    Vector2 stockOutput;

    static Vector2 saveInputPosition;
    static Vector2 saveOutputPosition;
    static bool saveCenterFlag = false;

    bool initFlag = false;
    bool bInitFlag = false;
    static bool holdingResetFlag = false;
    bool transformCompleteFlag = false;
    bool stockResetFlag = false;

    int outputCompleteIgnores;
    
    static Vector2 holdPos;
    Vector2 mmScale;
    private HPETDeltaStopwatch readStopwatch = new HPETDeltaStopwatch();
    private HPETDeltaStopwatch transformStopwatch = new HPETDeltaStopwatch();

    bool reportIsFirstAfterConsume = false;

    static bool savedFlag = false;

    float tResetTime;
    int tResetCode;
    bool tResetFlag;

    bool tTimeProximity;

    float tPixelGridMult;
    bool tPixelGridFlag;
    bool tDynamicMode;
    bool tLogInfo;
    
    public void AttemptInitialization() {
        if (Tablet != null) {
            mmScale = new Vector2
            (
                Tablet.Properties.Specifications.Digitizer.MaxX / Tablet.Properties.Specifications.Digitizer.Width, 
                Tablet.Properties.Specifications.Digitizer.MaxY / Tablet.Properties.Specifications.Digitizer.Height
            );
            if (Input != null && Output != null) {
                initPersistHandledFlag = false;
                CustomResetAbsoluteModeBinding.bPressFlag = false;
                CustomResetAbsoluteModeBinding.bReleaseFlag = false;
                StockMatrix = base.CreateTransformationMatrix();
                maxDrops = CustomResetAbsoluteModeSettings.tNearProximityDrops;
                outputCompleteIgnores = maxDrops + 1;
                stockInput = Input.Position;
                stockOutput = Output.Position;
                bInitFlag = false;
                resetCode = 0;
                tResetTime = CustomResetAbsoluteModeSettings.tResetTime;
                tResetCode = CustomResetAbsoluteModeSettings.tResetCode;
                tTimeProximity = CustomResetAbsoluteModeSettings.tTimeProximity;
                tPixelGridMult = CustomResetAbsoluteModeSettings.tPixelGridMult;
                if (tPixelGridMult >= 1.0f) tPixelGridFlag = true;
                else tPixelGridFlag = false;
                tDynamicMode = CustomResetAbsoluteModeSettings.tDynamicMode;
                tLogInfo = CustomResetAbsoluteModeSettings.tLogInfo;

                tResetCode = CustomResetAbsoluteModeSettings.tResetCode;
                initFlag = true;
    
                if (!saveCenterFlag) {
                    saveInputPosition = stockInput;
                    saveOutputPosition = stockOutput;
                    saveCenterFlag = true;
                }

                tPersistenceCode = CustomResetAbsoluteModeSettings.tPersistenceCode;
                
                if (tPersistenceCode == 2) {
                    Input.Position = saveInputPosition;
                    Output.Position = saveOutputPosition;
                    TransformationMatrix = base.CreateTransformationMatrix();
                }
            }  
        }
    }
    
    public void DragUpdate() {   
        UpdateInputPos(dragHold + new Vector2(
            dragOffset.X / mmScale.X,
            dragOffset.Y / mmScale.Y
        ));
    }  

    public void BaseTransform(IAbsolutePositionReport report) {
        if (transformCompleteFlag) lastPreTransformOutput = report.Position;
        base.Transform(report);
    }

    public void UpdateInputPos(Vector2 position) {
        Input!.Position = position;
        saveInputPosition = position;
    }

    public void UpdateOutputPos(Vector2 position) {
        Output!.Position = position;
        saveOutputPosition = position;
    }
}

[PluginName("Custom Reset Absolute Mode Binding")]
public class CustomResetAbsoluteModeBinding : IStateBinding
{
    [Property("Reset Mode"), DefaultPropertyValue("Set Tablet Area Center To Position"), PropertyValidated(nameof(resetModes)), ToolTip
    (
        "Changes what pressing and holding the binding will do."
    )]
    public string Mode { get; set; } = string.Empty;        // name "Mode" is displayed in GUI
    public static IEnumerable<string> resetModes { get; set; } = new List<string> { "Set Tablet Area Center To Position", "Drag Tablet Area", "Set Both Centers To Position", "Reset To Stock Settings" };
    internal static int bResetCode = 0;
    int resetCodeSetting = 0;
    
    public void Initialize() {
        resetCodeSetting = Mode switch {
            "Reset To Stock Settings" => -2,
            "Set Both Centers To Position" => -1,
            "Drag Tablet Area" => 2,
            "Set Tablet Area Center To Position" => 1,
            _ => 0
        };
        initFlag = true;
    }

    public void Press(TabletReference tablet, IDeviceReport report)
    {  
        if (!initFlag) {
            Initialize();
        }
        
        bPressFlag = true;
        bReleaseFlag = false;
        bAuxFlag = true;
        bResetCode = resetCodeSetting;
    }

    public void Release(TabletReference tablet, IDeviceReport report)
    {
        if (!initFlag) {
            Initialize();
        }

        if (resetCodeSetting == bResetCode) {
            bReleaseFlag = true;
            bPressFlag = false;
            bAuxFlag = true;
            bResetCode = 0;
        }
    }

    bool initFlag = false;

    internal static bool bPressFlag = false; 
    internal static bool bReleaseFlag = false;
    internal static bool bAuxFlag = false;
}

[PluginName("Custom Reset Absolute Mode Settings")]
public class CustomResetAbsoluteModeSettings : ITool
{
    private const float DEFAULT_RESET_TIME = 25.0f;
    [Property("Reset Time (Hover Over The Textbox)"), DefaultPropertyValue(DEFAULT_RESET_TIME), ToolTip
    (
        "Important: You may need to apply and save multiple times for settings to be applied properly.\n\n" +
        "Has no effect if set to 0.0.\n" +
        "Only takes effect if the output mode is Custom Reset Absolute Mode.\n" +
        "Bindings can be used for this, and they override timed resets."
    )]
    public float resetTime
    {
        set => _resetTime = Math.Max(value, 0.0f);
        get => _resetTime;
    }
    public float _resetTime;
    internal static float tResetTime = DEFAULT_RESET_TIME;

    private const string DEFAULT_RESET_MODE = "None";
    private const int DEFAULT_RESET_CODE = 3;
    [Property("Reset Mode"), DefaultPropertyValue(DEFAULT_RESET_MODE), PropertyValidated(nameof(resetModes)), ToolTip
    (
        "Changes reset behavior. For more info, check the wiki."
    )]
    public string resetMode { get; set; } = string.Empty;
    public static IEnumerable<string> resetModes { get; set; } = new List<string> { "Set Tablet Area Center To Position", "Fake Relative Mode", "Set Both Centers To Position", "Reset To Stock Settings", "None" };
    public string tResetMode = DEFAULT_RESET_MODE;
    internal static int tResetCode = DEFAULT_RESET_CODE;

    private const int DEFAULT_NEAR_PROXIMITY_DROPS = 4;
    [Property("Near Proximity Extra Position Drops"), DefaultPropertyValue(DEFAULT_NEAR_PROXIMITY_DROPS), ToolTip
    (
        "Some tablets send reports with this confidence flag.\n" +
        "This amount of 'valid' tablet reports will be thrown out after the last untrustworthy report.\n" +
        "This is because pen buttons can be pressed down but show up as released in this situation.\n" +
        "This setting can be crucial for function."
    )]
    public int nearProximityDrops { set; get; }
    internal static int tNearProximityDrops = DEFAULT_NEAR_PROXIMITY_DROPS;

    private const bool DEFAULT_TIME_PROXIMITY = true;
    [BooleanProperty("Use Reset Time For Report Dropping", ""), DefaultPropertyValue(DEFAULT_TIME_PROXIMITY), ToolTip
    (
        "Uses reset time to drive function.\n" +
        "A PTK-x70 does not need this, but other tablets might."
    )]
    public bool timeProximity { set; get; }
    internal static bool tTimeProximity = DEFAULT_TIME_PROXIMITY;

    private const float DEFAULT_PIXEL_GRID_MULT = 0.0f;
    [Property("Pixel Grid Resolution Scale"), DefaultPropertyValue(DEFAULT_PIXEL_GRID_MULT), ToolTip
    (
        "Possible range: 1.0 - any, default 0.0\n" +
        "If set below 1.0, this has no effect.\n\n" +
        "This floors the pixel position of the cursor.\n" +
        "This multiplies the position by its value before flooring it, increasing resolution.\n" +
        "If set to 1.0 the cursor is set to whole pixels, if set to 2.0 the cursor is set to quadrants, and so on."
    )]
    public float pixelGridMult
    {
        set => _pixelGridMult = Math.Max(0.0f, value);
        get => _pixelGridMult;
    }
    public float _pixelGridMult;
    internal static float tPixelGridMult = DEFAULT_PIXEL_GRID_MULT;

    private const bool DEFAULT_DYNAMIC_MODE = true;
    [BooleanProperty("Dynamic Mode", ""), DefaultPropertyValue(DEFAULT_DYNAMIC_MODE), ToolTip
    (
        "If this is enabled and the above setting is 1.0 or above, the cursor won't move if the input position has not changed by one scaled pixel since the last move.\n" +
        "Also takes velocity into consideration, so it doesn't refuse to move for little reason."
    )]
    public bool dynamicMode { set; get; }
    internal static bool tDynamicMode = DEFAULT_DYNAMIC_MODE;

    private const string DEFAULT_PERSISTENCE_MODE = "Hard";
    private const int DEFAULT_PERSISTENCE_CODE = 2;
    [Property("Persistence Mode"), DefaultPropertyValue(DEFAULT_PERSISTENCE_MODE), PropertyValidated(nameof(persistenceModes)), ToolTip
    (
        "The default of Hard saves any changes even through applying/saving settings, only resetting settings when the binding for that is pressed.\n" +
        "Light resets on applying/saving settings if a binding is not being pressed."
    )]
    public string persistenceMode { get; set; } = string.Empty;        // name "Mode" is displayed in GUI
    public static IEnumerable<string> persistenceModes { get; set; } = new List<string> { "Light", "Hard" };
    public string tPersistenceMode = DEFAULT_PERSISTENCE_MODE;
    internal static int tPersistenceCode = DEFAULT_PERSISTENCE_CODE;

    private const bool DEFAULT_LOG_INFO = true;
    [BooleanProperty("Log Info", ""), DefaultPropertyValue(DEFAULT_LOG_INFO), ToolTip
    (
        "Logs reset and area information."
    )]
    public bool logInfo { set; get; }
    internal static bool tLogInfo = DEFAULT_LOG_INFO;

    public bool Initialize() {
        tResetTime = resetTime;
        tTimeProximity = timeProximity;
        tResetMode = resetMode;
        tResetCode = tResetMode switch {
            "Reset To Stock Settings" => -2,
            "Set Both Centers To Position" => -1,
            "Fake Relative Mode" => 2,
            "Set Tablet Area Center To Position" => 1,
            "None" => 3,
            _ => 0
        };
        tNearProximityDrops = nearProximityDrops;
        tPixelGridMult = pixelGridMult;
        tDynamicMode = dynamicMode;
        tPersistenceMode = persistenceMode;
        tPersistenceCode = tPersistenceMode switch {
            "Hard" => 2,
            _ => 1
        };
        tLogInfo = logInfo;
        return true;
    }

    public void Dispose() {
        tResetTime = DEFAULT_RESET_TIME;
        timeProximity = DEFAULT_TIME_PROXIMITY;
        tResetCode = DEFAULT_RESET_CODE;
        tNearProximityDrops = DEFAULT_NEAR_PROXIMITY_DROPS;
        tPixelGridMult = DEFAULT_PIXEL_GRID_MULT;
        tDynamicMode = DEFAULT_DYNAMIC_MODE;
        tPersistenceCode = DEFAULT_PERSISTENCE_CODE;
        tLogInfo = DEFAULT_LOG_INFO;
    }
}


