using System;
using System.Numerics;
using static Saturn.Utils;

namespace Saturn 
{
    public class KalmanFilter
    {
        private readonly double[,] scale_const;
        private readonly int states;
        private double lastMeasuredPos;
        private double lastMeasuredVel;

        private double dt;

        int movementAxis;

        private Matrix x;
        public Matrix tx;
        private Matrix P;
        private Matrix Q;
        private Matrix tQ;
        private Matrix R;
        private Matrix tR;
        private Matrix H;

        public int tuneID;

        public KalmanFilter(double initialPosition, MultifilterCore filter, int axis)
        {
            states = KALMAN_STATES + 2;

            scale_const = new double[states, states];
            for (int i = 0; i < states; i++)
            {
                int fac_n = 1;
                int fac_i = 0;
                for (int j = i; j < states; j++)
                {
                    scale_const[i, j] = 1d / fac_n;
                    fac_i++;
                    fac_n *= fac_i;
                }
            }

            tuneID = filter.tabletType;

            movementAxis = axis;

            lastMeasuredPos = initialPosition;
            double[,] xArr = new double[states, 1];
            xArr[0, 0] = initialPosition;

            x = Matrix.Build.DenseOfArray(xArr);
            tx = Matrix.Build.DenseOfArray(xArr);
            P = Matrix.Build.DenseIdentity(states);
            Q = Matrix.Build.DenseIdentity(states) * 1.0;
            tQ = Matrix.Build.DenseIdentity(states) * 1.0;
            R = Matrix.Build.DenseDiagonal(3, 3, 0.00001);
            tR = Matrix.Build.DenseDiagonal(3, 3, 0.00001);
            H = Matrix.Build.DenseDiagonal(3, states, 1);
        }

        public double Update(double measuredPos, MultifilterCore filter)
        {
            dt = filter.secAvg;
            double measuredVel = (measuredPos - lastMeasuredPos) / dt;
            double measuredAccel = (measuredVel - lastMeasuredVel) / dt;
            lastMeasuredPos = measuredPos;
            lastMeasuredVel = measuredVel;

            var z = Matrix.Build.DenseOfArray(new double[,] { { measuredPos }, { measuredVel }, { measuredAccel }});

            double[,] Aarr = new double[states, states];
            for (int i = 0; i < states; i++) 
            {
                double time_pow = 1;
                for (int j = i; j < states; j++) 
                {
                    Aarr[i, j] = time_pow * scale_const[i, j];
                    time_pow *= dt;
                } 
            }

            Matrix A = DetermineA(Aarr, filter);
            
            DetermineQR(filter);

            x = A * x;
            P = A * P * A.Transpose() + tQ;

            var S = H * P * H.Transpose() + tR;
            var K = P * H.Transpose() * S.Inverse();

            x = x + K * (z - H * x);
            
            P = (Matrix.Build.DenseIdentity(states) - K * H) * P;

            DetermineX(filter);

            return (A * tx)[0, 0];
        }

