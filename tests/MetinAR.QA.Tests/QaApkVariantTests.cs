using MetinAR.Editor;
using Xunit;

namespace MetinAR.QA.Tests
{
    public class QaApkVariantTests
    {
        [Fact]
        public void QaIdentityIsSeparateFromTheGame()
        {
            Assert.Equal("com.metinar.game.qa", QaApkVariant.ApplicationIdentifier);
            Assert.Null(QaApkVariant.SideBySideProblem("com.metinar.game", QaApkVariant.ApplicationIdentifier));
            Assert.NotEqual("MetinAR", QaApkVariant.ProductName);
            Assert.EndsWith("-qa.apk", QaApkVariant.ApkRelativePath);
            Assert.NotEqual("Builds/Android/MetinAR-ar-encounter-dev.apk", QaApkVariant.ApkRelativePath);
        }

        [Fact]
        public void SameIdAsTheGameIsRejected()
        {
            Assert.Contains("would replace", QaApkVariant.SideBySideProblem("com.metinar.game", "com.metinar.game"));
        }

        [Fact]
        public void DifferentCaseIsASeparatePackage()
        {
            // Android compares package names exactly; documenting that the check is ordinal.
            Assert.Null(QaApkVariant.SideBySideProblem("com.metinar.game", "com.metinar.Game"));
        }

        [Theory]
        [InlineData("com.metinar.game.qa", true)]
        [InlineData("com.metinar.game.mappoc", true)]
        [InlineData("com.metinar_2.qa", true)]
        [InlineData("metinar", false)]
        [InlineData("com..metinar", false)]
        [InlineData("com.metinar.", false)]
        [InlineData("com.1metinar", false)]
        [InlineData("com.metinar-game", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AndroidApplicationIdSyntax(string id, bool valid)
        {
            Assert.Equal(valid, QaApkVariant.IsValidAndroidApplicationId(id));
            if (!valid)
            {
                Assert.Contains("invalid", QaApkVariant.SideBySideProblem("com.metinar.game", id));
            }
        }
    }
}
