using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Minimal DDS decoder for the formats used by the DMO interface atlases:
    /// - DXT1 / BC1
    /// - DXT3 / BC2
    /// - DXT5 / BC3
    /// - uncompressed 32-bit RGB(A) using DDS channel masks
    ///
    /// Output is always a 32bpp ARGB Bitmap.
    /// </summary>
    public static class DdsImageLoader
    {
        private const uint DdpfAlphaPixels = 0x00000001;
        private const uint DdpfFourCc = 0x00000004;
        private const uint DdpfRgb = 0x00000040;

        public static Bitmap LoadBitmap(string path)
        {
            using FileStream fs =
                File.OpenRead(path);

            using BinaryReader br =
                new(
                    fs,
                    Encoding.ASCII,
                    leaveOpen: false);

            return LoadBitmap(br);
        }

        public static Bitmap LoadBitmap(BinaryReader br)
        {
            byte[] magic =
                br.ReadBytes(4);

            if (magic.Length != 4 ||
                magic[0] != (byte)'D' ||
                magic[1] != (byte)'D' ||
                magic[2] != (byte)'S' ||
                magic[3] != (byte)' ')
            {
                throw new InvalidDataException(
                    "DDS inválido: magic 'DDS ' não encontrado.");
            }

            uint headerSize =
                br.ReadUInt32();

            if (headerSize != 124)
            {
                throw new InvalidDataException(
                    $"DDS header size inválido: {headerSize}. Esperado=124.");
            }

            uint flags = br.ReadUInt32();
            int height = checked((int)br.ReadUInt32());
            int width = checked((int)br.ReadUInt32());
            uint pitchOrLinearSize = br.ReadUInt32();
            uint depth = br.ReadUInt32();
            uint mipMapCount = br.ReadUInt32();

            // reserved1[11]
            for (int i = 0; i < 11; i++)
                br.ReadUInt32();

            uint pixelFormatSize =
                br.ReadUInt32();

            if (pixelFormatSize != 32)
            {
                throw new InvalidDataException(
                    $"DDS pixel-format size inválido: {pixelFormatSize}. Esperado=32.");
            }

            uint pixelFlags =
                br.ReadUInt32();

            uint fourCc =
                br.ReadUInt32();

            uint rgbBitCount =
                br.ReadUInt32();

            uint rMask =
                br.ReadUInt32();

            uint gMask =
                br.ReadUInt32();

            uint bMask =
                br.ReadUInt32();

            uint aMask =
                br.ReadUInt32();

            // caps / caps2 / caps3 / caps4 / reserved2
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();

            if (width <= 0 ||
                height <= 0)
            {
                throw new InvalidDataException(
                    $"DDS possui dimensão inválida: {width}x{height}.");
            }

            byte[] rgba;

            if ((pixelFlags & DdpfFourCc) != 0)
            {
                string code =
                    FourCcToString(fourCc);

                rgba =
                    code switch
                    {
                        "DXT1" =>
                            DecodeDxt1(
                                br,
                                width,
                                height),

                        "DXT3" =>
                            DecodeDxt3(
                                br,
                                width,
                                height),

                        "DXT5" =>
                            DecodeDxt5(
                                br,
                                width,
                                height),

                        _ =>
                            throw new NotSupportedException(
                                $"DDS FourCC '{code}' ainda não é suportado.")
                    };
            }
            else if ((pixelFlags & DdpfRgb) != 0 &&
                     rgbBitCount == 32)
            {
                rgba =
                    DecodeUncompressed32(
                        br,
                        width,
                        height,
                        rMask,
                        gMask,
                        bMask,
                        aMask,
                        (pixelFlags & DdpfAlphaPixels) != 0);
            }
            else
            {
                throw new NotSupportedException(
                    $"Formato DDS ainda não suportado. " +
                    $"PixelFlags=0x{pixelFlags:X8}, RGBBits={rgbBitCount}.");
            }

            return CreateBitmap(
                width,
                height,
                rgba);
        }

        private static byte[] DecodeDxt1(
            BinaryReader br,
            int width,
            int height)
        {
            int blocksWide =
                Math.Max(
                    1,
                    (width + 3) / 4);

            int blocksHigh =
                Math.Max(
                    1,
                    (height + 3) / 4);

            byte[] output =
                new byte[
                    checked(
                        width *
                        height *
                        4)];

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    ushort c0 =
                        br.ReadUInt16();

                    ushort c1 =
                        br.ReadUInt16();

                    uint indices =
                        br.ReadUInt32();

                    Span<Color32> palette =
                        stackalloc Color32[4];

                    BuildDxtColorPalette(
                        c0,
                        c1,
                        allowTransparent:
                            true,
                        palette);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x =
                                bx * 4 + px;

                            int y =
                                by * 4 + py;

                            int selector =
                                (int)(
                                    (indices >>
                                     (2 * (py * 4 + px))) &
                                    0x3);

                            if (x >= width ||
                                y >= height)
                            {
                                continue;
                            }

                            WritePixel(
                                output,
                                width,
                                x,
                                y,
                                palette[selector]);
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeDxt3(
            BinaryReader br,
            int width,
            int height)
        {
            int blocksWide =
                Math.Max(
                    1,
                    (width + 3) / 4);

            int blocksHigh =
                Math.Max(
                    1,
                    (height + 3) / 4);

            byte[] output =
                new byte[
                    checked(
                        width *
                        height *
                        4)];

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    ulong alphaBits =
                        br.ReadUInt64();

                    ushort c0 =
                        br.ReadUInt16();

                    ushort c1 =
                        br.ReadUInt16();

                    uint indices =
                        br.ReadUInt32();

                    Span<Color32> palette =
                        stackalloc Color32[4];

                    BuildDxtColorPalette(
                        c0,
                        c1,
                        allowTransparent:
                            false,
                        palette);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int pixelIndex =
                                py * 4 + px;

                            int x =
                                bx * 4 + px;

                            int y =
                                by * 4 + py;

                            int selector =
                                (int)(
                                    (indices >>
                                     (2 * pixelIndex)) &
                                    0x3);

                            byte alpha4 =
                                (byte)(
                                    (alphaBits >>
                                     (4 * pixelIndex)) &
                                    0xF);

                            Color32 c =
                                palette[selector];

                            c.A =
                                (byte)(
                                    alpha4 * 17);

                            if (x >= width ||
                                y >= height)
                            {
                                continue;
                            }

                            WritePixel(
                                output,
                                width,
                                x,
                                y,
                                c);
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeDxt5(
            BinaryReader br,
            int width,
            int height)
        {
            int blocksWide =
                Math.Max(
                    1,
                    (width + 3) / 4);

            int blocksHigh =
                Math.Max(
                    1,
                    (height + 3) / 4);

            byte[] output =
                new byte[
                    checked(
                        width *
                        height *
                        4)];

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    byte a0 =
                        br.ReadByte();

                    byte a1 =
                        br.ReadByte();

                    ulong alphaIndexBits = 0;

                    for (int i = 0; i < 6; i++)
                    {
                        alphaIndexBits |=
                            (ulong)br.ReadByte() <<
                            (8 * i);
                    }

                    Span<byte> alphaPalette =
                        stackalloc byte[8];

                    BuildDxt5AlphaPalette(
                        a0,
                        a1,
                        alphaPalette);

                    ushort c0 =
                        br.ReadUInt16();

                    ushort c1 =
                        br.ReadUInt16();

                    uint indices =
                        br.ReadUInt32();

                    Span<Color32> palette =
                        stackalloc Color32[4];

                    BuildDxtColorPalette(
                        c0,
                        c1,
                        allowTransparent:
                            false,
                        palette);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int pixelIndex =
                                py * 4 + px;

                            int x =
                                bx * 4 + px;

                            int y =
                                by * 4 + py;

                            int colorSelector =
                                (int)(
                                    (indices >>
                                     (2 * pixelIndex)) &
                                    0x3);

                            int alphaSelector =
                                (int)(
                                    (alphaIndexBits >>
                                     (3 * pixelIndex)) &
                                    0x7);

                            Color32 c =
                                palette[colorSelector];

                            c.A =
                                alphaPalette[
                                    alphaSelector];

                            if (x >= width ||
                                y >= height)
                            {
                                continue;
                            }

                            WritePixel(
                                output,
                                width,
                                x,
                                y,
                                c);
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeUncompressed32(
            BinaryReader br,
            int width,
            int height,
            uint rMask,
            uint gMask,
            uint bMask,
            uint aMask,
            bool alphaFlag)
        {
            byte[] output =
                new byte[
                    checked(
                        width *
                        height *
                        4)];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint packed =
                        br.ReadUInt32();

                    byte r =
                        ExtractMaskedByte(
                            packed,
                            rMask);

                    byte g =
                        ExtractMaskedByte(
                            packed,
                            gMask);

                    byte b =
                        ExtractMaskedByte(
                            packed,
                            bMask);

                    byte a =
                        aMask != 0
                            ? ExtractMaskedByte(
                                packed,
                                aMask)
                            : (byte)255;

                    // Some old DDS files have valid A mask without the
                    // explicit DDPF_ALPHAPIXELS flag.
                    if (!alphaFlag &&
                        aMask == 0)
                    {
                        a = 255;
                    }

                    WritePixel(
                        output,
                        width,
                        x,
                        y,
                        new Color32(
                            r,
                            g,
                            b,
                            a));
                }
            }

            return output;
        }

        private static void BuildDxtColorPalette(
            ushort c0,
            ushort c1,
            bool allowTransparent,
            Span<Color32> palette)
        {
            palette[0] =
                DecodeRgb565(c0);

            palette[1] =
                DecodeRgb565(c1);

            if (!allowTransparent ||
                c0 > c1)
            {
                palette[2] =
                    Interpolate(
                        palette[0],
                        palette[1],
                        2,
                        1,
                        3);

                palette[3] =
                    Interpolate(
                        palette[0],
                        palette[1],
                        1,
                        2,
                        3);
            }
            else
            {
                palette[2] =
                    Interpolate(
                        palette[0],
                        palette[1],
                        1,
                        1,
                        2);

                palette[3] =
                    new Color32(
                        0,
                        0,
                        0,
                        0);
            }
        }

        private static void BuildDxt5AlphaPalette(
            byte a0,
            byte a1,
            Span<byte> palette)
        {
            palette[0] = a0;
            palette[1] = a1;

            if (a0 > a1)
            {
                palette[2] =
                    (byte)(
                        (6 * a0 + 1 * a1) /
                        7);

                palette[3] =
                    (byte)(
                        (5 * a0 + 2 * a1) /
                        7);

                palette[4] =
                    (byte)(
                        (4 * a0 + 3 * a1) /
                        7);

                palette[5] =
                    (byte)(
                        (3 * a0 + 4 * a1) /
                        7);

                palette[6] =
                    (byte)(
                        (2 * a0 + 5 * a1) /
                        7);

                palette[7] =
                    (byte)(
                        (1 * a0 + 6 * a1) /
                        7);
            }
            else
            {
                palette[2] =
                    (byte)(
                        (4 * a0 + 1 * a1) /
                        5);

                palette[3] =
                    (byte)(
                        (3 * a0 + 2 * a1) /
                        5);

                palette[4] =
                    (byte)(
                        (2 * a0 + 3 * a1) /
                        5);

                palette[5] =
                    (byte)(
                        (1 * a0 + 4 * a1) /
                        5);

                palette[6] = 0;
                palette[7] = 255;
            }
        }

        private static Color32 DecodeRgb565(
            ushort value)
        {
            int r5 =
                (value >> 11) &
                0x1F;

            int g6 =
                (value >> 5) &
                0x3F;

            int b5 =
                value &
                0x1F;

            byte r =
                (byte)(
                    (r5 * 255 + 15) /
                    31);

            byte g =
                (byte)(
                    (g6 * 255 + 31) /
                    63);

            byte b =
                (byte)(
                    (b5 * 255 + 15) /
                    31);

            return new Color32(
                r,
                g,
                b,
                255);
        }

        private static Color32 Interpolate(
            Color32 a,
            Color32 b,
            int wa,
            int wb,
            int divisor)
        {
            return new Color32(
                (byte)(
                    (wa * a.R + wb * b.R) /
                    divisor),
                (byte)(
                    (wa * a.G + wb * b.G) /
                    divisor),
                (byte)(
                    (wa * a.B + wb * b.B) /
                    divisor),
                255);
        }

        private static byte ExtractMaskedByte(
            uint packed,
            uint mask)
        {
            if (mask == 0)
                return 0;

            int shift =
                CountTrailingZeros(mask);

            uint shiftedMask =
                mask >>
                shift;

            uint value =
                (packed & mask) >>
                shift;

            if (shiftedMask == 0)
                return 0;

            return (byte)(
                (value * 255 +
                 shiftedMask / 2) /
                shiftedMask);
        }

        private static int CountTrailingZeros(
            uint value)
        {
            int count = 0;

            while ((value & 1) == 0 &&
                   count < 32)
            {
                value >>= 1;
                count++;
            }

            return count;
        }

        private static string FourCcToString(
            uint fourCc)
        {
            char c0 =
                (char)(
                    fourCc &
                    0xFF);

            char c1 =
                (char)(
                    (fourCc >> 8) &
                    0xFF);

            char c2 =
                (char)(
                    (fourCc >> 16) &
                    0xFF);

            char c3 =
                (char)(
                    (fourCc >> 24) &
                    0xFF);

            return new string(
                new[]
                {
                    c0,
                    c1,
                    c2,
                    c3
                });
        }

        private static Bitmap CreateBitmap(
            int width,
            int height,
            byte[] rgba)
        {
            var bitmap =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format32bppArgb);

            Rectangle rect =
                new Rectangle(
                    0,
                    0,
                    width,
                    height);

            BitmapData data =
                bitmap.LockBits(
                    rect,
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

            try
            {
                int stride =
                    data.Stride;

                byte[] row =
                    new byte[
                        checked(
                            width *
                            4)];

                for (int y = 0; y < height; y++)
                {
                    int sourceRow =
                        y *
                        width *
                        4;

                    for (int x = 0; x < width; x++)
                    {
                        int src =
                            sourceRow +
                            x * 4;

                        int dst =
                            x * 4;

                        // Bitmap Format32bppArgb expects BGRA in memory.
                        row[dst + 0] =
                            rgba[src + 2];

                        row[dst + 1] =
                            rgba[src + 1];

                        row[dst + 2] =
                            rgba[src + 0];

                        row[dst + 3] =
                            rgba[src + 3];
                    }

                    Marshal.Copy(
                        row,
                        0,
                        IntPtr.Add(
                            data.Scan0,
                            y * stride),
                        row.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        private static void WritePixel(
            byte[] output,
            int width,
            int x,
            int y,
            Color32 c)
        {
            int offset =
                (y * width + x) *
                4;

            output[offset + 0] =
                c.R;

            output[offset + 1] =
                c.G;

            output[offset + 2] =
                c.B;

            output[offset + 3] =
                c.A;
        }

        private struct Color32
        {
            public Color32(
                byte r,
                byte g,
                byte b,
                byte a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }

            public byte R;
            public byte G;
            public byte B;
            public byte A;
        }
    }
}
