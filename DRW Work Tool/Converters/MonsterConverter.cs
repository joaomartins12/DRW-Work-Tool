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
    public sealed class MonsterConverter : IGameDataConverter
    {
        public string Name => "Monster";

        private const int MonsterRecordSize = 396;
        private const int MonsterHitRecordSize = 8;
        private const int MonsterSkillRecordSize = 144;
        private const int MonsterSkillTermRecordSize = 12;

        private const int MonsterNameChars = 64;
        private const int MonsterCommentChars = 32;
        private const int MonsterTitleChars = 32;
        private const int MonsterReservedChars = 32;
        private const int NoticeEffectChars = 32;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Monster", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath);

            return stem.Equals("Monster", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("MonsterHit", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("MonstersSkill", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("MonstersSkillTerms", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Monster: não foi possível determinar XML\\Monster.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            long s0 = ms.Position;
            XDocument monster = ReadMonsterTable(br);
            long e0 = ms.Position;

            long s1 = ms.Position;
            XDocument hit = ReadMonsterHit(br);
            long e1 = ms.Position;

            long s2 = ms.Position;
            XDocument skills = ReadMonsterSkills(br);
            long e2 = ms.Position;

            long s3 = ms.Position;
            XDocument terms = ReadMonsterSkillTerms(br);
            long e3 = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Monster.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou em {ms.Position:N0}; tamanho total={ms.Length:N0}.");
            }

            SaveXml(monster, Path.Combine(folder, "Monster.xml"));
            SaveXml(hit, Path.Combine(folder, "MonsterHit.xml"));
            SaveXml(skills, Path.Combine(folder, "MonstersSkill.xml"));
            SaveXml(terms, Path.Combine(folder, "MonstersSkillTerms.xml"));

            AppLogger.Log(
                "Monster: BIN -> XML concluído. 4 XMLs gerados.");

            AppLogger.Log(
                $"Monster: secções em bytes -> " +
                $"Monster={e0 - s0:N0}, MonsterHit={e1 - s1:N0}, " +
                $"MonstersSkill={e2 - s2:N0}, MonstersSkillTerms={e3 - s3:N0}.");

            AppLogger.Log(
                $"Monster: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Directory.Exists(inputXml)
                    ? inputXml
                    : Path.GetDirectoryName(inputXml)
                        ?? throw new InvalidDataException(
                            "Monster: não foi possível determinar XML\\Monster.");

            string monsterPath = Path.Combine(folder, "Monster.xml");
            string hitPath = Path.Combine(folder, "MonsterHit.xml");
            string skillPath = Path.Combine(folder, "MonstersSkill.xml");
            string termsPath = Path.Combine(folder, "MonstersSkillTerms.xml");

            string[] required =
            {
                monsterPath,
                hitPath,
                skillPath,
                termsPath
            };

            List<string> missing =
                required.Where(x => !File.Exists(x)).ToList();

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    "Monster: faltam XMLs obrigatórios:\n" +
                    string.Join("\n", missing.Select(x => "- " + Path.GetFileName(x))) +
                    "\nSão necessários Monster.xml, MonsterHit.xml, " +
                    "MonstersSkill.xml e MonstersSkillTerms.xml.");
            }

            XDocument monster = LoadXml(monsterPath);
            XDocument hit = LoadXml(hitPath);
            XDocument skills = LoadXml(skillPath);
            XDocument terms = LoadXml(termsPath);

            ValidateSkillNoticeNames(skills);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test = new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteAll(test, monster, hit, skills, terms);
                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Monster: pasta Output inválida.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAll(bw, monster, hit, skills, terms);
            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Monster.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}.");
            }

            AppLogger.Log(
                "Monster: XML -> BIN concluído. 4 tabelas serializadas.");

            AppLogger.Log(
                $"Monster: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteAll(
            BinaryWriter bw,
            XDocument monster,
            XDocument hit,
            XDocument skills,
            XDocument terms)
        {
            WriteMonsterTable(bw, monster);
            WriteMonsterHit(bw, hit);
            WriteMonsterSkills(bw, skills);
            WriteMonsterSkillTerms(bw, terms);
        }

        // ============================================================
        // MONSTER MAIN TABLE
        // ============================================================

        private static XDocument ReadMonsterTable(BinaryReader br)
        {
            int count = ReadCount(br, "Monster.Count", 1_000_000);
            XElement root = new("Monsters");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint monsterId = br.ReadUInt32();
                uint modelDigimon = br.ReadUInt32();

                string name =
                    ReadFixedUnicode(br, MonsterNameChars, $"MonsterID={monsterId}.Name");

                string comment =
                    ReadFixedUnicode(br, MonsterCommentChars, $"MonsterID={monsterId}.Comment");

                uint reservedBeforeTitle = br.ReadUInt32();

                if (reservedBeforeTitle != 0)
                {
                    throw new InvalidDataException(
                        $"MonsterID={monsterId}: reservedBeforeTitle={reservedBeforeTitle}; esperado=0.");
                }

                string title =
                    ReadFixedUnicode(br, MonsterTitleChars, $"MonsterID={monsterId}.Title");

                byte[] reservedTitleBlock =
                    ReadExact(br, MonsterReservedChars * 2, $"MonsterID={monsterId}.ReservedTitleBlock");

                if (reservedTitleBlock.Any(x => x != 0))
                {
                    throw new InvalidDataException(
                        $"MonsterID={monsterId}: bloco reservado de 64 bytes contém dados. " +
                        "O XML atual não possui campo para os preservar.");
                }

                ushort level = br.ReadUInt16();
                ushort exp = br.ReadUInt16();
                ushort battle = br.ReadUInt16();
                ushort unknown = br.ReadUInt16();

                uint hp = br.ReadUInt32();
                uint ds = br.ReadUInt32();

                ushort de = br.ReadUInt16();
                ushort ev = br.ReadUInt16();
                ushort ms = br.ReadUInt16();
                ushort ws = br.ReadUInt16();
                ushort ct = br.ReadUInt16();
                ushort at = br.ReadUInt16();
                ushort attackSpeed = br.ReadUInt16();
                ushort ar = br.ReadUInt16();
                ushort ht = br.ReadUInt16();
                ushort sight = br.ReadUInt16();
                ushort huntRange = br.ReadUInt16();

                float scale = br.ReadSingle();

                ushort unknown2 = br.ReadUInt16();
                ushort monsterClass = br.ReadUInt16();

                ushort[] icons = new ushort[6];
                for (int x = 0; x < icons.Length; x++)
                    icons[x] = br.ReadUInt16();

                ushort expMin = br.ReadUInt16();
                ushort expMax = br.ReadUInt16();
                ushort unknown3 = br.ReadUInt16();

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    MonsterRecordSize,
                    $"MonsterID={monsterId}");

                root.Add(
                    new XElement(
                        "Monster",
                        new XElement("MonsterID", monsterId),
                        new XElement("ModelDigimon", modelDigimon),
                        new XElement("Name", name),
                        new XElement("Comment", comment),
                        new XElement("Title", title),
                        new XElement("HP", hp),
                        new XElement("DS", ds),
                        new XElement("DE", de),
                        new XElement("EV", ev),
                        new XElement("MS", ms),
                        new XElement("WS", ws),
                        new XElement("CT", ct),
                        new XElement("AT", at),
                        new XElement("AS", attackSpeed),
                        new XElement("AR", ar),
                        new XElement("HT", ht),
                        new XElement("Sight", sight),
                        new XElement("HuntRange", huntRange),
                        new XElement("Scale", scale.ToString("R", CultureInfo.InvariantCulture)),
                        new XElement("Unknown2", unknown2),
                        new XElement("Class", monsterClass),
                        new XElement("Icon1", icons[0]),
                        new XElement("Icon2", icons[1]),
                        new XElement("Icon3", icons[2]),
                        new XElement("Icon4", icons[3]),
                        new XElement("Icon5", icons[4]),
                        new XElement("Icon6", icons[5]),
                        new XElement("ExpMin", expMin),
                        new XElement("ExpMax", expMax),
                        new XElement("Unknown3", unknown3),

                        // O XML legado contém Title duas vezes.
                        new XElement("Title", title),

                        new XElement("Level", level),
                        new XElement("EXP", exp),
                        new XElement("Battle", battle),
                        new XElement("Unknown", unknown)));
            }

            return Xml(root);
        }

        private static void WriteMonsterTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(doc, "Monsters", "Monster.xml");
            List<XElement> rows = root.Elements("Monster").ToList();

            ValidateUnique(rows, "MonsterID", "Monster.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint monsterId =
                    RequiredUInt(row, "MonsterID", "Monster.xml");

                string context = $"MonsterID={monsterId}";
                long start = bw.BaseStream.Position;

                List<XElement> titleNodes =
                    row.Elements("Title").ToList();

                if (titleNodes.Count == 0)
                {
                    throw new InvalidDataException(
                        $"{context}: falta <Title>.");
                }

                string title = titleNodes[0].Value;

                if (titleNodes.Count > 1 &&
                    titleNodes.Any(x => x.Value != title))
                {
                    throw new InvalidDataException(
                        $"{context}: existem múltiplos <Title> com valores diferentes. " +
                        "O formato legado duplica a tag, mas ambas devem ter o mesmo conteúdo.");
                }

                bw.Write(monsterId);
                bw.Write(RequiredUInt(row, "ModelDigimon", context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "Name", context),
                    MonsterNameChars,
                    $"{context} <Name>");

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "Comment", context),
                    MonsterCommentChars,
                    $"{context} <Comment>");

                // Campo reservado físico antes do título.
                bw.Write((uint)0);

                WriteFixedUnicode(
                    bw,
                    title,
                    MonsterTitleChars,
                    $"{context} <Title>");

                // 64 bytes reservados, sempre 0 na amostra.
                bw.Write(new byte[MonsterReservedChars * 2]);

                bw.Write(RequiredUInt16(row, "Level", context));
                bw.Write(RequiredUInt16(row, "EXP", context));
                bw.Write(RequiredUInt16(row, "Battle", context));
                bw.Write(RequiredUInt16(row, "Unknown", context));

                bw.Write(RequiredUInt(row, "HP", context));
                bw.Write(RequiredUInt(row, "DS", context));

                foreach (string field in new[]
                {
                    "DE","EV","MS","WS","CT","AT","AS","AR","HT",
                    "Sight","HuntRange"
                })
                {
                    bw.Write(RequiredUInt16(row, field, context));
                }

                bw.Write(RequiredFloat(row, "Scale", context));

                foreach (string field in new[]
                {
                    "Unknown2","Class",
                    "Icon1","Icon2","Icon3","Icon4","Icon5","Icon6",
                    "ExpMin","ExpMax","Unknown3"
                })
                {
                    bw.Write(RequiredUInt16(row, field, context));
                }

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    MonsterRecordSize,
                    context);
            }
        }

        // ============================================================
        // MONSTER HIT
        // ============================================================

        private static XDocument ReadMonsterHit(BinaryReader br)
        {
            int count = ReadCount(br, "MonsterHit.Count", 100_000);
            XElement root = new("Monsters");

            for (int i = 0; i < count; i++)
            {
                uint level = br.ReadUInt32();
                uint hit = br.ReadUInt32();

                root.Add(
                    new XElement(
                        "Monster",
                        new XAttribute("Lv", level),
                        new XAttribute("Ht", hit)));
            }

            return Xml(root);
        }

        private static void WriteMonsterHit(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(doc, "Monsters", "MonsterHit.xml");
            List<XElement> rows = root.Elements("Monster").ToList();

            bw.Write(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row = rows[i];

                bw.Write(
                    RequiredAttributeUInt(
                        row,
                        "Lv",
                        $"MonsterHit row #{i + 1}"));

                bw.Write(
                    RequiredAttributeUInt(
                        row,
                        "Ht",
                        $"MonsterHit row #{i + 1}"));
            }
        }

        // ============================================================
        // MONSTER SKILLS
        // ============================================================

        private static XDocument ReadMonsterSkills(BinaryReader br)
        {
            int count = ReadCount(br, "MonstersSkill.Count", 1_000_000);
            XElement root = new("MonsterSkills");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                ushort skillIdx = br.ReadUInt16();
                ushort unk = br.ReadUInt16();

                uint monsterId = br.ReadUInt32();
                uint coolTime = br.ReadUInt32();
                uint castTime = br.ReadUInt32();

                ushort castCheck = br.ReadUInt16();
                ushort targetCnt = br.ReadUInt16();
                ushort targetMinCnt = br.ReadUInt16();
                ushort targetMaxCnt = br.ReadUInt16();
                ushort useTerms = br.ReadUInt16();
                ushort skillType = br.ReadUInt16();

                uint effValMin = br.ReadUInt32();
                uint effValMax = br.ReadUInt32();

                ushort unk2 = br.ReadUInt16();
                ushort rangeIdx = br.ReadUInt16();

                uint sequenceId = br.ReadUInt32();

                ushort aniDelay = br.ReadUInt16();
                ushort velocity = br.ReadUInt16();
                ushort accel = br.ReadUInt16();

                ushort effFactor = br.ReadUInt16();
                ushort effFactor2 = br.ReadUInt16();
                ushort effFactor3 = br.ReadUInt16();

                uint effFactVal = br.ReadUInt32();
                uint effFactVal2 = br.ReadUInt32();
                uint effFactVal3 = br.ReadUInt32();

                uint talkId = br.ReadUInt32();
                uint activeType = br.ReadUInt32();

                float noticeTime = br.ReadSingle();

                string noticeEffect =
                    ReadFixedUnicode(
                        br,
                        NoticeEffectChars,
                        $"MonsterSkill Skill_IDX={skillIdx}.NoticeEffname");

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    MonsterSkillRecordSize,
                    $"MonsterSkill Skill_IDX={skillIdx}");

                root.Add(
                    new XElement(
                        "MonsterSkill",
                        new XElement("Skill_IDX", skillIdx),
                        new XElement("unk", unk),
                        new XElement("MonsterID", monsterId),
                        new XElement("CoolTime", coolTime),
                        new XElement("CastTime", castTime),
                        new XElement("CastCheck", castCheck),
                        new XElement("Target_Cnt", targetCnt),
                        new XElement("Target_MinCnt", targetMinCnt),
                        new XElement("Target_MaxCnt", targetMaxCnt),
                        new XElement("UseTerms", useTerms),
                        new XElement("Skill_Type", skillType),
                        new XElement("Eff_Val_Min", effValMin),
                        new XElement("Eff_Val_Max", effValMax),
                        new XElement("unk2", unk2),
                        new XElement("RangeIDX", rangeIdx),
                        new XElement("SequenceID", sequenceId),
                        new XElement("Ani_Delay", aniDelay),
                        new XElement("Valocity", velocity),
                        new XElement("Accel", accel),
                        new XElement("Eff_Factor", effFactor),
                        new XElement("Eff_Factor2", effFactor2),
                        new XElement("Eff_Factor3", effFactor3),
                        new XElement("Eff_Fact_Val", effFactVal),
                        new XElement("Eff_Fact_Val2", effFactVal2),
                        new XElement("Eff_Fact_Val3", effFactVal3),
                        new XElement("TalkID", talkId),
                        new XElement("Activetype", activeType),
                        new XElement(
                            "NoticeTime",
                            noticeTime.ToString("R", CultureInfo.InvariantCulture)),
                        new XElement("NoticeEffname", noticeEffect)));
            }

            return Xml(root);
        }

        private static void WriteMonsterSkills(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "MonsterSkills", "MonstersSkill.xml");

            List<XElement> rows =
                root.Elements("MonsterSkill").ToList();

            ValidateUnique(rows, "Skill_IDX", "MonstersSkill.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort skillIdx =
                    RequiredUInt16(row, "Skill_IDX", "MonstersSkill.xml");

                string context =
                    $"MonsterSkill Skill_IDX={skillIdx}";

                long start = bw.BaseStream.Position;

                bw.Write(skillIdx);
                bw.Write(RequiredUInt16(row, "unk", context));

                bw.Write(RequiredUInt(row, "MonsterID", context));
                bw.Write(RequiredUInt(row, "CoolTime", context));
                bw.Write(RequiredUInt(row, "CastTime", context));

                foreach (string field in new[]
                {
                    "CastCheck","Target_Cnt","Target_MinCnt","Target_MaxCnt",
                    "UseTerms","Skill_Type"
                })
                {
                    bw.Write(RequiredUInt16(row, field, context));
                }

                bw.Write(RequiredUInt(row, "Eff_Val_Min", context));
                bw.Write(RequiredUInt(row, "Eff_Val_Max", context));

                bw.Write(RequiredUInt16(row, "unk2", context));
                bw.Write(RequiredUInt16(row, "RangeIDX", context));

                bw.Write(RequiredUInt(row, "SequenceID", context));

                foreach (string field in new[]
                {
                    "Ani_Delay","Valocity","Accel",
                    "Eff_Factor","Eff_Factor2","Eff_Factor3"
                })
                {
                    bw.Write(RequiredUInt16(row, field, context));
                }

                bw.Write(RequiredUInt(row, "Eff_Fact_Val", context));
                bw.Write(RequiredUInt(row, "Eff_Fact_Val2", context));
                bw.Write(RequiredUInt(row, "Eff_Fact_Val3", context));

                bw.Write(RequiredUInt(row, "TalkID", context));
                bw.Write(RequiredUInt(row, "Activetype", context));

                bw.Write(RequiredFloat(row, "NoticeTime", context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "NoticeEffname", context),
                    NoticeEffectChars,
                    $"{context} <NoticeEffname>");

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    MonsterSkillRecordSize,
                    context);
            }
        }

        private static void ValidateSkillNoticeNames(XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "MonsterSkills", "MonstersSkill.xml");

            List<XElement> suspicious = new();

            foreach (XElement row in root.Elements("MonsterSkill"))
            {
                string value =
                    RequiredTextAllowEmpty(
                        row,
                        "NoticeEffname",
                        "MonstersSkill.xml");

                if (value.Length == 1 &&
                    (value == "s" || value == "1" || value == "2"))
                {
                    suspicious.Add(row);
                }
            }

            if (suspicious.Count > 100)
            {
                List<string> sample =
                    suspicious
                        .Take(10)
                        .Select(
                            x =>
                                $"Skill_IDX={RequiredUInt16(x, "Skill_IDX", "MonstersSkill.xml")} " +
                                $"MonsterID={RequiredUInt(x, "MonsterID", "MonstersSkill.xml")} " +
                                $"NoticeEffname='{RequiredTextAllowEmpty(x, "NoticeEffname", "MonstersSkill.xml")}'")
                        .ToList();

                throw new InvalidDataException(
                    $"MonstersSkill.xml parece ter NoticeEffname truncados pelo exporter antigo. " +
                    $"Foram encontradas {suspicious.Count:N0} entradas suspeitas de 1 carácter. " +
                    $"O Monster.bin original contém paths completos em muitas destas skills. " +
                    $"Exemplos:\n{string.Join("\n", sample)}\n" +
                    "Usa um MonstersSkill.xml exportado pelo MonsterConverter atualizado " +
                    "ou substitui pelo MonstersSkill_Corrected.xml fornecido.");
            }
        }

        // ============================================================
        // MONSTER SKILL TERMS
        // ============================================================

        private static XDocument ReadMonsterSkillTerms(BinaryReader br)
        {
            int count =
                ReadCount(br, "MonsterSkillTerms.Count", 100_000);

            XElement root = new("MonsterSkillTerms");

            for (int i = 0; i < count; i++)
            {
                ushort idx = br.ReadUInt16();
                ushort direction = br.ReadUInt16();
                uint range = br.ReadUInt32();
                ushort targetingType = br.ReadUInt16();
                ushort refCode = br.ReadUInt16();

                root.Add(
                    new XElement(
                        "MonsterSkillTerm",
                        new XElement("IDX", idx),
                        new XElement("Direction", direction),
                        new XElement("Range", range),
                        new XElement("TargetingType", targetingType),
                        new XElement("RefCode", refCode)));
            }

            return Xml(root);
        }

        private static void WriteMonsterSkillTerms(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "MonsterSkillTerms",
                    "MonstersSkillTerms.xml");

            List<XElement> rows =
                root.Elements("MonsterSkillTerm").ToList();

            ValidateUnique(rows, "IDX", "MonstersSkillTerms.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort idx =
                    RequiredUInt16(row, "IDX", "MonstersSkillTerms.xml");

                string context =
                    $"MonsterSkillTerm IDX={idx}";

                bw.Write(idx);
                bw.Write(RequiredUInt16(row, "Direction", context));
                bw.Write(RequiredUInt(row, "Range", context));
                bw.Write(RequiredUInt16(row, "TargetingType", context));
                bw.Write(RequiredUInt16(row, "RefCode", context));
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] raw = br.ReadBytes(count);

            if (raw.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: BIN truncado. Esperados={count:N0} bytes, " +
                    $"recebidos={raw.Length:N0}.");
            }

            return raw;
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount,
            string field)
        {
            byte[] raw =
                ReadExact(br, wcharCount * 2, field);

            string text =
                Encoding.Unicode.GetString(raw);

            int zero = text.IndexOf('\0');

            return zero >= 0
                ? text[..zero]
                : text;
        }

        private static void WriteFixedUnicode(
            BinaryWriter bw,
            string value,
            int wcharCount,
            string field)
        {
            string text = value ?? string.Empty;
            byte[] raw = Encoding.Unicode.GetBytes(text);
            int capacity = wcharCount * 2;

            if (raw.Length > capacity)
            {
                throw new InvalidDataException(
                    $"{field}: texto demasiado longo. " +
                    $"Atual={text.Length:N0} chars/{raw.Length:N0} bytes. " +
                    $"Máximo={wcharCount:N0} chars/{capacity:N0} bytes.");
            }

            bw.Write(raw);

            if (raw.Length < capacity)
                bw.Write(new byte[capacity - raw.Length]);
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            if (br.BaseStream.Position + 4 > br.BaseStream.Length)
                throw new EndOfStreamException($"{field}: faltam 4 bytes.");

            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: Count inválido ({value}). Máximo esperado={max:N0}.");
            }

            return value;
        }

        private static void ValidateRecordSize(
            long actual,
            int expected,
            string context)
        {
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"{context}: record ocupa {actual:N0} bytes; " +
                    $"esperado={expected:N0}.");
            }
        }

        private static void ValidateUnique(
            IReadOnlyList<XElement> rows,
            string field,
            string file)
        {
            Dictionary<uint, int> seen = new();

            for (int i = 0; i < rows.Count; i++)
            {
                uint id = RequiredUInt(rows[i], field, $"{file} row #{i + 1}");

                if (seen.TryGetValue(id, out int old))
                {
                    throw new InvalidDataException(
                        $"{file}: <{field}> duplicado {id}. " +
                        $"Entradas #{old + 1} e #{i + 1}.");
                }

                seen[id] = i;
            }
        }

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(
                    path,
                    LoadOptions.SetLineInfo |
                    LoadOptions.PreserveWhitespace);
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
                throw new InvalidDataException($"{context}: XML sem root.");

            if (!root.Name.LocalName.Equals(expected, StringComparison.Ordinal))
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
            XElement? element = parent.Element(name);

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
            string raw =
                RequiredTextAllowEmpty(parent, name, context);

            if (!uint.TryParse(
                raw.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é UInt32 válido.");
            }

            return value;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredTextAllowEmpty(parent, name, context);

            if (!ushort.TryParse(
                raw.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não cabe em UInt16.");
            }

            return value;
        }

        private static float RequiredFloat(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredTextAllowEmpty(parent, name, context);

            if (!float.TryParse(
                raw.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é float32 válido.");
            }

            return value;
        }

        private static uint RequiredAttributeUInt(
            XElement element,
            string name,
            string context)
        {
            XAttribute? attr = element.Attribute(name);

            if (attr == null)
                throw new InvalidDataException($"{context}: falta atributo {name}.");

            if (!uint.TryParse(
                attr.Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"{context}: {name}='{attr.Value}' não é UInt32 válido.");
            }

            return value;
        }

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration("1.0", "utf-8", null),
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
                        OmitXmlDeclaration = false,
                        NewLineHandling = NewLineHandling.None
                    });

            document.Save(writer);
        }
    }
}
