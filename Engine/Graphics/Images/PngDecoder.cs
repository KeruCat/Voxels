using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Engine.Graphics.Images;

/// <summary>Изображение RGBA8, строки сверху вниз.</summary>
public sealed class DecodedImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Rgba { get; init; }
}

/// <summary>
/// Минимальный декодер PNG: 8 бит на канал, цвет RGB или RGBA, без чередования.
/// Сжатие — стандартный zlib из System.IO.Compression, фильтры строк (None/Sub/Up/Average/Paeth) — свои.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static DecodedImage Decode(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0;
        byte depth = 0, colorType = 0, interlace = 0;
        var compressed = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= file.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos));
            string type = Encoding.ASCII.GetString(file, pos + 4, 4);
            var data = file.AsSpan(pos + 8, len);
            pos += 12 + len;

            if (type == "IHDR")
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                depth = data[8];
                colorType = data[9];
                interlace = data[12];
            }
            else if (type == "IDAT") compressed.Write(data);
            else if (type == "IEND") break;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG header missing.");
        if (depth != 8 || (colorType != 6 && colorType != 2) || interlace != 0)
            throw new NotSupportedException($"PNG format not supported: depth {depth}, color type {colorType}, interlace {interlace}.");

        int bpp = colorType == 6 ? 4 : 3;
        int stride = width * bpp;

        byte[] raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var z = new ZLibStream(compressed, CompressionMode.Decompress))
            z.ReadExactly(raw);

        byte[] rgba = new byte[width * height * 4];
        byte[] prev = new byte[stride];
        byte[] cur = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = raw[rowStart];
            for (int x = 0; x < stride; x++)
            {
                byte rawByte = raw[rowStart + 1 + x];
                int a = x >= bpp ? cur[x - bpp] : 0;      // слева
                int b = prev[x];                            // сверху
                int c = x >= bpp ? prev[x - bpp] : 0;      // сверху-слева
                int value = filter switch
                {
                    0 => rawByte,
                    1 => rawByte + a,
                    2 => rawByte + b,
                    3 => rawByte + ((a + b) >> 1),
                    4 => rawByte + Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };
                cur[x] = (byte)value;
            }

            int dst = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int src = x * bpp;
                rgba[dst + x * 4 + 0] = cur[src + 0];
                rgba[dst + x * 4 + 1] = cur[src + 1];
                rgba[dst + x * 4 + 2] = cur[src + 2];
                rgba[dst + x * 4 + 3] = bpp == 4 ? cur[src + 3] : (byte)255;
            }

            (prev, cur) = (cur, prev);
        }

        return new DecodedImage { Width = width, Height = height, Rgba = rgba };
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
