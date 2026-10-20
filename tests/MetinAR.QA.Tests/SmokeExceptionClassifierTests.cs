using MetinAR.Editor;
using Xunit;

namespace MetinAR.QA.Tests
{
    /// <summary>METINAR-014: narrow classification of exceptions captured by the AR / world-map smoke runners.</summary>
    public class SmokeExceptionClassifierTests
    {
        // Verbatim shape of the exception reproduced on a fresh Library (clean main 8557a3e and METINAR-011 runs).
        private const string SearchDbCondition =
            "ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.\n"
            + "Parameter name: index\n"
            + "  at System.Collections.Generic.List`1[T].get_Item (System.Int32 index) [0x00009] in <c5ae8a4eeb824526b5df2932bc2395e0>:0 \n"
            + "  at UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78.MoveNext () [0x00089] in <76871fb05cc145ceba4a3eea6daeab4d>:0 \n"
            + "  at System.Linq.Enumerable.TryGetFirst[TSource] (System.Collections.Generic.IEnumerable`1[T] source, System.Boolean& found) [0x00045] in <f1b392d8fe224ca2b1f9fab1000b77eb>:0 \n"
            + "  at System.Linq.Enumerable.First[TSource] (System.Collections.Generic.IEnumerable`1[T] source) [0x00000] in <f1b392d8fe224ca2b1f9fab1000b77eb>:0 \n"
            + "  at UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase () [0x00005] in <76871fb05cc145ceba4a3eea6daeab4d>:0 \n"
            + "  at UnityEditor.Search.SearchInit.IndexationOnStartup () [0x0000a] in <76871fb05cc145ceba4a3eea6daeab4d>:0 \n"
            + "  at UnityEditor.EditorApplication.Internal_CallDelayFunctions () [0x00029] in <b5e1ac9fdc894216a0eeb18353904190>:0 ";

        private const string SearchDbUnityStack = "UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)\n";

        [Fact]
        public void KnownSearchDatabaseSignatureIsEditorInfrastructureNoise()
        {
            Assert.Equal(SmokeExceptionKind.KnownEditorInfrastructure, SmokeExceptionClassifier.Classify(SearchDbCondition, SearchDbUnityStack));
            Assert.False(SmokeExceptionClassifier.FailsSmoke(SearchDbCondition, SearchDbUnityStack));
        }

        [Fact]
        public void KnownSignatureInUnityColonFrameFormatIsAlsoRecognized()
        {
            string condition = "ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.\nParameter name: index";
            string stack =
                "System.Collections.Generic.List`1[T].get_Item (System.Int32 index)\n"
                + "UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78:MoveNext ()\n"
                + "System.Linq.Enumerable:First<UnityEditor.Search.SearchDatabase> (System.Collections.Generic.IEnumerable`1<UnityEditor.Search.SearchDatabase>)\n"
                + "UnityEditor.Search.SearchDatabase:GetDefaultSearchDatabase ()\n"
                + "UnityEditor.Search.SearchInit:IndexationOnStartup ()\n"
                + "UnityEditor.EditorApplication:Internal_CallDelayFunctions ()\n";
            // The first Unity frame has no ':' and is skipped by the parser; required + allowed frames still match.
            Assert.Equal(SmokeExceptionKind.KnownEditorInfrastructure, SmokeExceptionClassifier.Classify(condition, stack));
        }

        [Fact]
        public void InjectedMetinArExceptionFails()
        {
            string condition = "InvalidOperationException: METINAR-014 injected smoke failure";
            string stack = "MetinAR.Editor.ArEncounterSmokeTest/Runner:InjectMetinArException () (at Assets/MetinAR/Editor/ArEncounterSmokeTest.cs:900)\n"
                + "UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)\n";
            Assert.Equal(SmokeExceptionKind.MetinAR, SmokeExceptionClassifier.Classify(condition, stack));
            Assert.True(SmokeExceptionClassifier.FailsSmoke(condition, stack));
        }

