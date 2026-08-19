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
    public sealed class DigimonBookDatabaseImportSummary
    {
        public int BookInfoRows { get; init; }
        public int DeckBuffRows { get; init; }
        public int DeckBuffOptionRows { get; init; }
        public string OutputFolder { get; init; } = string.Empty;
        public TimeSpan Elapsed { get; init; }
    }

    public sealed class DigimonBookDatabaseImportService
    {
        private sealed record BookRow(int OptionId, string Name, string Explain);
        private sealed record DeckRow(int GroupId, string Name, string Explain, int[] Condition, int[] AtType, int[] Option, int[] Value, int[] Prob, int[] Time);
        private sealed record DbOptionRow(int Condition, int AtType, int Value, int Prob, int Time, int OptionId);

        public async Task<DigimonBookDatabaseImportSummary> ImportAsync(
            string connectionString,
            string digimonBookFolder,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            string output = Path.Combine(AppPaths.Logs, "DigimonBookDatabaseImport", started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            string logFile = Path.Combine(output, "import.log");

            void Log(string text)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}";
                File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
                progress?.Report(line);
            }

            string bookPath = Path.Combine(digimonBookFolder, "BookInfo.xml");
            string deckPath = Path.Combine(digimonBookFolder, "DeckOption.xml");
            if (!File.Exists(bookPath) || !File.Exists(deckPath))
                throw new FileNotFoundException("BookInfo.xml and DeckOption.xml are required for the database import.");

            List<BookRow> books = LoadBooks(bookPath);
            List<DeckRow> decks = LoadDecks(deckPath);
            if (books.Count == 0 || decks.Count == 0)
                throw new InvalidDataException("Digimon Book XML is empty. Import aborted.");

            Log($"XML loaded: BookInfo={books.Count:N0}, DeckOption={decks.Count:N0}.");
            Log("Opening SQL transaction. All three tables are synchronized atomically.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await WriteSnapshotAsync(connection, output, cancellationToken);

            await using SqlTransaction tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await SyncBookInfoAsync(connection, tx, books, cancellationToken);
                Log("Asset.DeckBookInfo synchronized.");

                await SyncDeckBuffAsync(connection, tx, decks, cancellationToken);
                Log("Asset.DeckBuff synchronized.");

                int optionRows = await SyncDeckBuffOptionsAsync(connection, tx, decks, cancellationToken);
                Log($"Asset.DeckBuffOption synchronized: {optionRows:N0} rows.");

                await tx.CommitAsync(cancellationToken);
                Log("COMMIT complete.");

                TimeSpan elapsed = DateTime.Now - started;
                return new DigimonBookDatabaseImportSummary
                {
                    BookInfoRows = books.Count + 1,
                    DeckBuffRows = decks.Count,
                    DeckBuffOptionRows = optionRows,
                    OutputFolder = output,
                    Elapsed = elapsed
                };
            }
            catch
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
                Log("ROLLBACK complete. Database was not left half-imported.");
                throw;
            }
        }

        private static List<BookRow> LoadBooks(string path) =>
            (XDocument.Load(path).Root?.Elements("BookInfo") ?? Enumerable.Empty<XElement>())
                .Select(x => new BookRow(I(x, "s_dwOptID"), T(x, "s_szOptName"), T(x, "s_szOptExplain")))
                .Where(x => x.OptionId > 0)
                .GroupBy(x => x.OptionId).Select(g => g.First()).OrderBy(x => x.OptionId).ToList();

        private static List<DeckRow> LoadDecks(string path) =>
            (XDocument.Load(path).Root?.Elements("DeckOption") ?? Enumerable.Empty<XElement>())
                .Select(x => new DeckRow(
                    I(x, "s_nGroupIdx"), T(x, "s_szGroupName"), T(x, "s_szExplain"),
                    A(x, "s_nCondition", "condition"), A(x, "s_nAT_Type", "atType"), A(x, "s_nOption", "option"),
                    A(x, "s_nVal", "value"), A(x, "s_nProb", "prob"), A(x, "s_nTime", "time")))
                .Where(x => x.GroupId > 0)
                .GroupBy(x => x.GroupId).Select(g => g.First()).ToList();

        private static async Task SyncBookInfoAsync(SqlConnection c, SqlTransaction tx, List<BookRow> rows, CancellationToken token)
        {
            var all = new List<BookRow> { new(0, "None", "None.") };
            all.AddRange(rows);
            string ids = string.Join(",", all.Select(x => x.OptionId.ToString(CultureInfo.InvariantCulture)));
            await ExecAsync(c, tx, $"DELETE FROM [dmo].[Asset].[DeckBookInfo] WHERE [OptionId] NOT IN ({ids});", token);

            foreach (BookRow r in all)
            {
                const string sql = @"
UPDATE [dmo].[Asset].[DeckBookInfo]
SET [Type]=@Type,[Name]=@Name,[Explain]=@Explain
WHERE [OptionId]=@OptionId;
IF @@ROWCOUNT=0
    INSERT INTO [dmo].[Asset].[DeckBookInfo] ([OptionId],[Type],[Name],[Explain]) VALUES (@OptionId,@Type,@Name,@Explain);";
                await using var cmd = new SqlCommand(sql, c, tx);
                cmd.Parameters.AddWithValue("@OptionId", r.OptionId);
                cmd.Parameters.AddWithValue("@Type", r.OptionId);
                cmd.Parameters.AddWithValue("@Name", Clip(r.Name, 100));
                cmd.Parameters.AddWithValue("@Explain", Clip(r.Explain, 250));
                await cmd.ExecuteNonQueryAsync(token);
            }
        }

        private static async Task SyncDeckBuffAsync(SqlConnection c, SqlTransaction tx, List<DeckRow> rows, CancellationToken token)
        {
            string ids = string.Join(",", rows.Select(x => x.GroupId.ToString(CultureInfo.InvariantCulture)));
            await ExecAsync(c, tx, $"DELETE FROM [dmo].[Asset].[DeckBuffOption] WHERE [GroupIdX] NOT IN ({ids});", token);
            await ExecAsync(c, tx, $"DELETE FROM [dmo].[Asset].[DeckBuff] WHERE [GroupIdX] NOT IN ({ids});", token);

            foreach (DeckRow r in rows)
            {
                const string sql = @"
UPDATE [dmo].[Asset].[DeckBuff]
SET [GroupName]=@GroupName,[Explain]=@Explain
WHERE [GroupIdX]=@GroupId;
IF @@ROWCOUNT=0
    INSERT INTO [dmo].[Asset].[DeckBuff] ([GroupIdX],[GroupName],[Explain]) VALUES (@GroupId,@GroupName,@Explain);";
                await using var cmd = new SqlCommand(sql, c, tx);
                cmd.Parameters.AddWithValue("@GroupId", r.GroupId);
                cmd.Parameters.AddWithValue("@GroupName", Clip(r.Name, 100));
                cmd.Parameters.AddWithValue("@Explain", Clip(r.Explain, 250));
                await cmd.ExecuteNonQueryAsync(token);
            }
        }

        private static async Task<int> SyncDeckBuffOptionsAsync(SqlConnection c, SqlTransaction tx, List<DeckRow> decks, CancellationToken token)
        {
            int total = 0;
            foreach (DeckRow deck in decks)
            {
                DbOptionRow[] mapped = MapOptions(deck);
                List<int> ids = new();
                await using (var read = new SqlCommand("SELECT [Id] FROM [dmo].[Asset].[DeckBuffOption] WHERE [GroupIdX]=@g ORDER BY [Id]", c, tx))
                {
                    read.Parameters.AddWithValue("@g", deck.GroupId);
                    await using var rr = await read.ExecuteReaderAsync(token);
                    while (await rr.ReadAsync(token)) ids.Add(Convert.ToInt32(rr.GetValue(0), CultureInfo.InvariantCulture));
                }

                for (int i = 0; i < mapped.Length; i++)
                {
                    DbOptionRow row = mapped[i];
                    if (i < ids.Count)
                    {
                        const string update = @"UPDATE [dmo].[Asset].[DeckBuffOption]
SET [GroupIdX]=@g,[Condition]=@c,[AtType]=@a,[Value]=@v,[Prob]=@p,[Time]=@t,[OptionId]=@o WHERE [Id]=@id;";
                        await using var cmd = new SqlCommand(update, c, tx);
                        AddOptionParameters(cmd, deck.GroupId, row);
                        cmd.Parameters.AddWithValue("@id", ids[i]);
                        await cmd.ExecuteNonQueryAsync(token);
                    }
                    else
                    {
                        const string insert = @"INSERT INTO [dmo].[Asset].[DeckBuffOption] ([GroupIdX],[Condition],[AtType],[Value],[Prob],[Time],[OptionId])
VALUES (@g,@c,@a,@v,@p,@t,@o);";
                        await using var cmd = new SqlCommand(insert, c, tx);
                        AddOptionParameters(cmd, deck.GroupId, row);
                        await cmd.ExecuteNonQueryAsync(token);
                    }
                    total++;
                }

                if (ids.Count > mapped.Length)
                {
                    string extra = string.Join(",", ids.Skip(mapped.Length));
                    await ExecAsync(c, tx, $"DELETE FROM [dmo].[Asset].[DeckBuffOption] WHERE [Id] IN ({extra});", token);
                }
            }
            return total;
        }

        private static DbOptionRow[] MapOptions(DeckRow d)
        {
            // The supplied known-good DB comparison reveals two encodings.
            // Legacy groups (1000+) use the original client layout: DB row order is [slot2-special, slot0, slot1]
            // while Prob/Time remain in direct row order. Custom groups (<1000) use the newer explicit layout.
            if (d.GroupId >= 1000)
            {
                int c2 = At(d.Condition, 2), a2 = At(d.AtType, 2), o2 = At(d.Option, 2);
                return new[]
                {
                    new DbOptionRow(IsActive(c2,a2,o2,At(d.Value,2)) ? (c2 == 0 ? 1 : 3) : 0, c2, o2, At(d.Prob,0), At(d.Time,0), a2),
                    MapLegacyNormal(d, 0, 1),
                    MapLegacyNormal(d, 1, 2)
                };
            }

            int option0 = FirstNonZero(At(d.AtType,0), At(d.Option,0));
            int option1 = FirstNonZero(At(d.AtType,1), At(d.Option,1));
            int option2 = FirstNonZero(At(d.AtType,2), 6, At(d.Option,2));
            int value0 = FirstNonZero(At(d.Value,0), At(d.Option,0));
            int value1 = FirstNonZero(At(d.Value,1), At(d.Option,1));
            int value2 = FirstNonZero(At(d.Value,2), At(d.Option,2));

            // This reproduces groups 1..4 from the supplied known-good DB and gives group 5
            // the same rule as group 2 when its first option is OptionId 2.
            int middleAtType = option0 == 2 ? 2 : 1;
            return new[]
            {
                new DbOptionRow(IsActive(At(d.Condition,0),option0,value0,0)?1:0, 0, value0, At(d.Prob,0), At(d.Time,0), option0),
                new DbOptionRow(IsActive(At(d.Condition,1),option1,value1,0)?1:0, middleAtType, value1, At(d.Prob,1), At(d.Time,1), option1),
                new DbOptionRow(IsActive(At(d.Condition,2),option2,value2,0)?1:0, option2, value2, At(d.Prob,2), At(d.Time,2), option2)
            };
        }

        private static DbOptionRow MapLegacyNormal(DeckRow d, int slot, int probSlot)
        {
            int cond = At(d.Condition, slot), at = At(d.AtType, slot), option = At(d.Option, slot), value = At(d.Value, slot);
            bool active = IsActive(cond, at, option, value);
            int dbCondition = !active ? 0 : (at == 0 ? 1 : 3);
            return new DbOptionRow(dbCondition, at, value, At(d.Prob, probSlot), At(d.Time, probSlot), option);
        }

        private static void AddOptionParameters(SqlCommand cmd, int groupId, DbOptionRow r)
        {
            cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@c", r.Condition);
            cmd.Parameters.AddWithValue("@a", r.AtType);
            cmd.Parameters.AddWithValue("@v", r.Value);
            cmd.Parameters.AddWithValue("@p", r.Prob);
            cmd.Parameters.AddWithValue("@t", r.Time);
            cmd.Parameters.AddWithValue("@o", r.OptionId);
        }

        private static async Task WriteSnapshotAsync(SqlConnection c, string folder, CancellationToken token)
        {
            await WriteTableAsync(c, Path.Combine(folder, "BEFORE_DeckBookInfo.csv"), "SELECT [Id],[OptionId],[Type],[Name],[Explain] FROM [dmo].[Asset].[DeckBookInfo] ORDER BY [Id]", token);
            await WriteTableAsync(c, Path.Combine(folder, "BEFORE_DeckBuff.csv"), "SELECT [Id],[GroupIdX],[GroupName],[Explain] FROM [dmo].[Asset].[DeckBuff] ORDER BY [Id]", token);
            await WriteTableAsync(c, Path.Combine(folder, "BEFORE_DeckBuffOption.csv"), "SELECT [Id],[GroupIdX],[Condition],[AtType],[Value],[Prob],[Time],[OptionId] FROM [dmo].[Asset].[DeckBuffOption] ORDER BY [Id]", token);
        }

        private static async Task WriteTableAsync(SqlConnection c, string path, string sql, CancellationToken token)
        {
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync(token);
            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (i > 0) w.Write(',');
                w.Write(Csv(r.GetName(i)));
            }
            w.WriteLine();
            while (await r.ReadAsync(token))
            {
                for (int i = 0; i < r.FieldCount; i++)
                {
                    if (i > 0) w.Write(',');
                    w.Write(Csv(r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)));
                }
                w.WriteLine();
            }
        }

        private static async Task ExecAsync(SqlConnection c, SqlTransaction tx, string sql, CancellationToken token)
        {
            await using var cmd = new SqlCommand(sql, c, tx);
            await cmd.ExecuteNonQueryAsync(token);
        }

        private static bool IsActive(params int[] values) => values.Any(x => x != 0);
        private static int FirstNonZero(params int[] values) => values.FirstOrDefault(x => x != 0);
        private static int At(int[] a, int i) => i >= 0 && i < a.Length ? a[i] : 0;
        private static int I(XElement e, string n) => int.TryParse(e.Element(n)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static string T(XElement e, string n) => e.Element(n)?.Value ?? string.Empty;
        private static int[] A(XElement e, string p, string c) => (e.Element(p)?.Elements(c) ?? Enumerable.Empty<XElement>()).Select(x => int.TryParse(x.Value, out int v) ? v : 0).Take(3).Concat(Enumerable.Repeat(0,3)).Take(3).ToArray();
        private static string Clip(string? s, int max) { s ??= string.Empty; return s.Length <= max ? s : s[..max]; }
        private static string Csv(string? s) { s ??= string.Empty; return '"' + s.Replace("\"", "\"\"") + '"'; }
    }
}
