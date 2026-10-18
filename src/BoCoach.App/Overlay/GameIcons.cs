using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BoCoach.App.Overlay;

/// <summary>
/// Los íconos del juego para los recordatorios del mouse, leídos de la instalación del usuario (no se copian al repo:
/// son del juego). Los mismos dibujos del árbol de tecnologías: la casa, el centro urbano (en DE, una pareja de
/// aldeanos) y el botón de aldeanos inactivos. Si no se encuentran, los recordatorios usan sus dibujos propios.
/// </summary>
public sealed record GameIcons(ImageSource? House, ImageSource? TownCenter, ImageSource? IdleVillager)
{
    private const string Textures = @"widgetui\textures\ingame";

    private static readonly Lazy<GameIcons> Shared = new(() => Load(FindGame()));

    /// <summary>Los íconos de la instalación del juego (se buscan una vez).</summary>
    public static GameIcons Current => Shared.Value;

    public static GameIcons Load(string? game)
    {
        if (game is null)
            return new GameIcons(null, null, null);
        var textures = Path.Combine(game, Textures);
        return new GameIcons(
            Dds(Path.Combine(textures, "buildings", "034_house.DDS")),
            Dds(Path.Combine(textures, "buildings", "028_town_center.DDS")),
            Png(Path.Combine(textures, "icons", "idle-villager_normal.png")));
    }

    /// <summary>
    /// Carpeta del juego: la del proceso abierto (solo la ruta del ejecutable, sin leer su memoria) o, si no está
    /// abierto, la que tenga AoE2DE en alguna biblioteca de Steam.
    /// </summary>
    public static string? FindGame()
    {
        foreach (var name in Vision.Capture.GameWindow.ProcessNames)
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (ImagePath(process.Id) is { } exe && Path.GetDirectoryName(exe) is { } dir && IsGame(dir))
                        return dir;
                }
            }
        }
        return SteamLibraries().Select(l => Path.Combine(l, "steamapps", "common", "AoE2DE")).FirstOrDefault(IsGame);
    }

    private static bool IsGame(string dir) => Directory.Exists(Path.Combine(dir, Textures));

    private static IEnumerable<string> SteamLibraries()
    {
        string? steam;
        try
        {
            steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException)
        {
            yield break;
        }
        if (steam is null)
            yield break;
        steam = steam.Replace('/', '\\');
        yield return steam;
        var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        string text;
        try
        {
            text = File.ReadAllText(folders);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    /// <summary>
    /// Un .DDS de la interfaz del juego: sin comprimir, 32 bits, R G B A (los íconos de widgetui son así). Otro formato,
    /// null.
    /// </summary>
    public static BitmapSource? Dds(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        const int header = 128;
        if (bytes.Length < header || Encoding.ASCII.GetString(bytes, 0, 4) != "DDS ")
            return null;
        var height = BitConverter.ToInt32(bytes, 12);
        var width = BitConverter.ToInt32(bytes, 16);
        var flags = BitConverter.ToUInt32(bytes, 80);
        var bits = BitConverter.ToInt32(bytes, 88);
        var redMask = BitConverter.ToUInt32(bytes, 92);
        const uint rgb = 0x40;
        if ((flags & rgb) == 0 || bits != 32 || redMask != 0xFF || width <= 0 || height <= 0 || bytes.Length < header + width * height * 4)
            return null;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // R G B A → B G R A
            pixels[i] = bytes[header + i + 2];
            pixels[i + 1] = bytes[header + i + 1];
            pixels[i + 2] = bytes[header + i];
            pixels[i + 3] = bytes[header + i + 3];
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static BitmapImage? Png(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ImagePath(int processId)
    {
        const uint queryLimitedInformation = 0x1000;
        var handle = OpenProcess(queryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
