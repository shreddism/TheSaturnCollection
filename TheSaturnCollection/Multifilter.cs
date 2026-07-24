using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using System.Runtime.Intrinsics.X86;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Multifilter")]
    public class Multifilter : AsyncPositionedPipelineElement<IDeviceReport>
    {
        public Multifilter() : base()
        {
        }

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        [Property("Reverse EMA"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "Equivalent to what is seen in Reconstructor and Temporal Resampler.\n" +
            "ONLY touch this IF your tablet has hardware smoothing!\n" +
            "Follow the instructions from the wiki."
        )]
        public double reverseSmoothing
        {
            set => _reverseSmoothing = Math.Clamp(value, 0.001, 1.0);
            get => _reverseSmoothing;
        }
        public double _reverseSmoothing;

        [Property("Stock EMA Weight"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "Common EMA smoothing.\n" +
            "The below options are more adaptable."
        )]
        public double stockWeight
        { 
            set => _stockWeight = Math.Clamp(value, 0.001, 1.0);
            get => _stockWeight;
        }
        public double _stockWeight;

        [Property("Inner Radius"), DefaultPropertyValue(25.0), ToolTip
        (
            "Possible range: 0.0 - any, default 25.0\n\n" +

            "A full deadzone for movement. Unit is in raw tablet data.\n" +
            "Directionally separated with smooth position transition to raw based on itself."
        )]
        public double rInner
        { 
            set => _rInner = Math.Max(value, 0.0);
            get => _rInner;
        }
        public double _rInner;

        [Property("Smoothed Antichatter"), DefaultPropertyValue(50.0), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0\n\n" +
            
            "Sets base behavior for distance smoothing. Unit is raw tablet data.\n" +
            "Goes to raw position based on the setting below."
        )]
        public double smoothDist
        { 
            set => _smoothDist = Math.Max(value, 0.0);
            get => _smoothDist;
        }
        public double _smoothDist;

        [Property("Separated Threshold Mult"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.5 - any, default 1.0\n\n" +

            "Lower values are more eager to send smoothed position to raw."
        )]
        public double sepMult
        {
            set => _sepMult = Math.Clamp(value, 0.5, 100000.0);
            get => _sepMult;
        }
        public double _sepMult;

        [Property("Accel Response Aggressiveness"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Some people like using Devocub or Radial Follow for their more exaggerated snap effect that they bring.\n" +
            "This adaptively brings that to sharp acceleration, so your cursor won't lock up on a small movement.\n" +
            "Sensitivity is based on Area Scale and X modifier.\n" +
            "Putting this too high, like above ~2, will make the cursor far less readable, so it isn't recommended.\n" +
            "If hovering on a PTK-x70, this may be unreliable, as reporting becomes buggy on the hardware level.\n" +
            "This could also apply to a PTH-x60 tablet, but this is untested.\n" +
            "General users - don't put above 0."
        )]
        public double aResponse
        { 
            set => _aResponse = Math.Max(value, 0.0);
            get => _aResponse;
        }
        public double _aResponse;

        [Property("Directional Antichatter Threshold"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Works somewhat like Devocub Antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 5 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly.\n" +
            "If you are unsure, keep at 0."
        )]
        public double dacOuter
        { 
            set => _dacOuter = Math.Max(value, 0.0);
            get => _dacOuter;
        }
        public double _dacOuter;

        [Property("Area Scale"), DefaultPropertyValue(0.5), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5\n\n" +

            "Multiplies every area-subjective threshold, mostly failsafes.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public double areaScale
        { 
            set => _areaScale = Math.Clamp(value, 0.01, 5.0);
            get => _areaScale;
        }
        public double _areaScale;

        [Property("X Modifier"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0\n\n" +

            "Acts as aspect ratio compensation.\n" +
            "Divide display area ratio by tablet area ratio, then use that value.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public double xMod
        { 
            set => _xMod = Math.Clamp(value, 0.01, 100.0);
            get => _xMod;
        }
        public double _xMod;

        [BooleanProperty("Interpolation", ""), DefaultPropertyValue(true), ToolTip
        (
            "Enables interpolation.\n" +
            "If this is disabled, no settings below this will have an effect."
        )]
        public bool interp { set; get; }

        [Property("Wire - Filter Mode"), PropertyValidated(nameof(wireModes)), DefaultPropertyValue("Wire - Interp"), ToolTip
        (
            "Controls ConsumeState calling UpdateState and when below filtering applies.\n" +
            "Check the wiki for more info."
        )]
        public string wireMode
        { 
            set => _wireMode = value; 
            get => (_wireMode != null) ? _wireMode : "Wire - Point"; 
        }
        public static string[] wireModes => new[] {
            "Non-Wire - Point",
            "Non-Wire - Interp",
            "Wire - Point",
            "Wire - Interp"
        }; 
        public string? _wireMode;

        [Property("Prediction Ratio"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0\n\n" +

            "Uses a Kalman filter that considers acceleration instead of just position and velocity.\n" +
            "Because of this, it has far less average position error under movement than what is seen in Temporal Resampler.\n" +
            "Beyond that, by default on certain tablets the filter's parameters and other values are tuned for better accuracy."
        )]
        public double frameShift
        { 
            set => _frameShift = Math.Clamp(value, 0.0, 1.0);
            get => _frameShift;
        }
        public double _frameShift;

        [Property("Expected Milliseconds Per Report"), DefaultPropertyValue(0.0), ToolTip
        (
            "Has no effect if set to 0.\n" +
            "You should know what you are doing if you change this from 0."
        )]
        public double msOverride
        { 
            set => _msOverride = Math.Max(value, 0.0);
            get => _msOverride;
        }
        public double _msOverride;

        [BooleanProperty("Enable Extra Settings", ""), DefaultPropertyValue(false), ToolTip
        (
            "In Tools, there are more settings for the multifilter.\n" +
            "However, they can only be configured for all tablets,\n" +
            "so this controls where they are applied."
        )]
        public bool ExGate { set; get; }

        public HPETDeltaStopwatch perf = new HPETDeltaStopwatch();

        protected override void ConsumeState()
        {
            if (!configinit || config == null) {
                ReadConfig();
                configinit = true;
                return;
            }

            if (!init && !auxinit && State is IAuxReport aux) {
                filter = new MultifilterCore(this);
                filter.IDTablet(name, ref filter.tabletType);
                auxinit = true;
            }

            if (init && (filter!.tabletType == 1 || filter!.tabletType == 2) && State is IProximityReport p) {
                if (p.NearProximity == false) {
                    filter.emergency = 2;
                    filter.eflag = false;
                }
            }

            if ((auxinit || init) && (filter!.tabletType == 6 && State is IAuxReport g620aux)) {
                filter.auxButtons = (bool[])g620aux.AuxButtons.Clone();
                filter.emergency = 5;
                filter.eflag = false;
            }

            if (State is ITabletReport report) {  
                if (!init) {
                    if (!auxinit){
                        filter = new MultifilterCore(this);
                    }
                    if (filter != null) {
                        filter!.Initialize(report);
                        init = true;
                    }
                    OnEmit();
                    return;
                }
                filter!.HandleConsume(report);

                if (filter.wireFlag) {
                    UpdateState();
                }

                if (!interp) {
                    report.Position = filter.fipos[0].AsVector2();
                    OnEmit();
                }
            }
            else {
                OnEmit();
            } 
        }

        protected override void UpdateState()
        {
            if (!configinit || config == null) {
                ReadConfig();
                configinit = true;
                return;
            }

            if (interp && State is ITabletReport report && PenIsInRange() && init) {   
                filter!.HandleUpdate(report);
                OnEmit();
            }
        }

        public void ReadConfig() {
            config = new MultifilterConfig();
            configStatus = MultifilterConfigParser.ReadConfig(ref config, name);

        }

        bool init;
        bool auxinit;
        bool configinit;
        int configStatus;

        MultifilterCore? filter;
        public MultifilterConfig? config;

        [TabletReference]
        public TabletReference TabletReference { set { name = value.Properties.Name; } }
        public string name = string.Empty;
    }
/*
    [PluginName("Saturn - Multifilter - Extra Settings")]
    public class MultifilterExtraSettings : ITool {

        [BooleanProperty("Alternate Hover Settings", ""), DefaultPropertyValue(false), ToolTip
        (
            "If set to true, the below settings will apply to hovering. If set to false, they will do nothing."
        )]
        public static bool hoverSettings { set; get; }

        [Property("Reverse EMA (Hover)"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0"
        )]
        public static double reverseSmoothingH
        {
            set => _reverseSmoothingH = Math.Clamp(value, 0.001, 1.0);
            get => _reverseSmoothingH;
        }
        public static double _reverseSmoothingH;

        [Property("Stock EMA Weight (Hover)"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0"
        )]
        public static double stockWeightH
        { 
            set => _stockWeightH = Math.Clamp(value, 0.001, 1.0);
            get => _stockWeightH;
        }
        public static double _stockWeightH;

        [Property("Inner Radius (Hover)"), DefaultPropertyValue(25.0), ToolTip
        (
            "Possible range: 0.0 - any, default 25.0"
        )]
        public static double rInnerH
        { 
            set => _rInnerH = Math.Max(value, 0.0);
            get => _rInnerH;
        }
        public static double _rInnerH;

        [Property("Smoothed Antichatter (Hover)"), DefaultPropertyValue(50.0), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0"
        )]
        public static double smoothDistH
        { 
            set => _smoothDistH = Math.Max(value, 0.0);
            get => _smoothDistH;
        }
        public static double _smoothDistH;

        [Property("Separated Threshold Mult (Hover)"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.5 - any, default 1.0"
        )]
        public static double sepMultH
        {
            set => _sepMultH = Math.Clamp(value, 0.5, 100000.0);
            get => _sepMultH;
        }
        public static double _sepMultH;

        [Property("Accel Response Aggressiveness (Hover)"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0"
        )]
        public static double aResponseH
        { 
            set => _aResponseH = Math.Max(value, 0.0);
            get => _aResponseH;
        }
        public static double _aResponseH;

        [Property("Directional Antichatter Threshold (Hover)"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0"
        )]
        public static double dacOuterH
        { 
            set => _dacOuterH = Math.Max(value, 0.0);
            get => _dacOuterH;
        }
        public static double _dacOuterH;

        [Property("Area Scale (Hover)"), DefaultPropertyValue(0.5), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5"
        )]
        public static double areaScaleH
        { 
            set => _areaScaleH = Math.Clamp(value, 0.01, 5);
            get => _areaScaleH;
        }
        public static double _areaScaleH;

        [Property("X Modifier (Hover)"), DefaultPropertyValue(1.0), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0"
        )]
        public static double xModH
        { 
            set => _xModH = Math.Clamp(value, 0.01, 100.0);
            get => _xModH;
        }
        public static double _xModH;

        [Property("Prediction Ratio (Hover)"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0"
        )]
        public static double frameShiftH
        { 
            set => _frameShiftH = Math.Clamp(value, 0.0, 1.0);
            get => _frameShiftH;
        }
        public static double _frameShiftH;

        [Property("Expected Milliseconds Per Report (Hover)"), DefaultPropertyValue(0.0), ToolTip
        (
            "You should know what you are doing if you change this from 0."
        )]
        public static double msOverrideH
        { 
            set => _msOverrideH = Math.Max(value, 0.0);
            get => _msOverrideH;
        }
        public static double _msOverrideH;

        [Property("Time Scale"), DefaultPropertyValue(1.0), ToolTip
        (
            "time scale"
        )]
        public static double timeScale
        { 
            set => _timeScale = Math.Clamp(value, 0.1, 10.0);
            get => _timeScale;
        }
        public static double _timeScale;

        [BooleanProperty("*Disable* Tablet-Specific Tweaks", ""), DefaultPropertyValue(false), ToolTip
        (
            "Bugfixes and prediction improvements are automatically made on certain tablets.\n" +
            "This disables them!\n" +
            "For a supported list, check the README/wiki."
        )]
        public static bool disableTabletToggle { set; get; }

        [BooleanProperty("Vectorized Prediction", ""), DefaultPropertyValue(false), ToolTip
        (
            "If your CPU supports it (x86 AVX/FMA), explicitly vectorizes matrix multiplication in prediction when 4x4 matrices are multiplied.\n" +
            "Tested to be faster and should be faster on any modern CPU, but your mileage may vary.\n" +
            "This setting is global, even if the filter's toggle disables extra settings."
        )]
        public static bool _VecMatMul { set; get; }

        public bool Initialize() {
            ExEnabled = true;
            VecMatMul = _VecMatMul;
            if (VecMatMul) {
                if (Avx.IsSupported) {
                    Log.Write("MultifilterExtraSettings", "AVX is supported.");
                    if (Fma.IsSupported) {
                        Log.Write("MultifilterExtraSettings", "FMA is supported.");
                    }
                }
                else {
                    Log.Write("MultifilterExtraSettings", "The Vectorize setting will have no effect.");
                }
            }
            return true;
        }

        public void Dispose() {
            ExEnabled = false;
            VecMatMul = false;
        }
        public static bool ExEnabled = false;
        public static bool VecMatMul = false;
    }
    */
}