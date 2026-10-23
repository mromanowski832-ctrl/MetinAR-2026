using System;
using System.Text.RegularExpressions;

namespace MetinAR.Editor
{
    /// <summary>
    /// METINAR-037 (#73, audit T19): identity of the QA development APK. The QA build installs next to the game
    /// (<c>com.metinar.game</c>) and never replaces it on the shared test phone. Engine-free so the rules are unit-tested
    /// in tests/MetinAR.QA.Tests; the Unity side is <see cref="MetinARAndroidDevBuild.BuildArEncounterQaApk"/>.
    /// </summary>
    public static class QaApkVariant
    {
        public const string ApplicationIdentifier = "com.metinar.game.qa";
        public const string ProductName = "MetinAR QA";
        public const string ApkRelativePath = "Builds/Android/MetinAR-ar-encounter-qa.apk";

        // Android applicationId: at least two segments, each starting with a letter; letters, digits and '_' only.
        private static readonly Regex AndroidId = new Regex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant);

        public static bool IsValidAndroidApplicationId(string id)
        {
            return !string.IsNullOrEmpty(id) && AndroidId.IsMatch(id);
        }

        /// <summary>
        /// Returns null when <paramref name="variantId"/> can be installed side by side with <paramref name="gameId"/>,
        /// otherwise the reason. Android compares package names exactly (case-sensitive), so any other valid id is a
        /// separate app; the same id would replace the installed game.
        /// </summary>
        public static string SideBySideProblem(string gameId, string variantId)
        {
            if (!IsValidAndroidApplicationId(variantId))
            {
                return "invalid Android applicationId '" + variantId + "'";
            }

            if (string.Equals(gameId, variantId, StringComparison.Ordinal))
            {
                return "QA applicationId equals the game id '" + gameId + "' and would replace the installed game";
            }

            return null;
        }
    }
}
