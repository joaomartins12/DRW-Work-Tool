using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class DigimonListEditorService
    {
        private readonly Dictionary<uint, Bitmap> _icons = new();

        public string FilePath { get; }
        public XDocument Document { get; private set; }

        public int DigimonCount =>
            Document.Root?.Elements("Digimon").Count() ?? 0;

        public int SkillSlots =>
            int.TryParse(
                Document.Root?.Attribute("SkillSlots")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 5;

        private DigimonListEditorService(
            string filePath,
            XDocument document)
        {
            FilePath = filePath;
            Document = document;
        }

        public static DigimonListEditorService Load(
            string filePath,
            IProgress<StartupPreloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            int progressStart = 0,
            int progressEnd = 100)
        {
            string full = Path.GetFullPath(filePath);

            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(
                new StartupPreloadProgress(
                    progressStart,
                    "Loading Digimon_List.xml..."));

            XDocument document =
                XDocument.Load(
                    full,
                    LoadOptions.None);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Digimon_List.xml has no root element.");

            if (!root.Name.LocalName.Equals(
                    "DigimonList",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Digimon_List.xml root must be <DigimonList>.");
            }

            var service =
                new DigimonListEditorService(
                    full,
                    document);

            service.PreloadIcons(
                progress,
                cancellationToken,
                progressStart,
                progressEnd);

            return service;
        }

        public void ReplaceDocument(
            XDocument document)
        {
            Document =
                document ??
                throw new ArgumentNullException(
                    nameof(document));
        }

        public Bitmap? GetIcon(
            uint id)
        {
            if (id == 0)
                return null;

            return _icons.TryGetValue(
                    id,
                    out Bitmap? image)
                ? image
                : null;
        }

        private void PreloadIcons(
            IProgress<StartupPreloadProgress>? progress,
            CancellationToken cancellationToken,
            int progressStart,
            int progressEnd)
        {
            XElement root = Document.Root!;

            uint[] wantedIds =
                root.Elements("Digimon")
                    .SelectMany(
                        x =>
                            new[]
                            {
                                ParseUInt(x.Attribute("ID")?.Value),
                                ParseUInt(x.Element("ModelID")?.Value)
                            })
                    .Where(x => x != 0)
                    .Distinct()
                    .ToArray();

            if (wantedIds.Length == 0)
                return;

            string imageRoot =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "ImgDatabase",
                    "Digimon");

            if (!Directory.Exists(imageRoot))
            {
                progress?.Report(
                    new StartupPreloadProgress(
                        progressEnd,
                        $"Digimon_List ready: {DigimonCount:N0} Digimon. ImgDatabase\\Digimon not found."));
                return;
            }

            var fileIndex =
                new Dictionary<uint, string>();

            try
            {
                foreach (string file in Directory.EnumerateFiles(
                             imageRoot,
                             "*.*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string ext =
                        Path.GetExtension(file);

                    bool supported =
                        ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);

                    if (!supported)
                        continue;

                    if (!uint.TryParse(
                            Path.GetFileNameWithoutExtension(file),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out uint id))
                    {
                        continue;
                    }

                    if (!fileIndex.ContainsKey(id))
                        fileIndex[id] = file;
                }
            }
            catch
            {
                // Icons are optional; XML preload must remain usable.
            }

            int span =
                Math.Max(
                    0,
                    progressEnd - progressStart);

            for (int i = 0;
                 i < wantedIds.Length;
                 i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint id = wantedIds[i];

                if (fileIndex.TryGetValue(
                        id,
                        out string? file))
                {
                    Bitmap? image =
                        LoadBitmap(file);

                    if (image != null)
                        _icons[id] = image;
                }

                if (i % 20 == 0 ||
                    i == wantedIds.Length - 1)
                {
                    double ratio =
                        (i + 1) /
                        (double)wantedIds.Length;

                    int percent =
                        progressStart +
                        (int)Math.Round(
                            ratio * span);

                    progress?.Report(
                        new StartupPreloadProgress(
                            percent,
                            $"Caching Digimon images... {i + 1:N0}/{wantedIds.Length:N0}"));
                }
            }

            progress?.Report(
                new StartupPreloadProgress(
                    progressEnd,
                    $"Digimon database ready: {DigimonCount:N0} Digimon, {_icons.Count:N0} images in memory."));
        }

        public static Bitmap? TryLoadIconFromDatabase(uint id)
        {
            if(id==0)return null;

            string root=Path.Combine(AppContext.BaseDirectory,"ImgDatabase","Digimon");
            if(!Directory.Exists(root))return null;

            try
            {
                foreach(string file in Directory.EnumerateFiles(root,"*.*",SearchOption.AllDirectories))
                {
                    if(!uint.TryParse(
                        Path.GetFileNameWithoutExtension(file),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint parsed) || parsed!=id)
                        continue;

                    string ext=Path.GetExtension(file);
                    bool supported=
                        ext.Equals(".png",StringComparison.OrdinalIgnoreCase)||
                        ext.Equals(".bmp",StringComparison.OrdinalIgnoreCase)||
                        ext.Equals(".jpg",StringComparison.OrdinalIgnoreCase)||
                        ext.Equals(".jpeg",StringComparison.OrdinalIgnoreCase)||
                        ext.Equals(".tga",StringComparison.OrdinalIgnoreCase);

                    if(!supported)continue;

                    Bitmap? result=LoadBitmap(file);
                    if(result!=null)return result;
                }
            }
            catch{}

            return null;
        }

        private static Bitmap? LoadBitmap(string path)
        {
            try
            {
                if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase))
                    return LoadTga(path);

                using Image source=Image.FromFile(path);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap LoadTga(string path)
        {
            using var stream=File.OpenRead(path);
            using var reader=new BinaryReader(stream);

            byte idLength=reader.ReadByte();
            byte colorMapType=reader.ReadByte();
            byte imageType=reader.ReadByte();

            _=reader.ReadUInt16();
            ushort colorMapLength=reader.ReadUInt16();
            byte colorMapDepth=reader.ReadByte();

            _=reader.ReadUInt16();
            _=reader.ReadUInt16();

            ushort width=reader.ReadUInt16();
            ushort height=reader.ReadUInt16();
            byte pixelDepth=reader.ReadByte();
            byte descriptor=reader.ReadByte();

            if(width==0||height==0)throw new InvalidDataException("Invalid TGA dimensions.");
            if(colorMapType!=0)throw new NotSupportedException("Color-mapped TGA is not supported.");

            bool trueColor=imageType==2||imageType==10;
            bool gray=imageType==3||imageType==11;
            bool rle=imageType==10||imageType==11;

            if(!trueColor&&!gray)throw new NotSupportedException($"Unsupported TGA type {imageType}.");
            if(trueColor&&pixelDepth!=24&&pixelDepth!=32)throw new NotSupportedException($"Unsupported TGA depth {pixelDepth}.");
            if(gray&&pixelDepth!=8)throw new NotSupportedException($"Unsupported grayscale TGA depth {pixelDepth}.");

            if(idLength>0)reader.ReadBytes(idLength);

            int pixelCount=width*height;
            byte[] bgra=new byte[pixelCount*4];
            int target=0;

            void ReadOne(int index)
            {
                byte b,g,r,a=255;
                if(gray)
                {
                    byte v=reader.ReadByte();
                    b=g=r=v;
                }
                else
                {
                    b=reader.ReadByte();g=reader.ReadByte();r=reader.ReadByte();
                    if(pixelDepth==32)a=reader.ReadByte();
                }

                int o=index*4;
                bgra[o]=b;bgra[o+1]=g;bgra[o+2]=r;bgra[o+3]=a;
            }

            if(!rle)
            {
                while(target<pixelCount)ReadOne(target++);
            }
            else
            {
                int bytesPerPixel=gray?1:pixelDepth/8;

                while(target<pixelCount)
                {
                    byte packet=reader.ReadByte();
                    int count=(packet&0x7F)+1;

                    if((packet&0x80)!=0)
                    {
                        byte[] px=reader.ReadBytes(bytesPerPixel);
                        if(px.Length!=bytesPerPixel)throw new EndOfStreamException();

                        for(int i=0;i<count&&target<pixelCount;i++,target++)
                        {
                            int o=target*4;
                            if(gray)
                            {
                                bgra[o]=bgra[o+1]=bgra[o+2]=px[0];bgra[o+3]=255;
                            }
                            else
                            {
                                bgra[o]=px[0];bgra[o+1]=px[1];bgra[o+2]=px[2];
                                bgra[o+3]=bytesPerPixel==4?px[3]:(byte)255;
                            }
                        }
                    }
                    else
                    {
                        for(int i=0;i<count&&target<pixelCount;i++,target++)ReadOne(target);
                    }
                }
            }

            bool top=(descriptor&0x20)!=0;
            bool right=(descriptor&0x10)!=0;
            byte[] oriented=new byte[bgra.Length];

            for(int y=0;y<height;y++)
            for(int x=0;x<width;x++)
            {
                int sx=right?width-1-x:x;
                int sy=top?y:height-1-y;
                Buffer.BlockCopy(bgra,(sy*width+sx)*4,oriented,(y*width+x)*4,4);
            }

            var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
            var rect=new Rectangle(0,0,width,height);
            BitmapData data=bitmap.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);

            try
            {
                int rowBytes=width*4;
                for(int y=0;y<height;y++)
                    Marshal.Copy(oriented,y*rowBytes,data.Scan0+y*data.Stride,rowBytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        private static uint ParseUInt(
            string? value)
        {
            return uint.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint result)
                ? result
                : 0;
        }
    }
}
