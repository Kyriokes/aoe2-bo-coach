using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Vision.Capture;

namespace BoCoach.App.Tests;

public sealed class LiveCoachTests : IDisposable
{
    private readonly DirectoryInfo _library = Directory.CreateTempSubdirectory("bocoach-live-");
    private readonly CoachData _data;

    public LiveCoachTests()
    {
        _data = CoachData.Load(_library.FullName);
    }

    public void Dispose() => _library.Delete(recursive: true);

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    /// <summary>Capturas que explotan: al pedir la captura o al leer una región, con una excepción cualquiera.</summary>
    private sealed class ExplodingFrames(bool atNext) : IFrameSource
    {
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public int Served { get; private set; }

        public Frame? Next()
        {
            Served++;
            if (atNext)
                throw new KeyNotFoundException("explotó la captura");
            return new Frame(_ => throw new KeyNotFoundException("explotó la lectura"), Served * 0.5, Area);
        }
    }

    // Antes solo se atrapaban cuatro tipos de excepción: cualquier otra (un error de programación en la lectura o en
    // el coach) cortaba el loop en silencio y el overlay quedaba congelado con la última captura.
    [Fact]
    public async Task Una_excepcion_inesperada_al_leer_una_captura_se_informa_y_el_loop_sigue()
    {
        var frames = new ExplodingFrames(atNext: false);
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        Assert.True(await live.StepAsync());
        Assert.Contains("explotó la lectura", last?.Error);
        Assert.True(await live.StepAsync());
        Assert.Equal(2, frames.Served);
    }

    [Fact]
    public async Task Una_excepcion_inesperada_al_capturar_se_informa_y_el_loop_sigue()
    {
        var frames = new ExplodingFrames(atNext: true);
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        Assert.True(await live.StepAsync());
        Assert.Contains("explotó la captura", last?.Error);
        Assert.True(await live.StepAsync());
        Assert.Equal(2, frames.Served);
    }
}
