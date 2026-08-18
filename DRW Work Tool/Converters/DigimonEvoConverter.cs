using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class DigimonEvoConverter : IGameDataConverter
    {
        public string Name => "DigimonEvo";

        private const int EvolutionRecordSize = 328;
        private const int EvolutionTypeCount = 9;
        private const int BoneNameBytes = 32;

        private static readonly Encoding Cp949 = CreateCp949();

        private static readonly string[] UInt16Fields =
        {
            "m_nEnableSlot",
            "m_nOpenQualification",
            "m_nOpenLevel",
            "m_nOpenQuest",
            "m_nOpenItemTypeS",
            "m_nOpenItemNum",
            "m_nUseItem",
            "m_nUseItemNum",
            "m_nIntimacy",
            "m_nOpenCrest",
            "m_EvoCard1",
            "m_EvoCard2",
            "m_EvoCard3",
            "m_nEvoDigimental",
            "m_nEvoTamerDS",
            "m_nDummy"
        };

        private static readonly string[] Int32MotionFields =
        {
            "StartPosX",
            "StartPosY",
            "m_nStartHegiht",
            "m_nStartRot",
            "OtherPosX",
            "OtherPosY",
            "m_nEndHegiht",
            "m_nEndRot",
            "m_nSpeed"
        };

        private static readonly string[] TailUInt16Fields =
        {
            "m_nChipsetType",
            "m_nChipsetTypeC",
            "m_nChipsetNum",
            "m_nChipsetTypeP",
            "m_nJoGressesNum",
            "unknow1"
        };

        private static readonly string[] JoGressTacticsFields =
        {
            "m_nJoGress_Tacticses1",
            "m_nJoGress_Tacticses2",
            "m_nJoGress_Tacticses3",
            "m_nJoGress_Tacticses4"
        };

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("DigimonEvo", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("DigimonEvo", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            XElement root = new("DigimonList");

            int digimonCount =
                ReadCount(
                    br,
                    "DigimonCount",
                    100_000);

            int totalEvolutions = 0;

            for (int d = 0; d < digimonCount; d++)
            {
                uint digimonId = br.ReadUInt32();
                int battleType = br.ReadInt32();

                int evolutionCount =
                    ReadCount(
                        br,
                        $"Digimon[{d}].CountEvo",
                        100_000);

                totalEvolutions += evolutionCount;

                XElement digimon =
                    new(
                        "Digimon",
                        new XElement("digiId", digimonId),
                        new XElement("BattleType", battleType),
                        new XElement("CountEvo", evolutionCount));

                for (int e = 0; e < evolutionCount; e++)
                {
                    long recordStart = ms.Position;

                    uint evoDigimonId = br.ReadUInt32();
                    ushort level = br.ReadUInt16();
                    ushort nType = br.ReadUInt16();

                    XElement evolution =
                        new(
                            "Evolution",
                            new XElement("digiId", evoDigimonId),
                            new XElement("Level", level),
                            new XElement("nType", nType),

                            // Este campo existia no extractor XML antigo,
                            // mas não ocupa bytes próprios no BIN.
                            new XElement("uShort1", 0));

                    for (int slot = 0;
                         slot < EvolutionTypeCount;
                         slot++)
                    {
                        uint nSlot = br.ReadUInt32();
                        uint nextDigimonId = br.ReadUInt32();

                        evolution.Add(
                            new XElement(
                                "EvolutionType",
                                new XElement("nSlot", nSlot),
                                new XElement("dwDigimonID", nextDigimonId)));
                    }

                    evolution.Add(
                        new XElement("m_IconPos", br.ReadInt32()));

                    evolution.Add(
                        new XElement("m_IconPos2", br.ReadInt32()));

                    foreach (string field in UInt16Fields)
                    {
                        evolution.Add(
                            new XElement(
                                field,
                                br.ReadUInt16()));
                    }

                    evolution.Add(
                        new XElement("Render", br.ReadInt32()));

                    foreach (string field in Int32MotionFields)
                    {
                        evolution.Add(
                            new XElement(
                                field,
                                br.ReadInt32()));
                    }

                    evolution.Add(
                        new XElement("m_dwAni", br.ReadUInt32()));

                    evolution.Add(
                        new XElement("unknow", br.ReadInt32()));

                    double startTime = br.ReadDouble();
                    double endTime = br.ReadDouble();

                    evolution.Add(
                        new XElement(
                            "m_fStartTime",
                            FormatDouble(startTime)));

                    evolution.Add(
                        new XElement(
                            "m_fEndTime",
                            FormatDouble(endTime)));

                    evolution.Add(
                        new XElement("m_nR", br.ReadInt32()));

                    evolution.Add(
                        new XElement("m_nG", br.ReadInt32()));

                    evolution.Add(
                        new XElement("m_nB", br.ReadInt32()));

                    evolution.Add(
                        new XElement(
                            "m_szLeve",
                            ReadFixedCp949(br, BoneNameBytes)));

                    evolution.Add(
                        new XElement(
                            "m_szEnchant",
                            ReadFixedCp949(br, BoneNameBytes)));

                    evolution.Add(
                        new XElement(
                            "m_szSize",
                            ReadFixedCp949(br, BoneNameBytes)));

                    int evolutionTree = br.ReadInt32();

                    evolution.Add(
                        new XElement(
                            "m_nEvolutionTree",
                            evolutionTree));

                    // Existe um DWORD reservado a zero no BIN.
                    // O XML antigo chamava ao valor seguinte
                    // m_nJoGressQuestCheck. Nos dois únicos casos
                    // não-zero do XML fornecido, esse valor é igual
                    // a m_nOpenQuest, mas o DWORD físico continua zero.
                    uint reserved = br.ReadUInt32();

                    if (reserved != 0)
                    {
                        throw new InvalidDataException(
                            $"DigimonEvo: DWORD reservado não-zero no " +
                            $"Digimon={evoDigimonId}, Level={level}. " +
                            $"Valor={reserved}.");
                    }

                    ushort openQuest =
                        ushort.Parse(
                            evolution.Element("m_nOpenQuest")!.Value,
                            CultureInfo.InvariantCulture);

                    int joGressQuestCheck =
                        evolutionTree == 2
                            ? openQuest
                            : 0;

                    evolution.Add(
                        new XElement(
                            "m_nJoGressQuestCheck",
                            joGressQuestCheck));

                    foreach (string field in TailUInt16Fields)
                    {
                        evolution.Add(
                            new XElement(
                                field,
                                br.ReadUInt16()));
                    }

                    foreach (string field in JoGressTacticsFields)
                    {
                        evolution.Add(
                            new XElement(
                                field,
                                br.ReadUInt32()));
                    }

                    long consumed =
                        ms.Position - recordStart;

                    if (consumed != EvolutionRecordSize)
                    {
                        throw new InvalidDataException(
                            $"DigimonEvo: Evolution record " +
                            $"Digimon={evoDigimonId}, Level={level} " +
                            $"ocupou {consumed} bytes; " +
                            $"esperado={EvolutionRecordSize}.");
                    }

                    digimon.Add(evolution);
                }

                root.Add(digimon);
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"DigimonEvo.bin contém {extra:N0} bytes extra. " +
                    $"Leitura terminou no offset {ms.Position:N0}, " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Pasta XML inválida para DigimonEvo."));

            SaveXml(
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "utf-8",
                        null),
                    root),
                outputXml);

            long expectedSize =
                CalculateExpectedSize(
                    digimonCount,
                    totalEvolutions);

            AppLogger.Log(
                $"DigimonEvo: BIN -> XML concluído. " +
                $"Digimons={digimonCount}, Evolutions={totalEvolutions}.");

            AppLogger.Log(
                $"DigimonEvo: tamanho BIN verificado: " +
                $"{data.Length:N0} / {expectedSize:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                XDocument.Load(
                    inputXml,
                    LoadOptions.SetLineInfo);

            XElement root =
                doc.Root
                ?? throw new InvalidDataException(
                    "DigimonEvo.xml não possui elemento root.");

            if (root.Name.LocalName != "DigimonList")
            {
                throw new InvalidDataException(
                    $"Root inválido em DigimonEvo.xml: " +
                    $"<{root.Name.LocalName}>. " +
                    $"Esperado <DigimonList>.");
            }

            List<XElement> digimons =
                root.Elements("Digimon").ToList();

            int totalEvolutions = 0;

            foreach (XElement digimon in digimons)
            {
                List<XElement> evolutions =
                    digimon.Elements("Evolution").ToList();

                int declaredCount =
                    RequiredInt(
                        digimon,
                        "CountEvo",
                        $"Digimon {RequiredUInt(digimon, "digiId", "Digimon")}");

                if (declaredCount != evolutions.Count)
                {
                    throw new InvalidDataException(
                        $"Digimon {RequiredUInt(digimon, "digiId", "Digimon")}: " +
                        $"<CountEvo>={declaredCount}, mas existem " +
                        $"{evolutions.Count} elementos <Evolution>.");
                }

                totalEvolutions += evolutions.Count;

                foreach (XElement evolution in evolutions)
                {
                    ValidateEvolution(evolution);
                }
            }

            long expectedSize =
                CalculateExpectedSize(
                    digimons.Count,
                    totalEvolutions);

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para DigimonEvo."));

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(
                    fs,
                    Encoding.UTF8,
                    leaveOpen: true);

            bw.Write(digimons.Count);

            foreach (XElement digimon in digimons)
            {
                uint groupId =
                    RequiredUInt(
                        digimon,
                        "digiId",
                        "Digimon");

                int battleType =
                    RequiredInt(
                        digimon,
                        "BattleType",
                        $"Digimon {groupId}");

                List<XElement> evolutions =
                    digimon.Elements("Evolution").ToList();

                bw.Write(groupId);
                bw.Write(battleType);
                bw.Write(evolutions.Count);

                foreach (XElement evolution in evolutions)
                {
                    WriteEvolution(bw, evolution);
                }
            }

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"DigimonEvo.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, " +
                    $"Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"DigimonEvo: XML -> BIN concluído. " +
                $"Digimons={digimons.Count}, Evolutions={totalEvolutions}.");

            AppLogger.Log(
                $"DigimonEvo: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. " +
                $"Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteEvolution(
            BinaryWriter bw,
            XElement evolution)
        {
            uint digiId =
                RequiredUInt(
                    evolution,
                    "digiId",
                    "Evolution");

            ushort level =
                RequiredUInt16(
                    evolution,
                    "Level",
                    $"Evolution {digiId}");

            ushort nType =
                RequiredUInt16(
                    evolution,
                    "nType",
                    $"Evolution {digiId}");

            // uShort1 é mantido no XML por compatibilidade,
            // mas não possui storage separado no BIN.
            XElement? uShort1 =
                evolution.Element("uShort1");

            if (uShort1 != null &&
                !ushort.TryParse(
                    uShort1.Value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort dummyValue))
            {
                throw new InvalidDataException(
                    $"Evolution {digiId}: " +
                    $"<uShort1>='{uShort1.Value}' não é UInt16 válido.");
            }

            if (uShort1 != null &&
                ushort.Parse(
                    uShort1.Value.Trim(),
                    CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidDataException(
                    $"Evolution {digiId}: <uShort1> deve permanecer 0. " +
                    $"Este campo existe no XML antigo, mas não possui bytes próprios no BIN.");
            }

            bw.Write(digiId);
            bw.Write(level);
            bw.Write(nType);

            List<XElement> evoTypes =
                evolution.Elements("EvolutionType").ToList();

            if (evoTypes.Count != EvolutionTypeCount)
            {
                throw new InvalidDataException(
                    $"Evolution {digiId}, Level {level}: " +
                    $"existem {evoTypes.Count} <EvolutionType>; " +
                    $"o BIN exige exatamente {EvolutionTypeCount}.");
            }

            foreach (XElement evoType in evoTypes)
            {
                bw.Write(
                    RequiredUInt(
                        evoType,
                        "nSlot",
                        $"Evolution {digiId}, Level {level}"));

                bw.Write(
                    RequiredUInt(
                        evoType,
                        "dwDigimonID",
                        $"Evolution {digiId}, Level {level}"));
            }

            bw.Write(
                RequiredInt(
                    evolution,
                    "m_IconPos",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredInt(
                    evolution,
                    "m_IconPos2",
                    $"Evolution {digiId}"));

            foreach (string field in UInt16Fields)
            {
                bw.Write(
                    RequiredUInt16(
                        evolution,
                        field,
                        $"Evolution {digiId}"));
            }

            bw.Write(
                RequiredInt(
                    evolution,
                    "Render",
                    $"Evolution {digiId}"));

            foreach (string field in Int32MotionFields)
            {
                bw.Write(
                    RequiredInt(
                        evolution,
                        field,
                        $"Evolution {digiId}"));
            }

            bw.Write(
                RequiredUInt(
                    evolution,
                    "m_dwAni",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredInt(
                    evolution,
                    "unknow",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredDouble(
                    evolution,
                    "m_fStartTime",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredDouble(
                    evolution,
                    "m_fEndTime",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredInt(
                    evolution,
                    "m_nR",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredInt(
                    evolution,
                    "m_nG",
                    $"Evolution {digiId}"));

            bw.Write(
                RequiredInt(
                    evolution,
                    "m_nB",
                    $"Evolution {digiId}"));

            WriteFixedCp949(
                bw,
                RequiredText(
                    evolution,
                    "m_szLeve",
                    $"Evolution {digiId}",
                    allowEmpty: true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szLeve>");

            WriteFixedCp949(
                bw,
                RequiredText(
                    evolution,
                    "m_szEnchant",
                    $"Evolution {digiId}",
                    allowEmpty: true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szEnchant>");

            WriteFixedCp949(
                bw,
                RequiredText(
                    evolution,
                    "m_szSize",
                    $"Evolution {digiId}",
                    allowEmpty: true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szSize>");

            int evolutionTree =
                RequiredInt(
                    evolution,
                    "m_nEvolutionTree",
                    $"Evolution {digiId}");

            bw.Write(evolutionTree);

            // DWORD reservado. Confirmado zero em todos os 824 records
            // do BIN fornecido.
            bw.Write(0u);

            // O XML antigo contém m_nJoGressQuestCheck, mas esse valor
            // não ocupa este DWORD. Nos records JoGress do XML fornecido
            // ele replica m_nOpenQuest.
            XElement? questCheck =
                evolution.Element("m_nJoGressQuestCheck");

            if (questCheck != null)
            {
                int value =
                    ParseInt(
                        questCheck.Value,
                        $"Evolution {digiId} <m_nJoGressQuestCheck>");

                int openQuest =
                    RequiredUInt16(
                        evolution,
                        "m_nOpenQuest",
                        $"Evolution {digiId}");

                int expectedQuestCheck =
                    evolutionTree == 2
                        ? openQuest
                        : 0;

                if (value != expectedQuestCheck)
                {
                    throw new InvalidDataException(
                        $"Evolution {digiId}: " +
                        $"<m_nJoGressQuestCheck>={value}, " +
                        $"mas para m_nEvolutionTree={evolutionTree} " +
                        $"o XML compatível espera {expectedQuestCheck}. " +
                        $"Este campo é derivado e não ocupa bytes próprios no BIN.");
                }
            }

            foreach (string field in TailUInt16Fields)
            {
                bw.Write(
                    RequiredUInt16(
                        evolution,
                        field,
                        $"Evolution {digiId}"));
            }

            foreach (string field in JoGressTacticsFields)
            {
                bw.Write(
                    RequiredUInt(
                        evolution,
                        field,
                        $"Evolution {digiId}"));
            }
        }

        private static void ValidateEvolution(
            XElement evolution)
        {
            uint digiId =
                RequiredUInt(
                    evolution,
                    "digiId",
                    "Evolution");

            RequiredUInt16(
                evolution,
                "Level",
                $"Evolution {digiId}");

            RequiredUInt16(
                evolution,
                "nType",
                $"Evolution {digiId}");

            List<XElement> evoTypes =
                evolution.Elements("EvolutionType").ToList();

            if (evoTypes.Count != EvolutionTypeCount)
            {
                throw new InvalidDataException(
                    $"Evolution {digiId}: " +
                    $"existem {evoTypes.Count} <EvolutionType>; " +
                    $"esperado={EvolutionTypeCount}.");
            }

            foreach (string field in UInt16Fields)
            {
                RequiredUInt16(
                    evolution,
                    field,
                    $"Evolution {digiId}");
            }

            foreach (string field in TailUInt16Fields)
            {
                RequiredUInt16(
                    evolution,
                    field,
                    $"Evolution {digiId}");
            }

            ValidateFixedCp949(
                RequiredText(
                    evolution,
                    "m_szLeve",
                    $"Evolution {digiId}",
                    true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szLeve>");

            ValidateFixedCp949(
                RequiredText(
                    evolution,
                    "m_szEnchant",
                    $"Evolution {digiId}",
                    true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szEnchant>");

            ValidateFixedCp949(
                RequiredText(
                    evolution,
                    "m_szSize",
                    $"Evolution {digiId}",
                    true),
                BoneNameBytes,
                $"Evolution {digiId} <m_szSize>");
        }

        private static long CalculateExpectedSize(
            int digimonCount,
            int totalEvolutions)
        {
            return
                4L +
                digimonCount * 12L +
                totalEvolutions * EvolutionRecordSize;
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

        private static string ReadFixedCp949(
            BinaryReader br,
            int byteCount)
        {
            byte[] bytes =
                br.ReadBytes(byteCount);

            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"Esperados {byteCount} bytes CP949, " +
                    $"recebidos {bytes.Length}.");
            }

            int zero =
                Array.IndexOf(
                    bytes,
                    (byte)0);

            if (zero < 0)
                zero = bytes.Length;

            return Cp949.GetString(
                bytes,
                0,
                zero);
        }

        private static void WriteFixedCp949(
            BinaryWriter bw,
            string text,
            int byteCount,
            string field)
        {
            byte[] raw =
                Cp949.GetBytes(text ?? "");

            if (raw.Length >= byteCount)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes CP949; " +
                    $"o buffer suporta no máximo {byteCount - 1} bytes + terminador.");
            }

            byte[] buffer =
                new byte[byteCount];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static void ValidateFixedCp949(
            string text,
            int byteCount,
            string field)
        {
            int size =
                Cp949.GetByteCount(text ?? "");

            if (size >= byteCount)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {size} bytes CP949; " +
                    $"limite={byteCount - 1} bytes.");
            }
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

            string value =
                element.Value;

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
            return ParseInt(
                RequiredText(
                    parent,
                    name,
                    context),
                $"{context} <{name}>");
        }

        private static int ParseInt(
            string text,
            string context)
        {
            if (!int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            {
                throw new InvalidDataException(
                    $"{context}='{text}' não é um Int32 válido.");
            }

            return value;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string text =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!uint.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{text}' não é um UInt32 válido.");
            }

            return value;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string text =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{text}' " +
                    $"não cabe num UInt16 (0..65535).");
            }

            return value;
        }

        private static double RequiredDouble(
            XElement parent,
            string name,
            string context)
        {
            string text =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{text}' não é um Double válido.");
            }

            return value;
        }

        private static string FormatDouble(
            double value)
        {
            if (value == Math.Truncate(value))
            {
                return value.ToString(
                    "0",
                    CultureInfo.InvariantCulture);
            }

            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
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
