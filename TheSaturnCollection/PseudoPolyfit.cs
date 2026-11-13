using System;
using System.Numerics;
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
    [PluginName("Saturn - Pseudo-Polyfit Filter")]
    public class PseudoPolyfit : IPositionedPipelineElement<IDeviceReport>
    {
        public PseudoPolyfit() : base()
        {        
        }

        public PipelinePosition Position => PipelinePosition.PreTransform;

        const string POLYFIT_TOOLTIP = (
            "Your tablet should have a very high report rate for this filter to function well.\n" +
            "This filter makes a weighted sum of your last position values\n" +
            "that is identical to the result of fitting a polynomial of a chosen degree onto them,\n" +
            "given that your samples are evenly timed internally. If that is not the case, or if there are any major outliers,\n" +
            "this filter may not work as well."
        );
        
        [Property("Position - Samples"), DefaultPropertyValue(16), ToolTip
        (
            "Possible range: 8 - 128, default 16\n\n" + POLYFIT_TOOLTIP
        )]
        public int samples
        {
            set => _samples = Math.Clamp(value, 8, 128);
            get => _samples;
        }
        public int _samples = 16;

        [Property("Position - Derivatives"), DefaultPropertyValue(2), ToolTip
        (
            "Possible range: 1 - 16, default 2\n\n" + POLYFIT_TOOLTIP
        )]
        public int derivatives
        {
            set => _derivatives = Math.Clamp(value, 1, 16);
            get => _derivatives;
        }
        public int _derivatives = 2;

        [Property("Position - Dynamic Threshold"), DefaultPropertyValue(0.0), ToolTip
        (
            "Possible range: 0.0 - any, default 0.0\n\n" +
            "If this is set to 0, only the two above settings will apply.\n" +
            "Otherwise, used samples and derivatives will be chosen based on movement relative to this setting."
        )]
        public double dynThreshold
        {
            set => _dynThreshold = Math.Max(value, 0.0);
            get => _dynThreshold;
        }
        public double _dynThreshold = 0.0;

        [BooleanProperty("Position - Average Many Fits", ""), DefaultPropertyValue(true), ToolTip
        (
            "This *should* improve the filter's performance under noise and extreme movements.\n\n" +
            "If you encounter any weirdness, check this out first."
        )]
        public bool fitAverage { set; get; } = true;
        
        [Property("Filter Character"), DefaultPropertyValue(0.5), ToolTip
        (
            "Possible range: 0.0 - 1.0, default 0.0\n\n" +
            "This filter has two filters inside it which act separately but have very similar base workings\n" +
            "and need the same information, so it wouldn't work to have them be separate.\n" +
            "0.0 makes only the position filter above active,\n" +
            "1.0 makes only the velocity filter below active,\n" +
            "and 0.5 makes the final output halfway between the output of the two filters."
        )]
        public double character
        {
            set => _character = Math.Clamp(value, 0.0, 1.0);
            get => _character;
        }
        public double _character = 0.5;

        const string VELOCITY_TOOLTIP = (
            "This filter averages some number of pseudo-fits of per-report position change to 1 and 2 derivatives.\n" +
            "For each of the derivatives, the location is the number of samples the fit average starts at,\n" +
            "and the number of samples for the fits counts up to the count setting.\n" +
            "A higher number of samples being averaged means a cleaner path, but a less accurate one.\n" +
            "2-derivative fits naturally need more samples to be cleaner, but are more accurate under extreme movement.\n" +
            "It's probably better to average a higher number of fits (count) at a lower starting number of samples (location)."
        );

        [Property("Velocity - 1D Location"), DefaultPropertyValue(4), ToolTip
        (
            "Possible range: 2 - 32, default 4\n\n" + VELOCITY_TOOLTIP
        )]
        public int v1SampleRange
        {
            set => _v1SampleRange = Math.Clamp(value, 2, 32);
            get => _v1SampleRange;
        }
        public int _v1SampleRange = 4;

        [Property("Velocity - 1D Fit Count"), DefaultPropertyValue(4), ToolTip
        (
            "Possible range: 0 - 32, default 4\n\n" + VELOCITY_TOOLTIP
        )]
        public int v1SampleCount
        {
            set => _v1SampleCount = Math.Clamp(value, 0, 32);
            get => _v1SampleCount;
        }
        public int _v1SampleCount = 4;

        [Property("Velocity - 2D Location"), DefaultPropertyValue(8), ToolTip
        (
            "Possible range: 3 - 64, default 8\n\n" + VELOCITY_TOOLTIP
        )]
        public int v2SampleRange
        {
            set => _v2SampleRange = Math.Clamp(value, 3, 32);
            get => _v2SampleRange;
        }
        public int _v2SampleRange = 8;

        [Property("Velocity - 2D Fit Count"), DefaultPropertyValue(2), ToolTip
        (
            "Possible range: 0 - 32, default 2\n\n" + VELOCITY_TOOLTIP
        )]
        public int v2SampleCount
        {
            set => _v2SampleCount = Math.Clamp(value, 0, 32);
            get => _v2SampleCount;
        }
        public int _v2SampleCount = 2;
    
        public event Action<IDeviceReport>? Emit;

        public HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();

        public void Consume(IDeviceReport value)
        {
            if (value is ITabletReport report) {       
                if (!calc) {
                    CalcStaticWeights();
                }

                if (reportStopwatch.Restart().TotalMilliseconds > 10) tick = 0;

                if (tick < long.MaxValue) tick++;

                cpos = new Double2(report.Position);

                InsertAtFirst(pos, cpos);
                
                double angle = (pos[0] - pos[fulcrum]).Angle();
                for (int i = 0; i < adjpoint.Length; i++) {
                    adjpoint[i] = Double2.Rotate(pos[i] - pos[fulcrum], -angle);
                    ax[i] = adjpoint[i].X;
                    ay[i] = adjpoint[i].Y;
                    
                }

                InsertAtFirst(diffx, pos[0].X - pos[1].X);
                InsertAtFirst(diffy, pos[0].Y - pos[1].Y);

                if (tick < maxUsedSamples + 1) {
                    Emit.Invoke(value);
                    output2 = pos[0];
                    return;
                }

                if (character < 1.0) {
                    UpdateTable();

                    int useDerivativesX = derivatives;
                    int useDerivativesY = derivatives;
                    int useSamples = samples;

                    if (dynThreshold != 0.0) {
                        if (simplev[3].Length() < dynThreshold && simplea[3].Length() < dynThreshold * 8.0 && simplej[3].Length() < dynThreshold * 16.0) {
                            sampleFloor = Math.Max(1, Math.Min(samples, sampleFloor) - (samples / 4));
                        }
                        else sampleFloor++;

                        useSamples = Math.Max(samples / 2, Math.Min(samples, sampleFloor));

                        if (derivatives > 1) {
                            useDerivativesX = DynamicDerivative(Math.Abs(simplev[7].X), Math.Abs(simplea[7].X), Math.Abs(simplej[7].X), ref derivativeFloorX);
                            useDerivativesY = DynamicDerivative(Math.Abs(simplev[7].Y), Math.Abs(simplea[7].Y), Math.Abs(simplej[7].Y), ref derivativeFloorY);
                        }
                    }

                    if (dynThreshold != 0.0 && simplev[0].Length() < dynThreshold * 1.0 + 5.0 && simplej[7].Length() > dynThreshold * 4.0 + 20.0) useDerivativesX = Math.Min(derivatives + 1, useDerivativesX + 1);

                    if (dynThreshold != 0.0 && simplej[7].Length() > dynThreshold * 2.0 + 120.0 && simplev[7].Length() > dynThreshold + 100.0) {
                        useSamples = Math.Min(useSamples, samples / 2);
                    }

                    useSamples = Math.Max(Math.Max(useDerivativesX, useDerivativesY) + 1, useSamples);
                    
                    bool proper = false;
                    bool fx = (dynThreshold == 0.0);
                    bool fy = (dynThreshold == 0.0);
                    double tx = 0;
                    double ty = 0;

                    output1 = adjpoint[0];

                    while (!proper) {
                        if (fitAverage && derivatives < samples) {
                            int dxrStart = useDerivativesX - 1;
                            int dyrStart = useDerivativesY - 1;
                            int dxrEnd = useDerivativesX;
                            int dyrEnd = useDerivativesY;
                            int srStart = useSamples - 3;
                            int srEnd = useSamples;

                            if ((dynThreshold != 0.0) && (simplej[7].Length() > dynThreshold * 2.0 + 20.0)) {
                                dxrStart++;
                                dyrStart++;
                            }

                            if ((dynThreshold != 0.0) && (simplej[7].Length() > dynThreshold * 4.0 + 20.0 || simplea[7].Length() > dynThreshold * 4.0 + 20.0)) {
                                dxrEnd++;
                                dyrEnd++;
                            }

                            dxrStart = Math.Max(dxrStart, 1);
                            dyrStart = Math.Max(dyrStart, 1);
                            dxrEnd = Math.Max(dxrStart, Math.Min(derivatives, dxrEnd));
                            dyrEnd = Math.Max(dyrStart, Math.Min(derivatives, dyrEnd));
                            srStart = Math.Max(1, srStart);

                            double[] fitsx = new double[(dxrEnd - dxrStart) + 1];
                            double[] fitsy = new double[(dyrEnd - dyrStart) + 1];

                            tx = 0.0;
                            ty = 0.0;

                            for (int d = dxrStart; d < dxrEnd + 1; d++) {
                                int sampleshift = Math.Min(0, ((srStart - 1) * (d - dxrEnd)) / derivatives);
                                if (srStart + sampleshift < 1) sampleshift = -(srStart - 1);
                                for (int s = srStart; s < srEnd + 1; s++) {
                                    fitsx[d - dxrStart] += FitAtZero(weights[d][s + sampleshift][0], ax);
                                }
                                fitsx[d - dxrStart] /= (double)((srEnd - srStart) + 1);

                                tx += fitsx[d - dxrStart];
                            }
                            tx /= (double)((dxrEnd - dxrStart) + 1);


                            for (int d = dyrStart; d < dyrEnd + 1; d++) {
                                int sampleshift = Math.Min(0,((srStart - 1) * (d - dyrEnd)) / derivatives);
                                if (srStart + sampleshift < 1) sampleshift = -(srStart - 1);
                                for (int s = srStart; s < srEnd + 1; s++) {
                                    fitsy[d - dyrStart] += FitAtZero(weights[d][s + sampleshift][0], ay);
                                }
                                fitsy[d - dyrStart] /= (double)((srEnd - srStart) + 1);

                                ty += fitsy[d - dyrStart];
                            }
                            ty /= (double)((dyrEnd - dyrStart) + 1);
                        }
                        else {
                            tx = FitAtZero(weights[useDerivativesX][useSamples][0], ax);
                            ty = FitAtZero(weights[useDerivativesX][useSamples][0], ay);
                        }

                        double dx = Math.Abs(tx - adjpoint[0].X);
                        double dy = Math.Abs(ty - adjpoint[0].Y);

                        fx = ((fx) || dx < dynThreshold * 1.25);
                        fy = ((fy) || dy < dynThreshold * 1.25);

                        if (!fx) {
                            if (useDerivativesX < derivatives + 1) {
                                useDerivativesX++;
                            }
                            else fx = true;
                        }

                        if (!fy) {
                            if (useDerivativesY < derivatives + 1) {
                                useDerivativesY++;
                            }
                            else fy = true;
                        }
                        
                        proper = fx & fy;
                    }
                    output1 = new Double2(tx, ty);
                }

                if (character > 0.0 && divisor != 0.0) {
                    Double2 fitDir = Double2.Zero;                 

                    for (int i = v1SampleRange; i < v1SampleRange + v1SampleCount; i++) {
                        fitDir += FitV2(1, i, 0, weights, diffx, diffy);
                    }

                    for (int i = v2SampleRange; i < v2SampleRange + v2SampleCount; i++) {
                        fitDir += FitV2(2, i, 0, weights, diffx, diffy);
                    }

                    fitDir /= divisor;

                    output2 += fitDir;

                    if (tick < maxUsedSamples * 2 + 128) output2 = Double2.Lerp(output2, pos[0], 0.1);
                    else output2 = Double2.Lerp(output2, pos[0], 0.0001);
                }
                else output2 = pos[0];

                Double2 result = Double2.Lerp(Double2.Rotate(output1, angle) + pos[fulcrum], output2, character);

                report.Position = result.AsVector2();
            }
            Emit.Invoke(value);
        }

        int DynamicDerivative(double v, double a, double j, ref int floor) {
            floor = Math.Max(derivatives - 3, Math.Min(1 + (sampleFloor / (derivatives / 2)), floor));
            
            if (v < dynThreshold * 4.0 + 20.0) {
                if (a < dynThreshold * 2.0 && j < dynThreshold * 4.0) {
                    if (simplev[7].Length() < dynThreshold * 3.0 && simplea[7].Length() < dynThreshold * 5.0) {
                        floor = Math.Min(derivatives - 4, floor + 1);
                    }
                    else {
                        floor = Math.Min(Math.Max(2, derivatives - 3), floor + 1);
                    }
                }
                else if (a < dynThreshold * 2.0 + 10.0 && j < dynThreshold * 4.0 + 20.0) {
                    floor = Math.Min(derivatives - 3, floor + 1);
                }
                else {
                    if (v > dynThreshold) floor = Math.Min(3, floor + 1);
                    else floor = Math.Min(Math.Max(3,derivatives - 2), floor + 1);
                }
            }
            else floor = Math.Min(derivatives, floor + 1);

            if ((sampleFloor > samples / 2) && (j < dynThreshold * 2.0 + 20.0) && (a < dynThreshold + 10.0)) {
                floor = Math.Min(Math.Max(3,derivatives - 3), floor);
            }

            return Math.Max(Math.Max(1, derivatives - 4), Math.Min(derivatives, floor));
        }

        bool calc = false;

        double[]? diffx;
        double[]? diffy;

        double divisor;
        Double2 cpos, output1, output2;
        double[]? ax, ay;
        int fulcrum;
        int maxUsedSamples;
        int sampleFloor;
        int derivativeFloorX, derivativeFloorY;

        Double2[]? pos;
        Double2[]? adjpoint;
        const int force_table_history = 8;
        Double2[] simplev = new Double2[force_table_history];
        Double2[] simplea = new Double2[force_table_history];
        Double2[] simplej = new Double2[force_table_history];

        public void UpdateTable() {
            for (int i = 0; i < force_table_history; i++) {
                int t1 = i + 1;
                double div = (double)t1;
                simplev[i] = (adjpoint[0] - adjpoint[t1]);
                simplea[i] = ((simplev[i]) - (adjpoint[t1] - adjpoint[t1 * 2]));
                simplej[i] = ((simplea[i]) - ((adjpoint[t1 * 2] - adjpoint[t1 * 3]) - (adjpoint[t1 * 3] - adjpoint[t1 * 4])));
            }
        }

        long tick;

        static double[][][][]? weights;
        static int weightedDegree = 0;
        static int weightedSamples = 0;
        
        [OnDependencyLoad]
        public void CalcStaticWeights() {
            int maxSamples = Math.Max(samples + 1, Math.Max(v1SampleCount + v1SampleRange, v2SampleCount + v2SampleRange));
            int maxLength = Math.Max(33, maxSamples);
            maxUsedSamples = maxLength;
            diffx = new double[maxLength];
            diffy = new double[maxLength];
            derivatives = Math.Min(derivatives, samples - 1);
            fulcrum = Math.Max(1, samples / 8);
            pos = new Double2[maxLength];
            adjpoint = new Double2[maxLength];

            if (maxSamples != weightedSamples || derivatives != weightedDegree) {
                int max = derivatives + 3;
                weights = new double[max][][][];
                for (int i = 1; i < max; i++) {
                    weights[i] = new double[maxSamples + 1][][];
                    for (int j = 1; j < maxSamples + 1; j++) {
                        weights[i][j] = InterceptWeights(j, i);
                    }
                }
                weightedSamples = maxSamples;
                weightedDegree = derivatives;    
            }

            divisor = (double)(v1SampleCount + v2SampleCount);

            ax = new double[maxLength];
            ay = new double[maxLength];
            
            calc = true;
        }

        static double[][] InterceptWeights(int n, int degree)
        {
            if (n == 1) {
                double[][] oneArr = new double[1][];
                double[] one = {1};
                oneArr[0] = one;
                return oneArr;
            }

            var V = Matrix<double>.Build.Dense(n, degree + 1,
                (i, j) => Math.Pow(i / (double)(n - 1), j));

            double[][] res = new double[degree + 1][];

            for (int i = 0; i < degree + 1; i++) {
                res[i] = V.PseudoInverse().Row(i).ToArray();
            }
            return res;
        }
    }
}