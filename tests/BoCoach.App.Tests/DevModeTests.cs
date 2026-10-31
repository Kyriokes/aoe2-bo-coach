using BoCoach.App.Capture;

namespace BoCoach.App.Tests;

/// <summary>Modo desarrollador (spec 2026-10-30, app para el público, parte 2).</summary>
public sealed class DevModeTests
{
    [Fact]
    public void Cinco_clics_rapidos_prenden_y_cuatro_no()
    {
        var clicks = new VersionClicks();
        var t = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.False(clicks.Click(t));
        Assert.False(clicks.Click(t.AddSeconds(0.5)));
        Assert.False(clicks.Click(t.AddSeconds(1)));
        Assert.False(clicks.Click(t.AddSeconds(1.5)));
        Assert.True(clicks.Click(t.AddSeconds(2)));
        Assert.False(clicks.Click(t.AddSeconds(2.2))); // vuelve a contar
    }

    [Fact]
    public void El_panel_en_vivo_cambia_de_titulo_con_el_modo()
    {
        Assert.Equal("En la partida", new DevMode().Header);
        Assert.Equal("Lo que ve el coach", new DevMode { IsOn = true }.Header);
    }

    [Fact]
    public void Clics_lentos_no_prenden()
    {
        var clicks = new VersionClicks();
        var t = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.All(Enumerable.Range(0, 10), i => Assert.False(clicks.Click(t.AddSeconds(i))));
    }

    private sealed class FakeRecording : IRecordingControl
    {
        public bool IsRecording { get; private set; }
        public int Starts { get; private set; }

        public void StartRecording()
        {
            IsRecording = true;
            Starts++;
        }

        public void StopRecording() => IsRecording = false;
    }

    [Fact]
    public void Graba_cada_partida_y_corta_al_terminar()
    {
        var rec = new FakeRecording();
        var auto = new AutoRecorder(rec, () => true);
        auto.OnFrame(false);
        Assert.False(rec.IsRecording);
        auto.OnFrame(true);
        auto.OnFrame(true);
        Assert.Equal(1, rec.Starts);
        auto.OnFrame(false);
        Assert.False(rec.IsRecording);
        auto.OnFrame(true); // partida siguiente
        Assert.Equal(2, rec.Starts);
        auto.OnCoachStopped();
        Assert.False(rec.IsRecording);
    }

    [Fact]
    public void No_toca_una_grabacion_manual_ni_vuelve_si_se_corto_a_mano()
    {
        var rec = new FakeRecording();
        var auto = new AutoRecorder(rec, () => true);
        rec.StartRecording(); // a mano, antes de la partida
        auto.OnFrame(true);
        auto.OnFrame(false);
        Assert.True(rec.IsRecording); // la manual sigue
        rec.StopRecording();
        auto.OnFrame(true);
        Assert.True(rec.IsRecording);
        rec.StopRecording(); // el jugador la corta a mano
        auto.OnFrame(true);
        Assert.False(rec.IsRecording); // no vuelve en la misma partida
    }

    [Fact]
    public void Deshabilitada_no_arranca_pero_termina_la_suya()
    {
        var enabled = true;
        var rec = new FakeRecording();
        var auto = new AutoRecorder(rec, () => enabled);
        auto.OnFrame(true);
        enabled = false;
        auto.OnFrame(false);
        Assert.False(rec.IsRecording);
        auto.OnFrame(true);
        Assert.Equal(1, rec.Starts);
    }

    // Revisión: apagar el modo con una grabación en curso escondía el botón y no había cómo cortarla.
    [Fact]
    public void El_boton_de_grabar_se_ve_con_el_modo_o_grabando()
    {
        Assert.False(CaptureToolsViewModel.ShowsRecordButton(devMode: false, recording: false));
        Assert.True(CaptureToolsViewModel.ShowsRecordButton(devMode: true, recording: false));
        Assert.True(CaptureToolsViewModel.ShowsRecordButton(devMode: false, recording: true));
    }
}
