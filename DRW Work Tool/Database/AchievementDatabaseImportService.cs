using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class AchievementDatabaseImportSummary
    {
        public int Rows { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    public sealed class AchievementDatabaseImportService
    {
        public async Task<AchievementDatabaseImportSummary> ImportAsync(
            string connectionString,
            string achieveXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            string logDir = Path.Combine(AppPaths.Logs, "DatabaseImports");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "achievement_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");

            void Log(string message)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logFile, line + Environment.NewLine);
                progress?.Report(line);
            }

            Log("ACHIEVE -> DATABASE started.");
            Log("Mapping prepared from Achieve.xml + supplied Asset.Achievement schema: Id=physical order, QuestId=s_nQuestID, BuffId=s_nBuffCode.");
            Log("Asset.Achievement.Type is imported as 0. The supplied working DB sample shows Type=0 for the existing base achievements; s_nType remains an Achieve.xml classification and is not copied into this DB column.");

            if (!File.Exists(achieveXml))
                throw new FileNotFoundException("Achieve.xml was not found.", achieveXml);

            XDocument doc = XDocument.Load(achieveXml, LoadOptions.None);
            XElement root = doc.Root ?? throw new InvalidDataException("Achieve.xml has no root.");
            if (!root.Name.LocalName.Equals("AchieveSINFOs", StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected Achieve.xml root <{root.Name.LocalName}>.");

            var nodes = root.Elements("AchieveSINFO").ToList();
            if (nodes.Count == 0)
                throw new InvalidDataException("Achieve.xml contains no AchieveSINFO records.");

            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("QuestId", typeof(int));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("BuffId", typeof(int));

            int physical = 0;
            foreach (XElement node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                physical++;
                int questId = RequiredInt(node, "s_nQuestID", physical);
                int buffId = RequiredInt(node, "s_nBuffCode", physical);
                table.Rows.Add(physical, questId, 0, buffId);
            }

            int duplicateQuestIds = nodes
                .GroupBy(x => x.Element("s_nQuestID")?.Value ?? string.Empty)
                .Count(g => g.Count() > 1);
            if (duplicateQuestIds > 0)
                Log($"WARNING: {duplicateQuestIds:N0} duplicated QuestId groups exist in Achieve.xml; physical rows are intentionally preserved.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var schema = new SqlCommand(
                "SELECT TOP (0) [Id],[QuestId],[Type],[BuffId] FROM [dmo].[Asset].[Achievement];",
                connection))
            {
                await schema.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var xa = new SqlCommand("SET XACT_ABORT ON;", connection))
                await xa.ExecuteNonQueryAsync(cancellationToken);

            await using SqlTransaction tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                Log($"Validated {table.Rows.Count:N0} achievement rows. Clearing Asset.Achievement inside transaction...");
                await using (var clear = new SqlCommand("DELETE FROM [dmo].[Asset].[Achievement];", connection, tx))
                {
                    clear.CommandTimeout = 120;
                    await clear.ExecuteNonQueryAsync(cancellationToken);
                }

                using var bulk = new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints,
                    tx)
                {
                    DestinationTableName = "[dmo].[Asset].[Achievement]",
                    BatchSize = 500,
                    BulkCopyTimeout = 180
                };

                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                await bulk.WriteToServerAsync(table, cancellationToken);

                await using (var verify = new SqlCommand("SELECT COUNT_BIG(*) FROM [dmo].[Asset].[Achievement];", connection, tx))
                {
                    long actual = Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                    if (actual != table.Rows.Count)
                        throw new InvalidOperationException($"Achievement row count mismatch. Expected={table.Rows.Count:N0}, actual={actual:N0}.");
                }

                await tx.CommitAsync(cancellationToken);
                sw.Stop();
                Log($"COMMIT OK. Asset.Achievement={table.Rows.Count:N0} rows in {sw.Elapsed.TotalSeconds:N1}s.");
                return new AchievementDatabaseImportSummary { Rows = table.Rows.Count, Elapsed = sw.Elapsed, LogFile = logFile };
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    Log("ROLLBACK OK. Database restored to the state before Achievement import.");
                }
                catch (Exception rollbackEx)
                {
                    Log("ROLLBACK ERROR: " + rollbackEx.Message);
                }
                throw;
            }
        }

        private static int RequiredInt(XElement node, string field, int row)
        {
            string? raw = node.Element(field)?.Value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new InvalidDataException($"AchieveSINFO row {row}: <{field}> invalid: '{raw}'.");
            return value;
        }
    }
}
