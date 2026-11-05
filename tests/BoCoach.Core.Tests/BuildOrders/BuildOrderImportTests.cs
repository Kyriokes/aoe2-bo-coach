using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Tests.BuildOrders;

public class BuildOrderImportTests
{
    private static string Fixture(params string[] path) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, .. path]));

    [Fact]
    public void Read_GuiaDeRtsOverlay_LaConvierte()
    {
        var result = BuildOrderImport.Read(Fixture("fixtures", "rts-overlay", "22p-scouts.json"), TestData.GameData);

        Assert.Equal("rts_overlay", result.Source);
        Assert.NotEmpty(result.BuildOrder.Steps);
    }

    [Fact]
    public void Read_GuiaDeBuildOrderGuide_LaConvierte()
    {
        var result = BuildOrderImport.Read(Fixture("fixtures", "buildorderguide", "armenians.json"), TestData.GameData);

        Assert.Equal("buildorderguide", result.Source);
        Assert.NotEmpty(result.BuildOrder.Steps);
    }

    [Fact]
    public void Read_BoDeLaApp_LaLeeSinAvisos()
    {
        var result = BuildOrderImport.Read(Fixture("build_orders", "scouts_22p.json"), TestData.GameData);

        Assert.Equal("bocoach", result.Source);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ReadJson_TextoQueNoEsJson_DevuelveError()
    {
        var json = JsonNode.Parse(BuildOrderImport.ReadJson("{no", TestData.GameData))!;

        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(json["error"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("""{"build_order":[null]}""")]
    [InlineData("""{"build_order":[{"notes":[null, 5, {"a":1}]}]}""")]
    [InlineData("""{"build_order":[{"villager_count":"x","resources":[1],"notes":"no"}]}""")]
    [InlineData("""{"build":[null, 3, {"type":5}],"resources":"x"}""")]
    [InlineData("""{"build":[{"type":"newVillagers","count":"x","task":null}]}""")]
    [InlineData("""{"schema_version":2,"steps":[null]}""")]
    [InlineData("""[1,2]""")]
    public void ReadJson_GuiaRara_NoTira(string guide)
    {
        var json = JsonNode.Parse(BuildOrderImport.ReadJson(guide, TestData.GameData))!;

        Assert.NotNull(json["ok"]);
    }

    // Una clave repetida tiraba ArgumentException al tocar el objeto, y nadie la esperaba (CR-12).
    [Theory]
    [InlineData("""{"build_order":[],"build_order":[]}""")]
    [InlineData("""{"build_order":[{"notes":[],"notes":[]}]}""")]
    [InlineData("""{"build":[],"build":[],"civilization":"x"}""")]
    [InlineData("""{"build":[{"type":"build","type":"lure"}],"civilization":"Franks"}""")]
    [InlineData("""{"schema_version":2,"schema_version":2,"name":"x"}""")]
    [InlineData("""{"schema_version":2,"name":"x","steps":[{"id":"a","id":"b"}]}""")]
    [InlineData("""{"a":1,"a":2}""")]
    public void ReadJson_ClavesRepetidas_DevuelveError(string json)
    {
        var result = JsonNode.Parse(BuildOrderImport.ReadJson(json, TestData.GameData))!;

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Contains("JSON inválido", result["error"]!.GetValue<string>());
        Assert.DoesNotContain("línea )", result["error"]!.GetValue<string>());
    }

    [Fact]
    public void ReadJson_Guia_TraeLaBoYLosAvisos()
    {
        var json = JsonNode.Parse(BuildOrderImport.ReadJson(Fixture("fixtures", "rts-overlay", "22p-scouts.json"), TestData.GameData))!;

        Assert.True(json["ok"]!.GetValue<bool>());
        Assert.Equal("rts_overlay", json["source"]!.GetValue<string>());
        Assert.IsType<JsonArray>(json["warnings"]);
        Assert.NotEmpty(json["build_order"]!["steps"]!.AsArray());
    }
}
