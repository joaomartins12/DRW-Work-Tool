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
    public sealed class TacticsConverter : IGameDataConverter
    {
        public string Name => "Tactics";

        public bool MatchesBin(string filePath) =>
            Normalize(Path.GetFileNameWithoutExtension(filePath)) == "tactics";

        public bool MatchesXml(string filePath) =>
            Normalize(Path.GetFileNameWithoutExtension(filePath)) == "tactics";

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc = XDocument.Load(inputXml, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root ?? throw new InvalidDataException("XML sem elemento root.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputBin)!);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs);

            // 1. HatchData
            List<XElement> entries = root.Element("HatchData")?.Elements("Entry").ToList()
                                      ?? new List<XElement>();

            bw.Write(entries.Count);

            foreach (XElement e in entries)
            {
                Dictionary<string, XElement> grades = e.Elements("Data")
                    .Where(x => x.Attribute("Grade") != null)
                    .ToDictionary(x => x.Attribute("Grade")!.Value, x => x);

                if (!grades.TryGetValue("Low", out XElement? low) ||
                    !grades.TryGetValue("Mid", out XElement? mid))
                    throw new InvalidDataException("HatchData Entry precisa de Data Grade='Low' e 'Mid'.");

                bw.Write(ParseUInt(Required(e, "Key")));
                bw.Write(ParseInt(Required(e, "DigimonID")));

                bw.Write(ParseInt(Required(low, "ItemSType")));
                bw.Write(ParseInt(Required(mid, "ItemSType")));

                bw.Write(checked((ushort)ParseInt(Required(low, "Count"))));
                bw.Write(checked((ushort)ParseInt(Required(mid, "Count"))));

                bw.Write(checked((byte)ParseInt(Required(low, "LimitLevel"))));
                bw.Write(checked((byte)ParseInt(Required(mid, "LimitLevel"))));

                bw.Write(checked((byte)ParseInt(Required(low, "ViewWarning"))));
                bw.Write(checked((byte)ParseInt(Required(mid, "ViewWarning"))));
            }

            // 2. Explains
            List<XElement> explains = root.Element("Explains")?.Elements("Explain").ToList()
                                       ?? new List<XElement>();

            bw.Write(explains.Count);

            foreach (XElement e in explains)
            {
                bw.Write(ParseUInt(Required(e, "Key")));
                bw.Write(ParseUInt(Required(e, "TacticsMonID")));
                WriteWideBuffer(bw, e.Attribute("Name")?.Value ?? string.Empty, 64);
                WriteWideBuffer(bw, e.Value ?? string.Empty, 512);
            }

            // 3. EnchantItems
            List<XElement> items = root.Element("EnchantItems")?.Elements("Item").ToList()
                                    ?? new List<XElement>();

            bw.Write(items.Count);

            foreach (XElement it in items)
            {
                bw.Write(ParseUInt(Required(it, "Code")));
                bw.Write(ParseInt(Required(it, "LowLevel")));
                bw.Write(ParseInt(Required(it, "HighLevel")));
                bw.Write(ParseUInt(Required(it, "NeedMoney")));
            }

            // 4. EnchantStats
            List<XElement> stats = root.Element("EnchantStats")?.Elements("Stat").ToList()
                                    ?? new List<XElement>();

            bw.Write(stats.Count);

            foreach (XElement st in stats)
            {
                List<XElement> ranges = st.Elements("Range").ToList();

                bw.Write(ParseInt(Required(st, "Index")));
                bw.Write(ranges.Count);

                foreach (XElement r in ranges)
                {
                    bw.Write(ParseInt(Required(r, "LowEnchant")));
                    bw.Write(ParseInt(Required(r, "HighEnchant")));
                    bw.Write(ParseInt(Required(r, "GrowMin")));
                    bw.Write(ParseInt(Required(r, "GrowMax")));
                    bw.Write(ParseInt(Required(r, "NormalMin")));
                    bw.Write(ParseInt(Required(r, "NormalMax")));
                    bw.Write(ParseInt(Required(r, "Special")));
                }
            }

            // 5. EnchantDefaultCorrect
            bw.Write(ParseInt(root.Element("EnchantDefaultCorrect")?.Value ?? "5"));

            // 6. SameTypeCorrect
            List<XElement> groups = root.Element("SameTypeCorrect")?.Elements("Group").ToList()
                                     ?? new List<XElement>();

            bw.Write(groups.Count);

            foreach (XElement g in groups)
            {
                List<XElement> corrects = g.Elements("Correct").ToList();

                bw.Write(ParseInt(Required(g, "SameType")));
                bw.Write(corrects.Count);

                foreach (XElement c in corrects)
                {
                    bw.Write(ParseInt(Required(c, "HatchGrade")));
                    bw.Write(ParseFloat(Required(c, "Value")));
                }
            }

            // 7. TranscendInfo
            List<XElement> evos = root.Element("TranscendInfo")?.Elements("EvoType").ToList()
                                   ?? new List<XElement>();

            bw.Write(evos.Count);

            foreach (XElement ev in evos)
            {
                List<XElement> reqs = ev.Elements("Req").ToList();

                bw.Write(ParseInt(Required(ev, "Value")));
                bw.Write(reqs.Count);

                foreach (XElement r in reqs)
                {
                    bw.Write(ParseInt(Required(r, "CurrentHatch")));
                    bw.Write(ParseInt(Required(r, "NeedLevel")));
                    bw.Write(ParseInt(Required(r, "NeedEnchant")));
                    bw.Write(ParseInt(Required(r, "MatEvoMin")));
                    bw.Write(ParseInt(Required(r, "MatEvoMax")));
                    bw.Write(ParseInt(Required(r, "NeedScale")));
                    bw.Write(ParseInt(Required(r, "NextHatch")));
                    bw.Write(ParseInt(Required(r, "MatHatchMin")));
                    bw.Write(ParseInt(Required(r, "MatHatchMax")));
                    bw.Write(ParseUInt(Required(r, "Cost")));
                    bw.Write(ParseUInt(Required(r, "MaxExp")));
                }
            }

            // 8. TranscendEvo
            List<XElement> digis = root.Element("TranscendEvo")?.Elements("Digimon").ToList()
                                    ?? new List<XElement>();

            bw.Write(digis.Count);

            foreach (XElement d in digis)
            {
                var grouped = d.Elements("Material")
                    .GroupBy(m => ChargeToInt(Required(m, "Charge")))
                    .OrderBy(g => g.Key)
                    .ToList();

                bw.Write(ParseUInt(Required(d, "ID")));
                bw.Write(grouped.Count);

                foreach (var group in grouped)
                {
                    List<XElement> mats = group.ToList();

                    bw.Write(group.Key);
                    bw.Write(mats.Count);

                    foreach (XElement m in mats)
                    {
                        bw.Write(ParseInt(Required(m, "NeedCount")));
                        bw.Write(ParseUInt(Required(m, "ItemTypeLS")));
                        bw.Write(ParseUInt(Required(m, "ExpPercent")));
                    }
                }
            }

            // 9. FixedExpDigimon
            List<XElement> fixedDigis = root.Element("FixedExpDigimon")?.Elements("Digimon").ToList()
                                         ?? new List<XElement>();

            bw.Write(fixedDigis.Count);

            foreach (XElement d in fixedDigis)
            {
                List<XElement> fixedRows = d.Elements("Fixed").ToList();

                bw.Write(ParseUInt(Required(d, "ID")));
                bw.Write(fixedRows.Count);

                foreach (XElement f in fixedRows)
                {
                    bw.Write(ParseInt(Required(f, "HatchGrade")));
                    bw.Write(ParseUInt(Required(f, "Points")));
                }
            }

            AppLogger.Log($"{Name}: XML -> BIN concluído. Bytes={fs.Length}.");
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            using FileStream fs = File.OpenRead(inputBin);
            using BinaryReader br = new(fs);

            XElement root = new("Tactics");

            // 1. HatchData
            XElement hatchData = new("HatchData");
            int hatchCount = ReadCount(br, "HatchData");

            for (int i = 0; i < hatchCount; i++)
            {
                uint key = br.ReadUInt32();
                int digimonId = br.ReadInt32();

                int lowItem = br.ReadInt32();
                int midItem = br.ReadInt32();

                ushort lowCount = br.ReadUInt16();
                ushort midCount = br.ReadUInt16();

                byte lowLimit = br.ReadByte();
                byte midLimit = br.ReadByte();

                byte lowWarning = br.ReadByte();
                byte midWarning = br.ReadByte();

                hatchData.Add(new XElement("Entry",
                    new XAttribute("Key", key),
                    new XAttribute("DigimonID", digimonId),
                    new XElement("Data",
                        new XAttribute("Grade", "Low"),
                        new XAttribute("ItemSType", lowItem),
                        new XAttribute("Count", lowCount),
                        new XAttribute("LimitLevel", lowLimit),
                        new XAttribute("ViewWarning", lowWarning)),
                    new XElement("Data",
                        new XAttribute("Grade", "Mid"),
                        new XAttribute("ItemSType", midItem),
                        new XAttribute("Count", midCount),
                        new XAttribute("LimitLevel", midLimit),
                        new XAttribute("ViewWarning", midWarning))));
            }
            root.Add(hatchData);

            // 2. Explains
            XElement explains = new("Explains");
            int explainCount = ReadCount(br, "Explains");

            for (int i = 0; i < explainCount; i++)
            {
                uint key = br.ReadUInt32();
                uint tacticsMonId = br.ReadUInt32();
                string name = ReadWideBuffer(br, 64);
                string text = ReadWideBuffer(br, 512);

                explains.Add(new XElement("Explain",
                    new XAttribute("Key", key),
                    new XAttribute("TacticsMonID", tacticsMonId),
                    new XAttribute("Name", name),
                    text));
            }
            root.Add(explains);

            // 3. EnchantItems
            XElement enchantItems = new("EnchantItems");
            int itemCount = ReadCount(br, "EnchantItems");

            for (int i = 0; i < itemCount; i++)
            {
                enchantItems.Add(new XElement("Item",
                    new XAttribute("Code", br.ReadUInt32()),
                    new XAttribute("LowLevel", br.ReadInt32()),
                    new XAttribute("HighLevel", br.ReadInt32()),
                    new XAttribute("NeedMoney", br.ReadUInt32())));
            }
            root.Add(enchantItems);

            // 4. EnchantStats
            XElement enchantStats = new("EnchantStats");
            int statCount = ReadCount(br, "EnchantStats");

            for (int i = 0; i < statCount; i++)
            {
                int index = br.ReadInt32();
                int rangeCount = ReadCount(br, "EnchantStats.Range");

                XElement stat = new("Stat", new XAttribute("Index", index));

                for (int r = 0; r < rangeCount; r++)
                {
                    stat.Add(new XElement("Range",
                        new XAttribute("LowEnchant", br.ReadInt32()),
                        new XAttribute("HighEnchant", br.ReadInt32()),
                        new XAttribute("GrowMin", br.ReadInt32()),
                        new XAttribute("GrowMax", br.ReadInt32()),
                        new XAttribute("NormalMin", br.ReadInt32()),
                        new XAttribute("NormalMax", br.ReadInt32()),
                        new XAttribute("Special", br.ReadInt32())));
                }

                enchantStats.Add(stat);
            }
            root.Add(enchantStats);

            // 5. EnchantDefaultCorrect
            root.Add(new XElement("EnchantDefaultCorrect", br.ReadInt32()));

            // 6. SameTypeCorrect
            XElement sameTypeCorrect = new("SameTypeCorrect");
            int groupCount = ReadCount(br, "SameTypeCorrect");

            for (int i = 0; i < groupCount; i++)
            {
                int sameType = br.ReadInt32();
                int correctCount = ReadCount(br, "SameTypeCorrect.Correct");

                XElement group = new("Group", new XAttribute("SameType", sameType));

                for (int c = 0; c < correctCount; c++)
                {
                    group.Add(new XElement("Correct",
                        new XAttribute("HatchGrade", br.ReadInt32()),
                        new XAttribute("Value", F(br.ReadSingle()))));
                }

                sameTypeCorrect.Add(group);
            }
            root.Add(sameTypeCorrect);

            // 7. TranscendInfo
            XElement transcendInfo = new("TranscendInfo");
            int evoCount = ReadCount(br, "TranscendInfo");

            for (int i = 0; i < evoCount; i++)
            {
                int value = br.ReadInt32();
                int reqCount = ReadCount(br, "TranscendInfo.Req");

                XElement evo = new("EvoType", new XAttribute("Value", value));

                for (int r = 0; r < reqCount; r++)
                {
                    evo.Add(new XElement("Req",
                        new XAttribute("CurrentHatch", br.ReadInt32()),
                        new XAttribute("NeedLevel", br.ReadInt32()),
                        new XAttribute("NeedEnchant", br.ReadInt32()),
                        new XAttribute("MatEvoMin", br.ReadInt32()),
                        new XAttribute("MatEvoMax", br.ReadInt32()),
                        new XAttribute("NeedScale", br.ReadInt32()),
                        new XAttribute("NextHatch", br.ReadInt32()),
                        new XAttribute("MatHatchMin", br.ReadInt32()),
                        new XAttribute("MatHatchMax", br.ReadInt32()),
                        new XAttribute("Cost", br.ReadUInt32()),
                        new XAttribute("MaxExp", br.ReadUInt32())));
                }

                transcendInfo.Add(evo);
            }
            root.Add(transcendInfo);

            // 8. TranscendEvo
            XElement transcendEvo = new("TranscendEvo");
            int digiCount = ReadCount(br, "TranscendEvo");

            for (int i = 0; i < digiCount; i++)
            {
                uint id = br.ReadUInt32();
                int chargeGroupCount = ReadCount(br, "TranscendEvo.ChargeGroup");

                XElement dig = new("Digimon", new XAttribute("ID", id));

                for (int g = 0; g < chargeGroupCount; g++)
                {
                    int use = br.ReadInt32();
                    int matCount = ReadCount(br, "TranscendEvo.Material");

                    for (int m = 0; m < matCount; m++)
                    {
                        dig.Add(new XElement("Material",
                            new XAttribute("Charge", ChargeToLabel(use)),
                            new XAttribute("NeedCount", br.ReadInt32()),
                            new XAttribute("ItemTypeLS", br.ReadUInt32()),
                            new XAttribute("ExpPercent", br.ReadUInt32())));
                    }
                }

                transcendEvo.Add(dig);
            }
            root.Add(transcendEvo);

            // 9. FixedExpDigimon
            XElement fixedExpDigimon = new("FixedExpDigimon");
            int fixedDigiCount = ReadCount(br, "FixedExpDigimon");

            for (int i = 0; i < fixedDigiCount; i++)
            {
                uint id = br.ReadUInt32();
                int fixedCount = ReadCount(br, "FixedExpDigimon.Fixed");

                XElement dig = new("Digimon", new XAttribute("ID", id));

                for (int f = 0; f < fixedCount; f++)
                {
                    dig.Add(new XElement("Fixed",
                        new XAttribute("HatchGrade", br.ReadInt32()),
                        new XAttribute("Points", br.ReadUInt32())));
                }

                fixedExpDigimon.Add(dig);
            }
            root.Add(fixedExpDigimon);

            if (fs.Position != fs.Length)
                AppLogger.Log($"{Name}: aviso - restam {fs.Length - fs.Position} bytes por ler no fim do BIN.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputXml)!);

            new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                root).Save(outputXml);

            AppLogger.Log($"{Name}: BIN -> XML concluído. Bytes lidos={fs.Position}/{fs.Length}.");
        }

        private static int ReadCount(BinaryReader br, string section)
        {
            int count = br.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"{section}: count negativo ({count}).");
            return count;
        }

        private static string Required(XElement e, string attr) =>
            e.Attribute(attr)?.Value
            ?? throw new InvalidDataException($"Atributo obrigatório '{attr}' em <{e.Name}>.");

        private static int ParseInt(string s) =>
            int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static uint ParseUInt(string s) =>
            uint.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static float ParseFloat(string s) =>
            float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string F(float v) =>
            v.ToString("R", CultureInfo.InvariantCulture);

        private static int ChargeToInt(string value)
        {
            if (value.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                return 1;

            if (value.Equals("Hyper", StringComparison.OrdinalIgnoreCase))
                return 2;

            return ParseInt(value);
        }

        private static string ChargeToLabel(int use) =>
            use switch
            {
                1 => "Regular",
                2 => "Hyper",
                _ => use.ToString(CultureInfo.InvariantCulture)
            };

        private static string Normalize(string value) =>
            new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static void WriteWideBuffer(BinaryWriter bw, string text, int wcharCount)
        {
            byte[] result = new byte[wcharCount * 2];
            byte[] raw = Encoding.Unicode.GetBytes(text ?? string.Empty);

            int max = (wcharCount - 1) * 2;
            int length = Math.Min(raw.Length, max);
            Buffer.BlockCopy(raw, 0, result, 0, length);

            bw.Write(result);
        }

        private static string ReadWideBuffer(BinaryReader br, int wcharCount)
        {
            byte[] bytes = br.ReadBytes(wcharCount * 2);
            if (bytes.Length != wcharCount * 2)
                throw new EndOfStreamException("Fim inesperado ao ler buffer UTF-16.");

            string value = Encoding.Unicode.GetString(bytes);
            int zero = value.IndexOf('\0');

            return zero >= 0 ? value[..zero] : value;
        }
    }
}
