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

        private int tuneID;

        private double dt;

        int movementAxis;

        private Matrix x;
        private Matrix P;
        private Matrix Q;
        private Matrix tQ;
        private Matrix R;
        private Matrix tR;
        private Matrix H;

        public KalmanFilter(double initialPosition, InterpFilter filter, int axis)
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
            P = Matrix.Build.DenseIdentity(states);
            Q = Matrix.Build.DenseIdentity(states) * 1.0;
            tQ = Matrix.Build.DenseIdentity(states) * 1.0;
            R = Matrix.Build.DenseDiagonal(3, 3, 0.00001);
            tR = Matrix.Build.DenseDiagonal(3, 3, 0.00001);
            H = Matrix.Build.DenseDiagonal(3, states, 1);
        }

        public double Update(double measuredPos, InterpFilter filter)
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

            return (A * x)[0, 0];
        }

        private void DetermineQR(InterpFilter filter) {
            if (tuneID == 0) {
                tQ = Q;
                tR = R;
            }
            else {
                double v1, v2;
                switch (tuneID) {
                    case 1:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                            v2 = Math.Abs((filter.ddir[0]).X) + Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                            v2 = Math.Abs((filter.ddir[0]).Y) + Math.Abs((filter.ddir[0] - filter.ddir[1]).Y);
                        }
                        double fac = Smoothstep(v2 / spro(v1 / 10), 3.0, 6.0);
                        if (!filter.nonconf) {
                            tQ.data[0, 0] = 0.1;
                            tQ.data[1, 1] = 0.25 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                            tQ.data[2, 2] = 2.5 / (1 + fac * 5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                            tQ.data[3, 3] = 6.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * (10000 * Smoothstep(v2, 5.0, 25.0))) / spro(v1 / 25.0));
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 1.0 - 0.99999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                            tR.data[2,2] = 1.0 - 0.99999 * Math.Pow(Smoothstep((v1 + v2) + Vector2.Distance(filter.dir[0], filter.dir[2]), 5.0, 50.0), 1.0);
                        }
                        else {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.01;
                            tQ.data[2, 2] = 0.01;
                            tQ.data[3, 3] = 0.01;
                            tR.data[0,0] = 10000;
                            tR.data[1,1] = 100000;
                            tR.data[2,2] = 1000000;
                        }
                    break;
                    case 2:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                            v2 = Math.Abs((filter.ddir[0]).X) + Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                            v2 = Math.Abs((filter.ddir[0]).Y) + Math.Abs((filter.ddir[0] - filter.ddir[1]).Y);
                        }
                        if (!filter.nonconf) {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.25;
                            tQ.data[2, 2] = 1.0;
                            tQ.data[3, 3] = 60.0;
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 1.0 - 0.999999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                            tR.data[2,2] = 1.0 - 0.999999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 30.0), 2.0);
                        }
                        else {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.01;
                            tQ.data[2, 2] = 0.01;
                            tQ.data[3, 3] = 0.01;
                            tR.data[0,0] = 10000;
                            tR.data[1,1] = 100000;
                            tR.data[2,2] = 1000000;
                        }
                    break;
                    default:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                        }
                        tQ.data[0, 0] = 0.01;
                        tQ.data[1, 1] = 0.25;
                        tQ.data[2, 2] = 5.0;
                        tQ.data[3, 3] = 60.0;
                        tR.data[0, 0] = 0.00001;
                        tR.data[1, 1] = 0.001 - 0.00099 * Smoothstep(v1, 5.0, 10.0);
                        tR.data[2, 2] = 0.1 - 0.09999 * Smoothstep(v1, 5.0, 10.0);
                    break;
                }   
            }
        }

        private Matrix DetermineA(double[,] Aarr, InterpFilter filter) {
            Matrix A = Matrix.Build.DenseOfArray(Aarr);
            if (tuneID == 0) {
                return A;
            }
            else {
                double v1, v2, v3;
                switch (tuneID) {
                case 1:   
                    if (movementAxis == 0) {
                        v1 = Math.Abs(filter.dir[0].X - filter.dir[2].X);
                        v3 = Math.Abs(filter.ddir[0].X);
                        v2 = v3 / spro(Math.Abs(filter.dir[0].X / 10));
                    }
                    else {
                        v1 = Math.Abs(filter.dir[0].Y - filter.dir[2].Y);
                        v3 = Math.Abs(filter.ddir[0].Y);
                        v2 = v3 / spro(Math.Abs(filter.dir[0].Y / 10));
                    }
                    A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                    A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                break;
                case 2:
                break;
                default:
                break;
                }
            
            }
            return A;
        }

        private void DetermineX(InterpFilter filter) {
            if (tuneID == 0) {
                return;
            }
            else {
                double v1, v2, v3;
                switch (tuneID) {
                    case 1:
                        if (movementAxis == 0) {
                            v1 = (Math.Abs(filter.dir[0].X) + Math.Abs(filter.ddir[0].X));
                            v3 = Math.Abs(filter.dir[0].X - filter.dir[5].X);
                            v2 = v3 + Math.Abs(filter.ddir[0].X) + Math.Abs(filter.ddir[0].X - filter.ddir[1].X); 
                        }
                        else {
                            v1 = (Math.Abs(filter.dir[0].Y) + Math.Abs(filter.ddir[0].Y));
                            v3 = Math.Abs(filter.dir[0].Y - filter.dir[5].Y);
                            v2 = v3 + Math.Abs(filter.ddir[0].Y) + Math.Abs(filter.ddir[0].Y - filter.ddir[1].Y); 
                        }
                        double tmp1 = Smoothstep(v1, 10.0, 25.0);
                        x[2,0] *= Smoothstep(v2, 0.0, 7.5) * ((1.5 + tmp1) - (2.5 * tmp1) * Smoothstep(v3, 10.0, 5.0));
                    break;
                    case 2:
                    break;
                    default:
                    break;
                }
            }
        }
        
        public double Update2(double measuredPos, InterpFilter filter)
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

            Matrix A = DetermineA2(Aarr, filter);

            DetermineQR2(filter);

            x = A * x;
            P = A * P * A.Transpose() + tQ;

            var S = H * P * H.Transpose() + tR;
            var K = P * H.Transpose() * S.Inverse();

            x = x + K * (z - H * x);
            
            P = (Matrix.Build.DenseIdentity(states) - K * H) * P;

            DetermineX2(filter);

            return (A * x)[0, 0];
        }

        private void DetermineQR2(InterpFilter filter) {
            if (tuneID == 0) {
                tQ = Q;
                tR = R;
            }
            else {
                double v1, v2;
                switch (tuneID) {
                    case 1:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                            v2 = Math.Abs((filter.ddir[0]).X) + Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                            v2 = Math.Abs((filter.ddir[0]).Y) + Math.Abs((filter.ddir[0] - filter.ddir[1]).Y);
                        }
                        double fac = Smoothstep(v2 / spro(v1 / 10), 3.0, 6.0);
                        if (!filter.nonconf) {
                            tQ.data[0, 0] = 0.1;
                            tQ.data[1, 1] = 0.25 * (1 + fac * 5 * Smoothstep((v1 + v2), 5.0, 50.0));
                            tQ.data[2, 2] = 2.5 / (1 + fac * 5 * Smoothstep((Smoothstep(v2, 10.0, 50.0) * v1), 10.0, 50.0));
                            tQ.data[3, 3] = 6.0 * (1 + ((10 * Smoothstep(v1, 0.0, 5.0)) + fac * (10000 * Smoothstep(v2, 5.0, 25.0))) / spro(v1 / 25.0));
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 1.0 - 0.99999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                            tR.data[2,2] = 1.0 - 0.99999 * Math.Pow(Smoothstep((v1 + v2) + Vector2.Distance(filter.dir[0], filter.dir[2]), 5.0, 50.0), 1.0);
                        }
                        else {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.01;
                            tQ.data[2, 2] = 0.01;
                            tQ.data[3, 3] = 0.01;
                            tR.data[0,0] = 10000;
                            tR.data[1,1] = 100000;
                            tR.data[2,2] = 1000000;
                        }
                    break;
                    case 2:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                            v2 = Math.Abs((filter.ddir[0]).X) + Math.Abs((filter.ddir[0] - filter.ddir[1]).X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                            v2 = Math.Abs((filter.ddir[0]).Y) + Math.Abs((filter.ddir[0] - filter.ddir[1]).Y);
                        }
                        if (!filter.nonconf) {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.25;
                            tQ.data[2, 2] = 1.0;
                            tQ.data[3, 3] = 60.0;
                            tR.data[0,0] = 0.00001 - 0.000009 * Math.Pow(Smoothstep((v1 + v2), 0.0, 10.0), 0.5);
                            tR.data[1,1] = 1.0 - 0.999999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 20.0), 2.0);
                            tR.data[2,2] = 1.0 - 0.999999 * Math.Pow(Smoothstep((v1 + v2), 5.0, 30.0), 2.0);
                        }
                        else {
                            tQ.data[0, 0] = 0.01;
                            tQ.data[1, 1] = 0.01;
                            tQ.data[2, 2] = 0.01;
                            tQ.data[3, 3] = 0.01;
                            tR.data[0,0] = 10000;
                            tR.data[1,1] = 100000;
                            tR.data[2,2] = 1000000;
                        }
                    break;
                    default:
                        if (movementAxis == 0) {
                            v1 = Math.Abs(filter.dir[0].X);
                        }
                        else {
                            v1 = Math.Abs(filter.dir[0].Y);
                        }
                        tQ.data[0, 0] = 0.01;
                        tQ.data[1, 1] = 0.25;
                        tQ.data[2, 2] = 5.0;
                        tQ.data[3, 3] = 60.0;
                        tR.data[0, 0] = 0.00001;
                        tR.data[1, 1] = 0.001 - 0.00099 * Smoothstep(v1, 5.0, 10.0);
                        tR.data[2, 2] = 0.1 - 0.09999 * Smoothstep(v1, 5.0, 10.0);
                    break;
                }   
            }
        }

        private Matrix DetermineA2(double[,] Aarr, InterpFilter filter) {
            Matrix A = Matrix.Build.DenseOfArray(Aarr);
            if (tuneID == 0) {
                return A;
            }
            else {
                double v1, v2, v3;
                switch (tuneID) {
                case 1:   
                    if (movementAxis == 0) {
                        v1 = Math.Abs(filter.dir[0].X - filter.dir[2].X);
                        v3 = Math.Abs(filter.ddir[0].X);
                        v2 = v3 / spro(Math.Abs(filter.dir[0].X / 10));
                    }
                    else {
                        v1 = Math.Abs(filter.dir[0].Y - filter.dir[2].Y);
                        v3 = Math.Abs(filter.ddir[0].Y);
                        v2 = v3 / spro(Math.Abs(filter.dir[0].Y / 10));
                    }
                    A[2, 2] *= 0.5 + 0.5 * Math.Pow(Smoothstep(v1, 0.1, 5), 0.5);
                    A[2, 3] *= 5.0 * Smoothstep(v2, 0.0, v3 + 0.1);
                break;
                case 2:
                break;
                default:
                break;
                }
            
            }
            return A;
        }

        private void DetermineX2(InterpFilter filter) {
            if (tuneID == 0) {
                return;
            }
            else {
                double v1, v2, v3;
                switch (tuneID) {
                    case 1:
                        if (movementAxis == 0) {
                            v1 = (Math.Abs(filter.dir[0].X) + Math.Abs(filter.ddir[0].X));
                            v3 = Math.Abs(filter.dir[0].X - filter.dir[5].X);
                            v2 = v3 + Math.Abs(filter.ddir[0].X) + Math.Abs(filter.ddir[0].X - filter.ddir[1].X); 
                        }
                        else {
                            v1 = (Math.Abs(filter.dir[0].Y) + Math.Abs(filter.ddir[0].Y));
                            v3 = Math.Abs(filter.dir[0].Y - filter.dir[5].Y);
                            v2 = v3 + Math.Abs(filter.ddir[0].Y) + Math.Abs(filter.ddir[0].Y - filter.ddir[1].Y); 
                        }
                        double tmp1 = Smoothstep(v1, 10.0, 25.0);
                        x[2,0] *= Smoothstep(v2, 0.0, 7.5) * ((1.5 + tmp1) - (2.5 * tmp1) * Smoothstep(v3, 10.0, 5.0));
                    break;
                    case 2:
                    break;
                    default:
                    break;
                }
            }
        }
    }

    public class KalmanVector2
    {
        private KalmanFilter xFilter;
        private KalmanFilter yFilter;

        public KalmanVector2(Vector2 initialPosition, InterpFilter filter)
        {
            xFilter = new KalmanFilter(initialPosition.X, filter, 0);
            yFilter = new KalmanFilter(initialPosition.Y, filter, 1);
        }

        public Vector2 Update(Vector2 measuredPosition, InterpFilter filter)
        {
            float xState = (float)xFilter.Update(measuredPosition.X, filter); //Math.Abs((double)v1.X), Math.Abs((double)v2.X) + Math.Abs((double)v3.X));
            float yState = (float)yFilter.Update(measuredPosition.Y, filter); // Math.Abs((double)v1.Y), Math.Abs((double)v2.Y) + Math.Abs((double)v3.Y));
            return new Vector2(xState, yState);
        }

        public Vector2 Update2(Vector2 measuredPosition, InterpFilter filter)
        {
            float xState = (float)xFilter.Update2(measuredPosition.X, filter); //Math.Abs((double)v1.X), Math.Abs((double)v2.X) + Math.Abs((double)v3.X));
            float yState = (float)yFilter.Update2(measuredPosition.Y, filter); // Math.Abs((double)v1.Y), Math.Abs((double)v2.Y) + Math.Abs((double)v3.Y));
            return new Vector2(xState, yState);
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

    public class KalmanFolk
    {
        private readonly double[,] scale_const;
        private readonly int states;
        private double lastMeasuredPos;

        private Matrix x;
        private Matrix P;
        private Matrix Q;
        private Matrix R;
        private Matrix H;

        public KalmanFolk(uint statesNumber, double initialPosition)
        {
            states = (int)statesNumber + 2;

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

            lastMeasuredPos = initialPosition;
            double[,] xArr = new double[states, 1];
            xArr[0, 0] = initialPosition;

            x = Matrix.Build.DenseOfArray(xArr);
            P = Matrix.Build.DenseIdentity(states);
            Q = Matrix.Build.DenseIdentity(states) * 1.0;
            R = Matrix.Build.DenseDiagonal(2, 2, 0.00001);
            H = Matrix.Build.DenseDiagonal(2, states, 1);
        }

        public double Update(double measuredPos, double dt, bool nonconfident)
        {
            double measuredVel = (measuredPos - lastMeasuredPos) / dt;
            lastMeasuredPos = measuredPos;

            var z = Matrix.Build.DenseOfArray(new double[,] { { measuredPos }, { measuredVel } });

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

            /*
                vvvvvvvvvvv
            4 states should look like this
            double[,] Aarr = new double[,] {
                {          1,          dt^1/1!,    dt^2/2!,    dt^3/3!     },
                {          0,          1,          dt^1/1!,    dt^2/2!     },
                {          0,          0,          1,          dt^1/1!     },
                {          0,          0,          0,          1           }
            }
            */

            var A = Matrix.Build.DenseOfArray(Aarr);

            x = A * x;
            P = A * P * A.Transpose() + Q;

            var S = H * P * H.Transpose() + R;
            var K = P * H.Transpose() * S.Inverse();

            x = x + K * (z - H * x);
            P = (Matrix.Build.DenseIdentity(states) - K * H) * P;

            return (A * x)[0, 0];
        }
    }

    public class KalmanVectorFolk
    {
        private KalmanFolk xFilter;
        private KalmanFolk yFilter;

        public KalmanVectorFolk(uint states, Vector2 initialPosition)
        {
            xFilter = new KalmanFolk(states, initialPosition.X);
            yFilter = new KalmanFolk(states, initialPosition.Y);
        }

        public Vector2 Update(Vector2 measuredPosition, float dt, bool nonconfident)
        {
            float xState = (float)xFilter.Update(measuredPosition.X, dt, nonconfident);
            float yState = (float)yFilter.Update(measuredPosition.Y, dt, nonconfident);
            return new Vector2(xState, yState);
        }
    }
}