        private void DetermineQR(MultifilterCore filter) {
            if (tuneID == 0) {
                tQ = Q;
                tR = R;
            }
            else {
                double v1, v2, v3, v4, fac;
                if (movementAxis == 0) {
                    v1 = Math.Abs(filter.dir[0].X);
                    v2 = Math.Abs((filter.ddir[0]).X) + Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                    v3 = Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                    v4 = Math.Abs((filter.dir[0] - filter.dir[2]).X);
                }
                else {
                    v1 = Math.Abs(filter.dir[0].Y);
                    v2 = Math.Abs((filter.ddir[0]).Y) + Math.Abs((filter.ddir[0] - filter.ddir[1]).Y);
                    v4 = Math.Abs((filter.dir[0] - filter.dir[2]).Y);
                }
                switch (tuneID) {
                    case 1:
                        fac = Smoothstep(v2 / spro(v1 / 10), 3.0, 6.0);
                        if (filter.pressure[0] > 0 && !filter.nonconf) {
                            tQ.data[0, 0] = 0.1;
                            tQ.data[1, 1] = 0.9 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                            tQ.data[2, 2] = 9 / (1 + fac * 5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                            tQ.data[3, 3] = 1 * 15.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * (12000 * Smoothstep(v2, 5.0, 25.0))) / spro(v1 / 25.0));
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 0.00001;
                            tR.data[2,2] = 0.00001;
                        }
                        else {
                            tQ.data[0, 0] = 1;
                            tQ.data[1, 1] = 5;
                            tQ.data[2, 2] = 25;
                            tQ.data[3, 3] = 125;
                            tR.data[0,0] = 0.00001;
                            tR.data[1,1] = 0.0001;
                            tR.data[2,2] = 0.001;
                        }
                    break;
                    case 2:
                        fac = Smoothstep(v2 / spro(v1 / 10), 3.0, 6.0);
                        if (filter.pressure[0] > 0 && !filter.nonconf) {
                            tQ.data[0, 0] = 0.1;
                            tQ.data[1, 1] = 0.9 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                            tQ.data[2, 2] = 9 / (1 + fac * 5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                            tQ.data[3, 3] = 1 * 15.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * (12000 * Smoothstep(v2, 5.0, 25.0))) / spro(v1 / 25.0));
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 0.00001;
                            tR.data[2,2] = 0.00001;
                        }
                        else {
                            tQ.data[0, 0] = 1;
                            tQ.data[1, 1] = 5;
                            tQ.data[2, 2] = 25;
                            tQ.data[3, 3] = 125;
                            tR.data[0,0] = 0.00001;
                            tR.data[1,1] = 0.0001;
                            tR.data[2,2] = 0.001;
                        }
                    break;
                    case 3:
                        fac = Smoothstep(v2 / spro(v1 / 10), 10.0, 20.0);
                        tQ.data[0, 0] = 0.1;
                        tQ.data[1, 1] = 1.125 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                        tQ.data[2, 2] = 22.5 / (1 + fac * 7.5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                        tQ.data[3, 3] = (30.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * ((1000 + 15000 * Step(v1, 500.0, 50.0)) * Smoothstep(v2, 5.0, 10.0))) / spro(v1 / 40)));
                        tR.data[0,0] = 0.0001 - 0.00009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                        tR.data[1,1] = 1.0 - 0.9999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                        tR.data[2,2] = 2.0 - 1.9999 * Math.Pow(Smoothstep((v1 + v2) + Vector2.Distance(filter.dir[0], filter.dir[2]), 5.0, 50.0), 1.0);
                    break;
                    case 4:
                        fac = Smoothstep(v2 / spro(v1 / 10), 3.0, 6.0);
                        tQ.data[0, 0] = 0.1;
                        tQ.data[1, 1] = 0.9 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                        tQ.data[2, 2] = 9 / (1 + fac * 5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                        tQ.data[3, 3] = 1 * 15.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * (12000 * Smoothstep(v2, 5.0, 25.0))) / spro(v1 / 25.0));
                        tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                        tR.data[1,1] = 0.00001;
                        tR.data[2,2] = 0.00001;
                    break;
                    case 5:
                        fac = Smoothstep(v2 / spro(v1 / 10), 10.0, 20.0);
                        if (filter.pressure[0] > 0 && !filter.nonconf) {
                            tQ.data[0, 0] = 0.1;
                            tQ.data[1, 1] = 1.125 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                            tQ.data[2, 2] = 22.5 / (1 + fac * 7.5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                            tQ.data[3, 3] = (30.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * ((1000 + 15000 * Step(v1, 500.0, 50.0)) * Smoothstep(v2, 5.0, 10.0))) / spro(v1 / 40)));
                            tR.data[0,0] = 0.0001 - 0.00009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 1.0 - 0.9999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                            tR.data[2,2] = 2.0 - 1.9999 * Math.Pow(Smoothstep((v1 + v2) + Vector2.Distance(filter.dir[0], filter.dir[2]), 5.0, 50.0), 1.0);
                        }
                        else {
                            tQ.data[0, 0] = 1;
                            tQ.data[1, 1] = 5;
                            tQ.data[2, 2] = 25;
                            tQ.data[3, 3] = 125;
                            tR.data[0,0] = 0.00001;
                            tR.data[1,1] = 0.0001;
                            tR.data[2,2] = 0.001;
                        }
                    break;
                    default:
                    break;
                }   
            }
        }

        private Matrix DetermineA(double[,] Aarr, MultifilterCore filter) {
            Matrix A = Matrix.Build.DenseOfArray(Aarr);
            if (tuneID == 0) {
                return A;
            }
            else {
                double v1, v2, v3, v4;
                if (movementAxis == 0) {
                    v1 = Math.Abs(filter.dir[0].X - filter.dir[2].X);
                    v3 = Math.Abs(filter.ddir[0].X);
                    v2 = v3 / spro(Math.Abs(filter.dir[0].X / 10));
                    v4 = Math.Abs(filter.dir[0].X);

                }
                else {
                    v1 = Math.Abs(filter.dir[0].Y - filter.dir[2].Y);
                    v3 = Math.Abs(filter.ddir[0].Y);
                    v2 = v3 / spro(Math.Abs(filter.dir[0].Y / 10));
                    v4 = Math.Abs(filter.dir[0].Y);
                }

                switch (tuneID) {
                case 1:
                    if (!filter.nonconf) {
                        v2 = v3 / spro(v4 / (6 + 12 * Step(v4, 0.0, 200.0)));
                        A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                        A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                    }                
                break;
                case 2:
                    if (!filter.nonconf) {
                        v2 = v3 / spro(v4 / (6 + 12 * Step(v4, 0.0, 200.0)));
                        A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                        A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                    }
                break;
                case 3:
                    A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                    A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                break;
                case 4:
                    v2 = v3 / spro(v4 / (6 + 12 * Step(v4, 0.0, 200.0)));
                    A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                    A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                break;
                case 5:
                    if (!filter.nonconf) {
                        A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                        A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                    }
                break;
                default:
                break;
                }
            
            }
            return A;
        }

        private void DetermineX(MultifilterCore filter) {
            tx = Matrix.Build.DenseOfArray((double[,])x.data.Clone());

            if (tuneID == 0) {
                return;
            }
            else {
                double v1, v2, v3, tmp1;
                if (movementAxis == 0) {
                    v1 = (Math.Abs(filter.dir[0].X) + Math.Abs(filter.ddir[0].X));
                    v3 = Math.Abs(filter.dir[0].X - filter.dir[5].X) + Math.Abs(filter.dir[0].X - filter.dir[2].X);
                    v2 = v3 + Math.Abs(filter.ddir[0].X) + Math.Abs(filter.ddir[0].X - filter.ddir[1].X); 
                }
                else {
                    v1 = (Math.Abs(filter.dir[0].Y) + Math.Abs(filter.ddir[0].Y));
                    v3 = Math.Abs(filter.dir[0].Y - filter.dir[5].Y) + Math.Abs(filter.dir[0].Y - filter.dir[2].Y);
                    v2 = v3 + Math.Abs(filter.ddir[0].Y) + Math.Abs(filter.ddir[0].Y - filter.ddir[1].Y); 
                }

                switch (tuneID) {
                    case 1:
                        if (!filter.nonconf) {
                            tmp1 = Step(v1, 5.0, 25.0);
                            tx[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (1.25 * tmp1) * Step(v3, 10.0, 0.0));
                            x[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (0.75 * tmp1) * Step(v3, 10.0, 0.0));
                        }
                    break;
                    case 2:
                        if (!filter.nonconf) {
                            tmp1 = Step(v1, 5.0, 25.0);
                            tx[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (1.25 * tmp1) * Step(v3, 10.0, 0.0));
                            x[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (0.75 * tmp1) * Step(v3, 10.0, 0.0));
                        }
                    break;
                    case 3:
                        tmp1 = Smoothstep(v1, 25.0, 250.0);
                        x[2,0] *= Smoothstep(v2, 50, 100.0) * ((0.2 + tmp1) - (tmp1) * Smoothstep(v3, 20.0, 10.0));
                    break;
                    case 4:
                        tmp1 = Step(v1, 5.0, 25.0);
                        tx[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (1.25 * tmp1) * Step(v3, 10.0, 0.0));
                        x[2,0] *= Step(v2, 5.0, 15.0) * ((1 + tmp1) - (0.75 * tmp1) * Step(v3, 10.0, 0.0));
                    break;
                    case 5:
                        if (!filter.nonconf) {
                            tmp1 = Smoothstep(v1, 25.0, 250.0);
                            x[2,0] *= Smoothstep(v2, 50, 100.0) * ((0.2 + tmp1) - (tmp1) * Smoothstep(v3, 20.0, 10.0));
                        }
                    break;
                    default:
                    break;
                }
            }
        }
    }

    public class KalmanVector2
    {
        public KalmanFilter xFilter;
        public KalmanFilter yFilter;

        public KalmanVector2(Vector2 initialPosition, MultifilterCore filter)
        {
            xFilter = new KalmanFilter(initialPosition.X, filter, 0);
            yFilter = new KalmanFilter(initialPosition.Y, filter, 1);
        }

        public Vector2 Update(Vector2 measuredPosition, MultifilterCore filter)
        {
            float xState = (float)xFilter.Update(measuredPosition.X, filter);
            float yState = (float)yFilter.Update(measuredPosition.Y, filter);
            return new Vector2(xState, yState);
        }

        public void SwapID(int type, MultifilterCore filter)
        {
            filter.tabletType = type;
            xFilter.tuneID = type;
            yFilter.tuneID = type;
        }
    }

    public class Matrix
    {
        public double[,] data;

        public Matrix(double[,] data)
        {
            this.data = data;
        }

        public int Rows => data.GetLength(0);
        public int Cols => data.GetLength(1);

        public double this[int i, int j]
        {
            get => data[i, j];
            set => data[i, j] = value;
        }

        public static Matrix operator +(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] + b[i, j];
            return new Matrix(result);
        }

        public static Matrix operator -(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] - b[i, j];
            return new Matrix(result);
        }

        public static Matrix operator *(Matrix a, Matrix b)
        {
            var result = new double[a.Rows, b.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < b.Cols; j++)
                    for (int k = 0; k < a.Cols; k++)
                        result[i, j] += a[i, k] * b[k, j];
            return new Matrix(result);
        }

        public static Matrix operator *(Matrix a, double scalar)
        {
            var result = new double[a.Rows, a.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < a.Cols; j++)
                    result[i, j] = a[i, j] * scalar;
            return new Matrix(result);
        }

        public Matrix Transpose()
        {
            var result = new double[Cols, Rows];
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    result[j, i] = data[i, j];
            return new Matrix(result);
        }

        public Matrix Inverse()
        {
            if (Rows != Cols) throw new InvalidOperationException("Matrix must be square to invert.");

            int n = Rows;
            var result = new double[n, n];
            var identity = Build.DenseIdentity(n).data;
            var copy = (double[,])data.Clone();

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    result[i, j] = identity[i, j];

            for (int i = 0; i < n; i++)
            {
                double diag = copy[i, i];
                if (diag == 0) throw new InvalidOperationException("Matrix is singular.");

                for (int j = 0; j < n; j++)
                {
                    copy[i, j] /= diag;
                    result[i, j] /= diag;
                }

                for (int k = 0; k < n; k++)
                {
                    if (k == i) continue;
                    double factor = copy[k, i];
                    for (int j = 0; j < n; j++)
                    {
                        copy[k, j] -= factor * copy[i, j];
                        result[k, j] -= factor * result[i, j];
                    }
                }
            }

            return new Matrix(result);
        }

        public static class Build
        {
            public static Matrix DenseOfArray(double[,] data) => new Matrix(data);

            public static Matrix DenseIdentity(int size)
            {
                var result = new double[size, size];
                for (int i = 0; i < size; i++) result[i, i] = 1;
                return new Matrix(result);
            }

            public static Matrix DenseDiagonal(int rows, int cols, Func<int, double> diagFunc)
            {
                var result = new double[rows, cols];
                for (int i = 0; i < Math.Min(rows, cols); i++)
                    result[i, i] = diagFunc(i);
                return new Matrix(result);
            }

            public static Matrix DenseDiagonal(int rows, int cols, double value)
            {
                return DenseDiagonal(rows, cols, _ => value);
            }
        }
    }
}