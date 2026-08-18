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
    public sealed class MapNpcConverter : IGameDataConverter
    {
        public string Name => "MapNpc";

        private const int RecordSize = 20;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapNpc", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapNpc", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            int count =
                ReadCount(
                    br,
                    "MapNpc.Count",
                    1_000_000);

            long expectedSize =
                4L + (long)count * RecordSize;

            if (data.LongLength != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapNpc.bin possui {data.LongLength:N0} bytes, " +
                    $"mas Count={count:N0} exige exatamente {expectedSize:N0} bytes. " +
                    $"Diferença={(data.LongLength - expectedSize):+#;-#;0} bytes.");
            }

            XElement root =
                new("MapNPCs");

            for (int i = 0; i < count; i++)
            {
                long start =
                    ms.Position;

                // ORDEM FÍSICA CONFIRMADA NO BIN:
                // NpcID, MapID, InitPosX, InitPosY, Rotation
                int npcId =
                    br.ReadInt32();

                int mapId =
                    br.ReadInt32();

                int initPosX =
                    br.ReadInt32();

                int initPosY =
                    br.ReadInt32();

                float rotation =
                    br.ReadSingle();

                long consumed =
                    ms.Position - start;

                if (consumed != RecordSize)
                {
                    throw new InvalidDataException(
                        $"MapNpc record #{i} / NpcID={npcId}: " +
                        $"record ocupa {consumed} bytes; esperado={RecordSize}.");
                }

                // Mantém a ordem visual do XML original.
                root.Add(
                    new XElement(
                        "MapNPC",
                        new XElement("MapID", mapId),
                        new XElement("NpcID", npcId),
                        new XElement("InitPosX", initPosX),
                        new XElement("InitPosY", initPosY),
                        new XElement(
                            "s_fRotation",
                            FloatText(rotation))));
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MapNpc.bin contém {extra:N0} bytes extra no final.");
            }

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\MapNpc.");

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
                $"MapNpc: BIN -> XML concluído. " +
                $"{count:N0} MapNPCs exportados.");

            AppLogger.Log(
                $"MapNpc: estrutura = 4 bytes Count + " +
                $"{count:N0} × {RecordSize} bytes.");

            AppLogger.Log(
                $"MapNpc: tamanho BIN verificado: " +
                $"{data.Length:N0} / {expectedSize:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "MapNPCs",
                    "MapNpc.xml");

            List<XElement> rows =
                root.Elements("MapNPC").ToList();

            long expectedSize =
                4L + (long)rows.Count * RecordSize;

            // Valida integralmente antes de substituir Output.
            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteTable(
                    test,
                    rows);

                test.Flush();

                if (testStream.Length != expectedSize)
                {
                    throw new InvalidDataException(
                        $"MapNpc: validação interna gerou " +
                        $"{testStream.Length:N0} bytes; " +
                        $"esperado={expectedSize:N0}.");
                }
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para MapNpc."));

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteTable(
                bw,
                rows);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapNpc.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"MapNpc: XML -> BIN concluído. " +
                $"{rows.Count:N0} MapNPCs serializados.");

            AppLogger.Log(
                $"MapNpc: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> rows)
        {
            bw.Write(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row =
                    rows[i];

                int npcId =
                    RequiredInt(
                        row,
                        "NpcID",
                        $"MapNPC #{i}");

                string context =
                    $"MapNPC NpcID={npcId}";

                long start =
                    bw.BaseStream.Position;

                // IMPORTANTE:
                // no XML MapID aparece primeiro,
                // mas no BIN NpcID vem primeiro.
                bw.Write(npcId);

                bw.Write(
                    RequiredInt(
                        row,
                        "MapID",
                        context));

                bw.Write(
                    RequiredInt(
                        row,
                        "InitPosX",
                        context));

                bw.Write(
                    RequiredInt(
                        row,
                        "InitPosY",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fRotation",
                        context));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != RecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed} bytes; " +
                        $"esperado={RecordSize}.");
                }
            }
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
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
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
            string context)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value =
                element.Value;

            if (string.IsNullOrWhiteSpace(value))
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
                    $"{context}: <{name}>='{value}' não é Int32 válido.");
            }

            return result;
        }

        private static float RequiredFloat(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!float.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é float válido. " +
                    "Usa ponto como separador decimal.");
            }

            if (float.IsNaN(result) ||
                float.IsInfinity(result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> não pode ser NaN ou Infinity.");
            }

            return result;
        }

        private static string FloatText(
            float value) =>
            value.ToString(
                "R",
                CultureInfo.InvariantCulture);

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
