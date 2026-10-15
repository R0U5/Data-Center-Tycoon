using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Xna.Framework;

namespace DataCenterTycoon;

public static class PngWriter
{
    public static void Write(string path, Color[] pixels, int width, int height)
    {
        byte[] raw = new byte[height * (width * 4 + 1)];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y * width + x];
                raw[o++] = c.R; raw[o++] = c.G; raw[o++] = c.B;
                raw[o++] = c.A == 0 ? (byte)255 : c.A;
            }
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            compressed = ms.ToArray();
        }

        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        WriteChunk(fs, "IHDR", Ihdr(width, height));
        WriteChunk(fs, "IDAT", compressed);
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static byte[] Ihdr(int w, int h)
    {
        var b = new byte[13];
        WriteBE(b, 0, w);
        WriteBE(b, 4, h);
        b[8] = 8; b[9] = 6; b[10] = 0; b[11] = 0; b[12] = 0;
        return b;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        crcBytes[0] = (byte)(crc >> 24); crcBytes[1] = (byte)(crc >> 16);
        crcBytes[2] = (byte)(crc >> 8); crcBytes[3] = (byte)crc;
        s.Write(crcBytes);
    }

    private static void WriteBE(byte[] b, int off, int v)
    {
        b[off] = (byte)(v >> 24); b[off + 1] = (byte)(v >> 16);
        b[off + 2] = (byte)(v >> 8); b[off + 3] = (byte)v;
    }

    private static readonly uint[] _crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var x in a) c = _crcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = _crcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
