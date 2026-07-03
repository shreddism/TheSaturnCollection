using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Saturn 
{
    public static class Utils 
    {   
        public const int HMAX = 6;
        public const int KALMAN_STATES = 2;
    
        public static double Default(double a, double b) => double.IsFinite(a) ? a : b;
        public static double WireMultAdjust(double a, float be, double br, bool w) => w ? a * Math.Clamp(br / be, 0, 1.5) : a;
        public static double WirePowAdjust(double a, double be, double br, bool w) => w ? Math.Pow(a, Math.Max(be / br, 0.66)) : a;
        public static double WireWeightAdjust(double a, double be, double br, bool w) => w ? 1 - Math.Pow(1 - Math.Clamp(a, 0, 1), Math.Clamp(br / be, 0, 1.5)) : a;

        public static double XWA(double be, double br, bool b, double ce, double cr, bool c) 
        {
            if (b) 
                return Math.Clamp(br / be, 0.0, 1.5);
            if (c) 
                return Default(cr / ce, 1.0);
            return 1.0;
        }

        public static double UAdjust(double a, double b) => 1 - Math.Pow(1 - a, b);

        public static double spro(double x) => Math.Log(x + 1) + 1;

        public static double DSFunction(double dist, double smoothDist, double halfSmoothDist) 
        {
            if (dist >= smoothDist) 
                return dist - halfSmoothDist;

            double x = (dist / smoothDist);
            return x * x * halfSmoothDist;
        }

        public static void InsertAtFirst<T>(T[] arr, T element)
        {
            for (int p = arr.Length - 1; p > 0; p--) arr[p] = arr[p - 1];
            arr[0] = element;
        }

        public static void NonInsertAtFirst<T>(T[] arr)
        {
            for (int p = arr.Length - 1; p > 0; p--) arr[p] = arr[p - 1];
        }

        public static double Smoothstep(double x, double start, double end)
        {
            x = Math.Clamp((x - start) / (end - start), 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        public static double Step(double x, double start, double end) => Math.Clamp((x - start) / (end - start), 0.0, 1.0);

        public static Double2 Trajectory(Double2 p0, Double2 p1, Double2 p2, double t) {
            Double2 tMid = 0.5 * (p0 + p2);
            return p2 + t * ((2.0 * p1) - p2 - tMid) + 0.5 * t * t * (2.0 * (tMid - p1));
        }

        public static Double2 PathDiff(Double2 s, Double2 e, Double2 p) {
            Double2 mp = p - s;
            Double2 me = e - s;
            double ca = -me.Angle();
            Double2 rp = Double2.Rotate(mp, ca);
            Double2 re = Double2.Rotate(me, ca);
            return rp - re;
        }

        public static void Identify(string name, ref int ID) 
        {
            switch (name) {
                case "Wacom PTK-470":
                case "Wacom PTK-670":
                case "Wacom PTK-870":
                    ID = 1;
                break;
                case "Wacom PTH-460":
                case "Wacom PTH-660":
                case "Wacom PTH-860":
                    ID = 2;
                break;
                case "Wacom CTL-480":
                case "Wacom CTL-680":
                case "Wacom CTH-480":
                case "Wacom CTH-680":
                case "Wacom CTL-472":
                case "Wacom CTL-672":
                case "Wacom CTL-471":
                case "Wacom CTL-671":
                case "Wacom CTL-470":
                case "Wacom CTH-470":
                case "Wacom CTH-670":
                case "Wacom CTE-460":
                case "Wacom CTE-660":
                case "Wacom CTH-461":
                case "Wacom CTH-661":
                case "Wacom CTL-460":
                case "Wacom CTH-460":
                case "Wacom CTL-660":
                case "Wacom MTE-450":
                case "Wacom CTE-450":
                case "Wacom CTE-650":
                    ID = 3;
                break;
                case "Wacom PTH-451":
                case "Wacom PTH-651":
                case "Wacom PTH-851":
                case "Wacom PTH-450":
                case "Wacom PTK-450":
                case "Wacom PTH-650":
                case "Wacom PTK-650":
                case "Wacom PTH-850":
                case "Wacom PTK-440":
                case "Wacom PTK-540WL":
                case "Wacom PTK-640":
                case "Wacom PTK-840":
                case "Wacom PTK-1240":
                case "Wacom PTZ-430":
                case "Wacom PTZ-431W":
                case "Wacom PTZ-630":
                case "Wacom PTZ-631W":
                case "Wacom PTZ-930":
                case "Wacom PTZ-1230":
                case "Wacom PTZ-1231W":
                    ID = 4;
                break;
                case "Wacom CTL-4100":
                case "Wacom CTL-4100WL":
                case "Wacom CTL-6100":
                case "Wacom CTL-6100WL":
                case "Wacom CTL-490":
                case "Wacom CTL-690":
                case "Wacom CTH-490":
                case "Wacom CTH-690":
                    ID = 5;
                break;
                case "Gaomon S620":
                    ID = 6;
                break;
                default:
                    ID = 0;
                break;
            }
        }

        public static void PlotD(string c, Double2 p, bool t) 
        {
            Console.Write(c + "x");
            Console.WriteLine(p.X);
            Console.Write(c + "y");
            Console.WriteLine(p.Y * - 1);
            if (t) {
                Console.WriteLine("xx");
                Console.WriteLine("dd");
            }
        }
        
        public static void PointGraph(string c, Double2 p, long i) {
            Console.WriteLine(c + "_{" + i + "}=(" + p.X + "," + -p.Y + ")");
        }
    }

    public struct Double2 
    {
        public double X;
        public double Y;

        public Double2(double ix, double iy) 
        {
            this.X = ix;
            this.Y = iy;
        }

        public Double2(Vector2 i) 
        {
            this.X = (double)i.X;
            this.Y = (double)i.Y;
        }

        public static Double2 Zero => new Double2(0.0, 0.0);

        public double LengthSquared() => (X * X + Y * Y);
        public double Length() => Math.Sqrt(this.LengthSquared());
        public static double DistanceSquared(Double2 a, Double2 b)
        {
            double cx = a.X - b.X;
            double cy = a.Y - b.Y;
            return cx * cx + cy * cy;
        }
        public static double Distance(Double2 a, Double2 b) => Math.Sqrt(DistanceSquared(a, b));
        public Double2 Normalize() => (this != Double2.Zero) ? (this / this.Length()) : Double2.Zero;
        public static double Dot(Double2 a, Double2 b) => (a.X * b.X + a.Y * b.Y);
        public static double Cross(Double2 a, Double2 b) => (a.X * b.Y - a.Y * b.X);
        public static double DotOfNormalized(Double2 a, Double2 b) => (a != Double2.Zero && b != Double2.Zero) ? Dot(a.Normalize(), b.Normalize()) : 0.0;
        public static double CrossOfNormalized(Double2 a, Double2 b) => (a != Double2.Zero && b != Double2.Zero) ? Cross(a.Normalize(), b.Normalize()) : 0.0;

        public bool IsFinite() => double.IsFinite(X + Y);

        public double Angle() => Math.Atan2(Y, X);

        public Double2 DefaultZero() => this.IsFinite() ? this : Double2.Zero;

        public static Double2 Rotate(Double2 p, double a)
        {
            double cosine = Math.Cos(a);
            double sine = Math.Sin(a);
            return new Double2((cosine * p.X) - (sine * p.Y), (sine * p.X) + (cosine * p.Y));
        }

        public static Double2 Lerp(Double2 a, Double2 b, double c) {
            double scale = Math.Clamp(c, 0.0, 1.0);
            return new Double2(double.Lerp(a.X, b.X, c), double.Lerp(a.Y, b.Y, c));
        }

        public static Double2 Clamp(Double2 a, Double2 b, Double2 c) {
            double dx = Math.Clamp(a.X, Math.Min(b.X, c.X), Math.Max(b.X, c.X));
            double dy = Math.Clamp(a.Y, Math.Min(b.Y, c.Y), Math.Max(b.Y, c.Y));
            return new Double2(dx, dy);
        }

        public override string ToString() => $"({X}, {Y})";

        public Vector2 AsVector2() => new Vector2((float)X, (float)Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 operator +(Double2 a, Double2 b) => new Double2(a.X + b.X, a.Y + b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 operator -(Double2 a, Double2 b) => new Double2(a.X - b.X, a.Y - b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 operator *(Double2 a, double b) => new Double2(a.X * b, a.Y * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 operator *(double b, Double2 a) => (a * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 operator /(Double2 a, double b) => new Double2(a.X / b, a.Y / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Double2 a, Double2 b) => (a.X == b.X && a.Y == b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Double2 a, Double2 b) => !(a == b);

        public override bool Equals(object? x) => (x is Double2 b) ? (this.X == b.X && this.Y == b.Y) : false;

        public override int GetHashCode() => HashCode.Combine(X, Y);
    }
}