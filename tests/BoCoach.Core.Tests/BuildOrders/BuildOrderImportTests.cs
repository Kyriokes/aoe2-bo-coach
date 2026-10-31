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
