using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Enumerar los procesos es caro y la app pregunta dónde está el juego 6 a 8 veces por segundo, también sin el juego. Con
/// la ventana en la mano alcanza con validarla; sin ella, se enumera como mucho una vez por segundo.
/// </summary>
public class GameWindowFinderTests
{
    private static readonly IntPtr Window = new(1234);
    private static readonly GameProcess Game = new(77, Window);

    private sealed class FakeProcesses : IGameProcesses
    {
        public int Enumerations { get; private set; }
        public int Validations { get; private set; }
        public List<GameProcess> Running { get; } = [];
        public HashSet<IntPtr> OpenWindows { get; } = [];

        public IReadOnlyList<GameProcess> Enumerate()
        {
            Enumerations++;
            return Running.ToList();
        }

        public bool StillOpen(GameProcess process)
        {
            Validations++;
            return OpenWindows.Contains(process.Window);
        }
    }

    private double _now;
    private readonly FakeProcesses _system = new();

    private GameWindowFinder Finder() => new(_system, () => _now);

    private void Run(GameProcess process)
    {
        _system.Running.Add(process);
        if (process.Window != IntPtr.Zero)
            _system.OpenWindows.Add(process.Window);
    }

    /// <summary>Lo que pasa en un segundo de la app: unas 8 preguntas.</summary>
    private void OneSecond(GameWindowFinder finder)
    {
        for (var i = 0; i < 8; i++)
        {
            finder.Processes();
            _now += 0.125;
        }
    }

    [Fact]
    public void Sin_el_juego_enumera_como_mucho_una_vez_por_segundo()
    {
        var finder = Finder();

        for (var second = 0; second < 5; second++)
            OneSecond(finder);

        Assert.Equal(5, _system.Enumerations);
        Assert.Empty(finder.Processes());
    }

    [Fact]
    public void Con_el_juego_abierto_valida_la_ventana_sin_enumerar()
    {
        Run(Game);
        var finder = Finder();

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal([Game], finder.Processes());
            _now += 0.125;
        }

        Assert.Equal(1, _system.Enumerations);
        Assert.True(_system.Validations >= 15);
    }

    [Fact]
    public void El_juego_que_se_abre_aparece_al_segundo_siguiente()
    {
        var finder = Finder();
        Assert.Empty(finder.Processes());

        Run(Game);
        _now += 0.5;
        Assert.Empty(finder.Processes());

        _now += 0.5;
        Assert.Equal([Game], finder.Processes());
    }

    [Fact]
    public void Si_la_ventana_se_cierra_se_entera_enseguida_y_vuelve_a_buscar()
    {
        Run(Game);
        var finder = Finder();
        finder.Processes();
        _now += 5;

        _system.OpenWindows.Clear();
        _system.Running.Clear();

        Assert.Empty(finder.Processes());
        Assert.Equal(2, _system.Enumerations);
    }

    // Un proceso nuevo de la ventana cerrada: en el mismo segundo no enumera otra vez, pero la ventana vieja ya no vale.
    [Fact]
    public void Una_ventana_que_deja_de_valer_no_se_sigue_devolviendo_aunque_no_toque_enumerar()
    {
        Run(Game);
        var finder = Finder();
        finder.Processes();
        _now += 0.2;

        _system.OpenWindows.Clear();

        var processes = finder.Processes();
        Assert.Equal(1, _system.Enumerations);
        Assert.Equal(IntPtr.Zero, Assert.Single(processes).Window); // el proceso sigue, sin ventana
    }

    // Mientras carga, el proceso existe y todavía no tiene ventana: cuenta como abierto, y se vuelve a mirar cada segundo.
    [Fact]
    public void Un_proceso_sin_ventana_cuenta_y_se_sigue_buscando_su_ventana()
    {
        Run(new GameProcess(77, IntPtr.Zero));
        var finder = Finder();

        Assert.Equal(IntPtr.Zero, Assert.Single(finder.Processes()).Window);

        _system.Running.Clear();
        Run(Game);
        _now += 1;
        Assert.Equal([Game], finder.Processes());
        Assert.Equal(2, _system.Enumerations);
    }

    // La ventana principal puede cambiar sin que se cierre la que había (no se pudo ver con el juego): cada tanto se mira.
    [Fact]
    public void Aunque_la_ventana_siga_valiendo_cada_tanto_se_vuelve_a_mirar()
    {
        Run(Game);
        var finder = Finder();
        finder.Processes();

        var other = new GameProcess(77, new IntPtr(5678));
        _system.Running.Clear();
        Run(other);
        _now += GameWindowFinder.RefreshSeconds;

        Assert.Equal([other], finder.Processes());
    }
}
