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
    public sealed class DigimonListConverter : IGameDataConverter
    {
        public string Name => "DigimonList";

        private static readonly string[] Stats =
        {
            "HP", "DS", "DefPower", "Evasion", "MoveSpeed",
            "CriticalRate", "AttPower", "AttSpeed", "AttRange", "HitRate"
        };

        public bool MatchesBin(string filePath)
        {
            string n = Normalize(Path.GetFileNameWithoutExtension(filePath));
            return n == "digimonlist";
        }

        public bool MatchesXml(string filePath)
        {
            string n = Normalize(Path.GetFileNameWithoutExtension(filePath));
            return n == "digimonlist";
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            XDocument doc = XDocument.Load(inputXml, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root ?? throw new InvalidDataException("XML sem elemento root.");

            int skillSlots = ParseInt(root.Attribute("SkillSlots")?.Value ?? "5");
            if (skillSlots is not (4 or 5))
                throw new InvalidDataException($"SkillSlots inválido: {skillSlots}. Esperado 4 ou 5.");

            int stride = 396 + skillSlots * 8 + 12 + 128 + 4;
            List<XElement> digimons = root.Elements("Digimon").ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(outputBin)!);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs);

            bw.Write(digimons.Count);

            foreach (XElement dig in digimons)
            {
                int digimonId = ParseInt(RequiredAttr(dig, "ID"));
                byte[] rec = new byte[stride];

                WriteInt32(rec, 0, digimonId);
                WriteInt32(rec, 4, ParseInt(Value(dig, "ModelID")));

                WriteWideBuffer(rec, 8, 64, dig.Attribute("Name")?.Value ?? string.Empty);
                WriteCp949Buffer(rec, 136, 64, Value(dig, "SoundDir", string.Empty));

                WriteSingle(rec, 200, ParseFloat(Value(dig, "SelectScale")));
                WriteWideBuffer(rec, 204, 64, Value(dig, "EvoEffectDir", string.Empty));

                WriteInt32(rec, 332, ParseInt(Value(dig, "EvolutionType")));
                WriteInt32(rec, 336, ParseInt(Value(dig, "AttributeType")));

                int[] families = ParseCsvInts(Value(dig, "FamilyTypes", "0,0,0"), 3);
                for (int i = 0; i < 3; i++)
                    WriteInt32(rec, 340 + i * 4, families[i]);

                WriteInt32(rec, 352, ParseInt(Value(dig, "BaseNatureType")));

                int[] natures = ParseCsvInts(Value(dig, "BaseNatureTypes", "0,0,0"), 3);
                for (int i = 0; i < 3; i++)
                    WriteInt32(rec, 356 + i * 4, natures[i]);

                WriteInt32(rec, 368, ParseInt(Value(dig, "BaseLevel")));

                XElement? stats = dig.Element("Stats");
                for (int i = 0; i < Stats.Length; i++)
                {
                    ushort v = checked((ushort)ParseInt(stats?.Attribute(Stats[i])?.Value ?? "0"));
                    WriteUInt16(rec, 372 + i * 2, v);
                }

                rec[392] = unchecked((byte)ParseInt(Value(dig, "DigimonType")));
                WriteUInt16(rec, 394, checked((ushort)ParseInt(Value(dig, "CharSize"))));

                Dictionary<int, (uint Id, int Req)> skills = new();
                XElement? skillsNode = dig.Element("Skills");

                if (skillsNode != null)
                {
                    foreach (XElement s in skillsNode.Elements("Skill"))
                    {
                        int slot = ParseInt(RequiredAttr(s, "Slot"));
                        uint id = ParseUInt(RequiredAttr(s, "ID"));
                        int req = ParseInt(s.Attribute("ReqPrevSkillLevel")?.Value ?? "0");
                        skills[slot] = (id, req);
                    }
                }

                foreach (var pair in skills)
                {
                    if (pair.Key >= skillSlots && pair.Value.Id != 0)
                        throw new InvalidDataException(
                            $"Digimon {digimonId}: skill no slot {pair.Key} excede SkillSlots={skillSlots}.");
                }

                for (int slot = 0; slot < skillSlots; slot++)
                {
                    skills.TryGetValue(slot, out var skill);
                    WriteUInt32(rec, 396 + slot * 8, skill.Id);
                    WriteInt32(rec, 400 + slot * 8, skill.Req);
                }

                int fbase = 396 + skillSlots * 8;

                WriteSingle(rec, fbase, ParseFloat(Value(dig, "WalkLen")));
                WriteSingle(rec, fbase + 4, ParseFloat(Value(dig, "RunLen")));
                WriteSingle(rec, fbase + 8, ParseFloat(Value(dig, "ARunLen")));

                WriteWideBuffer(rec, fbase + 12, 64, Value(dig, "Form", string.Empty));
                WriteInt32(rec, fbase + 140, ParseInt(Value(dig, "DigimonRank")));

                bw.Write(rec);
            }

            long expected = 4L + (long)digimons.Count * stride;
            if (fs.Length != expected)
                throw new InvalidDataException($"Tamanho BIN inesperado: {fs.Length}; esperado {expected}.");

            AppLogger.Log(
                $"{Name}: XML -> BIN concluído. Digimons={digimons.Count}, SkillSlots={skillSlots}, Stride={stride}, Bytes={fs.Length}.");
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] data = File.ReadAllBytes(inputBin);
            if (data.Length < 4)
                throw new InvalidDataException("BIN demasiado pequeno.");

            int count = BitConverter.ToInt32(data, 0);
            if (count < 0)
                throw new InvalidDataException($"Quantidade de Digimons inválida: {count}.");

            int skillSlots;

            if (count == 0)
            {
                skillSlots = 5;
            }
            else
            {
                int payload = data.Length - 4;
                if (payload % count != 0)
                    throw new InvalidDataException("O tamanho do BIN não corresponde ao número de registos.");

                int stride = payload / count;
                skillSlots = stride switch
                {
                    572 => 4,
                    580 => 5,
                    _ => throw new InvalidDataException(
                        $"Stride DigimonList desconhecido: {stride}. Esperado 572 ou 580.")
                };
            }

            int recordStride = 396 + skillSlots * 8 + 12 + 128 + 4;
            long expected = 4L + (long)count * recordStride;
            if (data.Length != expected)
                throw new InvalidDataException($"Tamanho inválido: {data.Length}; esperado {expected}.");

            XElement root = new("DigimonList",
                new XAttribute("SkillSlots", skillSlots));

            int pos = 4;

            for (int index = 0; index < count; index++)
            {
                int start = pos;

                int id = ReadInt32(data, start);
                int modelId = ReadInt32(data, start + 4);
                string name = ReadWideBuffer(data, start + 8, 64);
                string soundDir = ReadCp949Buffer(data, start + 136, 64);
                float selectScale = ReadSingle(data, start + 200);
                string evoEffectDir = ReadWideBuffer(data, start + 204, 64);

                int evolutionType = ReadInt32(data, start + 332);
                int attributeType = ReadInt32(data, start + 336);

                int[] fam =
                {
                    ReadInt32(data, start + 340),
                    ReadInt32(data, start + 344),
                    ReadInt32(data, start + 348)
                };

                int baseNatureType = ReadInt32(data, start + 352);

                int[] nat =
                {
                    ReadInt32(data, start + 356),
                    ReadInt32(data, start + 360),
                    ReadInt32(data, start + 364)
                };

                int baseLevel = ReadInt32(data, start + 368);

                XElement stats = new("Stats");
                for (int i = 0; i < Stats.Length; i++)
                    stats.SetAttributeValue(Stats[i], ReadUInt16(data, start + 372 + i * 2));

                int digimonType = data[start + 392];
                ushort charSize = ReadUInt16(data, start + 394);

                XElement skills = new("Skills");
                for (int slot = 0; slot < skillSlots; slot++)
                {
                    uint skillId = ReadUInt32(data, start + 396 + slot * 8);
                    int req = ReadInt32(data, start + 400 + slot * 8);

                    skills.Add(new XElement("Skill",
                        new XAttribute("Slot", slot),
                        new XAttribute("ID", skillId),
                        new XAttribute("ReqPrevSkillLevel", req)));
                }

                int fbase = start + 396 + skillSlots * 8;

                float walkLen = ReadSingle(data, fbase);
                float runLen = ReadSingle(data, fbase + 4);
                float aRunLen = ReadSingle(data, fbase + 8);
                string form = ReadWideBuffer(data, fbase + 12, 64);
                int rank = ReadInt32(data, fbase + 140);

                XElement dig = new("Digimon",
                    new XAttribute("ID", id),
                    new XAttribute("Name", name),
                    new XElement("ModelID", modelId),
                    new XElement("SoundDir", soundDir),
                    new XElement("SelectScale", F(selectScale)),
                    new XElement("EvoEffectDir", evoEffectDir),
                    new XElement("EvolutionType", evolutionType),
                    new XElement("AttributeType", attributeType),
                    new XElement("FamilyTypes", string.Join(",", fam)),
                    new XElement("BaseNatureType", baseNatureType),
                    new XElement("BaseNatureTypes", string.Join(",", nat)),
                    new XElement("BaseLevel", baseLevel),
                    stats,
                    new XElement("DigimonType", digimonType),
                    new XElement("CharSize", charSize),
                    skills,
                    new XElement("WalkLen", F(walkLen)),
                    new XElement("RunLen", F(runLen)),
                    new XElement("ARunLen", F(aRunLen)),
                    new XElement("Form", form),
                    new XElement("DigimonRank", rank));

                root.Add(dig);
                pos += recordStride;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputXml)!);

            XDocument doc = new(new XDeclaration("1.0", "utf-8", null), root);
            doc.Save(outputXml);

            AppLogger.Log(
                $"{Name}: BIN -> XML concluído. Digimons={count}, SkillSlots={skillSlots}, Stride={recordStride}.");
        }

        private static string Normalize(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static string RequiredAttr(XElement e, string name)
        {
            return e.Attribute(name)?.Value
                   ?? throw new InvalidDataException($"Atributo obrigatório '{name}' em <{e.Name}>.");
        }

        private static string Value(XElement e, string tag, string defaultValue = "0")
        {
            return e.Element(tag)?.Value ?? defaultValue;
        }

        private static int ParseInt(string s) =>
            int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static uint ParseUInt(string s) =>
            uint.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static float ParseFloat(string s) =>
            float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static int[] ParseCsvInts(string s, int expected)
        {
            int[] values = s.Split(',').Select(x => ParseInt(x)).ToArray();
            if (values.Length != expected)
                throw new InvalidDataException($"Esperados {expected} valores CSV, recebidos {values.Length}: {s}");
            return values;
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void WriteWideBuffer(byte[] rec, int offset, int wcharCount, string text)
        {
            byte[] raw = Encoding.Unicode.GetBytes(text ?? string.Empty);
            int max = (wcharCount - 1) * 2;
            int len = Math.Min(raw.Length, max);
            Buffer.BlockCopy(raw, 0, rec, offset, len);
        }

        private static void WriteCp949Buffer(byte[] rec, int offset, int charCount, string text)
        {
            Encoding cp949 = Encoding.GetEncoding(
                949,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);

            byte[] raw = cp949.GetBytes(text ?? string.Empty);
            int len = Math.Min(raw.Length, charCount - 1);
            Buffer.BlockCopy(raw, 0, rec, offset, len);
        }

        private static string ReadWideBuffer(byte[] data, int offset, int wcharCount)
        {
            int len = wcharCount * 2;
            string s = Encoding.Unicode.GetString(data, offset, len);
            int nul = s.IndexOf('\0');
            return nul >= 0 ? s[..nul] : s;
        }

        private static string ReadCp949Buffer(byte[] data, int offset, int charCount)
        {
            Encoding cp949 = Encoding.GetEncoding(
                949,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);

            int len = 0;
            while (len < charCount && data[offset + len] != 0)
                len++;

            return cp949.GetString(data, offset, len);
        }

        private static void WriteInt32(byte[] b, int o, int v) =>
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);

        private static void WriteUInt32(byte[] b, int o, uint v) =>
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);

        private static void WriteUInt16(byte[] b, int o, ushort v) =>
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 2);

        private static void WriteSingle(byte[] b, int o, float v) =>
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);

        private static int ReadInt32(byte[] b, int o) => BitConverter.ToInt32(b, o);
        private static uint ReadUInt32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
        private static ushort ReadUInt16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
        private static float ReadSingle(byte[] b, int o) => BitConverter.ToSingle(b, o);
    }
}
