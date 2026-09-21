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
            "This filter component makes sure to recover to its input on any substantial movement."
        )]
        public double rInner
        { 
            set => _rInner = Math.Max(value, 0.0);
            get => _rInner;
        }
        public double _rInner;

        [Property("Distance Smoothing"), DefaultPropertyValue(50.0), ToolTip
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

            "This multiplies the amount of movement needed to recover from all smoothing to raw, not counting inner radius.\n" +  
            "The maximum amount of recovery is determined by the stock weight."
        )]
        public double sepMult
        {
            set => _sepMult = Math.Max(value, 0.5);
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
            "May not work well in certain cases.\n" +
            "General users - don't put above 0."
        )]
        public double aResponse
        { 
            set => _aResponse = Math.Max(value, 0.0);
            get => _aResponse;
        }
        public double _aResponse;

        [Property("Area Scale"), DefaultPropertyValue(0.5), ToolTip
        (
            "Possible range: 0.01 - 5.0, default 0.5\n\n" +

            "Multiplies every area-subjective threshold.\n" +
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
            "Only matters if you want to make the filter screen-space with a non-fixed aspect ratio.\n" +
            "Divide display area ratio by tablet area ratio, then use that value.\n" +
            "If you are unsure, see the wiki instructions."
        )]
        public double xMod
        { 
            set => _xMod = Math.Clamp(value, 0.01, 100.0);
            get => _xMod;
        }
        public double _xMod;

        [BooleanProperty("Read Config File", ""), DefaultPropertyValue(false), ToolTip
        (
            "A config file can be created which can pass extra settings to the filter.\n" +
            "The filter attempts to read the file for your specific tablet if enabled."
        )]
        public bool ExGate { set; get; }

        [BooleanProperty("Interpolation", ""), DefaultPropertyValue(false), ToolTip
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

        protected override void ConsumeState()
        {
            if (!configinit || config == null) {
                ReadConfig();
                configinit = true;
                return;
            }

            if (!init && !auxinit && State is IAuxReport aux) {
                filter = new MultifilterCore(this);
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
            if (ExGate) {
                configStatus = MultifilterConfigParser.ReadConfig(ref config, name);
                if (configStatus != 0) {
                    config = new MultifilterConfig();
                    Log.Write("Multifilter", "Config file could not be read. Resetting to defaults.", LogLevel.Warning, false, false);
                }
                else {
                    Log.Write("Multifilter", "Config file read successfully.", LogLevel.Info, false, false);

                }
            }
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
}