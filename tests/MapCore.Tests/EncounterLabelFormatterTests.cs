using MetinAR.Map;
using Xunit;

namespace MapCore.Tests;

public class EncounterLabelFormatterTests
{
    [Fact]
    public void Notice_MatchesOwnerSpecExample()
    {
        Assert.Equal("Trujący Pająk \u2014 50 m", EncounterLabelFormatter.FormatNotice("Trujący Pająk", 51.7));
    }

    [Theory]
    [InlineData(0.2, "1 m")]
    [InlineData(7.4, "7 m")]
    [InlineData(12, "10 m")]
    [InlineData(13, "15 m")]
    [InlineData(97, "95 m")]
    [InlineData(99, "100 m")]
    [InlineData(123, "120 m")]
    [InlineData(994, "990 m")]
    [InlineData(996, "1,0 km")]
    [InlineData(1249, "1,2 km")]
    [InlineData(12345, "12,3 km")]
    [InlineData(double.NaN, "? m")]
    [InlineData(-3, "? m")]
    public void Distance_IsRoundedForStableDisplay(double meters, string expected)
    {
        Assert.Equal(expected, EncounterLabelFormatter.FormatDistance(meters));
    }

    [Fact]
    public void Marker_HasNameLevelDistance_AndAggressionMark()
    {
        Assert.Equal("Trujący Pająk (Poz. 5) !\n50 m", EncounterLabelFormatter.FormatMarker("Trujący Pająk", 5, 50, true));
        Assert.Equal("Wędrowny Handlarz (Poz. 1)\n120 m", EncounterLabelFormatter.FormatMarker(" Wędrowny Handlarz ", 0, 121, false));
        Assert.Equal("? \u2014 5 m", EncounterLabelFormatter.FormatNotice("  ", 5));
    }

    [Fact]
    public void Availability_Texts()
    {
        Assert.Contains("Walcz", EncounterLabelFormatter.FormatAvailability(EncounterAvailability.Available, 0));
        Assert.Equal("Podejdź bliżej na mapie (35 m)", EncounterLabelFormatter.FormatAvailability(EncounterAvailability.TooFar, 33));
        Assert.Equal("Słaby GPS \u2014 podejdź bliżej / poczekaj", EncounterLabelFormatter.FormatAvailability(EncounterAvailability.PoorAccuracy, 0));
        Assert.Equal("Zatrzymaj się przed włączeniem AR", EncounterLabelFormatter.FormatAvailability(EncounterAvailability.Moving, 0));
        Assert.Equal("", EncounterLabelFormatter.FormatAvailability(EncounterAvailability.None, 0));
    }

    [Fact]
    public void AvailableText_TellsToStopBeforeAr()
    {
        string t = EncounterLabelFormatter.FormatAvailability(EncounterAvailability.Available, 0);
        Assert.Contains("zatrzymaj się", t);
        Assert.Contains("AR", t);
    }

    /// <summary>Rule: movement happens only on the map with the camera off. No player-facing text may suggest walking in AR.</summary>
    [Fact]
    public void NoText_SuggestsWalkingWithArActive()
    {
        var texts = new List<string> { EncounterLabelFormatter.ArStayInPlaceRule };
        foreach (EncounterAvailability a in Enum.GetValues(typeof(EncounterAvailability)))
        {
            texts.Add(EncounterLabelFormatter.FormatAvailability(a, 12));
        }

        foreach (string t in texts)
        {
            string lower = t.ToLowerInvariant();
            bool mentionsAr = lower.Contains("ar") || lower.Contains("kamer");
            bool mentionsMove = lower.Contains("podejdź") || lower.Contains("idź") || lower.Contains("biegnij");
            Assert.False(mentionsAr && mentionsMove && !lower.Contains("na mapie") && !lower.Contains("nie chodź"), t);
        }

        Assert.Contains("na mapie", EncounterLabelFormatter.ArStayInPlaceRule);
        Assert.Contains("nie chodź", EncounterLabelFormatter.ArStayInPlaceRule);
    }
}
