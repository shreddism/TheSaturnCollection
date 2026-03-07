using System;
using System.Numerics;

namespace Saturn {
    public static class Utils {
        public static Vector2 Default(Vector2 a, Vector2 b) => vec2IsFinite(a) ? a : b;      
        public static Vector2 WireMultAdjust(Vector2 a, float be, float br, bool w) => w ? a * Math.Clamp(br / be, 0, 1.5f) : a;
        public static Vector2 MinLength(Vector2 a, Vector2 b) => a.LengthSquared() <= b.LengthSquared() ? a : b;
        public static Vector2 MaxLength(Vector2 a, Vector2 b) => a.LengthSquared() >= b.LengthSquared() ? a : b;

        public static float Default(float a, float b) => float.IsFinite(a) ? a : b;
        public static float WireMultAdjust(float a, float be, float br, bool w) => w ? a * Math.Clamp(br / be, 0, 1.5f) : a;
        public static float DotNorm(Vector2 a, Vector2 b) => Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b));
        public static float DotNorm(Vector2 a, Vector2 b, float x) => (a != Vector2.Zero && b != Vector2.Zero) ? Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)) : x;
        
        public static bool vec2IsFinite(Vector2 vec) => float.IsFinite(vec.X) & float.IsFinite(vec.Y);

        public static void InsertAtFirst<T>(T[] arr, T element)
        {
            for (int p = arr.Length - 1; p > 0; p--) arr[p] = arr[p - 1];
            arr[0] = element;
        }

        public static float Smoothstep(float x, float start, float end)
        {
            x = Math.Clamp((x - start) / (end - start), 0.0f, 1.0f);
            return x * x * (3.0f - 2.0f * x);
        }

        public static Vector2 Trajectory(Vector2 p0, Vector2 p1, Vector2 p2, float t) {
            Vector2 tMid = 0.5f * (p0 + p2);
            return p2 + t * ((2 * p1) - p2 - tMid) + 0.5f * t * t * (2 * (tMid - p1));
        }
        
        public static Vector2 PathDiff(Vector2 s, Vector2 e, Vector2 p) {
            Vector2 mp = p - s;
            Vector2 me = e - s;
            float ca = -MathF.Atan2(me.Y, me.X);
            Vector2 rp = Rotate(mp, ca);
            Vector2 re = Rotate(me, ca);
            return rp - re;
        }

        public static Vector2 Rotate(Vector2 p, float a) {
            float cosine = MathF.Cos(a);
            float sine = MathF.Sin(a);
            return new Vector2((cosine * p.X) - (sine * p.Y), (sine * p.X) + (cosine * p.Y));
        }
    }
}