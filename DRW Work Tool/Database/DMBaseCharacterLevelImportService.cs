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
    public sealed class DMBaseCharacterLevelImportSummary
    {
        public int ExistingTypes { get; init; }
        public int LevelsPerType { get; init; }
        public int InsertedRows { get; init; }
        public long CanonicalCurveKey { get; init; }
        public double CanonicalMatchPercent { get; init; }
        public string OutputFolder { get; init; } = string.Empty;
    }

    internal sealed record CharacterCurveRow(
        long Id, long CurveKey, int Level, long Exp,
        int As, int Ar, int At, int Bl, int Ct, int De, int Ds,
        int Ev, int Hp, int Ht, int Ms, int Ws);

    internal sealed record CharacterDbProbe(
        int Type, int Level, long ExpValue,
        int As, int Ar, int At, int Bl, int Ct, int De, int Ds,
        int Ev, int Hp, int Ht, int Ms, int Ws);

    public sealed class DMBaseCharacterLevelImportService
    {
        public async Task<DMBaseCharacterLevelImportSummary> ImportAsync(
            string connectionString,
            string xmlPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string file = Path.GetFileName(xmlPath);
            if (!file.StartsWith("TamerBase", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CharacterLevelStatus import only accepts TamerBase*.xml.");

            List<CharacterCurveRow> xml = LoadXml(xmlPath);
            if (xml.Count == 0)
                throw new InvalidDataException("No Tamer level rows were found in the XML.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            List<CharacterDbProbe> db = await ReadDbAsync(connection, cancellationToken);
            if (db.Count == 0)
                throw new InvalidOperationException("CharacterLevelStatus is empty. A current DB snapshot is required to preserve the existing Type set.");

            List<int> types = db.Select(x => x.Type).Distinct().OrderBy(x => x).ToList();
            if (types.Count == 0)
                throw new InvalidOperationException("No CharacterLevelStatus Type values were found.");

            (long curveKey, double match) = SelectCanonicalCurve(xml, db);
            if (match < 99.0)
                throw new InvalidOperationException($"No safe canonical Tamer curve was found. Best match was only {match:F2}%.");

            List<CharacterCurveRow> canonical = xml
                .Where(x => x.CurveKey == curveKey)
                .GroupBy(x => x.Level)
                .Select(g => g.OrderBy(x => x.Id).First())
                .OrderBy(x => x.Level)
                .ToList();

            if (canonical.Count == 0)
                throw new InvalidDataException("The selected canonical curve contains no rows.");

            string output = Path.Combine(
                AppPaths.Logs,
                "DMBaseLevelDatabaseImport",
                "Tamer_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            WriteBeforeSnapshot(output, db);
            WritePlan(output, types, canonical, curveKey, match);

            progress?.Report($"Canonical XML curve: {curveKey} ({match:F2}% match to current CharacterLevelStatus).");
            progress?.Report($"Preserving {types.Count:N0} existing Type values; importing {canonical.Count:N0} levels per Type.");
            progress?.Report("EXP mapping confirmed by diagnostic/current DB: ExpValue = XML Exp / 100.");

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var delete = new SqlCommand("DELETE FROM [dmo].[Asset].[CharacterLevelStatus]", connection, tx) { CommandTimeout = 180 })
                    await delete.ExecuteNonQueryAsync(cancellationToken);

                const string insertSql = @"INSERT INTO [dmo].[Asset].[CharacterLevelStatus]
([Type],[Level],[ExpValue],[ASValue],[ARValue],[ATValue],[BLValue],[CTValue],[DEValue],[DSValue],[EVValue],[HPValue],[HTValue],[MSValue],[WSValue])
VALUES (@Type,@Level,@Exp,@AS,@AR,@AT,@BL,@CT,@DE,@DS,@EV,@HP,@HT,@MS,@WS)";

                await using var insert = new SqlCommand(insertSql, connection, tx) { CommandTimeout = 180 };
                insert.Parameters.Add("@Type", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@Level", System.Data.SqlDbType.TinyInt);
                insert.Parameters.Add("@Exp", System.Data.SqlDbType.BigInt);
                insert.Parameters.Add("@AS", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@AR", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@AT", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@BL", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@CT", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@DE", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@DS", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@EV", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@HP", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@HT", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@MS", System.Data.SqlDbType.Int);
                insert.Parameters.Add("@WS", System.Data.SqlDbType.Int);

                int inserted = 0;
                foreach (int type in types)
                {
                    foreach (CharacterCurveRow row in canonical)
                    {
                        if (row.Level < byte.MinValue || row.Level > byte.MaxValue)
                            throw new InvalidOperationException($"Level {row.Level} exceeds the DB tinyint range.");

                        insert.Parameters["@Type"].Value = type;
                        insert.Parameters["@Level"].Value = row.Level;
                        insert.Parameters["@Exp"].Value = row.Exp / 100L;
                        insert.Parameters["@AS"].Value = row.As;
                        insert.Parameters["@AR"].Value = row.Ar;
                        insert.Parameters["@AT"].Value = row.At;
                        insert.Parameters["@BL"].Value = row.Bl;
                        insert.Parameters["@CT"].Value = row.Ct;
                        insert.Parameters["@DE"].Value = row.De;
                        insert.Parameters["@DS"].Value = row.Ds;
                        insert.Parameters["@EV"].Value = row.Ev;
                        insert.Parameters["@HP"].Value = row.Hp;
                        insert.Parameters["@HT"].Value = row.Ht;
                        insert.Parameters["@MS"].Value = row.Ms;
                        insert.Parameters["@WS"].Value = row.Ws;
                        await insert.ExecuteNonQueryAsync(cancellationToken);
                        inserted++;
                    }
                    progress?.Report($"Imported CharacterLevelStatus Type {type} ({canonical.Count:N0} levels).");
                }

                await tx.CommitAsync(cancellationToken);
                progress?.Report($"CharacterLevelStatus import completed: {inserted:N0} rows.");

                return new DMBaseCharacterLevelImportSummary
                {
                    ExistingTypes = types.Count,
                    LevelsPerType = canonical.Count,
                    InsertedRows = inserted,
                    CanonicalCurveKey = curveKey,
                    CanonicalMatchPercent = match,
                    OutputFolder = output
                };
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private static List<CharacterCurveRow> LoadXml(string path)
        {
            XDocument doc = XDocument.Load(path);
            return (doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                .Select(x =>
                {
                    long id = L(x, "Id");
                    int level = (int)L(x, "Level");
                    return new CharacterCurveRow(
                        id, id - level, level, L(x, "Exp"),
                        I(x,"As"), I(x,"Ar"), I(x,"At"), I(x,"Bl"), I(x,"Ct"), I(x,"De"), I(x,"Ds"),
                        I(x,"Ev"), I(x,"Hp"), I(x,"Ht"), I(x,"Ms"), I(x,"Ws"));
                })
                .Where(x => x.Id > 0 && x.Level > 0)
                .ToList();
        }

        private static async Task<List<CharacterDbProbe>> ReadDbAsync(SqlConnection c, CancellationToken token)
        {
            const string sql = "SELECT [Type],[Level],[ExpValue],[ASValue],[ARValue],[ATValue],[BLValue],[CTValue],[DEValue],[DSValue],[EVValue],[HPValue],[HTValue],[MSValue],[WSValue] FROM [dmo].[Asset].[CharacterLevelStatus] ORDER BY [Type],[Level]";
            var rows = new List<CharacterDbProbe>();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                rows.Add(new CharacterDbProbe(
                    Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture),
                    N(r,3),N(r,4),N(r,5),N(r,6),N(r,7),N(r,8),N(r,9),N(r,10),N(r,11),N(r,12),N(r,13),N(r,14)));
            }
            return rows;
        }

        private static (long CurveKey, double Percent) SelectCanonicalCurve(List<CharacterCurveRow> xml, List<CharacterDbProbe> db)
        {
            List<CharacterDbProbe> baseline = db.GroupBy(x => x.Level).Select(g => g.First()).OrderBy(x => x.Level).ToList();
            long bestKey = 0;
            double best = -1;
            foreach (IGrouping<long, CharacterCurveRow> curve in xml.GroupBy(x => x.CurveKey))
            {
                var byLevel = curve.GroupBy(x => x.Level).ToDictionary(g => g.Key, g => g.First());
                int exact = 0, compared = 0;
                foreach (CharacterDbProbe d in baseline)
                {
                    if (!byLevel.TryGetValue(d.Level, out CharacterCurveRow? x)) continue;
                    compared += 15;
                    exact += x.Exp / 100L == d.ExpValue ? 1 : 0;
                    exact += x.As == d.As ? 1 : 0;
                    exact += x.Ar == d.Ar ? 1 : 0;
                    exact += x.At == d.At ? 1 : 0;
                    exact += x.Bl == d.Bl ? 1 : 0;
                    exact += x.Ct == d.Ct ? 1 : 0;
                    exact += x.De == d.De ? 1 : 0;
                    exact += x.Ds == d.Ds ? 1 : 0;
                    exact += x.Ev == d.Ev ? 1 : 0;
                    exact += x.Hp == d.Hp ? 1 : 0;
                    exact += x.Ht == d.Ht ? 1 : 0;
                    exact += x.Ms == d.Ms ? 1 : 0;
                    exact += x.Ws == d.Ws ? 1 : 0;
                    // AS/AR/BL/WS are zero in the current Tamer table; count them once each through the XML values above.
                    compared -= 2; // 13 effective fields compared.
                }
                double pct = compared == 0 ? 0 : exact * 100.0 / compared;
                if (pct > best) { best = pct; bestKey = curve.Key; }
            }
            return (bestKey, best);
        }

        private static void WriteBeforeSnapshot(string folder, List<CharacterDbProbe> rows)
        {
            using var w = new StreamWriter(Path.Combine(folder,"BEFORE_CharacterLevelStatus.csv"),false,new UTF8Encoding(true));
            w.WriteLine("Type,Level,ExpValue,ASValue,ARValue,ATValue,BLValue,CTValue,DEValue,DSValue,EVValue,HPValue,HTValue,MSValue,WSValue");
            foreach (var x in rows)
                w.WriteLine($"{x.Type},{x.Level},{x.ExpValue},{x.As},{x.Ar},{x.At},{x.Bl},{x.Ct},{x.De},{x.Ds},{x.Ev},{x.Hp},{x.Ht},{x.Ms},{x.Ws}");
        }

        private static void WritePlan(string folder, List<int> types, List<CharacterCurveRow> rows, long key, double match)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DMBase Tamer -> CharacterLevelStatus import plan");
            sb.AppendLine("==============================================");
            sb.AppendLine($"Canonical CurveKey: {key}");
            sb.AppendLine($"Current DB match: {match:F4}%");
            sb.AppendLine($"Existing DB Types preserved: {string.Join(", ", types)}");
            sb.AppendLine($"Levels per Type: {rows.Count} ({rows.Min(x=>x.Level)}-{rows.Max(x=>x.Level)})");
            sb.AppendLine("EXP rule: ExpValue = XML Exp / 100 (integer division). ");
            sb.AppendLine("AS/AR/BL/WS are read from XML when present; absent fields become 0.");
            File.WriteAllText(Path.Combine(folder,"IMPORT_PLAN.txt"),sb.ToString(),Encoding.UTF8);
        }

        private static long L(XElement x, string name)
        {
            XElement? e = x.Elements().FirstOrDefault(z => z.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return long.TryParse(e?.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;
        }
        private static int I(XElement x, string name)
        {
            long n = L(x,name);
            return n > int.MaxValue ? int.MaxValue : n < int.MinValue ? int.MinValue : (int)n;
        }
        private static int N(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    }
}
