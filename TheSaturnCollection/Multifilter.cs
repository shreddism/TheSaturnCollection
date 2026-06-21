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

        [Property("Reverse EMA"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "Equivalent to what is seen in Reconstructor and Temporal Resampler.\n" +
            "ONLY touch this IF your tablet has hardware smoothing!\n" +
            "Follow the instructions from the wiki."
        )]
        public float reverseSmoothing
        {
            set => _reverseSmoothing = Math.Clamp(value, 0.001f, 1.0f);
            get => _reverseSmoothing;
        }
        public float _reverseSmoothing;

        [Property("Stock EMA Weight"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "Common EMA smoothing.\n" +
            "The below options are more adaptable."
        )]
        public float stockWeight
        { 
            set => _stockWeight = Math.Clamp(value, 0.001f, 1.0f);
            get => _stockWeight;
        }
        public float _stockWeight;

        [Property("Inner Radius"), DefaultPropertyValue(25.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 25.0\n\n" +

            "A full deadzone for movement. Unit is in raw tablet data.\n" +
            "Directionally separated with smooth position transition to raw based on itself."
        )]
        public float rInner
        { 
            set => _rInner = Math.Max(value, 0.0f);
            get => _rInner;
        }
        public float _rInner;

        [Property("Smoothed Antichatter"), DefaultPropertyValue(50.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0\n\n" +
            
            "Sets base behavior for distance smoothing. Unit is raw tablet data.\n" +
            "Goes to raw position based on the setting below."
        )]
        public float smoothDist
        { 
            set => _smoothDist = Math.Max(value, 0.0f);
            get => _smoothDist;
        }
        public float _smoothDist;

        [Property("Separated Threshold Mult"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.5 - any, default 1.0\n\n" +

            "Lower values are more eager to send smoothed position to raw."
        )]
        public float sepMult
        {
            set => _sepMult = Math.Clamp(value, 0.5f, 100000.0f);
            get => _sepMult;
        }
        public float _sepMult;

        [Property("Accel Response Aggressiveness"), DefaultPropertyValue(0.0f), ToolTip
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
        public float aResponse
        { 
            set => _aResponse = Math.Max(value, 0.0f);
            get => _aResponse;
        }
        public float _aResponse;

        [Property("Directional Antichatter Threshold"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Works somewhat like Devocub Antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 5 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly.\n" +
            "If you are unsure, keep at 0."
        )]
        public float dacOuter
        { 
            set => _dacOuter = Math.Max(value, 0.0f);
            get => _dacOuter;
        }
        public float _dacOuter;

        [Property("Area Scale"), DefaultPropertyValue(0.5f), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5\n\n" +

            "Multiplies every area-subjective threshold, mostly failsafes.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public float areaScale
        { 
            set => _areaScale = Math.Clamp(value, 0.01f, 5f);
            get => _areaScale;
        }
        public float _areaScale;

        [Property("X Modifier"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0\n\n" +

            "Acts as aspect ratio compensation.\n" +
            "Divide display area ratio by tablet area ratio, then use that value.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public float xMod
        { 
            set => _xMod = Math.Clamp(value, 0.01f, 100f);
            get => _xMod;
        }
        public float _xMod;

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

        [Property("Prediction Ratio"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0\n\n" +

            "Uses a Kalman filter that considers acceleration instead of just position and velocity.\n" +
            "Because of this, it has far less average position error under movement than what is seen in Temporal Resampler.\n" +
            "Beyond that, by default on certain tablets the filter's parameters and other values are tuned for better accuracy."
        )]
        public float frameShift
        { 
            set => _frameShift = Math.Clamp(value, 0.0f, 1.0f);
            get => _frameShift;
        }
        public float _frameShift;

        [Property("Expected Milliseconds Per Report"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Has no effect if set to 0.\n" +
            "You should know what you are doing if you change this from 0."
        )]
        public float msOverride
        { 
            set => _msOverride = Math.Max(value, 0.0f);
            get => _msOverride;
        }
        public float _msOverride;

        [BooleanProperty("Enable Extra Settings", ""), DefaultPropertyValue(false), ToolTip
        (
            "In Tools, there are more settings for the multifilter.\n" +
            "However, they can only be configured for all tablets,\n" +
            "so this controls where they are applied."
        )]
        public bool ExGate { set; get; }

        protected override void ConsumeState()
        {
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
                    report.Position = filter.fipos[0];
                    OnEmit();
                }
            }
            else {
                OnEmit();
            } 
        }

        protected override void UpdateState()
        {
            if (interp && State is ITabletReport report && PenIsInRange() && init) {   
                filter!.HandleUpdate(report);
                OnEmit();
            }
        }

        bool init;
        bool auxinit;

        MultifilterCore? filter;

        [TabletReference]
        public TabletReference TabletReference { set { name = value.Properties.Name; } }
        public string name = string.Empty;
    }

    [PluginName("Saturn - Multifilter - Extra Settings")]
    public class MultifilterExtraSettings : ITool {

        [BooleanProperty("Alternate Hover Settings", ""), DefaultPropertyValue(false), ToolTip
        (
            "If set to true, the below settings will apply to hovering. If set to false, they will do nothing."
        )]
        public static bool hoverSettings { set; get; }

        [Property("Reverse EMA (Hover)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0"
        )]
        public static float reverseSmoothingH
        {
            set => _reverseSmoothingH = Math.Clamp(value, 0.001f, 1.0f);
            get => _reverseSmoothingH;
        }
        public static float _reverseSmoothingH;

        [Property("Stock EMA Weight (Hover)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0"
        )]
        public static float stockWeightH
        { 
            set => _stockWeightH = Math.Clamp(value, 0.001f, 1.0f);
            get => _stockWeightH;
        }
        public static float _stockWeightH;

        [Property("Inner Radius (Hover)"), DefaultPropertyValue(25.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 25.0"
        )]
        public static float rInnerH
        { 
            set => _rInnerH = Math.Max(value, 0.0f);
            get => _rInnerH;
        }
        public static float _rInnerH;

        [Property("Smoothed Antichatter (Hover)"), DefaultPropertyValue(50.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 50.0"
        )]
        public static float smoothDistH
        { 
            set => _smoothDistH = Math.Max(value, 0.0f);
            get => _smoothDistH;
        }
        public static float _smoothDistH;

        [Property("Separated Threshold Mult (Hover)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.5 - any, default 1.0"
        )]
        public static float sepMultH
        {
            set => _sepMultH = Math.Clamp(value, 0.5f, 100000.0f);
            get => _sepMultH;
        }
        public static float _sepMultH;

        [Property("Accel Response Aggressiveness (Hover)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0"
        )]
        public static float aResponseH
        { 
            set => _aResponseH = Math.Max(value, 0.0f);
            get => _aResponseH;
        }
        public static float _aResponseH;

        [Property("Directional Antichatter Threshold (Hover)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0"
        )]
        public static float dacOuterH
        { 
            set => _dacOuterH = Math.Max(value, 0.0f);
            get => _dacOuterH;
        }
        public static float _dacOuterH;

        [Property("Area Scale (Hover)"), DefaultPropertyValue(0.5f), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5"
        )]
        public static float areaScaleH
        { 
            set => _areaScaleH = Math.Clamp(value, 0.01f, 5f);
            get => _areaScaleH;
        }
        public static float _areaScaleH;

        [Property("X Modifier (Hover)"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.01 - 100.0, default 1.0"
        )]
        public static float xModH
        { 
            set => _xModH = Math.Clamp(value, 0.01f, 100f);
            get => _xModH;
        }
        public static float _xModH;

        [Property("Prediction Ratio (Hover)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0"
        )]
        public static float frameShiftH
        { 
            set => _frameShiftH = Math.Clamp(value, 0.0f, 1.0f);
            get => _frameShiftH;
        }
        public static float _frameShiftH;

        [Property("Expected Milliseconds Per Report (Hover)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "You should know what you are doing if you change this from 0."
        )]
        public static float msOverrideH
        { 
            set => _msOverrideH = Math.Max(value, 0.0f);
            get => _msOverrideH;
        }
        public static float _msOverrideH;

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
}