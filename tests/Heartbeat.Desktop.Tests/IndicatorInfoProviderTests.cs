using System.IO;

using Heartbeat.Desktop.Info;

namespace Heartbeat.Desktop.Tests;

public sealed class IndicatorInfoProviderTests
{
    private const string ValidIndicator = """
        { "id": "heartRate", "name": "Heart rate", "unit": "bpm", "hazard": "gravityFields",
          "whyItMatters": "why", "howToMeasure": ["step"], "risks": "risk", "sources": ["s1"] }
        """;

    private const string ValidSources = """{ "s1": { "title": "Fuente", "url": "https://www.nasa.gov/x" } }""";

    private readonly ReferenceRangesInfoProvider _embedded = new();

    private static string Json(string sources = ValidSources, string indicator = ValidIndicator)
        => "{ \"version\": \"1\", \"sources\": " + sources + ", \"indicators\": [ " + indicator + " ] }";

    [Fact]
    public void The_embedded_contract_has_the_six_indicators_in_order()
    {
        Assert.Equal(
            ["bloodPressure", "heartRate", "bloodSugar", "bodyTemperature", "sleep", "mood"],
            _embedded.Indicators.Select(i => i.Id));
        Assert.False(string.IsNullOrWhiteSpace(_embedded.RangesVersion));
    }

    [Fact]
    public void Every_indicator_explains_why_how_and_risks()
    {
        foreach (IndicatorInfo indicator in _embedded.Indicators)
        {
            Assert.False(string.IsNullOrWhiteSpace(indicator.Name), indicator.Id);
            Assert.False(string.IsNullOrWhiteSpace(indicator.Unit), indicator.Id);
            Assert.False(string.IsNullOrWhiteSpace(indicator.WhyItMatters), indicator.Id);
            Assert.False(string.IsNullOrWhiteSpace(indicator.Risks), indicator.Id);
            Assert.NotEmpty(indicator.HowToMeasure);
            Assert.All(indicator.HowToMeasure, step => Assert.False(string.IsNullOrWhiteSpace(step)));
        }
    }

    [Fact]
    public void Every_indicator_cites_nasa_and_only_https_sources()
    {
        foreach (IndicatorInfo indicator in _embedded.Indicators)
        {
            Assert.Contains(indicator.Sources, s => s.Url.Host.EndsWith("nasa.gov", StringComparison.OrdinalIgnoreCase));
            Assert.All(indicator.Sources, s =>
            {
                Assert.Equal(Uri.UriSchemeHttps, s.Url.Scheme);
                Assert.False(string.IsNullOrWhiteSpace(s.Title));
            });
        }
    }

    [Fact]
    public void Hazards_are_shown_by_their_spanish_name()
    {
        Assert.Equal("Campos de gravedad", _embedded.Find("heartRate")!.HazardName);
        Assert.Equal("Aislamiento y confinamiento", _embedded.Find("mood")!.HazardName);
        Assert.Equal("Entorno cerrado y hostil", _embedded.Find("bodyTemperature")!.HazardName);
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_id()
    {
        Assert.Null(_embedded.Find("nope"));
        Assert.NotNull(_embedded.Find("sleep"));
    }

    [Fact]
    public void A_valid_minimal_file_is_read()
    {
        ReferenceRangesInfoProvider provider = new(Json());

        IndicatorInfo info = Assert.Single(provider.Indicators);
        Assert.Equal("heartRate", info.Id);
        Assert.Equal("Fuente", Assert.Single(info.Sources).Title);
    }

    [Fact]
    public void An_unknown_hazard_falls_back_to_its_id()
    {
        ReferenceRangesInfoProvider provider = new(Json(indicator: ValidIndicator.Replace("gravityFields", "newHazard")));

        Assert.Equal("newHazard", provider.Indicators[0].HazardName);
    }

    [Fact]
    public void A_source_that_is_not_defined_is_rejected()
    {
        string indicator = ValidIndicator.Replace("\"s1\"", "\"missing\"");

        Assert.Throws<InvalidDataException>(() => new ReferenceRangesInfoProvider(Json(indicator: indicator)));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void A_source_url_that_is_not_http_is_rejected(string url)
    {
        string sources = "{ \"s1\": { \"title\": \"Fuente\", \"url\": \"" + url + "\" } }";

        Assert.Throws<InvalidDataException>(() => new ReferenceRangesInfoProvider(Json(sources)));
    }

    [Theory]
    [InlineData("\"whyItMatters\": \"why\",", "")]
    [InlineData("\"howToMeasure\": [\"step\"],", "\"howToMeasure\": [],")]
    [InlineData("\"sources\": [\"s1\"]", "\"sources\": []")]
    [InlineData("\"risks\": \"risk\"", "\"risks\": \"\"")]
    public void An_incomplete_indicator_is_rejected(string find, string replace)
    {
        string indicator = ValidIndicator.Replace(find, replace);
        Assert.NotEqual(ValidIndicator, indicator);

        Assert.Throws<InvalidDataException>(() => new ReferenceRangesInfoProvider(Json(indicator: indicator)));
    }

    [Fact]
    public void A_file_without_indicators_is_rejected()
    {
        Assert.Throws<InvalidDataException>(
            () => new ReferenceRangesInfoProvider("{ \"version\": \"1\", \"sources\": {}, \"indicators\": [] }"));
    }
}
