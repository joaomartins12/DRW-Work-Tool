using DRW_Work_Tool.Core;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class CharCreateTableConverter : IGameDataConverter
    {
        public string Name => "CharCreateTable";

        private static readonly Encoding Cp949 = CreateCp949();

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("CharCreateTable", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("CharCreateTable", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            XElement characterRoot = new("CharacterList");
            XElement digimonRoot = new("DigimonList");

            // ========================================================
            // TAMERS
            // ========================================================
            int tamerCount = ReadCount(br, "TamerCount", 10_000);

            for (int i = 0; i < tamerCount; i++)
            {
                uint tamerId = br.ReadUInt32();
                byte show = br.ReadByte();
                byte enable = br.ReadByte();
                int seasonType = br.ReadInt32();

                int voiceSize =
                    ReadCount(
                        br,
                        $"Tamer[{i}].m_sVoiceSize",
                        1_000_000);

                string voiceFile =
                    ReadSizedCp949(
                        br,
                        voiceSize,
                        $"Tamer[{i}].m_sVoiceFile");

                int iconIdx = br.ReadInt32();

                int itemCount =
                    ReadCount(
                        br,
                        $"Tamer[{i}].CountDC",
                        100_000);

                XElement itemIds = new("ItemIDs");

                for (int item = 0; item < itemCount; item++)
                {
                    itemIds.Add(
                        new XElement(
                            "ItemID",
                            br.ReadInt32()));
                }

                characterRoot.Add(
                    new XElement(
                        "Tamer",
                        new XElement("dwTamerID", tamerId),
                        new XElement("m_bShow", show),
                        new XElement("m_bEnable", enable),
                        new XElement("m_nSeasonType", seasonType),
                        new XElement("m_sVoiceSize", voiceSize),
                        new XElement("m_sVoiceFile", voiceFile),
                        new XElement("m_nIconIdx", iconIdx),
                        new XElement("CountDC", itemCount),
                        itemIds));
            }

            long digimonSectionOffset = ms.Position;

            // ========================================================
            // DIGIMONS
            // ========================================================
            int digimonCount = ReadCount(br, "DigimonCount", 100_000);

            for (int i = 0; i < digimonCount; i++)
            {
                uint digimonId = br.ReadUInt32();
                byte show = br.ReadByte();
                byte enable = br.ReadByte();

                int voiceSize =
                    ReadCount(
                        br,
                        $"Digimon[{i}].m_sVoiceSize",
                        1_000_000);

                string voiceFile =
                    ReadSizedCp949(
                        br,
                        voiceSize,
                        $"Digimon[{i}].m_sVoiceFile");

                digimonRoot.Add(
                    new XElement(
                        "Digimon",
                        new XElement("m_digimonID", digimonId),
                        new XElement("d_bShow", show),
                        new XElement("d_bEnable", enable),
                        new XElement("m_sVoiceSize", voiceSize),
                        new XElement("m_sVoiceFile", voiceFile)));
            }

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"CharCreateTable.bin contém {extra} bytes extra após a estrutura esperada. " +
                    $"Leitura terminou no offset {ms.Position}, ficheiro possui {ms.Length} bytes.");
            }

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML de CharCreateTable.");

            Directory.CreateDirectory(folder);

            string digimonXml =
                Path.Combine(
                    folder,
                    "DigimonCreateTable.xml");

            SaveXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    characterRoot),
                outputXml);

            SaveXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    digimonRoot),
                digimonXml);

            AppLogger.Log(
                $"CharCreateTable: BIN -> XML concluído. " +
                $"Tamers={tamerCount}, Digimons={digimonCount}.");

            AppLogger.Log(
                $"CharCreateTable: secção Digimon começa no offset {digimonSectionOffset:N0}.");

            AppLogger.Log(
                $"CharCreateTable: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");

            AppLogger.Log(
                $"CharCreateTable XML: {outputXml}");

            AppLogger.Log(
                $"DigimonCreateTable XML: {digimonXml}");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML de CharCreateTable.");

            string digimonXml =
                Path.Combine(
                    folder,
                    "DigimonCreateTable.xml");

            if (!File.Exists(inputXml))
            {
                throw new FileNotFoundException(
                    $"CharCreateTable.xml não encontrado: {inputXml}",
                    inputXml);
            }

            if (!File.Exists(digimonXml))
            {
                throw new FileNotFoundException(
                    $"DigimonCreateTable.xml não encontrado: {digimonXml}",
                    digimonXml);
            }

            XDocument charDoc = XDocument.Load(inputXml);
            XDocument digimonDoc = XDocument.Load(digimonXml);

            XElement charRoot =
                charDoc.Root
                ?? throw new InvalidDataException(
                    "CharCreateTable.xml não possui elemento root.");

            XElement digiRoot =
                digimonDoc.Root
                ?? throw new InvalidDataException(
                    "DigimonCreateTable.xml não possui elemento root.");

            if (charRoot.Name.LocalName != "CharacterList")
            {
                throw new InvalidDataException(
                    $"Root inválido em CharCreateTable.xml: " +
                    $"<{charRoot.Name.LocalName}>. Esperado <CharacterList>.");
            }

            if (digiRoot.Name.LocalName != "DigimonList")
            {
                throw new InvalidDataException(
                    $"Root inválido em DigimonCreateTable.xml: " +
                    $"<{digiRoot.Name.LocalName}>. Esperado <DigimonList>.");
            }

            var tamers =
                charRoot.Elements("Tamer").ToList();

            var digimons =
                digiRoot.Elements("Digimon").ToList();

            long expectedSize = 4;

            // Calcula primeiro o tamanho esperado a partir do XML.
            foreach (XElement tamer in tamers)
            {
                string voice =
                    RequiredText(
                        tamer,
                        "m_sVoiceFile",
                        "Tamer",
                        allowEmpty: true);

                int declaredVoiceSize =
                    RequiredInt(
                        tamer,
                        "m_sVoiceSize",
                        "Tamer");

                int actualVoiceSize =
                    Cp949.GetByteCount(voice);

                if (declaredVoiceSize != actualVoiceSize)
                {
                    throw new InvalidDataException(
                        $"Tamer ID {RequiredUInt(tamer, "dwTamerID", "Tamer")}: " +
                        $"<m_sVoiceSize>={declaredVoiceSize}, mas " +
                        $"<m_sVoiceFile> ocupa {actualVoiceSize} bytes CP949.");
                }

                XElement? itemContainer =
                    tamer.Element("ItemIDs");

                int actualItems =
                    itemContainer?.Elements("ItemID").Count() ?? 0;

                int declaredItems =
                    RequiredInt(
                        tamer,
                        "CountDC",
                        "Tamer");

                if (declaredItems != actualItems)
                {
                    throw new InvalidDataException(
                        $"Tamer ID {RequiredUInt(tamer, "dwTamerID", "Tamer")}: " +
                        $"<CountDC>={declaredItems}, mas existem {actualItems} <ItemID>.");
                }

                // uint ID + byte + byte + int season + int voiceSize
                // + voice bytes + int icon + int CountDC + item IDs
                expectedSize +=
                    4 + 1 + 1 + 4 + 4 +
                    actualVoiceSize +
                    4 + 4 +
                    actualItems * 4L;
            }

            expectedSize += 4;

            foreach (XElement digimon in digimons)
            {
                string voice =
                    RequiredText(
                        digimon,
                        "m_sVoiceFile",
                        "Digimon",
                        allowEmpty: true);

                int declaredVoiceSize =
                    RequiredInt(
                        digimon,
                        "m_sVoiceSize",
                        "Digimon");

                int actualVoiceSize =
                    Cp949.GetByteCount(voice);

                if (declaredVoiceSize != actualVoiceSize)
                {
                    throw new InvalidDataException(
                        $"Digimon ID {RequiredUInt(digimon, "m_digimonID", "Digimon")}: " +
                        $"<m_sVoiceSize>={declaredVoiceSize}, mas " +
                        $"<m_sVoiceFile> ocupa {actualVoiceSize} bytes CP949.");
                }

                // uint ID + byte show + byte enable + int voiceSize + voice bytes
                expectedSize +=
                    4 + 1 + 1 + 4 +
                    actualVoiceSize;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            // ========================================================
            // TAMERS
            // ========================================================
            bw.Write(tamers.Count);

            foreach (XElement tamer in tamers)
            {
                uint id =
                    RequiredUInt(
                        tamer,
                        "dwTamerID",
                        "Tamer");

                bw.Write(id);

                bw.Write(
                    RequiredByte(
                        tamer,
                        "m_bShow",
                        $"Tamer {id}"));

                bw.Write(
                    RequiredByte(
                        tamer,
                        "m_bEnable",
                        $"Tamer {id}"));

                bw.Write(
                    RequiredInt(
                        tamer,
                        "m_nSeasonType",
                        $"Tamer {id}"));

                string voice =
                    RequiredText(
                        tamer,
                        "m_sVoiceFile",
                        $"Tamer {id}",
                        allowEmpty: true);

                byte[] voiceBytes =
                    Cp949.GetBytes(voice);

                bw.Write(voiceBytes.Length);
                bw.Write(voiceBytes);

                bw.Write(
                    RequiredInt(
                        tamer,
                        "m_nIconIdx",
                        $"Tamer {id}"));

                XElement? itemContainer =
                    tamer.Element("ItemIDs");

                var items =
                    itemContainer?
                        .Elements("ItemID")
                        .ToList()
                    ?? new System.Collections.Generic.List<XElement>();

                bw.Write(items.Count);

                foreach (XElement item in items)
                {
                    if (!int.TryParse(
                        item.Value.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int itemId))
                    {
                        throw new InvalidDataException(
                            $"Tamer {id}: <ItemID>='{item.Value}' não é um Int32 válido.");
                    }

                    bw.Write(itemId);
                }
            }

            // ========================================================
            // DIGIMONS
            // ========================================================
            bw.Write(digimons.Count);

            foreach (XElement digimon in digimons)
            {
                uint id =
                    RequiredUInt(
                        digimon,
                        "m_digimonID",
                        "Digimon");

                bw.Write(id);

                bw.Write(
                    RequiredByte(
                        digimon,
                        "d_bShow",
                        $"Digimon {id}"));

                bw.Write(
                    RequiredByte(
                        digimon,
                        "d_bEnable",
                        $"Digimon {id}"));

                string voice =
                    RequiredText(
                        digimon,
                        "m_sVoiceFile",
                        $"Digimon {id}",
                        allowEmpty: true);

                byte[] voiceBytes =
                    Cp949.GetBytes(voice);

                bw.Write(voiceBytes.Length);
                bw.Write(voiceBytes);
            }

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"CharCreateTable.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"CharCreateTable: XML -> BIN concluído. " +
                $"Tamers={tamers.Count}, Digimons={digimons.Count}.");

            AppLogger.Log(
                $"CharCreateTable: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
            }

            return value;
        }

        private static string ReadSizedCp949(
            BinaryReader br,
            int byteCount,
            string field)
        {
            byte[] bytes = br.ReadBytes(byteCount);

            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {byteCount} bytes, " +
                    $"mas só existem {bytes.Length}.");
            }

            return Cp949.GetString(bytes);
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

        private static int RequiredInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é um Int32 válido.");
            }

            return result;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
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
                    $"{context}: <{name}>='{value}' não é um UInt32 válido.");
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
                    $"{context}: <{name}>='{value}' deve estar entre 0 e 255.");
            }

            return result;
        }

        private static void SaveXml(
            XDocument document,
            string path)
        {
            using var writer =
                System.Xml.XmlWriter.Create(
                    path,
                    new System.Xml.XmlWriterSettings
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
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
    }
}
