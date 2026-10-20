using System;
using System.Numerics;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// 2-D affine transform  x' = A·x + B·y + C,  y' = D·x + E·y + F.  Used for the image ↔ model-tensor crops.
    /// <see cref="Multiply"/>(a, b) applies <c>b</c> first, then <c>a</c>.
    /// </summary>
    public readonly struct Affine2D
    {
        public readonly float A;
        public readonly float B;
        public readonly float C;
        public readonly float D;
        public readonly float E;
        public readonly float F;

        public Affine2D(float a, float b, float c, float d, float e, float f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        public static Affine2D Identity => new Affine2D(1f, 0f, 0f, 0f, 1f, 0f);

        public static Affine2D Translation(float x, float y) => new Affine2D(1f, 0f, x, 0f, 1f, y);

        public static Affine2D Translation(Vector2 t) => Translation(t.X, t.Y);

        public static Affine2D Scale(float sx, float sy) => new Affine2D(sx, 0f, 0f, 0f, sy, 0f);

        /// <summary>Rotation by <paramref name="radians"/>: (1,0) → (cos, sin), (0,1) → (−sin, cos).</summary>
        public static Affine2D Rotation(float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);
            return new Affine2D(c, -s, 0f, s, c, 0f);
        }

        public static Affine2D Multiply(Affine2D a, Affine2D b)
        {
            return new Affine2D(
                a.A * b.A + a.B * b.D,
                a.A * b.B + a.B * b.E,
                a.A * b.C + a.B * b.F + a.C,
                a.D * b.A + a.E * b.D,
                a.D * b.B + a.E * b.E,
                a.D * b.C + a.E * b.F + a.F);
        }

        public static Affine2D operator *(Affine2D a, Affine2D b) => Multiply(a, b);

        public float Determinant => A * E - B * D;

        public Vector2 Apply(Vector2 p) => new Vector2(A * p.X + B * p.Y + C, D * p.X + E * p.Y + F);

        public Vector2 Apply(float x, float y) => new Vector2(A * x + B * y + C, D * x + E * y + F);

        /// <summary>Linear part only (directions / offsets).</summary>
        public Vector2 ApplyVector(Vector2 v) => new Vector2(A * v.X + B * v.Y, D * v.X + E * v.Y);

        public Affine2D Inverse()
        {
            float det = Determinant;
            if (MathF.Abs(det) < 1e-12f || float.IsNaN(det))
            {
                throw new InvalidOperationException("Affine2D is not invertible.");
            }

            float inv = 1f / det;
            float a = E * inv;
            float b = -B * inv;
            float d = -D * inv;
            float e = A * inv;
            return new Affine2D(a, b, -(a * C + b * F), d, e, -(d * C + e * F));
        }

        public override string ToString() => $"[{A:0.###} {B:0.###} {C:0.###}; {D:0.###} {E:0.###} {F:0.###}]";
    }
}
