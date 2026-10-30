using System;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using OpenTabletDriver.Plugin.DependencyInjection;
using static Saturn.Utils;

namespace Saturn
{
    [PluginName("Saturn - Pseudo-Polyfit Filter (Prototype)")]
    public class PseudoPolyfit : IPositionedPipelineElement<IDeviceReport>
    {
        public PseudoPolyfit() : base()
        {        
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        [Property("Samples"), DefaultPropertyValue(32), ToolTip
        (
            "Possible range: 8 - 128, default 32"
        )]
        public int samples
        {
            set => _samples = Math.Clamp(value, 8, 128);
            get => _samples;
        }
        public int _samples;

        [Property("Derivatives"), DefaultPropertyValue(5), ToolTip
        (
            "Possible range: 1 - 16, default 5"
        )]
        public int derivatives
        {
            set => _derivatives = Math.Clamp(value, 1, 16);
            get => _derivatives;
        }
        public int _derivatives;

        [Property("Dynamic Threshold"), DefaultPropertyValue(5.0), ToolTip
        (
            "Possible range: 0.0 - any, default 5\n\n" +
            "If this is set to 0, only the two above settings will apply.\n" +
            "Otherwise, used samples and derivatives will be chosen based on movement relative to this setting."
        )]
        public double dynThreshold
        {
            set => _dynThreshold = Math.Max(value, 0.0);
            get => _dynThreshold;
        }
        public double _dynThreshold;
    

        public event Action<IDeviceReport>? Emit;

        public HPETDeltaStopwatch perf = new HPETDeltaStopwatch();

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report) {
                if (!calc) {
                    CalcStaticWeights();
                }
                
                if (tick < long.MaxValue) tick++;

                cpos = new Double2(report.Position);
                UpdateTable(cpos);
                InsertAtFirst(ax!, cpos.X);
                InsertAtFirst(ay!, cpos.Y);

                if (tick < samples) {
                    Emit!.Invoke(value);
                    return;
                }

                int dfloor = 1;
                int dceiling = derivatives + 1;

                if (dynThreshold != 0.0) {
                    if (simplev[7].Length() < dynThreshold) {
                        ldFloor = 8;
                        hdFloor = Math.Min(samples / 2, derivatives);
                        dceiling = 3;
                    }
                    else {
                        ldFloor++;
                        if (simplev[7].Length() < dynThreshold * 2.0 && simplea[7].Length() < dynThreshold * 2.5 && simplej[7].Length() < dynThreshold * 5.0) {
                            hdFloor = samples / 2;
                            dfloor = 2;
                            dceiling = 4;
                        }
                        else {
                            hdFloor++;
                            dfloor = 3;
                        }
                    }
                }
                else {
                    ldFloor = samples;
                    hdFloor = samples;
                    dfloor = derivatives;
                }


                int usedx = dfloor;
                int usedy = dfloor;

                for (int i = dfloor; i < dceiling; i++) {
                    useSamples = Math.Min(samples, ((i > 3) ? hdFloor : ldFloor));
                    tax[i] = FitAtZero(weights![i][useSamples], ax!);
                    dx[i] = Math.Abs(tax[i] - ax[0]);

                    if (i > dfloor) {
                        if (dx[i - 1] > dx[i] + (dynThreshold * 0.1)) {
                            usedx = i; 
                        }
                        else if (dx[i - 1] < dynThreshold * 0.25 && dx[i] < dynThreshold * 0.25){
                            usedx = i - 1;
                            break;
                        }
                    }
                }

                for (int i = dfloor; i < dceiling; i++) {
                    useSamples = Math.Min(samples, ((i > 3) ? hdFloor : ldFloor));
                    tay[i] = FitAtZero(weights![i][useSamples], ay!);
                    dy[i] = Math.Abs(tay[i] - ay[0]);

                    if (i > dfloor) {
                        if (dy[i - 1] > dy[i] + (dynThreshold * 0.1)) {
                            usedy = i; 
                        }
                        else if (dy[i - 1] < dynThreshold * 0.25 && dy[i] < dynThreshold * 0.25){
                            usedy = i - 1;
                            break;
                        }
                    }
                }
                    
                output = new Double2(tax[usedx], tay[usedy]);
                
              
                lp = cpos;
                lo = output;
                report.Position = output.AsVector2();
            }
            Emit!.Invoke(value);
        }

        bool calc = false;

        Double2 cpos, lp, output, lo;
        double[]? dx, dy;
        double[]? ax, ay;
        double[]? tax, tay;
        
        long tick;

        double[][][]? weights;

        int useSamples;
        int ldFloor, hdFloor;

        Double2[] pos = new Double2[33];
        const int force_table_history = 8;
        Double2[] simplev = new Double2[force_table_history];
        Double2[] simplea = new Double2[force_table_history];
        Double2[] simplej = new Double2[force_table_history];

        public void UpdateTable(Double2 position) {
            InsertAtFirst(pos, position);
            for (int i = 0; i < force_table_history; i++) {
                int t1 = i + 1;
                double div = (double)t1;
                simplev[i] = (pos[0] - pos[t1]);
                simplea[i] = ((simplev[i]) - (pos[t1] - pos[t1 * 2]));
                simplej[i] = ((simplea[i]) - ((pos[t1 * 2] - pos[t1 * 3]) - (pos[t1 * 3] - pos[t1 * 4])));
            }
        }

        [OnDependencyLoad]
        public void CalcStaticWeights() {
            ldFloor = 1;
            hdFloor = 1;
            weights = new double[derivatives + 1][][];
            for (int i = 1; i < derivatives + 1; i++) {
                weights[i] = new double[samples + 1][];
                for (int j = 1; j < samples + 1; j++) {
                    weights[i][j] = InterceptWeights(j, i);
                }
            }
            ax = new double[samples];
            ay = new double[samples];
            dx = new double[derivatives + 1];
            dy = new double[derivatives + 1];
            tax = new double[derivatives + 1];
            tay = new double[derivatives + 1];

            calc = true;
        }

        static double[] InterceptWeights(int n, int degree)
        {
            if (n == 1) {
                double[] one = {1};
                return one;
            }

            var V = Matrix<double>.Build.Dense(n, degree + 1,
                (i, j) => Math.Pow(i / (double)(n - 1), j));

            return V.PseudoInverse().Row(0).ToArray();
        }

        static double FitAtZero(double[] w, double[] s)
        {
            if (w[0] == 1.0) {
                return s[0];
            }

            double sum = 0;
            for (int i = 0; i < w.Length; i++)
                sum += w[i] * s[i];
            return sum;
        }
        
    }
}