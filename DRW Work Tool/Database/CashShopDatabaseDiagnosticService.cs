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
        public int CashItemCount { get; init; }
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
        public string QuantitySource { get; init; } = "First CashItems.Amount";
        public string PriceSource { get; init; } = "nRealSellingPrice";
        public string ActivatedSource { get; init; } = "Enabled";
        public string ItemNameSource { get; init; } = "Name (remove apostrophe only)";
        public int ComparedRows { get; init; }
        public double QuantityMatchPercent { get; init; }
        public double PriceMatchPercent { get; init; }
        public double ActivatedMatchPercent { get; init; }
        public double ItemNameMatchPercent { get; init; }
    }

    internal static class CashShopDatabaseXmlReader
    {
        private static readonly string[] CanonicalFolders = { "TamerInfo", "DigimonInfo", "AvatarInfo", "PackageInfo" };

        public static List<CashShopXmlDbRow> Load(string cashShopRoot, CancellationToken token)
        {
            if (!Directory.Exists(cashShopRoot))
                throw new DirectoryNotFoundException("CashShop folder was not found: " + cashShopRoot);

            Dictionary<int, string> itemNames = LoadItemListNames();
            var rows = new List<CashShopXmlDbRow>();
            int physical = 0;

            foreach (string folderName in CanonicalFolders)
            {
                string folder = Path.Combine(cashShopRoot, folderName);
                if (!Directory.Exists(folder)) continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    XDocument doc;
                    try { doc = XDocument.Load(file, LoadOptions.None); }
                    catch { continue; }
                    if (doc.Root?.Name.LocalName != "CashShopInformationCounts") continue;

                    foreach (XElement container in doc.Root.Elements("CashShopInformationCount"))
                    {
                        int cashShopId = ReadInt(container, "CashShopId");
                        XElement? cashInfo = container.Element("CashInfo");
                        if (cashInfo == null) continue;

                        foreach (XElement option in cashInfo.Elements("CASHINFO"))
                        {
                            token.ThrowIfCancellationRequested();
                            List<XElement> itemNodes = option.Element("CashItems")?.Elements("Item").ToList() ?? new List<XElement>();
                            XElement? firstItem = itemNodes.FirstOrDefault(x => ReadInt(x, "ItemId") > 0);
                            if (firstItem == null) continue;

                            int itemId = ReadInt(firstItem, "ItemId");
                            rows.Add(new CashShopXmlDbRow
                            {
                                PhysicalId = ++physical,
                                CashShopId = cashShopId,
                                UniqueId = ReadLong(option, "unique_id"),
                                ItemId = itemId,
                                Amount = Math.Max(1, ReadInt(firstItem, "Amount")),
                                DisplayCount = ReadInt(option, "nDispCount"),
                                RealPrice = ReadInt(option, "nRealSellingPrice"),
                                StandardPrice = ReadInt(option, "nStandardSellingPrice"),
                                Enabled = ReadInt(option, "Enabled"),
                                BActive = ReadInt(option, "bActive"),
                                Name = DatabaseItemName(option.Element("Name")?.Value),
                                CashName = DatabaseItemName(option.Element("CashName")?.Value),
                                ItemListName = itemNames.TryGetValue(itemId, out string? itemName) ? itemName : string.Empty,
                                CashItemCount = itemNodes.Count,
                                FilePath = file
                            });
                        }
                    }
                }
            }

            if (rows.Count == 0) throw new InvalidDataException("No canonical Cash Shop purchase-option rows were found.");
            return rows;
        }

        public static (int Containers, int Options) CountStructure(string cashShopRoot)
        {
            int containers = 0, options = 0;
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
                            .SelectMany(x => x.Element("CashInfo")?.Elements("CASHINFO") ?? Enumerable.Empty<XElement>()).Count();
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
                    if (idNode == null || !int.TryParse(idNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0 || result.ContainsKey(id)) continue;
                    string name = node.Element("s_szName")?.Value ?? node.Element("s_szItemName")?.Value ?? node.Element("ItemName")?.Value ?? node.Element("Name")?.Value ?? string.Empty;
                    result[id] = DatabaseItemName(name);
                }
            }
            catch { }
            return result;
        }

        internal static string DatabaseItemName(string? value) =>
            (value ?? string.Empty).Trim().Replace("'", string.Empty, StringComparison.Ordinal);

        private static int ReadInt(XElement node, string name) =>
            int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        private static long ReadLong(XElement node, string name) =>
            long.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0L;
    }

    internal static class CashShopDatabaseMappingDetector
    {
        public static CashShopDatabaseMapping Detect(IReadOnlyList<CashShopXmlDbRow> xml, IReadOnlyList<CashShopDbRow> db)
        {
            var pairs = Match(xml, db);
            if (pairs.Count == 0) return new CashShopDatabaseMapping();

            var qty = BestNumber(pairs, ("First CashItems.Amount", x => x.Amount), ("nDispCount", x => x.DisplayCount));
            var price = BestNumber(pairs, ("nRealSellingPrice", x => x.RealPrice), ("nStandardSellingPrice", x => x.StandardPrice));
            var active = BestNumber(pairs, ("Enabled", x => x.Enabled), ("bActive", x => x.BActive));
            var name = BestString(pairs,
                ("Name (remove apostrophe only)", x => x.Name),
                ("CashName", x => x.CashName),
                ("ItemList.Name", x => x.ItemListName));

            return new CashShopDatabaseMapping
            {
                QuantitySource = qty.Name, PriceSource = price.Name, ActivatedSource = active.Name, ItemNameSource = name.Name,
                ComparedRows = pairs.Count, QuantityMatchPercent = qty.Percent, PriceMatchPercent = price.Percent,
                ActivatedMatchPercent = active.Percent, ItemNameMatchPercent = name.Percent
            };
        }

        public static List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> Match(IReadOnlyList<CashShopXmlDbRow> xml, IReadOnlyList<CashShopDbRow> db)
        {
            var queues = xml.GroupBy(Key).ToDictionary(g => g.Key, g => new Queue<CashShopXmlDbRow>(g.OrderBy(x => x.PhysicalId)));
            var pairs = new List<(CashShopXmlDbRow, CashShopDbRow)>();
            foreach (CashShopDbRow row in db.OrderBy(x => x.Id))
                if (queues.TryGetValue(Key(row), out Queue<CashShopXmlDbRow>? queue) && queue.Count > 0)
                    pairs.Add((queue.Dequeue(), row));
            return pairs;
        }

        private static string Key(CashShopXmlDbRow x) => $"{x.UniqueId}|{x.ItemId}|{x.CashShopId}";
        private static string Key(CashShopDbRow x) => $"{x.UniqueId}|{x.ItemId}|{x.CashShopId}";

        private static (string Name, double Percent) BestNumber(List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, params (string Name, Func<CashShopXmlDbRow, int> Read)[] candidates)
        {
            string best = candidates[0].Name; int bestMatch = -1;
            foreach (var candidate in candidates)
            {
                int exact = pairs.Count(p => candidate.Read(p.Xml) == DbNumber(candidate.Name, p.Db));
                if (exact > bestMatch) { best = candidate.Name; bestMatch = exact; }
            }
            return (best, bestMatch * 100.0 / pairs.Count);
        }

        private static int DbNumber(string candidate, CashShopDbRow db)
        {
            if (candidate is "First CashItems.Amount" or "nDispCount") return db.Quantity;
            if (candidate is "nRealSellingPrice" or "nStandardSellingPrice") return db.Price;
            return db.Activated;
        }

        private static (string Name, double Percent) BestString(List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, params (string Name, Func<CashShopXmlDbRow, string> Read)[] candidates)
        {
            string best = candidates[0].Name; int bestMatch = -1;
            foreach (var candidate in candidates)
            {
                int exact = pairs.Count(p => candidate.Read(p.Xml) == p.Db.ItemName);
                if (exact > bestMatch) { best = candidate.Name; bestMatch = exact; }
            }
            return (best, bestMatch * 100.0 / pairs.Count);
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
            Log($"Canonical XML: containers={containers:N0}, purchase options={options:N0}, DB-shaped rows={xml.Count:N0} (FIRST CashItems/Item only).");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            List<CashShopDbRow> db = await ReadDatabaseAsync(connection, cancellationToken);
            Log($"Database snapshot: Asset.CashShop={db.Count:N0} rows.");

            var pairs = CashShopDatabaseMappingDetector.Match(xml, db);
            CashShopDatabaseMapping mapping = CashShopDatabaseMappingDetector.Detect(xml, db);
            Log($"Matched by Unique_Id + FIRST Item_Id + CashShopId: {pairs.Count:N0} rows.");
            Log($"Best Quantity mapping: {mapping.QuantitySource} ({mapping.QuantityMatchPercent:0.00}%).");
            Log($"Best Price mapping: {mapping.PriceSource} ({mapping.PriceMatchPercent:0.00}%).");
            Log($"Best Activated mapping: {mapping.ActivatedSource} ({mapping.ActivatedMatchPercent:0.00}%).");
            Log($"Best ItemName mapping: {mapping.ItemNameSource} ({mapping.ItemNameMatchPercent:0.00}%).");

            WriteFieldSummary(folder, pairs);
            WriteRaw(folder, pairs);
            string report = WriteHighSignal(folder, containers, options, xml, db, pairs, mapping);
            TimeSpan elapsed = DateTime.Now - started;
            Log($"Diagnostic completed in {elapsed.TotalSeconds:N1}s. Output: {folder}");
            return new CashShopDatabaseDiagnosticSummary { XmlContainers=containers, XmlOptions=options, XmlFlattenedRows=xml.Count, DatabaseRows=db.Count, MatchedRows=pairs.Count, OutputFolder=folder, HighSignalReport=report, Elapsed=elapsed };
        }

        internal static async Task<List<CashShopDbRow>> ReadDatabaseAsync(SqlConnection connection, CancellationToken token)
        {
            const string sql = "SELECT [Id],[Unique_Id],[Item_Id],[Quanty],[Price],[Activated],[ItemName],[CashShopId] FROM [dmo].[Asset].[CashShop] ORDER BY [Id];";
            var result = new List<CashShopDbRow>();
            await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 180 };
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                result.Add(new CashShopDbRow
                {
                    Id=Convert.ToInt32(reader.GetValue(0),CultureInfo.InvariantCulture),
                    UniqueId=Convert.ToInt64(reader.GetValue(1),CultureInfo.InvariantCulture),
                    ItemId=Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),
                    Quantity=Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture),
                    Price=Convert.ToInt32(reader.GetValue(4),CultureInfo.InvariantCulture),
                    Activated=Convert.ToInt32(reader.GetValue(5),CultureInfo.InvariantCulture),
                    ItemName=reader.IsDBNull(6)?string.Empty:Convert.ToString(reader.GetValue(6),CultureInfo.InvariantCulture)??string.Empty,
                    CashShopId=Convert.ToInt32(reader.GetValue(7),CultureInfo.InvariantCulture)
                });
            return result;
        }

        private static void WriteFieldSummary(string folder, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs)
        {
            string path = Path.Combine(folder, "CashShop_FieldMatchSummary.csv");
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("DB_Field,XML_Candidate,Compared,ExactMatches,MatchPercent");
            Num("Quanty","First CashItems.Amount",p=>p.Xml.Amount,p=>p.Db.Quantity);
            Num("Quanty","nDispCount",p=>p.Xml.DisplayCount,p=>p.Db.Quantity);
            Num("Price","nRealSellingPrice",p=>p.Xml.RealPrice,p=>p.Db.Price);
            Num("Price","nStandardSellingPrice",p=>p.Xml.StandardPrice,p=>p.Db.Price);
            Num("Activated","Enabled",p=>p.Xml.Enabled,p=>p.Db.Activated);
            Num("Activated","bActive",p=>p.Xml.BActive,p=>p.Db.Activated);
            Str("ItemName","Name (remove apostrophe only)",p=>p.Xml.Name);
            Str("ItemName","CashName",p=>p.Xml.CashName);
            Str("ItemName","ItemList.Name",p=>p.Xml.ItemListName);
            Num("Id","physical CASHINFO order",p=>p.Xml.PhysicalId,p=>p.Db.Id);
            void Num(string f,string c,Func<(CashShopXmlDbRow Xml,CashShopDbRow Db),int>a,Func<(CashShopXmlDbRow Xml,CashShopDbRow Db),int>b){int e=pairs.Count(p=>a(p)==b(p));w.WriteLine($"{Csv(f)},{Csv(c)},{pairs.Count},{e},{(pairs.Count==0?0:e*100.0/pairs.Count).ToString("0.000",CultureInfo.InvariantCulture)}");}
            void Str(string f,string c,Func<(CashShopXmlDbRow Xml,CashShopDbRow Db),string>a){int e=pairs.Count(p=>a(p)==p.Db.ItemName);w.WriteLine($"{Csv(f)},{Csv(c)},{pairs.Count},{e},{(pairs.Count==0?0:e*100.0/pairs.Count).ToString("0.000",CultureInfo.InvariantCulture)}");}
        }

        private static void WriteRaw(string folder, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs)
        {
            string path = Path.Combine(folder, "CashShop_RawComparison.csv");
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("DB_Id,DB_Unique_Id,XML_Unique_Id,DB_Item_Id,XML_FirstItem_Id,DB_Quanty,XML_FirstAmount,XML_nDispCount,DB_Price,XML_RealPrice,XML_StandardPrice,DB_Activated,XML_Enabled,XML_bActive,DB_ItemName,XML_DBItemName,XML_ItemListName,XML_CashItemCount,DB_CashShopId,XML_CashShopId,XML_File");
            foreach (var p in pairs)
                w.WriteLine(string.Join(",", new[] { p.Db.Id.ToString(),p.Db.UniqueId.ToString(),p.Xml.UniqueId.ToString(),p.Db.ItemId.ToString(),p.Xml.ItemId.ToString(),p.Db.Quantity.ToString(),p.Xml.Amount.ToString(),p.Xml.DisplayCount.ToString(),p.Db.Price.ToString(),p.Xml.RealPrice.ToString(),p.Xml.StandardPrice.ToString(),p.Db.Activated.ToString(),p.Xml.Enabled.ToString(),p.Xml.BActive.ToString(),Csv(p.Db.ItemName),Csv(p.Xml.Name),Csv(p.Xml.ItemListName),p.Xml.CashItemCount.ToString(),p.Db.CashShopId.ToString(),p.Xml.CashShopId.ToString(),Csv(p.Xml.FilePath) }));
        }

        private static string WriteHighSignal(string folder, int containers, int options, List<CashShopXmlDbRow> xml, List<CashShopDbRow> db, List<(CashShopXmlDbRow Xml, CashShopDbRow Db)> pairs, CashShopDatabaseMapping m)
        {
            string path = Path.Combine(folder, "HIGH_SIGNAL_REPORT.txt");
            var sb = new StringBuilder();
            sb.AppendLine("CASH SHOP XML <-> DATABASE HIGH SIGNAL REPORT");
            sb.AppendLine("READ-ONLY"); sb.AppendLine();
            sb.AppendLine($"Canonical CashShopInformationCount groups : {containers:N0}");
            sb.AppendLine($"CASHINFO purchase options                : {options:N0}");
            sb.AppendLine($"DB-shaped XML rows (1 per CASHINFO)      : {xml.Count:N0}");
            sb.AppendLine($"Asset.CashShop rows                      : {db.Count:N0}");
            sb.AppendLine($"Matched rows                             : {pairs.Count:N0}"); sb.AppendLine();
            sb.AppendLine("CONFIRMED STRUCTURE");
            sb.AppendLine("One Asset.CashShop row per CASHINFO purchase option.");
            sb.AppendLine("Item_Id    <- FIRST valid CashItems/Item/ItemId");
            sb.AppendLine("Quanty     <- FIRST valid CashItems/Item/Amount");
            sb.AppendLine("Price      <- nRealSellingPrice");
            sb.AppendLine("Activated  <- Enabled");
            sb.AppendLine("ItemName   <- Name, preserving literal backslash-n tokens and removing apostrophe only");
            sb.AppendLine("CashShopId <- CashShopInformationCount/CashShopId");
            sb.AppendLine("Unique_Id  <- CASHINFO.unique_id"); sb.AppendLine();
            sb.AppendLine($"Quantity confidence  : {m.QuantityMatchPercent:0.00}%");
            sb.AppendLine($"Price confidence     : {m.PriceMatchPercent:0.00}%");
            sb.AppendLine($"Activated confidence : {m.ActivatedMatchPercent:0.00}%");
            sb.AppendLine($"ItemName confidence  : {m.ItemNameMatchPercent:0.00}%");
            sb.AppendLine();
            sb.AppendLine("Package options can contain multiple CashItems. Only the first item is mirrored in Asset.CashShop; complete package contents remain in XML.");
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
