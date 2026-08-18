using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DRW_Work_Tool.Core
{
    public static class TgaImageLoader
    {
        public static Bitmap LoadBitmap(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            byte idLength = br.ReadByte();
            byte colorMapType = br.ReadByte();
            byte imageType = br.ReadByte();

            br.ReadUInt16();
            br.ReadUInt16();
            br.ReadByte();
            br.ReadUInt16();
            br.ReadUInt16();

            ushort width = br.ReadUInt16();
            ushort height = br.ReadUInt16();
            byte bpp = br.ReadByte();
            byte descriptor = br.ReadByte();

            if (width == 0 || height == 0)
                throw new InvalidDataException("TGA inválido: dimensões 0.");

            if (colorMapType != 0)
                throw new NotSupportedException("TGA com palette/color map não suportado.");

            if (imageType != 2 && imageType != 10)
                throw new NotSupportedException($"TGA type {imageType} não suportado. Apenas TrueColor 2/10.");

            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException($"TGA {bpp}bpp não suportado. Apenas 24/32bpp.");

            if (idLength > 0)
                br.ReadBytes(idLength);

            int pixelCount = width * height;
            var pixels = new Color[pixelCount];

            if (imageType == 2)
            {
                for (int i = 0; i < pixelCount; i++)
                    pixels[i] = ReadColor(br, bpp);
            }
            else
            {
                int written = 0;

                while (written < pixelCount)
                {
                    byte packet = br.ReadByte();
                    int count = (packet & 0x7F) + 1;

                    if ((packet & 0x80) != 0)
                    {
                        Color color = ReadColor(br, bpp);

                        for (int i = 0; i < count && written < pixelCount; i++)
                            pixels[written++] = color;
                    }
                    else
                    {
                        for (int i = 0; i < count && written < pixelCount; i++)
                            pixels[written++] = ReadColor(br, bpp);
                    }
                }
            }

            bool topOrigin = (descriptor & 0x20) != 0;
            bool rightOrigin = (descriptor & 0x10) != 0;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            int p = 0;
            for (int sy = 0; sy < height; sy++)
            {
                int y = topOrigin ? sy : height - 1 - sy;

                for (int sx = 0; sx < width; sx++)
                {
                    int x = rightOrigin ? width - 1 - sx : sx;
                    bitmap.SetPixel(x, y, pixels[p++]);
                }
            }

            return bitmap;
        }

        private static Color ReadColor(BinaryReader br, byte bpp)
        {
            byte b = br.ReadByte();
            byte g = br.ReadByte();
            byte r = br.ReadByte();
            byte a = bpp == 32 ? br.ReadByte() : (byte)255;

            return Color.FromArgb(a, r, g, b);
        }
    }
}
