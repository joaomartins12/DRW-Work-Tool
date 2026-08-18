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
    public sealed class SkillConverter : IGameDataConverter
    {
        public string Name => "Skill";

        private const int SkillRecordSize = 736;
        private const int SkillNameChars = 32;
        private const int SkillCommentChars = 256;
        private const int SkillApplyCount = 3;

        private const int TamerSkillRecordSize = 36;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Skill", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Skill", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\Skill.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            long skillStart = ms.Position;
            XDocument skillDoc = ReadSkillTable(br);
            long skillEnd = ms.Position;

            long tamerStart = ms.Position;
            XDocument tamerDoc = ReadTamerSkillTable(br);
            long tamerEnd = ms.Position;

            long areaStart = ms.Position;
            XDocument areaDoc = ReadAreaCheckTable(br);
            long areaEnd = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Skill.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho={ms.Length:N0}.");
            }

            SaveXml(
                skillDoc,
                Path.Combine(folder, "Skill.xml"));

            SaveXml(
                tamerDoc,
                Path.Combine(folder, "TamerSkill.xml"));

            SaveXml(
                areaDoc,
                Path.Combine(folder, "AreaCheck.xml"));

            AppLogger.Log(
                "Skill: BIN -> XML concluído. 3 XMLs gerados.");

            AppLogger.Log(
                $"Skill: secções em bytes -> " +
                $"Skill={skillEnd - skillStart:N0}, " +
                $"TamerSkill={tamerEnd - tamerStart:N0}, " +
                $"AreaCheck={areaEnd - areaStart:N0}.");

            AppLogger.Log(
                $"Skill: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\Skill.");

            string skillPath =
                Path.Combine(folder, "Skill.xml");

            string tamerPath =
                Path.Combine(folder, "TamerSkill.xml");

            string areaPath =
                Path.Combine(folder, "AreaCheck.xml");

            foreach (string path in new[]
            {
                skillPath,
                tamerPath,
                areaPath
            })
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Skill: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument skillDoc = LoadXml(skillPath);
            XDocument tamerDoc = LoadXml(tamerPath);
            XDocument areaDoc = LoadXml(areaPath);

            long expectedSize;

            // Valida tudo primeiro, sem substituir Output.
            using (MemoryStream counter = new())
            using (BinaryWriter test =
                new(counter, Encoding.UTF8, leaveOpen: true))
            {
                WriteSkillTable(test, skillDoc);
                WriteTamerSkillTable(test, tamerDoc);
                WriteAreaCheckTable(test, areaDoc);

                test.Flush();
                expectedSize = counter.Length;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Skill."));

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteSkillTable(bw, skillDoc);
            WriteTamerSkillTable(bw, tamerDoc);
            WriteAreaCheckTable(bw, areaDoc);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Skill.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "Skill: XML -> BIN concluído. " +
                "Skill, TamerSkill e AreaCheck validados.");

            AppLogger.Log(
                $"Skill: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // SKILL
        // ============================================================

        private static XDocument ReadSkillTable(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "Skill.Count",
                    1_000_000);

            XElement root =
                new("SkillDataArray");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                uint id =
                    br.ReadUInt32();

                string name =
                    ReadFixedUnicode(
                        br,
                        SkillNameChars);

                string comment =
                    ReadFixedUnicode(
                        br,
                        SkillCommentChars);

                XElement apply =
                    new("SkillApply");

                for (int a = 0; a < SkillApplyCount; a++)
                {
                    apply.Add(
                        new XElement(
                            "IncreaseApply",
                            new XElement("s_nA", br.ReadInt32()),
                            new XElement("s_nInvoke_Rate", br.ReadInt32()),
                            new XElement("s_nB", br.ReadInt32()),
                            new XElement("s_nC", br.ReadInt32()),
                            new XElement("s_nBuffCode", br.ReadUInt16()),
                            new XElement("s_nID", br.ReadUInt16()),
                            new XElement("s_nIncrease_B_Point", br.ReadInt32())));
                }

                XElement row =
                    new(
                        "SkillData",
                        new XElement("s_dwID", id),
                        new XElement("s_szName", name),
                        new XElement("s_szComment", comment),
                        apply,

                        new XElement("s_nLevelupPoint", br.ReadUInt16()),
                        new XElement("s_nMaxLevel", br.ReadUInt16()),
                        new XElement("s_nAttributeType", br.ReadUInt16()),
                        new XElement("s_nNatureType", br.ReadUInt16()),
                        new XElement("s_nFamilyType", br.ReadUInt16()),
                        new XElement("s_nUseHP", br.ReadUInt16()),
                        new XElement("s_nUseDS", br.ReadUInt16()),
                        new XElement("s_nIcon", br.ReadUInt16()),
                        new XElement("s_nTarget", br.ReadUInt16()),
                        new XElement("s_nAttType", br.ReadUInt16()),

                        new XElement("s_fAttRange", FloatText(br.ReadSingle())),
                        new XElement("s_fAttRange_MinDmg", FloatText(br.ReadSingle())),
                        new XElement("s_fAttRange_NorDmg", FloatText(br.ReadSingle())),
                        new XElement("s_fAttRange_MaxDmg", FloatText(br.ReadSingle())),

                        new XElement("s_nAttSphere", br.ReadUInt16()),
                        new XElement("s_fCastingTime", FloatText(br.ReadSingle())),
                        new XElement("s_fDamageTime", FloatText(br.ReadSingle())),
                        new XElement("s_nDamageDay", br.ReadInt32()),
                        new XElement("ink", br.ReadUInt16()),
                        new XElement("s_nDistanceTime", FloatText(br.ReadSingle())),
                        new XElement("s_fCooldownTime", FloatText(br.ReadSingle())),
                        new XElement("s_nCooldownDay", br.ReadUInt16()),
                        new XElement("unk", br.ReadUInt16()),
                        new XElement("s_fSkill_Velocity", FloatText(br.ReadSingle())),
                        new XElement("s_fSkill_Accel", FloatText(br.ReadSingle())),

                        new XElement("s_nSkillType", br.ReadUInt16()),
                        new XElement("s_nLimitLevel", br.ReadUInt16()),
                        new XElement("s_nSkillGroup", br.ReadUInt16()),
                        new XElement("s_nSkillRank", br.ReadUInt16()),
                        new XElement("s_nMemorySkill", br.ReadUInt16()),
                        new XElement("s_nReq_Item", br.ReadByte()),
                        new XElement("unk2", br.ReadByte()));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != SkillRecordSize)
                {
                    throw new InvalidDataException(
                        $"Skill ID={id}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={SkillRecordSize:N0}.");
                }

                root.Add(row);
            }

            return Xml(root);
        }

        private static void WriteSkillTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "SkillDataArray",
                    "Skill.xml");

            List<XElement> rows =
                root.Elements("SkillData").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(
                        row,
                        "s_dwID",
                        "Skill.xml");

                string context =
                    $"Skill ID={id}";

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
                    SkillNameChars,
                    $"{context} <s_szName>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "s_szComment",
                        context,
                        allowEmpty: true),
                    SkillCommentChars,
                    $"{context} <s_szComment>");

                XElement? skillApply =
                    row.Element("SkillApply");

                if (skillApply == null)
                {
                    throw new InvalidDataException(
                        $"{context}: falta <SkillApply>.");
                }

                List<XElement> applies =
                    skillApply
                        .Elements("IncreaseApply")
                        .ToList();

                if (applies.Count != SkillApplyCount)
                {
                    throw new InvalidDataException(
                        $"{context}: <SkillApply> deve conter exatamente " +
                        $"{SkillApplyCount} <IncreaseApply>; " +
                        $"encontrados {applies.Count}.");
                }

                for (int i = 0; i < applies.Count; i++)
                {
                    XElement apply =
                        applies[i];

                    string applyContext =
                        $"{context} IncreaseApply[{i}]";

                    bw.Write(
                        RequiredInt(
                            apply,
                            "s_nA",
                            applyContext));

                    bw.Write(
                        RequiredInt(
                            apply,
                            "s_nInvoke_Rate",
                            applyContext));

                    bw.Write(
                        RequiredInt(
                            apply,
                            "s_nB",
                            applyContext));

                    bw.Write(
                        RequiredInt(
                            apply,
                            "s_nC",
                            applyContext));

                    bw.Write(
                        RequiredUInt16(
                            apply,
                            "s_nBuffCode",
                            applyContext));

                    bw.Write(
                        RequiredUInt16(
                            apply,
                            "s_nID",
                            applyContext));

                    bw.Write(
                        RequiredInt(
                            apply,
                            "s_nIncrease_B_Point",
                            applyContext));
                }

                foreach (string field in new[]
                {
                    "s_nLevelupPoint",
                    "s_nMaxLevel",
                    "s_nAttributeType",
                    "s_nNatureType",
                    "s_nFamilyType",
                    "s_nUseHP",
                    "s_nUseDS",
                    "s_nIcon",
                    "s_nTarget",
                    "s_nAttType"
                })
                {
                    bw.Write(
                        RequiredUInt16(
                            row,
                            field,
                            context));
                }

                foreach (string field in new[]
                {
                    "s_fAttRange",
                    "s_fAttRange_MinDmg",
                    "s_fAttRange_NorDmg",
                    "s_fAttRange_MaxDmg"
                })
                {
                    bw.Write(
                        RequiredFloat(
                            row,
                            field,
                            context));
                }

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nAttSphere",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fCastingTime",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fDamageTime",
                        context));

                bw.Write(
                    RequiredInt(
                        row,
                        "s_nDamageDay",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "ink",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_nDistanceTime",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fCooldownTime",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nCooldownDay",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "unk",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fSkill_Velocity",
                        context));

                bw.Write(
                    RequiredFloat(
                        row,
                        "s_fSkill_Accel",
                        context));

                foreach (string field in new[]
                {
                    "s_nSkillType",
                    "s_nLimitLevel",
                    "s_nSkillGroup",
                    "s_nSkillRank",
                    "s_nMemorySkill"
                })
                {
                    bw.Write(
                        RequiredUInt16(
                            row,
                            field,
                            context));
                }

                bw.Write(
                    RequiredByte(
                        row,
                        "s_nReq_Item",
                        context));

                bw.Write(
                    RequiredByte(
                        row,
                        "unk2",
                        context));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != SkillRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={SkillRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // TAMER SKILL
        // ============================================================

        private static XDocument ReadTamerSkillTable(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "TamerSkill.Count",
                    100_000);

            XElement root =
                new("TamerSkillArray");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                uint index =
                    br.ReadUInt32();

                uint skillCode =
                    br.ReadUInt32();

                ushort type =
                    br.ReadUInt16();

                ushort unknown1 =
                    br.ReadUInt16();

                uint factor1 =
                    br.ReadUInt32();

                uint factor2 =
                    br.ReadUInt32();

                uint tamerSeq =
                    br.ReadUInt32();

                uint digimonSeq =
                    br.ReadUInt32();

                ushort useState =
                    br.ReadUInt16();

                ushort areaCheck =
                    br.ReadUInt16();

                ushort available =
                    br.ReadUInt16();

                ushort unknown3 =
                    br.ReadUInt16();

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != TamerSkillRecordSize)
                {
                    throw new InvalidDataException(
                        $"TamerSkill Index={index}: record ocupa {consumed} bytes; " +
                        $"esperado={TamerSkillRecordSize}.");
                }

                XElement row =
                    new("TamerSkill");

                row.Add(
                    new XElement(
                        "s_nIndex",
                        index));

                // Mantém o formato visual do XML de referência.
                if (index <= 6)
                {
                    row.Add(
                        new XElement(
                            "s_dwSkillCode",
                            skillCode));

                    row.Add(
                        new XElement(
                            "unknow",
                            0));
                }
                else
                {
                    row.Add(
                        new XElement(
                            "otherunknow",
                            0));

                    row.Add(
                        new XElement(
                            "s_dwSkillCode",
                            skillCode));
                }

                row.Add(
                    new XElement("s_nType", type),
                    new XElement("unknow1", unknown1),
                    new XElement("s_dwFactor1", factor1),
                    new XElement("s_dwFactor2", factor2),
                    new XElement("s_dwTamer_SeqID", tamerSeq),
                    new XElement("s_dwDigimon_SeqID", digimonSeq),
                    new XElement("s_nUseState", useState),
                    new XElement("s_nUse_Are_Check", areaCheck),
                    new XElement("s_nAvailable", available),
                    new XElement("unknow3", unknown3));

                root.Add(row);
            }

            return Xml(root);
        }

        private static void WriteTamerSkillTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "TamerSkillArray",
                    "TamerSkill.xml");

            List<XElement> rows =
                root.Elements("TamerSkill").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint index =
                    RequiredUInt(
                        row,
                        "s_nIndex",
                        "TamerSkill.xml");

                string context =
                    $"TamerSkill Index={index}";

                ValidateZeroAlias(
                    row,
                    "unknow",
                    context);

                ValidateZeroAlias(
                    row,
                    "otherunknow",
                    context);

                long start =
                    bw.BaseStream.Position;

                bw.Write(index);

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_dwSkillCode",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nType",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "unknow1",
                        context));

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_dwFactor1",
                        context));

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_dwFactor2",
                        context));

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_dwTamer_SeqID",
                        context));

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_dwDigimon_SeqID",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nUseState",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nUse_Are_Check",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nAvailable",
                        context));

                bw.Write(
                    RequiredUInt16(
                        row,
                        "unknow3",
                        context));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != TamerSkillRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed} bytes; " +
                        $"esperado={TamerSkillRecordSize}.");
                }
            }
        }

        // ============================================================
        // AREA CHECK
        // ============================================================

        private static XDocument ReadAreaCheckTable(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "AreaCheck.Count",
                    100_000);

            XElement root =
                new("AreaCheckArray");

            if (count != 0)
            {
                throw new InvalidDataException(
                    $"Skill.bin contém {count} entradas AreaCheck. " +
                    "A amostra analisada originalmente possui AreaCheck vazio, " +
                    "portanto o layout de records não-vazios ainda não está confirmado.");
            }

            return Xml(root);
        }

        private static void WriteAreaCheckTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "AreaCheckArray",
                    "AreaCheck.xml");

            List<XElement> entries =
                root.Elements().ToList();

            if (entries.Count != 0)
            {
                throw new InvalidDataException(
                    $"AreaCheck.xml contém {entries.Count} entrada(s), " +
                    "mas o Skill.bin de referência possui Count=0. " +
                    "Não é seguro inventar o layout de AreaCheck sem uma amostra " +
                    "de Skill.bin que contenha AreaCheck não-vazio.");
            }

            bw.Write(0);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static void ValidateZeroAlias(
            XElement parent,
            string name,
            string context)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
                return;

            if (!int.TryParse(
                element.Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{element.Value}' não é inteiro válido.");
            }

            if (value != 0)
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> é um campo auxiliar do XML " +
                    "e não possui bytes próprios no Skill.bin. " +
                    $"O valor deve permanecer 0; atual={value}.");
            }
        }

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
                    $"String UTF-16LE truncada. " +
                    $"Esperados={byteCount} bytes, recebidos={raw.Length}.");
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
                    $"Reduz o texto para no máximo {wcharCount} caracteres UTF-16 " +
                    "ou usa uma designação mais curta.");
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
