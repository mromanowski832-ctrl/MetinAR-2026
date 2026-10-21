using System;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Engine-free framing contract of the procedural FPP sword (#31 owner retest on A31, 2026-10-21):
    /// the sword was too big and too close, the blade covered the screen centre and the guard spanned the
    /// full width. Rest and Aura-hold poses keep the whole sword in the lower-right part of a portrait frame
    /// and the sword model is drawn at <see cref="SwordScale"/> (hit sampling uses the same scaled blade).
    /// Camera-local Unity space: metres, +X right, +Y up, +Z forward; Euler degrees in Unity order (Y * X * Z).
    /// Technical prototype values; the physical A31 FOV is still UNVERIFIED.
    /// </summary>
    public static class FppViewFraming
    {
        /// <summary>Uniform scale of hilt, guard, pommel and blade around the grip (hands stay life-size).</summary>
        public const double SwordScale = 0.75;

        public static readonly FppPoint RestPosition = new FppPoint(0.09, -0.17, 0.46);
        public static readonly FppPoint RestEuler = new FppPoint(15, -30, -22);
        public static readonly FppPoint AuraPosition = new FppPoint(0.10, -0.14, 0.50);
        public static readonly FppPoint AuraEuler = new FppPoint(10, -30, -18);

        /// <summary>Unscaled sword model points in the RightGrip frame (blade along +Y).</summary>
        public const double ModelBladeBaseY = 0.13, ModelBladeTipY = 0.94, ModelGuardY = 0.12, ModelGuardHalfWidth = 0.125;
        public const double ModelLeftGripY = -0.12;

        public static double BladeBaseY => ModelBladeBaseY * SwordScale;
        public static double BladeTipY => ModelBladeTipY * SwordScale;
        public static double LeftGripY => ModelLeftGripY * SwordScale;

        /// <summary>Estimated A31 portrait AR camera (69.4 deg vertical, 540x1200). Not a device measurement.</summary>
        public const double EstimatedA31VerticalFovDegrees = 69.4;
        public const double EstimatedA31PortraitAspect = 540.0 / 1200.0;

        /// <summary>Rotates <paramref name="v"/> by Unity Quaternion.Euler(e.X, e.Y, e.Z) = Qy * Qx * Qz.</summary>
        public static FppPoint Rotate(FppPoint euler, FppPoint v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            double cz = Math.Cos(Rad(euler.Z)), sz = Math.Sin(Rad(euler.Z));
            double x1 = cz * x - sz * y, y1 = sz * x + cz * y, z1 = z;
            double cx = Math.Cos(Rad(euler.X)), sx = Math.Sin(Rad(euler.X));
            double y2 = cx * y1 - sx * z1, z2 = sx * y1 + cx * z1, x2 = x1;
            double cy = Math.Cos(Rad(euler.Y)), sy = Math.Sin(Rad(euler.Y));
            return new FppPoint(cy * x2 + sy * z2, y2, -sy * x2 + cy * z2);
        }

        /// <summary>Camera-local point of a RightGrip-frame point for a grip pose.</summary>
        public static FppPoint GripToCamera(FppPoint gripPosition, FppPoint gripEuler, FppPoint local)
        {
            FppPoint r = Rotate(gripEuler, local);
            return new FppPoint(gripPosition.X + r.X, gripPosition.Y + r.Y, gripPosition.Z + r.Z);
        }

        /// <summary>Normalised device coordinates (-1..1, centre 0,0) of a camera-local point; false behind the camera.</summary>
        public static bool TryProject(FppPoint p, double verticalFovDegrees, double aspect, out double ndcX, out double ndcY)
        {
            ndcX = ndcY = double.NaN;
            if (!p.IsFinite || p.Z <= 1e-4 || !(verticalFovDegrees > 0 && verticalFovDegrees < 180) || !(aspect > 0)) return false;
            double ty = Math.Tan(Rad(verticalFovDegrees) * 0.5), tx = ty * aspect;
            ndcX = p.X / p.Z / tx;
            ndcY = p.Y / p.Z / ty;
            return true;
        }

        private static double Rad(double degrees) => degrees * Math.PI / 180.0;
    }
}
