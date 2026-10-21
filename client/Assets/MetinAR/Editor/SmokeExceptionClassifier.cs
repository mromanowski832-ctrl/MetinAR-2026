using System;
using System.Collections.Generic;

namespace MetinAR.Editor
{
    /// <summary>How a logged exception is treated by Play Mode smoke tests (METINAR-014).</summary>
    public enum SmokeExceptionKind
    {
        /// <summary>The exception or its stack involves MetinAR code. Always fails the smoke test.</summary>
        MetinAR,

        /// <summary>Not attributable to MetinAR and not a known signature. Fails the smoke test by default.</summary>
        Unknown,

        /// <summary>
        /// A reproduced, explicitly listed Unity Editor infrastructure signature whose whole stack is outside MetinAR.
        /// Reported, but does not fail the gameplay smoke.
        /// </summary>
        KnownEditorInfrastructure,
    }

    /// <summary>
    /// Engine-free classifier for exceptions captured by the editor smoke runners (unit-tested in tests/MetinAR.QA.Tests).
    /// Deliberately narrow: there is exactly one known signature, the fresh-Library Unity Search indexer failure
    /// (reproduced in METINAR-011 on clean main 8557a3e):
    /// <code>
    /// ArgumentOutOfRangeException: Index was out of range. ...
    ///   at System.Collections.Generic.List`1[T].get_Item (System.Int32 index)
    ///   at UnityEditor.Search.SearchDatabase+&lt;EnumerateAll&gt;d__78.MoveNext ()
    ///   at System.Linq.Enumerable.TryGetFirst / First
    ///   at UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()
    ///   at UnityEditor.Search.SearchInit.IndexationOnStartup ()
    ///   at UnityEditor.EditorApplication.Internal_CallDelayFunctions ()
    /// </code>
    /// It only matches when the message is that ArgumentOutOfRangeException, the stack contains both
    /// SearchDatabase.GetDefaultSearchDatabase and SearchInit.IndexationOnStartup, every frame belongs to the short
    /// allow-list below, and no frame is MetinAR code. Anything else is MetinAR or Unknown and fails.
    /// </summary>
    public static class SmokeExceptionClassifier
    {
        public const string SearchDatabaseSignatureId = "unity-editor-search-database-startup-index";

        private const string KnownMessagePrefix = "ArgumentOutOfRangeException: Index was out of range";

        private static readonly string[] RequiredFrames =
        {
            "UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase",
            "UnityEditor.Search.SearchInit.IndexationOnStartup",
        };

        // Every stack frame of the known signature must start with one of these.
        private static readonly string[] AllowedFramePrefixes =
        {
            "System.Collections.Generic.List`1",
            "System.Linq.Enumerable.",
            "UnityEditor.Search.SearchDatabase",
            "UnityEditor.Search.SearchInit.IndexationOnStartup",
            "UnityEditor.EditorApplication.Internal_CallDelayFunctions",
            "UnityEngine.StackTraceUtility.ExtractStringFromExceptionInternal",
            "UnityEngine.StackTraceUtility.ExtractStackTrace",
            "UnityEngine.Debug.ExtractStackTraceNoAlloc",
        };

        public static SmokeExceptionKind Classify(string condition, string stackTrace)
        {
            if (InvolvesMetinArCode(condition, stackTrace))
            {
                return SmokeExceptionKind.MetinAR;
            }

            return IsSearchDatabaseStartupSignature(condition, stackTrace)
                ? SmokeExceptionKind.KnownEditorInfrastructure
                : SmokeExceptionKind.Unknown;
        }

        /// <summary>
        /// True when any stack frame is in a MetinAR namespace or any frame location points into Assets/MetinAR.
        /// Message text is not used (it may mention the project name), and neither is the project folder name.
        /// </summary>
        public static bool InvolvesMetinArCode(string condition, string stackTrace)
        {
            string text = (condition ?? string.Empty) + "\n" + (stackTrace ?? string.Empty);
            if (text.IndexOf("Assets/MetinAR/", StringComparison.Ordinal) >= 0 || text.IndexOf("Assets\\MetinAR\\", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            foreach (string frame in ExtractFrames(text))
            {
                if (frame.StartsWith("MetinAR.", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the exception should fail a smoke test (MetinAR or Unknown).</summary>
        public static bool FailsSmoke(string condition, string stackTrace)
        {
            return Classify(condition, stackTrace) != SmokeExceptionKind.KnownEditorInfrastructure;
        }

        private static bool IsSearchDatabaseStartupSignature(string condition, string stackTrace)
        {
            string message = (condition ?? string.Empty).TrimStart();
            if (!message.StartsWith(KnownMessagePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            List<string> frames = ExtractFrames((condition ?? string.Empty) + "\n" + (stackTrace ?? string.Empty));
            if (frames.Count == 0)
            {
                return false; // cannot prove the origin
            }

            foreach (string required in RequiredFrames)
            {
                bool found = false;
                foreach (string f in frames)
                {
                    if (f.StartsWith(required, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            foreach (string f in frames)
            {
                bool allowed = false;
                foreach (string prefix in AllowedFramePrefixes)
                {
                    if (f.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extracts normalized frame names from Mono ("  at Ns.Type.Method (args) [0x..] in &lt;..&gt;:0") and Unity
        /// ("Ns.Type:Method (args) (at File.cs:12)") stack lines. Message lines and "Parameter name:" lines are skipped.
        /// The Unity "Type:Method" separator is normalized to "Type.Method".
        /// </summary>
        public static List<string> ExtractFrames(string text)
        {
            var frames = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return frames;
            }

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                bool mono = line.StartsWith("at ", StringComparison.Ordinal);
                if (mono)
                {
                    line = line.Substring(3).TrimStart();
                }

                int paren = line.IndexOf(" (", StringComparison.Ordinal);
                if (paren < 0)
                {
                    paren = line.IndexOf('(');
                }

                if (paren <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, paren).Trim();
                if (name.IndexOf(' ') >= 0 || name.IndexOf('.') < 0 || name.EndsWith(":", StringComparison.Ordinal))
                {
                    continue; // e.g. "ArgumentOutOfRangeException: Index was out of range. Must be ..." message text
                }

                frames.Add(name.Replace(':', '.'));
            }

            return frames;
        }
    }
}
