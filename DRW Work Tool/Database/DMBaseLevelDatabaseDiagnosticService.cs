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
    public sealed class DMBaseLevelDatabaseDiagnosticSummary
    {
        public int XmlRows { get; init; }
        public int DbRows { get; init; }
        public int CurveCount { get; init; }
        public int StrongMatches { get; init; }
        public bool IsDigimon { get; init; }
        public string TableName { get; init; } = string.Empty;
        public string OutputFolder { get; init; } = string.Empty;
        public string HighSignalReport { get; init; } = string.Empty;
        public TimeSpan Elapsed { get; init; }
    }

    internal sealed record DMBaseLevelXmlRow(
        long Id, int Level, long Exp, long At, long Ct, long De, long Ds,
        long Ev, long Hp, long Ht, long Ms, long As, long Ar, long Bl, long Ws,
        long CurveKey);

    internal sealed record DMBaseLevelDbRow(
        long Id, long Type, int Level, long Exp, long As, long Ar, long At,
        long Bl, long Ct, long De, long Ds, long Ev, long Hp, long Ht, long Ms,
        long Ws, long? StatusId, long? ScaleType);

    public sealed class DMBaseLevelDatabaseDiagnosticService
    {
        public async Task<DMBaseLevelDatabaseDiagnosticSummary> CompareAsync(
            string connectionString,
            string xmlPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            string fileName = Path.GetFileName(xmlPath);
            bool isDigimon = fileName.StartsWith("DigimonBase", StringComparison.OrdinalIgnoreCase);
            bool isTamer = fileName.StartsWith("TamerBase", StringComparison.OrdinalIgnoreCase);
            if (!isDigimon && !isTamer)
                throw new InvalidOperationException("This diagnostic only supports DigimonBase*.xml and TamerBase*.xml.");

            string table = isDigimon ? "DigimonLevelStatus" : "CharacterLevelStatus";
            string output = Path.Combine(
                AppPaths.Logs,
                "DMBaseLevelDatabaseDiagnostic",
                (isDigimon ? "Digimon" : "Tamer") + "_" + started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            string logFile = Path.Combine(output, "diagnostic.log");

            void Log(string text)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}";
                File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
                progress?.Report(line);
            }

            Log($"DMBASE LEVEL DATABASE DIAGNOSTIC started for {fileName} in READ-ONLY mode.");
            Log("No INSERT / UPDATE / DELETE statement is executed by this diagnostic.");

            List<DMBaseLevelXmlRow> xml = LoadXml(xmlPath);
            if (xml.Count == 0)
                throw new InvalidDataException("No level records were found in the selected XML.");
            int curveCount = xml.Select(x => x.CurveKey).Distinct().Count();
            Log($"XML loaded: {xml.Count:N0} rows, {curveCount:N0} detected curves, levels {xml.Min(x => x.Level)}-{xml.Max(x => x.Level)}.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            Log("SQL connection opened. Reading table schema and snapshot...");

            await WriteSchemaAsync(connection, table, output, cancellationToken);
            List<DMBaseLevelDbRow> db = await ReadDatabaseAsync(connection, isDigimon, cancellationToken);
            Log($"DB snapshot: [dmo].[Asset].[{table}] = {db.Count:N0} rows.");

            WriteXmlRaw(output, xml);
            WriteDbRaw(output, db, isDigimon);
            FieldScoreResult fieldScore = WriteFieldScores(output, xml, db, isDigimon);
            MatchResult match = WriteValueMatches(output, xml, db, isDigimon);
            WriteCurveSummary(output, xml, match.Matches);
            if (isDigimon)
                WriteStatusIdJoin(output, xml, db);

            string highSignal = WriteHighSignal(
                output, fileName, table, xml, db, curveCount, isDigimon,
                fieldScore, match);

            TimeSpan elapsed = DateTime.Now - started;
            Log($"Diagnostic completed in {elapsed.TotalSeconds:N1}s. Output: {output}");
            return new DMBaseLevelDatabaseDiagnosticSummary
            {
                XmlRows = xml.Count,
                DbRows = db.Count,
                CurveCount = curveCount,
                StrongMatches = match.StrongMatches,
                IsDigimon = isDigimon,
                TableName = table,
                OutputFolder = output,
                HighSignalReport = highSignal,
                Elapsed = elapsed
            };
        }

        private static List<DMBaseLevelXmlRow> LoadXml(string path)
        {
            XDocument doc = XDocument.Load(path);
            return (doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                .Select(x =>
                {
                    long id = L(x, "Id");
                    int level = (int)L(x, "Level");
                    return new DMBaseLevelXmlRow(
                        id, level, L(x, "Exp"), L(x, "At"), L(x, "Ct"), L(x, "De"),
                        L(x, "Ds"), L(x, "Ev"), L(x, "Hp"), L(x, "Ht"), L(x, "Ms"),
                        0, 0, 0, 0, id - level);
                })
                .Where(x => x.Id > 0 && x.Level > 0)
                .ToList();
        }

        private static async Task<List<DMBaseLevelDbRow>> ReadDatabaseAsync(
            SqlConnection connection, bool isDigimon, CancellationToken token)
        {
            string extra = isDigimon ? ",[StatusId],[ScaleType]" : string.Empty;
            string table = isDigimon ? "DigimonLevelStatus" : "CharacterLevelStatus";
            string sql = $"SELECT [Id],[Type],[Level],[ExpValue],[ASValue],[ARValue],[ATValue],[BLValue],[CTValue],[DEValue],[DSValue],[EVValue],[HPValue],[HTValue],[MSValue],[WSValue]{extra} FROM [dmo].[Asset].[{table}] ORDER BY [Type],[Level],[Id]";
            var rows = new List<DMBaseLevelDbRow>();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                rows.Add(new DMBaseLevelDbRow(
                    N(reader, 0), N(reader, 1), (int)N(reader, 2), N(reader, 3),
                    N(reader, 4), N(reader, 5), N(reader, 6), N(reader, 7),
                    N(reader, 8), N(reader, 9), N(reader, 10), N(reader, 11),
                    N(reader, 12), N(reader, 13), N(reader, 14), N(reader, 15),
                    isDigimon ? N(reader, 16) : null,
                    isDigimon ? N(reader, 17) : null));
            }
            return rows;
        }

        private static async Task WriteSchemaAsync(SqlConnection connection, string table, string folder, CancellationToken token)
        {
            string path = Path.Combine(folder, table + "_Schema.csv");
            const string sql = "SELECT [COLUMN_NAME],[DATA_TYPE],[IS_NULLABLE],[ORDINAL_POSITION] FROM [dmo].[INFORMATION_SCHEMA].[COLUMNS] WHERE [TABLE_SCHEMA]='Asset' AND [TABLE_NAME]=@table ORDER BY [ORDINAL_POSITION]";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@table", table);
            await using var reader = await command.ExecuteReaderAsync(token);
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("Ordinal,Column,DataType,Nullable");
            while (await reader.ReadAsync(token))
                w.WriteLine($"{N(reader, 3)},{Csv(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture))},{Csv(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture))},{Csv(Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture))}");
        }

        private static void WriteXmlRaw(string folder, List<DMBaseLevelXmlRow> rows)
        {
            using var w = new StreamWriter(Path.Combine(folder, "XML_Raw.csv"), false, new UTF8Encoding(true));
            w.WriteLine("XmlId,CurveKey,Level,Exp,AT,CT,DE,DS,EV,HP,HT,MS");
            foreach (DMBaseLevelXmlRow x in rows)
                w.WriteLine($"{x.Id},{x.CurveKey},{x.Level},{x.Exp},{x.At},{x.Ct},{x.De},{x.Ds},{x.Ev},{x.Hp},{x.Ht},{x.Ms}");
        }

        private static void WriteDbRaw(string folder, List<DMBaseLevelDbRow> rows, bool isDigimon)
        {
            using var w = new StreamWriter(Path.Combine(folder, "DB_Raw.csv"), false, new UTF8Encoding(true));
            w.WriteLine("DbId,Type,Level,ExpValue,ASValue,ARValue,ATValue,BLValue,CTValue,DEValue,DSValue,EVValue,HPValue,HTValue,MSValue,WSValue,StatusId,ScaleType");
            foreach (DMBaseLevelDbRow d in rows)
                w.WriteLine($"{d.Id},{d.Type},{d.Level},{d.Exp},{d.As},{d.Ar},{d.At},{d.Bl},{d.Ct},{d.De},{d.Ds},{d.Ev},{d.Hp},{d.Ht},{d.Ms},{d.Ws},{(isDigimon ? d.StatusId : null)},{(isDigimon ? d.ScaleType : null)}");
        }

        private sealed class FieldScoreResult
        {
            public Dictionary<string, double> Scores { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static FieldScoreResult WriteFieldScores(
            string folder, List<DMBaseLevelXmlRow> xml, List<DMBaseLevelDbRow> db, bool isDigimon)
        {
            var result = new FieldScoreResult();
            var pairs = new List<(DMBaseLevelXmlRow X, DMBaseLevelDbRow D)>();
            if (isDigimon)
            {
                var byStatus = db.Where(x => x.StatusId.HasValue).GroupBy(x => x.StatusId!.Value).ToDictionary(x => x.Key, x => x.First());
                foreach (DMBaseLevelXmlRow x in xml)
                    if (byStatus.TryGetValue(x.Id, out DMBaseLevelDbRow? d)) pairs.Add((x, d));
            }
            if (pairs.Count == 0)
            {
                foreach (DMBaseLevelXmlRow x in xml.Take(5000))
                {
                    DMBaseLevelDbRow? d = db.Where(z => z.Level == x.Level).OrderByDescending(z => Score(x, z)).FirstOrDefault();
                    if (d != null && Score(x, d) >= 7) pairs.Add((x, d));
                }
            }

            var fields = new (string Name, Func<DMBaseLevelXmlRow, long> X, Func<DMBaseLevelDbRow, long> D)[]
            {
                ("Exp -> ExpValue", x=>x.Exp,d=>d.Exp), ("At -> ATValue",x=>x.At,d=>d.At),
                ("Ct -> CTValue",x=>x.Ct,d=>d.Ct), ("De -> DEValue",x=>x.De,d=>d.De),
                ("Ds -> DSValue",x=>x.Ds,d=>d.Ds), ("Ev -> EVValue",x=>x.Ev,d=>d.Ev),
                ("Hp -> HPValue",x=>x.Hp,d=>d.Hp), ("Ht -> HTValue",x=>x.Ht,d=>d.Ht),
                ("Ms -> MSValue",x=>x.Ms,d=>d.Ms)
            };
            using var w = new StreamWriter(Path.Combine(folder, "Field_Mapping_Score.csv"), false, new UTF8Encoding(true));
            w.WriteLine("CandidateMapping,Exact,Compared,Percent");
            foreach (var field in fields)
            {
                int exact = pairs.Count(p => field.X(p.X) == field.D(p.D));
                double pct = pairs.Count == 0 ? 0 : exact * 100.0 / pairs.Count;
                result.Scores[field.Name] = pct;
                w.WriteLine($"{Csv(field.Name)},{exact},{pairs.Count},{pct:F4}");
            }
            return result;
        }

        private sealed record RowMatch(DMBaseLevelXmlRow Xml, DMBaseLevelDbRow? Db, int Score, int MaxScore);
        private sealed class MatchResult
        {
            public List<RowMatch> Matches { get; } = new();
            public int StrongMatches { get; set; }
        }

        private static MatchResult WriteValueMatches(
            string folder, List<DMBaseLevelXmlRow> xml, List<DMBaseLevelDbRow> db, bool isDigimon)
        {
            var result = new MatchResult();
            Dictionary<int, List<DMBaseLevelDbRow>> byLevel = db.GroupBy(x => x.Level).ToDictionary(x => x.Key, x => x.ToList());
            using var w = new StreamWriter(Path.Combine(folder, "XML_to_DB_ValueMatches.csv"), false, new UTF8Encoding(true));
            w.WriteLine("XmlId,CurveKey,Level,BestDbId,BestDbType,DbStatusId,DbScaleType,Score,MaxScore,Percent,StatusIdExact,ExpExact,ATExact,CTExact,DEExact,DSExact,EVExact,HPExact,HTExact,MSExact");
            foreach (DMBaseLevelXmlRow x in xml)
            {
                DMBaseLevelDbRow? best = null;
                int bestScore = -1;
                if (byLevel.TryGetValue(x.Level, out List<DMBaseLevelDbRow>? candidates))
                {
                    foreach (DMBaseLevelDbRow d in candidates)
                    {
                        int score = Score(x, d);
                        if (isDigimon && d.StatusId == x.Id) score += 6;
                        if (score > bestScore) { best = d; bestScore = score; }
                    }
                }
                int max = isDigimon ? 15 : 9;
                if (bestScore >= (isDigimon ? 13 : 8)) result.StrongMatches++;
                result.Matches.Add(new RowMatch(x, best, Math.Max(0, bestScore), max));
                if (best == null)
                {
                    w.WriteLine($"{x.Id},{x.CurveKey},{x.Level},,,,,0,{max},0,,,,,,,,,,");
                    continue;
                }
                w.WriteLine(string.Join(",", new[]
                {
                    x.Id.ToString(), x.CurveKey.ToString(), x.Level.ToString(), best.Id.ToString(), best.Type.ToString(),
                    best.StatusId?.ToString() ?? "", best.ScaleType?.ToString() ?? "", Math.Max(0,bestScore).ToString(), max.ToString(),
                    (Math.Max(0,bestScore)*100.0/max).ToString("F2",CultureInfo.InvariantCulture),
                    B(best.StatusId==x.Id), B(best.Exp==x.Exp), B(best.At==x.At), B(best.Ct==x.Ct), B(best.De==x.De),
                    B(best.Ds==x.Ds), B(best.Ev==x.Ev), B(best.Hp==x.Hp), B(best.Ht==x.Ht), B(best.Ms==x.Ms)
                }));
            }
            return result;
        }

        private static void WriteCurveSummary(string folder, List<DMBaseLevelXmlRow> xml, List<RowMatch> matches)
        {
            using var w = new StreamWriter(Path.Combine(folder, "Curve_to_Type_Candidates.csv"), false, new UTF8Encoding(true));
            w.WriteLine("CurveKey,XmlRows,MinLevel,MaxLevel,BestDbType,MatchedRows,AverageScorePercent,DistinctCandidateTypes");
            foreach (IGrouping<long, DMBaseLevelXmlRow> curve in xml.GroupBy(x => x.CurveKey).OrderBy(x => x.Key))
            {
                List<RowMatch> m = matches.Where(x => x.Xml.CurveKey == curve.Key && x.Db != null).ToList();
                var typeGroups = m.GroupBy(x => x.Db!.Type).OrderByDescending(x => x.Count()).ThenByDescending(x => x.Average(z => z.Score));
                var best = typeGroups.FirstOrDefault();
                double avg = m.Count == 0 ? 0 : m.Average(x => x.Score * 100.0 / x.MaxScore);
                w.WriteLine($"{curve.Key},{curve.Count()},{curve.Min(x=>x.Level)},{curve.Max(x=>x.Level)},{best?.Key.ToString() ?? ""},{best?.Count() ?? 0},{avg:F2},{typeGroups.Count()}");
            }
        }

        private static void WriteStatusIdJoin(string folder, List<DMBaseLevelXmlRow> xml, List<DMBaseLevelDbRow> db)
        {
            var byStatus = db.Where(x => x.StatusId.HasValue).GroupBy(x => x.StatusId!.Value).ToDictionary(x => x.Key, x => x.ToList());
            using var w = new StreamWriter(Path.Combine(folder, "Digimon_StatusId_Join.csv"), false, new UTF8Encoding(true));
            w.WriteLine("XmlId,CurveKey,XmlLevel,DbRows,DbId,DbType,DbLevel,ScaleType,AllMappedStatsExact");
            foreach (DMBaseLevelXmlRow x in xml)
            {
                if (!byStatus.TryGetValue(x.Id, out List<DMBaseLevelDbRow>? rows))
                {
                    w.WriteLine($"{x.Id},{x.CurveKey},{x.Level},0,,,,,");
                    continue;
                }
                foreach (DMBaseLevelDbRow d in rows)
                    w.WriteLine($"{x.Id},{x.CurveKey},{x.Level},{rows.Count},{d.Id},{d.Type},{d.Level},{d.ScaleType},{B(Score(x,d)==9)}");
            }
        }

        private static string WriteHighSignal(
            string folder, string fileName, string table, List<DMBaseLevelXmlRow> xml,
            List<DMBaseLevelDbRow> db, int curves, bool isDigimon,
            FieldScoreResult fields, MatchResult matches)
        {
            string path = Path.Combine(folder, "HIGH_SIGNAL_REPORT.txt");
            var sb = new StringBuilder();
            sb.AppendLine("DMBase Level ↔ Database diagnostic");
            sb.AppendLine("===================================");
            sb.AppendLine($"XML: {fileName}");
            sb.AppendLine($"DB table: [dmo].[Asset].[{table}]");
            sb.AppendLine($"XML rows: {xml.Count:N0}");
            sb.AppendLine($"Detected XML curves (Id-Level key): {curves:N0}");
            sb.AppendLine($"DB rows: {db.Count:N0}");
            sb.AppendLine($"Strong value matches: {matches.StrongMatches:N0} / {xml.Count:N0}");
            sb.AppendLine();
            sb.AppendLine("Candidate field mapping exact percentages:");
            foreach (var pair in fields.Scores.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {pair.Key,-20} {pair.Value,8:F3}%");
            sb.AppendLine();
            if (isDigimon)
            {
                int statusExact = xml.Count(x => db.Any(d => d.StatusId == x.Id));
                sb.AppendLine($"Digimon StatusId == XML Id coverage: {statusExact:N0}/{xml.Count:N0} ({(xml.Count==0?0:statusExact*100.0/xml.Count):F3}%)");
                sb.AppendLine("Inspect Digimon_StatusId_Join.csv to confirm Type and ScaleType rules before enabling writes.");
            }
            else
            {
                sb.AppendLine("CharacterLevelStatus has no StatusId column. Curve_to_Type_Candidates.csv therefore infers Type by same-level stat signatures.");
            }
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: This comparison is intentionally read-only. Use these files to establish the Type/StatusId/ScaleType rules before the importer is unlocked.");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static int Score(DMBaseLevelXmlRow x, DMBaseLevelDbRow d)
        {
            int s = 0;
            if (x.Exp == d.Exp) s++;
            if (x.At == d.At) s++;
            if (x.Ct == d.Ct) s++;
            if (x.De == d.De) s++;
            if (x.Ds == d.Ds) s++;
            if (x.Ev == d.Ev) s++;
            if (x.Hp == d.Hp) s++;
            if (x.Ht == d.Ht) s++;
            if (x.Ms == d.Ms) s++;
            return s;
        }

        private static long L(XElement x, string name) =>
            long.TryParse(x.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

        private static long N(SqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0;
            object value = reader.GetValue(ordinal);
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static string Csv(string? value)
        {
            string s = value ?? string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string B(bool value) => value ? "1" : "0";
    }
}
