using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using OpenTabletDriver.Plugin.Attributes;

namespace Saturn 
{
    public class MultifilterConfig 
    {
        public bool HoverSettingsEnabled { get; set; } = false;
        public double ReverseEmaHover { get; set; } = 1.0;
        public double StockEmaWeightHover { get; set; } = 1.0;
        public double InnerRadiusHover { get; set; } = 0.0;
        public double DistanceSmoothingHover { get; set; } = 0.0;
        public double SepMultHover { get; set; } = 1.0;
        public double AccelResponseHover { get; set; } = 0.0;
        public double DirectionalAntichatterHover { get; set; } = 0.0;
        public double AreaScaleHover { get; set; } = 1.0;
        public double XModifierHover { get; set; } = 1.0;
        public double PredictionRatioHover { get; set; } = 1.0;
        public double MsOverrideHover { get; set; } = 0.0;

        public double TimeScale { get; set; } = 1.0;
        public double DistanceSmoothingTimeScale { get; set; } = 1.0;
        public double AccelResponseTimeScale { get; set; } = 1.0;

        public bool DisableTabletTweaks { get; set; } = false;
        public bool VecMatMul { get; set; } = false;

        public bool ForceAltTiming { get; set; } = false;
        public double AltTimingConfidence { get; set; } = 0.1;

        public int ScaleStockEmaWeightByMovement { get; set; } = 0;                   
        public int ScaleInnerRadiusByMovement { get; set; } = 0;
        public int ScaleDistanceSmoothingByMovement { get; set; } = 0;
        public int ScaleSepMultByMovement { get; set; } = 0;
        public int ScaleAccelResponseByMovement { get; set; } = 0;
        public int ScaleDirectionalAntichatterByMovement { get; set; } = 0;
        public int ScaleAreaScaleByMovement { get; set; } = 0;
        public int ScaleXModifierByMovement { get; set; } = 0;
        public int ScalePredictionRatioByMovement { get; set; } = 0;
        public int ScaleTimeScaleByMovement { get; set; } = 0;
        public int ScaleDistanceSmoothingTimeScaleByMovement { get; set; } = 0;
        public int ScaleAccelResponseTimeScaleByMovement { get; set; } = 0;

        public double StartVelocityThreshold { get; set; } = 0.0;
        public double StartAccelThreshold { get; set; } = 0.0;
        public double StartJerkThreshold { get; set; } = 0.0;
        public double StartAbsAThreshold { get; set; } = 0.0;
        public double StartAAbsJThreshold { get; set; } = 0.0;
        public double StartAbsAAbsJThreshold { get; set; } = 0.0;

        public double EndVelocityThreshold { get; set; } = 0.0;
        public double EndAccelThreshold { get; set; } = 0.0;
        public double EndJerkThreshold { get; set; } = 0.0;
        public double EndAbsAThreshold { get; set; } = 0.0;
        public double EndAAbsJThreshold { get; set; } = 0.0;
        public double EndAbsAAbsJThreshold { get; set; } = 0.0;

        public double StartStockEmaWeightMult { get; set; } = 1.0;
        public double StartInnerRadiusMult { get; set; } = 1.0;
        public double StartDistanceSmoothingMult { get; set; } = 1.0;
        public double StartSepMultMult { get; set; } = 1.0;
        public double StartAccelResponseMult { get; set; } = 1.0;
        public double StartDirectionalAntichatterMult { get; set; } = 1.0;
        public double StartAreaScaleMult { get; set; } = 1.0;
        public double StartXModifierMult { get; set; } = 1.0;
        public double StartPredictionRatioMult { get; set; } = 1.0;
        public double StartTimeScaleMult { get; set; } = 1.0;
        public double StartDistanceSmoothingTimeScaleMult { get; set; } = 1.0;
        public double StartAccelResponseTimeScaleMult { get; set; } = 1.0;

        public double EndStockEmaWeightMult { get; set; } = 1.0;
        public double EndInnerRadiusMult { get; set; } = 1.0;
        public double EndDistanceSmoothingMult { get; set; } = 1.0;
        public double EndSepMultMult { get; set; } = 1.0;
        public double EndAccelResponseMult { get; set; } = 1.0;
        public double EndDirectionalAntichatterMult { get; set; } = 1.0;
        public double EndAreaScaleMult { get; set; } = 1.0;
        public double EndXModifierMult { get; set; } = 1.0;
        public double EndPredictionRatioMult { get; set; } = 1.0;
        public double EndTimeScaleMult { get; set; } = 1.0;
        public double EndDistanceSmoothingTimeScaleMult { get; set; } = 1.0;
        public double EndAccelResponseTimeScaleMult { get; set; } = 1.0;

        public double DistanceSmoothingPower { get; set; } = 2.0;

        public double AccelResponsePower { get; set; } = 3.0;
        public double AccelResponseBaseInnerDistanceThreshold { get; set; } = 500.0;
        public double AccelResponseBaseOuterDistanceThreshold { get; set; } = 3500.0;

        public int CompactFlags { get; set; } = 0;


        public bool Enabled { get; private set; } = false;
        public static bool vmm { get; private set; } = false;

        public void Enable() {
            this.Enabled = true;
            if (VecMatMul) {
                vmm = true;
            }
        }

    }

    public class MultifilterConfigParser 
    {
        public static int ReadConfig(ref MultifilterConfig config, string name) 
        {
            string exPath = Path.Combine(AppContext.BaseDirectory, "SaturnConfig", name + ".json");
            if (File.Exists(exPath)) {
                try {
                    string json = File.ReadAllText(exPath);
                    var loaded = JsonSerializer.Deserialize<MultifilterConfig>(json);
                    if (loaded != null) {
                        config = loaded;
                        config.Enable();
                    }
                    else {
                        return 3;
                    }
                }
                catch {
                    return 2;
                }
                return 0;
            } 
            else return 1;
        }
    }
}

