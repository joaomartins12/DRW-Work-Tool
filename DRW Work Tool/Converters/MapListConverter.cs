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
    public sealed class MapListConverter : IGameDataConverter
    {
        public string Name => "MapList";

        private static readonly Encoding Cp949 = CreateCp949();

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapList", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapList", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            int count =
                ReadCount(
                    br,
                    "MapList.Count",
                    1_000_000);

            XElement root =
                new("MapData");

            for (int i = 0; i < count; i++)
            {
                uint mapId =
                    br.ReadUInt32();

                string context =
                    $"MapList MapID={mapId}";

                string mapName =
                    ReadDynamicCp949(
                        br,
                        context + ".MapName");

                string mapPath =
                    ReadDynamicCp949(
                        br,
                        context + ".MapPath");

                string bgSound =
                    ReadDynamicCp949(
                        br,
                        context + ".BGSound");

                uint width =
                    br.ReadUInt32();

                uint height =
                    br.ReadUInt32();

                string descriptionEng =
                    ReadDynamicUnicode(
                        br,
                        context + ".MapDescription_Eng");

                string description =
                    ReadDynamicUnicode(
                        br,
                        context + ".MapDescription");

                ushort resurrectionMapId =
                    br.ReadUInt16();

                ushort reserved =
                    br.ReadUInt16();

                if (reserved != 0)
                {
                    throw new InvalidDataException(
                        $"{context}: campo reservado possui valor {reserved}; " +
                        "esperado=0. O XML atual não possui campo para preservar " +
                        "este valor sem perda de informação.");
                }

                ushort mapRegionId =
                    br.ReadUInt16();

                ushort fatigueType =
                    br.ReadUInt16();

                ushort fatigueDebuff =
                    br.ReadUInt16();

                ushort fatigueStartTime =
                    br.ReadUInt16();

                ushort fatigueAddTime =
                    br.ReadUInt16();

                short fatigueAddPoint =
                    br.ReadInt16();

                ushort cameraMaxLevel =
                    br.ReadUInt16();

                ushort flags =
                    br.ReadUInt16();

                byte xgConsumeType =
                    (byte)(flags & 0x00FF);

                byte battleTagUse =
                    (byte)((flags >> 8) & 0x00FF);

                root.Add(
                    new XElement(
                        "Map",
                        new XElement("MapID", mapId),
                        new XElement("MapName", mapName),
                        new XElement("MapPath", mapPath),
                        new XElement("BGSound", bgSound),
                        new XElement("Width", width),
                        new XElement("Height", height),
                        new XElement("MapDescription_Eng", descriptionEng),
                        new XElement("MapDescription", description),
                        new XElement("ResurrectionMapID", resurrectionMapId),
                        new XElement("MapRegionID", mapRegionId),
                        new XElement("FatigueType", fatigueType),
                        new XElement("FatigueDeBuff", fatigueDebuff),
                        new XElement("FatigueStartTime", fatigueStartTime),
                        new XElement("FatigueAddTime", fatigueAddTime),
                        new XElement("FatigueAddPoint", fatigueAddPoint),
                        new XElement("CameraMaxLevel", cameraMaxLevel),
                        new XElement("XgConsumeType", xgConsumeType),
                        new XElement("BattleTagUse", battleTagUse)));
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MapList.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            string? folder =
                Path.GetDirectoryName(outputXml);

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidDataException(
                    "Não foi possível determinar XML\\MapList.");
            }

            Directory.CreateDirectory(folder);

            SaveXml(
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "utf-8",
                        null),
                    root),
                outputXml);

            AppLogger.Log(
                $"MapList: BIN -> XML concluído. " +
                $"{count:N0} mapas exportados.");

            AppLogger.Log(
                $"MapList: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "MapData",
                    "MapList.xml");

            List<XElement> maps =
                root.Elements("Map").ToList();

            long expectedSize;

            // Valida toda a estrutura antes de substituir Output.
            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteTable(
                    test,
                    maps);

                test.Flush();
                expectedSize = testStream.Length;
            }

            string? outputFolder =
                Path.GetDirectoryName(outputBin);

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidDataException(
                    "Pasta Output inválida para MapList.");
            }

            Directory.CreateDirectory(outputFolder);

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteTable(
                bw,
                maps);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapList.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"MapList: XML -> BIN concluído. " +
                $"{maps.Count:N0} mapas serializados.");

            AppLogger.Log(
                $"MapList: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> maps)
        {
            bw.Write(maps.Count);

            for (int i = 0; i < maps.Count; i++)
            {
                XElement map =
                    maps[i];

                uint mapId =
                    RequiredUInt(
                        map,
                        "MapID",
                        $"Map #{i}");

                string context =
                    $"MapList MapID={mapId}";

                bw.Write(mapId);

                WriteDynamicCp949(
                    bw,
                    RequiredTextAllowEmpty(
                        map,
                        "MapName",
                        context),
                    context + " <MapName>");

                WriteDynamicCp949(
                    bw,
                    RequiredTextAllowEmpty(
                        map,
                        "MapPath",
                        context),
                    context + " <MapPath>");

                WriteDynamicCp949(
                    bw,
                    RequiredTextAllowEmpty(
                        map,
                        "BGSound",
                        context),
                    context + " <BGSound>");

                bw.Write(
                    RequiredUInt(
                        map,
                        "Width",
                        context));

                bw.Write(
                    RequiredUInt(
                        map,
                        "Height",
                        context));

                WriteDynamicUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        map,
                        "MapDescription_Eng",
                        context));

                WriteDynamicUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        map,
                        "MapDescription",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "ResurrectionMapID",
                        context));

                // Campo físico reservado:
                // 0 em 172 / 172 mapas da amostra.
                bw.Write((ushort)0);

                bw.Write(
                    RequiredUInt16(
                        map,
                        "MapRegionID",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "FatigueType",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "FatigueDeBuff",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "FatigueStartTime",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "FatigueAddTime",
                        context));

                bw.Write(
                    RequiredInt16(
                        map,
                        "FatigueAddPoint",
                        context));

                bw.Write(
                    RequiredUInt16(
                        map,
                        "CameraMaxLevel",
                        context));

                byte xgConsumeType =
                    RequiredByte(
                        map,
                        "XgConsumeType",
                        context);

                byte battleTagUse =
                    RequiredByte(
                        map,
                        "BattleTagUse",
                        context);

                // No BIN estes dois campos não são UInt16 separados.
                //
                // Byte baixo  = XgConsumeType
                // Byte alto   = BattleTagUse
                //
                // Exemplos confirmados:
                // XG=1, BT=0 -> 0x0001
                // XG=0, BT=1 -> 0x0100
                ushort flags =
                    (ushort)(
                        xgConsumeType |
                        (battleTagUse << 8));

                bw.Write(flags);
            }
        }

        // ============================================================
        // STRINGS
        // ============================================================

        private static string ReadDynamicCp949(
            BinaryReader br,
            string field)
        {
            int byteCount =
                ReadCount(
                    br,
                    field + ".ByteLength",
                    10_000_000);

            byte[] raw =
                ReadExact(
                    br,
                    byteCount,
                    field);

            return Cp949.GetString(raw);
        }

        private static void WriteDynamicCp949(
            BinaryWriter bw,
            string value,
            string field)
        {
            byte[] raw;

            try
            {
                raw =
                    Cp949.GetBytes(
                        value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                throw new InvalidDataException(
                    $"{field}: contém caracteres que não podem ser " +
                    "representados em CP949. Usa texto compatível com a " +
                    "codificação original do MapList.bin.",
                    ex);
            }

            bw.Write(raw.Length);
            bw.Write(raw);
        }

        private static string ReadDynamicUnicode(
            BinaryReader br,
            string field)
        {
            int charCount =
                ReadCount(
                    br,
                    field + ".CharacterCount",
                    10_000_000);

            int byteCount =
                checked(charCount * 2);

            byte[] raw =
                ReadExact(
                    br,
                    byteCount,
                    field);

            return Encoding.Unicode.GetString(raw);
        }

        private static void WriteDynamicUnicode(
            BinaryWriter bw,
            string value)
        {
            string text =
                value ?? string.Empty;

            byte[] raw =
                Encoding.Unicode.GetBytes(text);

            if (raw.Length != checked(text.Length * 2))
            {
                throw new InvalidDataException(
                    "MapList: inconsistência interna ao codificar UTF-16LE.");
            }

            // O BIN guarda CharacterCount e não ByteCount.
            bw.Write(text.Length);
            bw.Write(raw);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] raw =
                br.ReadBytes(count);

            if (raw.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {count:N0} bytes; " +
                    $"recebidos {raw.Length:N0}. O BIN parece truncado.");
            }

            return raw;
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value =
                br.ReadInt32();

            if (value < 0 ||
                value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count/tamanho inválido ({value}). " +
                    $"Esperado entre 0 e {max:N0}.");
            }

            return value;
        }

        private static XDocument LoadXml(
            string path)
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
            XElement? root =
                doc.Root;

            if (root == null)
            {
                throw new InvalidDataException(
                    $"{context}: XML sem root.");
            }

            if (!root.Name.LocalName.Equals(
                expected,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
        }

        private static string RequiredTextAllowEmpty(
            XElement parent,
            string name,
            string context)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            return element.Value;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredTextAllowEmpty(
                    parent,
                    name,
                    context);

            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é UInt32 válido.");
            }

            return result;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredTextAllowEmpty(
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
                    $"{context}: <{name}>='{value}' não cabe em UInt16 " +
                    "(0..65535).");
            }

            return result;
        }

        private static short RequiredInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredTextAllowEmpty(
                    parent,
                    name,
                    context);

            if (!short.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out short result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em Int16 " +
                    "(-32768..32767).");
            }

            return result;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredTextAllowEmpty(
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
                    $"{context}: <{name}>='{value}' não cabe em byte " +
                    "(0..255).");
            }

            return result;
        }

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

        private static Encoding CreateCp949()
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(
                949,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }
    }
}
