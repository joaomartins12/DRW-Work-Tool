using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class CashShopDatabaseDiagnosticSummary
    {
        public int XmlContainers { get; init; }
        public int XmlOptions { get; init; }
        public int XmlFlattenedRows { get; init; }
        public int DatabaseRows { get; init; }
        public int MatchedRows { get; init; }
        public string OutputFolder { get; init; } = string.Empty;
        public string HighSignalReport { get; init; } = string.Empty;
        public TimeSpan Elapsed { get; init; }
    }

    internal sealed class CashShopXmlDbRow
    {
        public int PhysicalId { get; init; }
        public int CashShopId { get; init; }
        public long UniqueId { get; init; }
        public int ItemId { get; init; }
        public int Amount { get; init; }
        public int DisplayCount { get; init; }
        public int RealPrice { get; init; }
        public int StandardPrice { get; init; }
        public int Enabled { get; init; }
        public int BActive { get; init; }
        public string Name { get; init; } = string.Empty;
        public string CashName { get; init; } = string.Empty;
        public string ItemListName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
    }

    internal sealed class CashShopDbRow
    {
        public int Id { get; init; }
        public long UniqueId { get; init; }
        public int ItemId { get; init; }
        public int Quantity { get; init; }
        public int Price { get; init; }
        public int Activated { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public int CashShopId { get; init; }
    }

    public sealed class CashShopDatabaseMapping
    {
        public string QuantitySource { get; init; } = "CashItems.Amount";
        public string PriceSource { get; init; } = "nRealSellingPrice";
        public string ActivatedSource { get; init; } = "Enabled";
        public string ItemNameSource { get; init; } = "Name";
        public int ComparedRows { get; init; }
        public double QuantityMatchPercent { get; init; }
        public double PriceMatchPercent { get; init; }
        public double ActivatedMatchPercent { get; init; }
        public double ItemNameMatchPercent { get; init; }
    }

    internal static class CashShopDatabaseXmlReader
    {
        private static readonly string[] CanonicalFolders =
        {
            "TamerInfo",
            "DigimonInfo",
            "AvatarInfo",
            "PackageInfo"
        };

        public static List<CashShopXmlDbRow> Load(string cashShopRoot, CancellationToken token)
        {
            if (!Directory.Exists(cashShopRoot))
                throw new DirectoryNotFoundException("CashShop folder was not found: " + cashShopRoot);

            Dictionary<int, string> itemNames = LoadItemListNames();
            var result = new List<CashShopXmlDbRow>();
            int physical = 0;

            foreach (string folderName in CanonicalFolders)
            {
                string folder = Path.Combine(cashShopRoot, folderName);
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    XDocument doc;
                    try { doc = XDocument.Load(file, LoadOptions.None); }
                    catch { continue; }

                    if (doc.Root?.Name.LocalName != "CashShopInformationCounts")
                        continue;

                    foreach (XElement container in doc.Root.Elements("CashShopInformationCount"))
                    {
                        int cashShopId = ReadInt(container, "CashShopId");
                        XElement? cashInfo = container.Element("CashInfo");
                        if (cashInfo == null)
                            continue;

                        foreach (XElement option in cashInfo.Elements("CASHINFO"))
                        {
                            long uniqueId = ReadLong(option, "unique_id");
                            int displayCount = ReadInt(option, "nDispCount");
                            int realPrice = ReadInt(option, "nRealSellingPrice");
                            int standardPrice = ReadInt(option, "nStandardSellingPrice");
                            int enabled = ReadInt(option, "Enabled");
                            int bActive = ReadInt(option, "bActive");
                            string name = NormalizeName(option.Element("Name")?.Value);
                            string cashName = NormalizeName(option.Element("CashName")?.Value);

                            XElement? items = option.Element("CashItems");
                            if (items == null)
                                continue;

                            foreach (XElement item in items.Elements("Item"))
                            {
                                token.ThrowIfCancellationRequested();
                                int itemId = ReadInt(item, "ItemId");
                                if (itemId <= 0)
                                    continue;

                                physical++;
                                result.Add(new CashShopXmlDbRow
                                {
                                    PhysicalId = physical,
                                    CashShopId = cashShopId,
                                    UniqueId = uniqueId,
                                    ItemId = itemId,
                                    Amount = Math.Max(1, ReadInt(item, "Amount")),
                                    DisplayCount = displayCount,
                                    RealPrice = realPrice,
                                    StandardPrice = standardPrice,
                                    Enabled = enabled,
                                    BActive = bActive,
                                    Name = name,
                                    CashName = cashName,
                                    ItemListName = itemNames.TryGetValue(itemId, out string? itemName) ? itemName : string.Empty,
                                    FilePath = file
                                });
                            }
                        }
                    }
                }
            }

            if (result.Count == 0)
                throw new InvalidDataException("No canonical Cash Shop item rows were found.");

            return result;
        }

        public static (int Containers, int Options) CountStructure(string cashShopRoot)
        {
            int containers = 0;
            int options = 0;
            foreach (string folderName in CanonicalFolders)
            {
                string folder = Path.Combine(cashShopRoot, folderName);
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        XDocument doc = XDocument.Load(file, LoadOptions.None);
                        if (doc.Root?.Name.LocalName != "CashShopInformationCounts") continue;
                        containers += doc.Root.Elements("CashShopInformationCount").Count();
                        options += doc.Root.Elements("CashShopInformationCount")
                            .SelectMany(x => x.Element("CashInfo")?.Elements("CASHINFO") ?? Enumerable.Empty<XElement>())
                            .Count();
                    }
                    catch { }
                }
            }
            return (containers, options);
        }

        private static Dictionary<int, string> LoadItemListNames()
        {
            var result = new Dictionary<int, string>();
            if (!Directory.Exists(AppPaths.Xml)) return result;
            string? path = Directory.EnumerateFiles(AppPaths.Xml, "ItemList.xml", SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault();
            if (path == null) return result;

            try
            {
                XDocument doc = XDocument.Load(path, LoadOptions.None);
                foreach (XElement node in doc.Descendants())
                {
                    XElement? idNode = node.Element("s_dwItemID") ?? node.Element("s_nItemID") ?? node.Element("ItemId") ?? node.Element("ItemID");
                    if (idNode == null || !int.TryParse(idNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0 || result.ContainsKey(id))
                        continue;
                    string name = node.Element("s_szName")?.Value ?? node.Element("s_szItemName")?.Value ?? node.Element("ItemName")?.Value ?? node.Element("Name")?.Value ?? string.Empty;
                    result[id] = NormalizeName(name);
                }
            }
            catch { }

            return result;
        }

        private static string NormalizeName(string? value) =>
            (value ?? string.Empty).Replace("\\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();

        private static int ReadInt(XElement node, string name) =>
            int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        private static long ReadLong(XElement node, string name) =>
            long.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0L;
    }

    internal static class CashShopDatabaseMappingDetector
    {
        public static CashShopDatabaseMapping Detect(IReadOnlyList<CashShopXmlDbRow> xml, IReadOnlyList<CashShopDbRow> db)
        {
            List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs = Match(xml, db);
            if (pairs.Count == 0)
                return new CashShopDatabaseMapping();

            (string qtyName, double qtyPct) = BestNumber(pairs,
                ("CashItems.Amount", x => x.Amount),
                ("nDispCount", x => x.DisplayCount));

            (string priceName, double pricePct) = BestNumber(pairs,
                ("nRealSellingPrice", x => x.RealPrice),
                ("nStandardSellingPrice", x => x.StandardPrice));

            (string activeName, double activePct) = BestNumber(pairs,
                ("Enabled", x => x.Enabled),
                ("bActive", x => x.BActive));

            (string nameSource, double namePct) = BestString(pairs,
                ("Name", x => x.Name),
                ("CashName", x => x.CashName),
                ("ItemList.Name", x => x.ItemListName));

            return new CashShopDatabaseMapping
            {
                QuantitySource = qtyName,
                PriceSource = priceName,
                ActivatedSource = activeName,
                ItemNameSource = nameSource,
                ComparedRows = pairs.Count,
                QuantityMatchPercent = qtyPct,
                PriceMatchPercent = pricePct,
                ActivatedMatchPercent = activePct,
                ItemNameMatchPercent = namePct
            };
        }

        public static List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> Match(IReadOnlyList<CashShopXmlDbRow> xml, IReadOnlyList<CashShopDbRow> db)
        {
            var queues = xml.GroupBy(Key).ToDictionary(g => g.Key, g => new Queue<CashShopXmlDbRow>(g.OrderBy(x => x.PhysicalId)));
            var pairs = new List<(CashShopXmlDbRow, CashShopDbRow)>();
            foreach (CashShopDbRow row in db.OrderBy(x => x.Id))
            {
                string key = Key(row);
                if (queues.TryGetValue(key, out Queue<CashShopXmlDbRow>? queue) && queue.Count > 0)
                    pairs.Add((queue.Dequeue(), row));
            }
            return pairs;
        }

        private static string Key(CashShopXmlDbRow x) => $"{x.UniqueId}|{x.ItemId}|{x.CashShopId}";
        private static string Key(CashShopDbRow x) => $"{x.UniqueId}|{x.ItemId}|{x.CashShopId}";

        private static (string Name, double Percent) BestNumber(List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, params (string Name, Func<CashShopXmlDbRow, int> Read)[] candidates)
        {
            string best = candidates[0].Name;
            int bestMatch = -1;
            foreach (var candidate in candidates)
            {
                int exact = pairs.Count(p => candidate.Read(p.Xml) == NumberFor(candidate.Name, p.Db));
                if (exact > bestMatch) { bestMatch = exact; best = candidate.Name; }
            }
            return (best, pairs.Count == 0 ? 0 : bestMatch * 100.0 / pairs.Count);
        }

        private static int NumberFor(string candidate, CashShopDbRow db)
        {
            if (candidate is "CashItems.Amount" or "nDispCount") return db.Quantity;
            if (candidate is "nRealSellingPrice" or "nStandardSellingPrice") return db.Price;
            return db.Activated;
        }

        private static (string Name, double Percent) BestString(List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, params (string Name, Func<CashShopXmlDbRow, string> Read)[] candidates)
        {
            string best = candidates[0].Name;
            int bestMatch = -1;
            foreach (var candidate in candidates)
            {
                int exact = pairs.Count(p => string.Equals(candidate.Read(p.Xml), p.Db.ItemName, StringComparison.Ordinal));
                if (exact > bestMatch) { bestMatch = exact; best = candidate.Name; }
            }
            return (best, pairs.Count == 0 ? 0 : bestMatch * 100.0 / pairs.Count);
        }
    }

    public sealed class CashShopDatabaseDiagnosticService
    {
        public async Task<CashShopDatabaseDiagnosticSummary> CompareAsync(string connectionString, string cashShopRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            string folder = Path.Combine(AppPaths.Logs, "CashShopDatabaseDiagnostic", started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            string logPath = Path.Combine(folder, "diagnostic.log");

            void Log(string text)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}";
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
                progress?.Report(line);
            }

            Log("CASH SHOP DATABASE DIAGNOSTIC started in READ-ONLY mode.");
            Log("No INSERT / UPDATE / DELETE is executed.");

            List<CashShopXmlDbRow> xml = await Task.Run(() => CashShopDatabaseXmlReader.Load(cashShopRoot, cancellationToken), cancellationToken);
            (int containers, int options) = CashShopDatabaseXmlReader.CountStructure(cashShopRoot);
            Log($"Canonical XML: containers={containers:N0}, purchase options={options:N0}, flattened item rows={xml.Count:N0}.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            List<CashShopDbRow> db = await ReadDatabaseAsync(connection, cancellationToken);
            Log($"Database snapshot: Asset.CashShop={db.Count:N0} rows.");

            List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs = CashShopDatabaseMappingDetector.Match(xml, db);
            CashShopDatabaseMapping mapping = CashShopDatabaseMappingDetector.Detect(xml, db);
            Log($"Matched by Unique_Id + Item_Id + CashShopId: {pairs.Count:N0} rows.");
            Log($"Best Quantity mapping: {mapping.QuantitySource} ({mapping.QuantityMatchPercent:0.00}%).");
            Log($"Best Price mapping: {mapping.PriceSource} ({mapping.PriceMatchPercent:0.00}%).");
            Log($"Best Activated mapping: {mapping.ActivatedSource} ({mapping.ActivatedMatchPercent:0.00}%).");
            Log($"Best ItemName mapping: {mapping.ItemNameSource} ({mapping.ItemNameMatchPercent:0.00}%).");

            WriteFieldSummary(folder, pairs, mapping);
            WriteRaw(folder, pairs);
            string report = WriteHighSignal(folder, containers, options, xml, db, pairs, mapping);

            TimeSpan elapsed = DateTime.Now - started;
            Log($"Diagnostic completed in {elapsed.TotalSeconds:N1}s. Output: {folder}");

            return new CashShopDatabaseDiagnosticSummary
            {
                XmlContainers = containers,
                XmlOptions = options,
                XmlFlattenedRows = xml.Count,
                DatabaseRows = db.Count,
                MatchedRows = pairs.Count,
                OutputFolder = folder,
                HighSignalReport = report,
                Elapsed = elapsed
            };
        }

        internal static async Task<List<CashShopDbRow>> ReadDatabaseAsync(SqlConnection connection, CancellationToken token)
        {
            const string sql = "SELECT [Id],[Unique_Id],[Item_Id],[Quanty],[Price],[Activated],[ItemName],[CashShopId] FROM [dmo].[Asset].[CashShop] ORDER BY [Id];";
            var result = new List<CashShopDbRow>();
            await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 180 };
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                result.Add(new CashShopDbRow
                {
                    Id = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    UniqueId = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                    ItemId = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Quantity = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                    Price = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
                    Activated = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
                    ItemName = reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty,
                    CashShopId = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture)
                });
            }
            return result;
        }

        private static void WriteFieldSummary(string folder, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, CashShopDatabaseMapping mapping)
        {
            string path = Path.Combine(folder, "CashShop_FieldMatchSummary.csv");
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("DB_Field,XML_Candidate,Compared,ExactMatches,MatchPercent");
            WriteNumber("Quanty", "CashItems.Amount", p => p.Xml.Amount, p => p.Db.Quantity);
            WriteNumber("Quanty", "nDispCount", p => p.Xml.DisplayCount, p => p.Db.Quantity);
            WriteNumber("Price", "nRealSellingPrice", p => p.Xml.RealPrice, p => p.Db.Price);
            WriteNumber("Price", "nStandardSellingPrice", p => p.Xml.StandardPrice, p => p.Db.Price);
            WriteNumber("Activated", "Enabled", p => p.Xml.Enabled, p => p.Db.Activated);
            WriteNumber("Activated", "bActive", p => p.Xml.BActive, p => p.Db.Activated);
            WriteString("ItemName", "Name", p => p.Xml.Name);
            WriteString("ItemName", "CashName", p => p.Xml.CashName);
            WriteString("ItemName", "ItemList.Name", p => p.Xml.ItemListName);
            WriteNumber("Id", "physical flattened row", p => p.Xml.PhysicalId, p => p.Db.Id);

            void WriteNumber(string dbField, string candidate, Func<(CashShopXmlDbRow Xml, CashShopDbRow Db), int> a, Func<(CashShopXmlDbRow Xml, CashShopDbRow Db), int> b)
            {
                int exact = pairs.Count(p => a(p) == b(p));
                double pct = pairs.Count == 0 ? 0 : exact * 100.0 / pairs.Count;
                w.WriteLine($"{Csv(dbField)},{Csv(candidate)},{pairs.Count},{exact},{pct.ToString("0.000", CultureInfo.InvariantCulture)}");
            }

            void WriteString(string dbField, string candidate, Func<(CashShopXmlDbRow Xml, CashShopDbRow Db), string> a)
            {
                int exact = pairs.Count(p => string.Equals(a(p), p.Db.ItemName, StringComparison.Ordinal));
                double pct = pairs.Count == 0 ? 0 : exact * 100.0 / pairs.Count;
                w.WriteLine($"{Csv(dbField)},{Csv(candidate)},{pairs.Count},{exact},{pct.ToString("0.000", CultureInfo.InvariantCulture)}");
            }
        }

        private static void WriteRaw(string folder, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs)
        {
            string path = Path.Combine(folder, "CashShop_RawComparison.csv");
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("DB_Id,DB_Unique_Id,XML_Unique_Id,DB_Item_Id,XML_Item_Id,DB_Quanty,XML_Amount,XML_nDispCount,DB_Price,XML_RealPrice,XML_StandardPrice,DB_Activated,XML_Enabled,XML_bActive,DB_ItemName,XML_Name,XML_CashName,XML_ItemListName,DB_CashShopId,XML_CashShopId,XML_File");
            foreach (var p in pairs)
            {
                w.WriteLine(string.Join(",", new[]
                {
                    p.Db.Id.ToString(CultureInfo.InvariantCulture), p.Db.UniqueId.ToString(CultureInfo.InvariantCulture), p.Xml.UniqueId.ToString(CultureInfo.InvariantCulture),
                    p.Db.ItemId.ToString(CultureInfo.InvariantCulture), p.Xml.ItemId.ToString(CultureInfo.InvariantCulture), p.Db.Quantity.ToString(CultureInfo.InvariantCulture),
                    p.Xml.Amount.ToString(CultureInfo.InvariantCulture), p.Xml.DisplayCount.ToString(CultureInfo.InvariantCulture), p.Db.Price.ToString(CultureInfo.InvariantCulture),
                    p.Xml.RealPrice.ToString(CultureInfo.InvariantCulture), p.Xml.StandardPrice.ToString(CultureInfo.InvariantCulture), p.Db.Activated.ToString(CultureInfo.InvariantCulture),
                    p.Xml.Enabled.ToString(CultureInfo.InvariantCulture), p.Xml.BActive.ToString(CultureInfo.InvariantCulture), Csv(p.Db.ItemName), Csv(p.Xml.Name), Csv(p.Xml.CashName), Csv(p.Xml.ItemListName),
                    p.Db.CashShopId.ToString(CultureInfo.InvariantCulture), p.Xml.CashShopId.ToString(CultureInfo.InvariantCulture), Csv(p.Xml.FilePath)
                }));
            }
        }

        private static string WriteHighSignal(string folder, int containers, int options, List<CashShopXmlDbRow> xml, List<CashShopDbRow> db, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, CashShopDatabaseMapping mapping)
        {
            string path = Path.Combine(folder, "HIGH_SIGNAL_REPORT.txt");
            var sb = new StringBuilder();
            sb.AppendLine("CASH SHOP XML <-> DATABASE HIGH SIGNAL REPORT");
            sb.AppendLine("READ-ONLY");
            sb.AppendLine();
            sb.AppendLine($"Canonical CashShopInformationCount groups : {containers:N0}");
            sb.AppendLine($"CASHINFO purchase options                : {options:N0}");
            sb.AppendLine($"Flattened XML item rows                  : {xml.Count:N0}");
            sb.AppendLine($"Asset.CashShop rows                      : {db.Count:N0}");
            sb.AppendLine($"Matched composite-key rows               : {pairs.Count:N0}");
            sb.AppendLine();
            sb.AppendLine("STRUCTURAL CANDIDATES");
            sb.AppendLine("Unique_Id  <- CASHINFO.unique_id");
            sb.AppendLine("Item_Id    <- CashItems/Item/ItemId");
            sb.AppendLine("CashShopId <- CashShopInformationCount/CashShopId");
            sb.AppendLine($"Quanty     <- {mapping.QuantitySource}  [{mapping.QuantityMatchPercent:0.00}%]");
            sb.AppendLine($"Price      <- {mapping.PriceSource}  [{mapping.PriceMatchPercent:0.00}%]");
            sb.AppendLine($"Activated  <- {mapping.ActivatedSource}  [{mapping.ActivatedMatchPercent:0.00}%]");
            sb.AppendLine($"ItemName   <- {mapping.ItemNameSource}  [{mapping.ItemNameMatchPercent:0.00}%]");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: one DB row is expected per CASHINFO x CashItems/Item relationship. Multiple price/quantity tiers remain separate rows because each CASHINFO has its own unique_id.");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string? value)
        {
            string text = value ?? string.Empty;
            if (text.Contains('"')) text = text.Replace("\"", "\"\"");
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text + "\"" : text;
        }
    }
}
