using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class WorldMapConverter : IGameDataConverter
    {
        public string Name => "WorldMap";

        private const int WorldRecordSize = 616;
        private const int AreaRecordSize = 632;

        private const int NameChars = 48;
        private const int CommentChars = 256;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("WorldMap", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("WorldMapInfo", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\WorldMap.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            long worldStart = ms.Position;
            XDocument worldDoc = ReadWorldMapInfo(br);
            long worldEnd = ms.Position;

            long areaStart = ms.Position;
            XDocument areaDoc = ReadAreaMapInfo(br);
            long areaEnd = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"WorldMap.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            SaveXml(
                worldDoc,
                Path.Combine(folder, "WorldMapInfo.xml"));

            SaveXml(
                areaDoc,
                Path.Combine(folder, "AreaMapInfo.xml"));

            AppLogger.Log(
                "WorldMap: BIN -> XML concluído. 2 XMLs gerados.");

            AppLogger.Log(
                $"WorldMap: secções em bytes -> " +
                $"WorldMapInfo={worldEnd - worldStart:N0}, " +
                $"AreaMapInfo={areaEnd - areaStart:N0}.");

            AppLogger.Log(
                $"WorldMap: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\WorldMap.");

            string worldPath =
                Path.Combine(folder, "WorldMapInfo.xml");

            string areaPath =
                Path.Combine(folder, "AreaMapInfo.xml");

            foreach (string path in new[] { worldPath, areaPath })
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"WorldMap: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument worldDoc = LoadXml(worldPath);
            XDocument areaDoc = LoadXml(areaPath);

            long expectedSize;

            // Valida integralmente antes de criar/substituir Output.
            using (MemoryStream counter = new())
            using (BinaryWriter test =
                new(counter, Encoding.UTF8, leaveOpen: true))
            {
                WriteWorldMapInfo(test, worldDoc);
                WriteAreaMapInfo(test, areaDoc);

                test.Flush();
                expectedSize = counter.Length;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para WorldMap."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteWorldMapInfo(bw, worldDoc);
            WriteAreaMapInfo(bw, areaDoc);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"WorldMap.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "WorldMap: XML -> BIN concluído. " +
                "WorldMapInfo e AreaMapInfo validados.");

            AppLogger.Log(
                $"WorldMap: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // WORLD MAP INFO
        // ============================================================

        private static XDocument ReadWorldMapInfo(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "WorldMapInfo.Count",
                    100_000);

            XElement root =
                new("WorldMapInfo");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                ushort id = br.ReadUInt16();

                string name =
                    ReadFixedUnicode(
                        br,
                        NameChars);

                string comment =
                    ReadFixedUnicode(
                        br,
                        CommentChars);

                ushort worldType = br.ReadUInt16();
                ushort uiX = br.ReadUInt16();
                ushort uiY = br.ReadUInt16();

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != WorldRecordSize)
                {
                    throw new InvalidDataException(
                        $"WorldMapInfo ID={id}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={WorldRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "WorldMapInfo",
                        new XElement("s_nID", id),
                        new XElement("s_szName", name),
                        new XElement("s_szComment", comment),
                        new XElement("s_nWorldType", worldType),
                        new XElement("s_nUI_X", uiX),
                        new XElement("s_nUI_Y", uiY)));
            }

            return Xml(root);
        }

        private static void WriteWorldMapInfo(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "WorldMapInfo",
                    "WorldMapInfo.xml");

            List<XElement> rows =
                root.Elements("WorldMapInfo").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort id =
                    RequiredUInt16(
                        row,
                        "s_nID",
                        "WorldMapInfo.xml");

                string context =
                    $"WorldMapInfo ID={id}";

                long start =
                    bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "s_szName",
                        context,
                        allowEmpty: true),
                    NameChars,
                    $"{context} <s_szName>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "s_szComment",
                        context,
                        allowEmpty: true),
                    CommentChars,
                    $"{context} <s_szComment>");

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nWorldType",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nUI_X",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nUI_Y",
                        context));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != WorldRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={WorldRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // AREA MAP INFO
        // ============================================================

        private static XDocument ReadAreaMapInfo(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "AreaMapInfo.Count",
                    1_000_000);

            XElement root =
                new("AreaMapInfo");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                ushort mapId = br.ReadUInt16();

                string name =
                    ReadFixedUnicode(
                        br,
                        NameChars);

                string comment =
                    ReadFixedUnicode(
                        br,
                        CommentChars);

                byte areaType = br.ReadByte();
                byte fieldType = br.ReadByte();
                byte ftDetail = br.ReadByte();

                // Padding físico confirmado no struct original.
                byte pad0 = br.ReadByte();
                byte pad1 = br.ReadByte();
                byte pad2 = br.ReadByte();

                if (pad0 != 0 || pad1 != 0 || pad2 != 0)
                {
                    throw new InvalidDataException(
                        $"AreaMapInfo MapID={mapId}: padding de 3 bytes " +
                        $"não está a zero ({pad0}, {pad1}, {pad2}).");
                }

                ushort uiX = br.ReadUInt16();
                ushort uiY = br.ReadUInt16();

                float gaussian0 = br.ReadSingle();
                float gaussian1 = br.ReadSingle();
                float gaussian2 = br.ReadSingle();

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != AreaRecordSize)
                {
                    throw new InvalidDataException(
                        $"AreaMapInfo MapID={mapId}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={AreaRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "AreaMapInfo",
                        new XElement("d_nMapID", mapId),
                        new XElement("d_szName", name),
                        new XElement("d_szComment", comment),
                        new XElement("d_nAreaType", areaType),
                        new XElement("d_nFieldType", fieldType),
                        new XElement("d_nFTDetail", ftDetail),
                        new XElement("d_nUI_X", uiX),
                        new XElement("d_nUI_Y", uiY),
                        new XElement(
                            "d_fGaussianBlur",
                            new XElement("item", FloatText(gaussian0)),
                            new XElement("item", FloatText(gaussian1)),
                            new XElement("item", FloatText(gaussian2)))));
            }

            return Xml(root);
        }

        private static void WriteAreaMapInfo(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "AreaMapInfo",
                    "AreaMapInfo.xml");

            List<XElement> rows =
                root.Elements("AreaMapInfo").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort mapId =
                    RequiredUInt16(
                        row,
                        "d_nMapID",
                        "AreaMapInfo.xml");

                string context =
                    $"AreaMapInfo MapID={mapId}";

                long start =
                    bw.BaseStream.Position;

                bw.Write(mapId);

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "d_szName",
                        context,
                        allowEmpty: true),
                    NameChars,
                    $"{context} <d_szName>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "d_szComment",
                        context,
                        allowEmpty: true),
                    CommentChars,
                    $"{context} <d_szComment>");

                bw.Write(
                    RequiredByte(
                        row,
                        "d_nAreaType",
                        context));

                bw.Write(
                    RequiredByte(
                        row,
                        "d_nFieldType",
                        context));

                bw.Write(
                    RequiredByte(
                        row,
                        "d_nFTDetail",
                        context));

                // 3 bytes de padding reais do record.
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((byte)0);

                bw.Write(
                    RequiredUInt16(
                        row,
                        "d_nUI_X",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "d_nUI_Y",
                        context));

                XElement? blur =
                    row.Element("d_fGaussianBlur");

                if (blur == null)
                {
                    throw new InvalidDataException(
                        $"{context}: falta <d_fGaussianBlur>.");
                }

                List<XElement> items =
                    blur.Elements("item").ToList();

                if (items.Count != 3)
                {
                    throw new InvalidDataException(
                        $"{context}: <d_fGaussianBlur> deve conter exatamente " +
                        $"3 <item>; encontrados {items.Count}.");
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        ParseFloat(
                            item.Value,
                            $"{context} <d_fGaussianBlur>/<item>"));
                }

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != AreaRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={AreaRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            int byteCount =
                checked(wcharCount * 2);

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"String UTF-16LE truncada: " +
                    $"esperados={byteCount:N0} bytes, " +
                    $"recebidos={raw.Length:N0}.");
            }

            string value =
                Encoding.Unicode.GetString(raw);

            int zero =
                value.IndexOf('\0');

            return zero >= 0
                ? value[..zero]
                : value;
        }

        private static void WriteFixedUnicode(
            BinaryWriter bw,
            string value,
            int wcharCount,
            string field)
        {
            byte[] raw =
                Encoding.Unicode.GetBytes(
                    value ?? string.Empty);

            int maxBytes =
                checked(wcharCount * 2);

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field}: o texto ocupa {raw.Length:N0} bytes UTF-16LE, " +
                    $"mas o buffer binário é wchar[{wcharCount}] = " +
                    $"{maxBytes:N0} bytes. " +
                    $"Reduz o texto para no máximo {wcharCount} caracteres UTF-16.");
            }

            byte[] buffer =
                new byte[maxBytes];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value =
                br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
            }

            return value;
        }

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(
                    path,
                    LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                throw;
            }
        }

        private static XElement RequireRoot(
            XDocument doc,
            string expected,
            string context)
        {
            XElement? root = doc.Root;

            if (root == null)
            {
                throw new InvalidDataException(
                    $"{context}: XML sem root.");
            }

            if (root.Name.LocalName != expected)
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
        }

        private static string RequiredText(
            XElement parent,
            string name,
            string context,
            bool allowEmpty = false)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value = element.Value;

            if (!allowEmpty &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> está vazio.");
            }

            return value;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em UInt16 (0..65535).");
            }

            return result;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!byte.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em byte (0..255).");
            }

            return result;
        }

        private static float ParseFloat(
            string value,
            string context)
        {
            if (!float.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não é float válido. " +
                    "Usa ponto como separador decimal.");
            }

            if (float.IsNaN(result) ||
                float.IsInfinity(result))
            {
                throw new InvalidDataException(
                    $"{context} não pode ser NaN ou Infinity.");
            }

            return result;
        }

        private static string FloatText(float value) =>
            value.ToString(
                "R",
                CultureInfo.InvariantCulture);

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration(
                    "1.0",
                    "utf-8",
                    null),
                root);

        private static void SaveXml(
            XDocument document,
            string path)
        {
            using XmlWriter writer =
                XmlWriter.Create(
                    path,
                    new XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = new UTF8Encoding(false),
                        OmitXmlDeclaration = false
                    });

            document.Save(writer);
        }
    }
}
