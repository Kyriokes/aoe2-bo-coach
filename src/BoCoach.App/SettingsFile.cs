using System.IO;
using System.Text;

namespace BoCoach.App;

/// <summary>Los archivos de la carpeta del usuario (ajustes y cuenta): no quedan a medias y uno roto no se pierde.</summary>
internal static class SettingsFile
{
    /// <summary>
    /// Escribe por un temporal y recién al final reemplaza el archivo. Escribiendo directo se trunca primero: un corte de
    /// luz dejaba el archivo vacío y al abrir volvían los valores de fábrica, también "Compartir mis partidas".
    /// </summary>
    public static void WriteAtomically(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(new UTF8Encoding(false).GetBytes(text));
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deja una copia de un archivo que no se pudo leer, para poder mirarlo; si no se puede, sigue igual.</summary>
    public static void KeepBrokenCopy(string path)
    {
        try
        {
            File.Copy(path, path + ".roto", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
