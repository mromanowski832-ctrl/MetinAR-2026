using System;
using System.IO;

namespace MetinAR.Gameplay.Tests
{
    /// <summary>Locates the repository and reads the spec tables in docs/design/tables.</summary>
    internal static class TestData
    {
        private static readonly Lazy<string> Root = new Lazy<string>(FindRoot);

        public static string RepoRoot { get { return Root.Value; } }

        /// <summary>Spec table from docs/design/tables (levels 1–99 since PR #61).</summary>
        public static string RepoTable(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepoRoot, "docs", "design", "tables", fileName));
        }

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "docs", "design", "tables")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Repository root (docs/design/tables) not found from " + AppContext.BaseDirectory);
        }
    }

    /// <summary>Random source returning scripted values; fails the test when more values are drawn than scripted.</summary>
    internal sealed class ScriptedRandom : IGameplayRandom
    {
        private readonly double[] _values;
        public int Calls { get; private set; }

        public ScriptedRandom(params double[] values) { _values = values; }

        public double NextDouble()
        {
            if (Calls >= _values.Length) throw new InvalidOperationException("Unexpected random draw #" + (Calls + 1));
            return _values[Calls++];
        }
    }
}
