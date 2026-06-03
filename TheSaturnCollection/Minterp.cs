using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Multifilter (Interpolated)")]
    public class MultifilterI : AsyncPositionedPipelineElement<IDeviceReport>
    {
        public MultifilterI() : base()
        {
        }

        public override PipelinePosition Position => PipelinePosition.PreTransform;


        [Property("Prediction Ratio (Hover Over The Textbox)"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Important: Only enable ONE multifilter, probably this one! That's why they're called multifilters!\n" +
            "Issues that arise from failing to read this are the user's fault.\n\n" +

            "Possible range: 0.0 - 1.0, default 0.0\n\n" +

            "Uses a Kalman filter that considers acceleration instead of just position and velocity.\n" +
            "Because of this, it has far less error under movement than what is seen in Temporal Resampler.\n" +
            "Beyond that, by default on certain tablets the filter's parameters are tuned for better accuracy."
        )]
        public float frameShift
        { 
            set => _frameShift = Math.Clamp(value, 0.0f, 1.0f);
            get => _frameShift;
        }
        public float _frameShift;

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

        [Property("Directional Antichatter Inner Threshold"), DefaultPropertyValue(0.0f), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +

            "Works somewhat like Devocub Antichatter, but placed on per-report direction. Units are in raw tablet data.\n" + 
            "This shouldn't go very high, maybe 2 at the highest.\n" + 
            "Internal thresholds are used to prevent this from messing things up horribly.\n" +
            "If you are unsure, keep at 0."
        )]
        public float dacInner
        { 
            set => _dacInner = Math.Max(value, 0.0f);
            get => _dacInner;
        }
        public float _dacInner;

        [Property("Directional Antichatter Outer Threshold"), DefaultPropertyValue(0.0f), ToolTip
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

        [Property("Wire - Filter Mode"), PropertyValidated(nameof(wireModes)), DefaultPropertyValue("Non-Wire - Interp"), ToolTip
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
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();

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

        [Property("Stock EMA Weight"), DefaultPropertyValue(1.0f), ToolTip
        (
            "Possible range: 0.001 - 1.0, default 1.0\n\n" +

            "EMA weight, but it can change based on the current situation.\n" +
            "The below options control adaptivity."
        )]
        public float stockWeight
        { 
            set => _stockWeight = Math.Clamp(value, 0.001f, 1.0f);
            get => _stockWeight;
        }
        public float _stockWeight;

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

            "Some like using Devocub or Radial Follow for their more exaggerated snap effect that they bring.\n" +
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

        [Property("Expected Milliseconds Per Report Override"), DefaultPropertyValue(0.0f), ToolTip
        (
            "You should know what you are doing if you change this from 0.\n" +
            "Wacom PTK-x70 - make this 3.302466 if using given pen, otherwise you are on your own."
        )]
        public float msOverride
        { 
            set => _msOverride = Math.Max(value, 0.0f);
            get => _msOverride;
        }
        public float _msOverride;

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

        [BooleanProperty("Tablet-Specific Tweaks", ""), DefaultPropertyValue(true), ToolTip
        (
            "If applicable, will change certain behaviors for certain tablets.\n" +
            "This inlcudes things like non-default dynamic Kalman filter parameters for improved accuracy and preventing bugs.\n" +
            "Don't disable unless you have a good reason to."
        )]
        public bool tabletToggle { set; get; }

        InterpFilter? filter;

        bool init;

        protected override void ConsumeState()
        {
            if (init && filter!.pflag && State is IProximityReport p) {
                if (p.NearProximity == false)
                    return;
            }
            if (State is ITabletReport report) {  
                if (!init) {
                    filter = new InterpFilter(this);
                    if (filter != null) {
                        filter!.Initialize(report);
                        init = true;
                    }
                    return;
                }
                int fawk = filter!.HandleConsume(report);
                if (fawk == 1) {
                    UpdateState();
                }
            }
            else {
                OnEmit();
            } 
        }

        protected override void UpdateState()
        {
            if (State is ITabletReport report && PenIsInRange() && init) {   
                filter!.HandleUpdate(report);
                
                OnEmit();
            }
        }

        

        [TabletReference]
        public TabletReference TabletReference { set { name = value.Properties.Name; } }
        public string name = string.Empty;
    }
}

