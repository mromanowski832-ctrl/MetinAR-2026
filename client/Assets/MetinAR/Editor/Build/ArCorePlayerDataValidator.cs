using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MetinAR.Editor.Build
{
    /// <summary>
    /// Engine-free check that an Android APK can start ARCore (#31, integration APK f194aa9 on A31:
    /// "Failed to load session subsystem" → AR unsupported). ARCore's C# subsystem descriptors (session, camera, planes,
    /// anchors, raycasts, ...) are registered by <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> methods
    /// listed in <c>assets/bin/Data/RuntimeInitializeOnLoads.json</c>. The broken APK had no <c>Unity.XR.ARCore</c>
    /// entries there because it was built while the editor's ACTIVE build target was not Android. The native
    /// <c>UnitySubsystemsManifest.json</c> of ARCore only declares the input subsystem (identical in working and broken
    /// APKs) — the session descriptor is never in it.
    /// </summary>
    public static class ArCorePlayerDataValidator
    {
        public const string RuntimeInitializeOnLoadsEntry = "assets/bin/Data/RuntimeInitializeOnLoads.json";
        public const string ArCoreSubsystemsManifestEntry = "assets/bin/Data/UnitySubsystems/UnityARCore/UnitySubsystemsManifest.json";
        public const string ArCoreNativeLibraryEntry = "lib/arm64-v8a/libUnityARCore.so";

        /// <summary>
        /// All 11 <c>Unity.XR.ARCore</c> runtime-initialize registrations of ARCore 6.x as found in the working A31 APK
        /// (8d0acee). Session is the one whose absence broke f194aa9; v0 requires all of them.
        /// </summary>
        public static readonly string[] RequiredRegistrations =
        {
            "ARCoreSessionSubsystem.RegisterDescriptor",
            "ARCoreCameraSubsystem.Register",
            "ARCorePlaneSubsystem.RegisterDescriptor",
            "ARCoreRaycastSubsystem.RegisterDescriptor",
            "ARCoreAnchorSubsystem.RegisterDescriptor",
            "ARCorePointCloudSubsystem.RegisterDescriptor",
            "ARCoreEnvironmentProbeSubsystem.Register",
            "ARCoreFaceSubsystem.RegisterDescriptor",
            "ARCoreImageTrackingSubsystem.RegisterDescriptor",
            "ARCoreOcclusionSubsystem.Register",
            "InputLayoutLoader.RegisterLayouts",
        };

        /// <summary>How many of <see cref="RequiredRegistrations"/> the JSON contains (for the build log: "11/11").</summary>
        public static int CountRequiredRegistrations(string runtimeInitializeOnLoadsJson)
        {
            int n = 0;
            foreach (string required in RequiredRegistrations)
            {
                if (HasRegistration(runtimeInitializeOnLoadsJson, "Unity.XR.ARCore", required)) n++;
            }

            return n;
        }

        /// <summary>
        /// Returns one problem per missing piece; empty = ARCore can register its subsystems.
        /// <paramref name="runtimeInitializeOnLoadsJson"/> is the content of <see cref="RuntimeInitializeOnLoadsEntry"/> (null if absent).
        /// </summary>
        public static IReadOnlyList<string> FindProblems(IEnumerable<string> apkEntryNames, string runtimeInitializeOnLoadsJson)
        {
            if (apkEntryNames == null) throw new ArgumentNullException(nameof(apkEntryNames));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string n in apkEntryNames)
            {
                if (!string.IsNullOrEmpty(n)) names.Add(n.Replace('\\', '/'));
            }

            var problems = new List<string>();
            if (!names.Contains(ArCoreNativeLibraryEntry)) problems.Add("missing " + ArCoreNativeLibraryEntry);
            if (!names.Contains(ArCoreSubsystemsManifestEntry)) problems.Add("missing " + ArCoreSubsystemsManifestEntry);
            if (!names.Contains(RuntimeInitializeOnLoadsEntry) || runtimeInitializeOnLoadsJson == null)
            {
                problems.Add("missing " + RuntimeInitializeOnLoadsEntry);
                return problems;
            }

            foreach (string required in RequiredRegistrations)
            {
                if (!HasRegistration(runtimeInitializeOnLoadsJson, "Unity.XR.ARCore", required))
                {
                    problems.Add(RuntimeInitializeOnLoadsEntry + " has no Unity.XR.ARCore " + required
                                 + " (ARCore subsystems will not register: 'Failed to load session subsystem'; build with the active target Android)");
                }
            }

            return problems;
        }

        /// <summary>True when the JSON lists <paramref name="classDotMethod"/> of <paramref name="assembly"/> in one record.</summary>
        public static bool HasRegistration(string json, string assembly, string classDotMethod)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(classDotMethod)) return false;
            int dot = classDotMethod.LastIndexOf('.');
            if (dot <= 0) return false;
            string cls = classDotMethod.Substring(0, dot), method = classDotMethod.Substring(dot + 1);
            foreach (Match record in Regex.Matches(json, @"\{[^{}]*\}"))
            {
                string r = record.Value;
                if (Field(r, "assemblyName") == assembly && Field(r, "className") == cls && Field(r, "methodName") == method) return true;
            }

            return false;
        }

        private static string Field(string record, string name)
        {
            Match m = Regex.Match(record, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
