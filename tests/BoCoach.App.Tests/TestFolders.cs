using System.Runtime.CompilerServices;
using BoCoach.App.Coaching;

namespace BoCoach.App.Tests;

/// <summary>Lo que la app escribe en las carpetas del usuario va, en los tests, a una carpeta temporal.</summary>
internal static class TestFolders
{
    [ModuleInitializer]
    internal static void Init()
    {
        CoachSessionViewModel.DefaultReportsFolder = Path.Combine(Path.GetTempPath(), "BoCoach.App.Tests", "informes");
        CoachSessionViewModel.DefaultRecordsFolder = Path.Combine(Path.GetTempPath(), "BoCoach.App.Tests", "registros");
    }
}
