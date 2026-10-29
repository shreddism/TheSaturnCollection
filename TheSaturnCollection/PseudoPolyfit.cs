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

        [Property("Dynamic Threshold"), DefaultPropertyValue(5), ToolTip
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
                    calc = true;
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

                int useDerivativesX = derivatives;
                int useDerivativesY = derivatives;

                int useSamples = samples;

                if (dynThreshold > 0.0) {
                    if (simplev[3].Length() < dynThreshold && simplea[3].Length() < dynThreshold * 8.0 && simplej[3].Length() < dynThreshold * 16.0) {
                        sampleFloor = Math.Max(1, Math.Min(samples, sampleFloor) - 4);
                    }
                    else sampleFloor++;

                    useSamples = Math.Max(1 + samples / 8, Math.Min(samples, sampleFloor));

                    if (derivatives > 1) {
                        useDerivativesX = DynamicDerivative(Math.Abs(simplev[7].X), Math.Abs(simplea[7].X), Math.Abs(simplej[7].X), ref derivativeFloorX);
                        useDerivativesY = DynamicDerivative(Math.Abs(simplev[7].Y), Math.Abs(simplea[7].Y), Math.Abs(simplej[7].Y), ref derivativeFloorY);
                    }
                }

                bool proper = false;

                bool cycle = false;

                bool fx = false;
                bool fy = false;
                while (!proper) {
                    tx = FitAtZero(weights![useDerivativesX][useSamples], ax!);
                    ty = FitAtZero(weights![useDerivativesY][useSamples], ay!);
                    double dx = Math.Abs(tx - cpos.X);
                    double dy = Math.Abs(ty - cpos.Y);

                    fx = ((fx) || (dynThreshold == 0.0) || dx < dynThreshold * 1.5);
                    fy = ((fy) || (dynThreshold == 0.0) || dy < dynThreshold * 1.5);

                    if (cycle) {
                        if (dx > cdx) {
                            tx = cx;
                        }
                        else {
                            cdx = dx;
                        }

                        if (dy > cdy) {
                            ty = cy;
                        }
                        else {
                            cdy = dy;
                        }
                    }
                    else {
                        cdx = dx;
                        cdy = dy;
                    }

                    cx = tx;
                    cy = ty;

                    if (!fx) { 
                        if (useDerivativesX < derivatives + 2) {
                            useDerivativesX++;
                            /*Console.WriteLine("x");
                            Console.WriteLine(useDerivativesX);
                            Console.WriteLine(useDerivativesY);*/
                        }
                        else fx = true;
                    }

                    if (!fy) {
                        if (useDerivativesY < derivatives + 2) {
                            useDerivativesY++;
                      /*      Console.WriteLine("y");
                            Console.WriteLine(useDerivativesX);
                            Console.WriteLine(useDerivativesY);*/
                        }
                        else fy = true;
                    }

                    cycle = true;

                    proper = fx & fy;
                }

                output = new Double2(tx, ty);


              //  output = Double2.Lerp(output, cpos, 0.5);
    
                lp = cpos;
                lo = output;
                report.Position = output.AsVector2();
            }
            Emit!.Invoke(value);
        }

        int DynamicDerivative(double v, double a, double j, ref int floor) {
            
            floor = Math.Max(derivatives - 3, Math.Min(1 + (sampleFloor / (derivatives / 2)), floor));
            
            if (v < dynThreshold * 7.5) {
                if (a < dynThreshold * 5.0 && j < dynThreshold * 5.0) {
                    if (simplev[7].Length() < dynThreshold * 3.0 && simplea[7].Length() < dynThreshold * 5.0) {
                        floor = Math.Min(derivatives - 3, floor + 1);
                    }
                    else {
                        floor = Math.Min(Math.Max(2, derivatives - 2), floor + 1);
                    }
                }
                else if (a < dynThreshold * 4.0 && j < dynThreshold * 4.0) {
                    floor = Math.Min(derivatives - 1, floor + 1);
                }
                else {
                    if (v > dynThreshold) floor = Math.Min(3, floor + 1);
                    else floor = Math.Min(Math.Max(3,derivatives - 2), floor + 1);
                }
            }
            else floor = Math.Min(derivatives, floor + 1);

            if ((sampleFloor > samples / 2) && (j < dynThreshold * 5.0) && (a < dynThreshold * 5.0)) {
                floor = Math.Min(Math.Max(3,derivatives - 1), floor);
            }

            if (simplej[7].Length() > dynThreshold * 16.0) {
                floor = derivatives;
            }

            return Math.Max(Math.Max(1, derivatives - 4), Math.Min(derivatives, floor));
        }

        bool calc = false;

        Double2 cpos, lp, output, lo;
        double tx, ty;
        double cx, cy;
        double cdx, cdy;
        double[]? ax, ay;

        int sampleFloor;
        int derivativeFloorX, derivativeFloorY;

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

        long tick;

        double[][][]? weights;
        double[][][]? weights2;

        [OnDependencyLoad]
        public void CalcStaticWeights() {
            int max = derivatives + 3;
            weights = new double[max][][];
            weights2 = new double[max][][];
            for (int i = 1; i < max; i++) {
                weights[i] = new double[samples + 1][];
                weights2[i] = new double[samples + 1][];
                for (int j = 1; j < samples + 1; j++) {
                    weights[i][j] = InterceptWeights(j, i);
                    weights2[i][j] = InterceptWeights(j, i);
                }
            }
            ax = new double[samples];
            ay = new double[samples];
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