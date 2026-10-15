using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BoCoach.Vision.Capture;

/// <summary>
/// PNG mínimo, sin dependencias: escribe RGB de 8 bits y lee PNG no entrelazados de 8 bits
/// (gris, RGB o RGBA, con cualquier filtro). Alcanza para las grabaciones y los golden files.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(CapturedImage image, string path)
    {
        using var file = File.Create(path);
        Write(image, file);
    }

    public static void Write(CapturedImage image, Stream output)
    {
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bits por canal
        header[9] = 2; // RGB
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + image.Width * 3];
            for (var y = 0; y < image.Height; y++)
            {
                row[0] = 0; // sin filtro
                for (var x = 0; x < image.Width; x++)
                {
                    var i = (y * image.Width + x) * 4;
                    row[1 + x * 3] = image.Bgra[i + 2];
                    row[2 + x * 3] = image.Bgra[i + 1];
                    row[3 + x * 3] = image.Bgra[i];
                }
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
    }

    public static CapturedImage Read(string path)
    {
        using var file = File.OpenRead(path);
        return Read(file);
    }

    public static CapturedImage Read(Stream input)
    {
        Span<byte> signature = stackalloc byte[8];
        input.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature))
            throw new InvalidDataException("No es un PNG.");

        int width = 0, height = 0, colorType = 0;
        using var compressed = new MemoryStream();
        var lengthAndType = new byte[8];
        while (true)
        {
            input.ReadExactly(lengthAndType);
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthAndType);
            var type = Encoding.ASCII.GetString(lengthAndType, 4, 4);
            var data = new byte[length];
            input.ReadExactly(data);
            input.ReadExactly(new byte[4]); // CRC

            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                colorType = data[9];
                if (data[8] != 8 || data[12] != 0 || colorType is not (0 or 2 or 6))
                    throw new InvalidDataException("Solo se leen PNG de 8 bits, no entrelazados, en gris, RGB o RGBA.");
            }
            else if (type == "IDAT")
            {
                compressed.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        var channels = colorType switch { 0 => 1, 2 => 3, _ => 4 };
        var stride = width * channels;
        var pixels = new byte[height * stride];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            var previous = new byte[stride];
            var current = new byte[stride];
            var filter = new byte[1];
            for (var y = 0; y < height; y++)
            {
                zlib.ReadExactly(filter);
                zlib.ReadExactly(current);
                Unfilter(filter[0], current, previous, channels);
                Buffer.BlockCopy(current, 0, pixels, y * stride, stride);
                (previous, current) = (current, previous);
            }
        }

        var bgra = new byte[width * height * 4];
        for (var p = 0; p < width * height; p++)
        {
            var s = p * channels;
            var d = p * 4;
            if (channels == 1)
            {
                bgra[d] = bgra[d + 1] = bgra[d + 2] = pixels[s];
            }
            else
            {
                bgra[d] = pixels[s + 2];
                bgra[d + 1] = pixels[s + 1];
                bgra[d + 2] = pixels[s];
            }
            bgra[d + 3] = channels == 4 ? pixels[s + 3] : (byte)255;
        }
        return new CapturedImage(width, height, bgra);
    }

    private static void Unfilter(byte filter, byte[] row, byte[] previous, int bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0;
            int up = previous[i];
            int upLeft = i >= bpp ? previous[i - bpp] : 0;
            row[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] + left),
                2 => (byte)(row[i] + up),
                3 => (byte)(row[i] + (left + up) / 2),
                4 => (byte)(row[i] + Paeth(left, up, upLeft)),
                _ => throw new InvalidDataException($"Filtro PNG desconocido: {filter}."),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