        [Fact]
        public void MetinArCodeInsideKnownSignatureStillFails()
        {
            string condition = SearchDbCondition + "\n  at MetinAR.ArEncounter.ArEncounterSceneController.Update () [0x00001] in <x>:0 ";
            Assert.Equal(SmokeExceptionKind.MetinAR, SmokeExceptionClassifier.Classify(condition, SearchDbUnityStack));
        }

        [Fact]
        public void ProjectNameInMessageOrFolderDoesNotChangeClassification()
        {
            string condition = SearchDbCondition.Replace("Parameter name: index", "Parameter name: index (project C:/Users/x/MetinAR-011)");
            Assert.Equal(SmokeExceptionKind.KnownEditorInfrastructure, SmokeExceptionClassifier.Classify(condition, SearchDbUnityStack));
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify("NullReferenceException: MetinAR text only", "UnityEngine.Foo:Bar ()\n"));
        }

        [Fact]
        public void FrameFileInsideAssetsMetinArIsMetinAr()
        {
            string stack = "UnityEngine.Events.InvokableCall:Invoke () (at Assets/MetinAR/Scripts/UI/WorldMapHud.cs:42)\n";
            Assert.Equal(SmokeExceptionKind.MetinAR, SmokeExceptionClassifier.Classify("NullReferenceException: x", stack));
        }

        [Fact]
        public void ArbitraryUnityRuntimeExceptionFails()
        {
            string condition = "NullReferenceException: Object reference not set to an instance of an object";
            string stack = "UnityEngine.XR.ARFoundation.ARSession:Update () (at ./Library/PackageCache/com.unity.xr.arfoundation/Runtime/ARSession.cs:10)\n";
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify(condition, stack));
            Assert.True(SmokeExceptionClassifier.FailsSmoke(condition, stack));
        }

        [Fact]
        public void SameMessageWithoutSearchFramesFails()
        {
            string condition = "ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.\nParameter name: index";
            string stack = "System.Collections.Generic.List`1[T].get_Item (System.Int32 index)\nUnityEngine.XR.Simulation.SimulationSessionSubsystem:Update ()\n";
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify(condition, stack));
        }

        [Fact]
        public void SearchFramesWithDifferentExceptionTypeFails()
        {
            string condition = SearchDbCondition.Replace("ArgumentOutOfRangeException: Index was out of range", "NullReferenceException: Object reference not set");
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify(condition, SearchDbUnityStack));
        }

        [Fact]
        public void KnownSignatureWithAnExtraUnlistedFrameFails()
        {
            string condition = SearchDbCondition + "\n  at UnityEditor.AssetDatabase.Refresh () [0x00001] in <x>:0 ";
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify(condition, SearchDbUnityStack));
        }

        [Fact]
        public void OnlyOneOfTheRequiredSearchFramesFails()
        {
            string condition = SearchDbCondition.Replace("UnityEditor.Search.SearchInit.IndexationOnStartup", "UnityEditor.Search.SearchService.Request");
            Assert.Equal(SmokeExceptionKind.Unknown, SmokeExceptionClassifier.Classify(condition, SearchDbUnityStack));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("ArgumentOutOfRangeException: Index was out of range. Must be non-negative", "")]
        public void MissingOrUnprovableStackFails(string condition, string stack)
        {
            Assert.True(SmokeExceptionClassifier.FailsSmoke(condition, stack));
        }

        [Fact]
        public void FrameExtractionHandlesMonoAndUnityFormats()
        {
            var frames = SmokeExceptionClassifier.ExtractFrames(
                "ArgumentOutOfRangeException: Index was out of range. Must be non-negative (x)\n"
                + "  at UnityEditor.Search.SearchInit.IndexationOnStartup () [0x0000a] in <a>:0 \n"
                + "MetinAR.Editor.Foo:Bar (int) (at Assets/Foo.cs:1)\n"
                + "Parameter name: index\n");
            Assert.Equal(new[] { "UnityEditor.Search.SearchInit.IndexationOnStartup", "MetinAR.Editor.Foo.Bar" }, frames);
        }
    }
}
