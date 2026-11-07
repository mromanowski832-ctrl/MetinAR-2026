using System;
using System.Text.RegularExpressions;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-008B P8: pure (no UnityEditor) description of the side-by-side map test variant and the
    /// save / apply / restore protocol for the PlayerSettings it temporarily switches. Unit-tested in
    /// tests/MapCore.Tests; the Unity adapter is <c>MapPocP8AndroidBuild</c>.
    /// The variant is a test tool id only. It is not a product rename and not for publishing.
    /// </summary>
    public static class MapPocP8BuildPlan
    {
        public const string VariantApplicationIdentifier = "com.metinar.game.mappoc";
        public const string VariantProductName = "MetinAR Mapa TEST";
        public const string ProductApplicationIdentifier = "com.metinar.game";
        public const string ApkRelativePath = "Builds/Android/MetinAR-mappoc-p8-dev.apk";

        // Android applicationId: >= 2 segments, each starting with a letter, [A-Za-z0-9_] only.
        private static readonly Regex AndroidId = new Regex("^[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z][A-Za-z0-9_]*)+$");

        public static bool IsValidAndroidApplicationId(string id)
        {
            return !string.IsNullOrEmpty(id) && AndroidId.IsMatch(id);
        }

        /// <summary>Values the variant build needs; everything else is taken from <paramref name="original"/>.</summary>
        public static BuildSettingsSnapshot VariantFrom(BuildSettingsSnapshot original, int il2cppBackend, int arm64Architecture,
            int gradleBuildSystem, int developmentBuildType)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            BuildSettingsSnapshot v = original.Clone();
            v.ApplicationIdentifier = VariantApplicationIdentifier;
            v.ProductName = VariantProductName;
            v.ScriptingBackend = il2cppBackend;
            v.TargetArchitectures = arm64Architecture;
            v.UseCustomKeystore = false;
            v.BuildAppBundle = false;
            v.AndroidBuildSystem = gradleBuildSystem;
            v.AndroidBuildType = developmentBuildType;
            return v;
        }

        /// <summary>
        /// Captures the current settings, applies <paramref name="variant"/>, runs <paramref name="body"/> and restores
        /// the captured values in a finally block, also when apply or body throws. Fields are restored one by one so a
        /// failing setter does not stop the rest from being restored; the first restore failure is rethrown afterwards.
        /// Refuses to start if the variant id is invalid or equals the product id (that would overwrite FPP on the phone).
        /// </summary>
        public static T RunWithVariant<T>(IBuildSettingsAccess access, Func<BuildSettingsSnapshot, BuildSettingsSnapshot> makeVariant,
            Func<T> body, out BuildSettingsSnapshot captured)
        {
            if (access == null) throw new ArgumentNullException(nameof(access));
            if (makeVariant == null) throw new ArgumentNullException(nameof(makeVariant));
            if (body == null) throw new ArgumentNullException(nameof(body));

            captured = access.Capture();
            BuildSettingsSnapshot variant = makeVariant(captured.Clone());
            if (!IsValidAndroidApplicationId(variant.ApplicationIdentifier))
                throw new InvalidOperationException("Invalid Android application id: " + variant.ApplicationIdentifier);
            if (string.Equals(variant.ApplicationIdentifier, captured.ApplicationIdentifier, StringComparison.Ordinal)
                || string.Equals(variant.ApplicationIdentifier, ProductApplicationIdentifier, StringComparison.Ordinal))
                throw new InvalidOperationException("Variant id must differ from the product id (would overwrite the installed game).");

            try
            {
                access.Apply(variant);
                return body();
            }
            finally
            {
                Restore(access, captured);
            }
        }

        private static void Restore(IBuildSettingsAccess access, BuildSettingsSnapshot original)
        {
            Exception first = null;
            foreach (Action step in access.RestoreSteps(original))
            {
                try { step(); }
                catch (Exception e) { if (first == null) first = e; }
            }

            access.Persist();
            if (first != null) throw new InvalidOperationException("PlayerSettings restore failed", first);
        }
    }

    /// <summary>Plain copy of every setting the P8 variant touches (Unity enums carried as ints).</summary>
    public sealed class BuildSettingsSnapshot
    {
        public string ApplicationIdentifier;
        public string ProductName;
        public int ScriptingBackend;
        public int TargetArchitectures;
        public bool UseCustomKeystore;
        public bool BuildAppBundle;
        public int AndroidBuildSystem;
        public int AndroidBuildType;

        public BuildSettingsSnapshot Clone()
        {
            return (BuildSettingsSnapshot)MemberwiseClone();
        }

        public bool SameAs(BuildSettingsSnapshot o)
        {
            return o != null && ApplicationIdentifier == o.ApplicationIdentifier && ProductName == o.ProductName
                   && ScriptingBackend == o.ScriptingBackend && TargetArchitectures == o.TargetArchitectures
                   && UseCustomKeystore == o.UseCustomKeystore && BuildAppBundle == o.BuildAppBundle
                   && AndroidBuildSystem == o.AndroidBuildSystem && AndroidBuildType == o.AndroidBuildType;
        }

        public override string ToString()
        {
            return "id=" + ApplicationIdentifier + " name=" + ProductName + " backend=" + ScriptingBackend + " arch=" + TargetArchitectures
                   + " customKeystore=" + UseCustomKeystore + " aab=" + BuildAppBundle + " buildSystem=" + AndroidBuildSystem
                   + " buildType=" + AndroidBuildType;
        }
    }

    /// <summary>Settings backend: PlayerSettings/EditorUserBuildSettings in Unity, a fake in tests.</summary>
    public interface IBuildSettingsAccess
    {
        BuildSettingsSnapshot Capture();
        void Apply(BuildSettingsSnapshot values);
        /// <summary>One independent action per field, so restore continues after a single failing setter.</summary>
        System.Collections.Generic.IEnumerable<Action> RestoreSteps(BuildSettingsSnapshot original);
        /// <summary>Write restored values to disk (AssetDatabase.SaveAssets in Unity).</summary>
        void Persist();
    }
}
