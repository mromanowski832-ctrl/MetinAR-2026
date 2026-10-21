using Xunit;

namespace MetinAR.Gameplay.Tests
{
    public class GameplayMathTests
    {
        [Theory]
        [InlineData(2.5, 2)]
        [InlineData(3.5, 4)]
        [InlineData(84.4, 84)]
        [InlineData(-0.5, 0)]
        public void RoundToInt_IsHalfToEven_LikePythonRound(double value, int expected)
        {
            Assert.Equal(expected, GameplayMath.RoundToInt(value));
        }

        [Theory]
        [InlineData(25, 10, 20)]
        [InlineData(35, 10, 40)]
        [InlineData(156, 10, 160)]
        [InlineData(117.5, 5, 120)]
        [InlineData(112.5, 5, 110)]
        public void RoundTo_MatchesGeneratorRoundTo(double value, int step, int expected)
        {
            Assert.Equal(expected, GameplayMath.RoundTo(value, step));
        }
    }
}
