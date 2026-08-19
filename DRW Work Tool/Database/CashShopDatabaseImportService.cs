using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DRW_Work_Tool.Core
{
    public sealed class CashShopDatabaseImportSummary
    {
        public int Rows { get; init; }
        public CashShopDatabaseMapping Mapping { get; init; } = new();
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    public sealed class CashShopDatabaseImportService
    {
        private const string TableName = "[dmo].[Asset].[CashShop]";

        public async Task<CashShopDatabaseImportSummary> ImportAsync(
            string connectionString,
            string cashShopRoot,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            string logDir = Path.Combine(AppPaths.Logs, "DatabaseImports");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "cashshop_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");

            void Log(string message)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logFile, line + Environment.NewLine);
                progress?.Report(line);
            }

            Log("CASH SHOP -> DATABASE started.");
            Log("Canonical folders only: TamerInfo, DigimonInfo, AvatarInfo, PackageInfo. Numbered duplicate trees are ignored.");
            Log("Confirmed legacy mapping: ONE Asset.CashShop row per CASHINFO purchase option; only the FIRST valid CashItems/Item is mirrored in the DB.");

            List<CashShopXmlDbRow> xmlRows = await Task.Run(
                () => CashShopDatabaseXmlReader.Load(cashShopRoot, cancellationToken), cancellationToken);
            (int containers, int optionsCount) = CashShopDatabaseXmlReader.CountStructure(cashShopRoot);
            Log($"Prepared DB-shaped rows: {xmlRows.Count:N0}; containers={containers:N0}; CASHINFO options={optionsCount:N0}.");

            // Every importable CASHINFO should produce exactly one DB row.
            // A difference is allowed only for malformed/no-item options, and is explicitly logged.
            if (xmlRows.Count != optionsCount)
                Log($"WARNING: {optionsCount - xmlRows.Count:N0} CASHINFO option(s) have no valid first CashItems/Item and will not produce a DB row.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            Log("SQL connection established.");

            await ValidateSchemaAsync(connection, cancellationToken);
            bool idIsIdentity = await IsIdentityAsync(connection, cancellationToken);
            bool activatedIsBit = await ActivatedIsBitAsync(connection, cancellationToken);
            Log($"Schema OK. Id identity={idIsIdentity}; Activated bit={activatedIsBit}.");

            List<CashShopDbRow> existing = await CashShopDatabaseDiagnosticService.ReadDatabaseAsync(connection, cancellationToken);
            CashShopDatabaseMapping mapping = CashShopDatabaseMappingDetector.Detect(xmlRows, existing);

            if (mapping.ComparedRows >= 25)
            {
                Log($"Mapping verified against current working DB ({mapping.ComparedRows:N0} matched rows):");
                Log($"  Quanty    <- {mapping.QuantitySource} ({mapping.QuantityMatchPercent:0.00}%)");
                Log($"  Price     <- {mapping.PriceSource} ({mapping.PriceMatchPercent:0.00}%)");
                Log($"  Activated <- {mapping.ActivatedSource} ({mapping.ActivatedMatchPercent:0.00}%)");
                Log($"  ItemName  <- {mapping.ItemNameSource} ({mapping.ItemNameMatchPercent:0.00}%)");
            }
            else
            {
                Log("Too few current DB matches for inference. Using the confirmed legacy mapping: first Amount / real price / Enabled / Name without apostrophe.");
                mapping = new CashShopDatabaseMapping();
            }

            if (mapping.ComparedRows >= 25 &&
                (mapping.QuantityMatchPercent < 99.0 ||
                 mapping.PriceMatchPercent < 99.0 ||
                 mapping.ActivatedMatchPercent < 99.0 ||
                 mapping.ItemNameMatchPercent < 99.0))
            {
                throw new InvalidDataException(
                    "Cash Shop mapping no longer matches the known-good database closely enough for a destructive import. " +
                    "Run COMPARE DB and inspect HIGH_SIGNAL_REPORT.txt. " +
                    $"Quantity={mapping.QuantityMatchPercent:0.00}%, Price={mapping.PriceMatchPercent:0.00}%, " +
                    $"Activated={mapping.ActivatedMatchPercent:0.00}%, ItemName={mapping.ItemNameMatchPercent:0.00}%.");
            }

            DataTable table = BuildTable(xmlRows, mapping, idIsIdentity, activatedIsBit);
            Log($"Validated DataTable: {table.Rows.Count:N0} rows.");

            await using (var xa = new SqlCommand("SET XACT_ABORT ON;", connection))
                await xa.ExecuteNonQueryAsync(cancellationToken);

            await using SqlTransaction tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                Log("Transaction started. Clearing Asset.CashShop...");
                await using (var clear = new SqlCommand($"DELETE FROM {TableName};", connection, tx))
                {
                    clear.CommandTimeout = 120;
                    await clear.ExecuteNonQueryAsync(cancellationToken);
                }

                if (idIsIdentity)
                {
                    await using var reseed = new SqlCommand("DBCC CHECKIDENT ('dmo.Asset.CashShop', RESEED, 0);", connection, tx);
                    reseed.CommandTimeout = 60;
                    await reseed.ExecuteNonQueryAsync(cancellationToken);
                    Log("Identity reseeded to 0.");
                }

                SqlBulkCopyOptions options = SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.KeepNulls;
                if (!idIsIdentity) options |= SqlBulkCopyOptions.KeepIdentity;

                using var bulk = new SqlBulkCopy(connection, options, tx)
                {
                    DestinationTableName = TableName,
                    BatchSize = 1000,
                    BulkCopyTimeout = 180,
                    EnableStreaming = true
                };
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table, cancellationToken);

                await using (var verify = new SqlCommand($"SELECT COUNT_BIG(*) FROM {TableName};", connection, tx))
                {
                    long actual = Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                    if (actual != table.Rows.Count)
                        throw new InvalidOperationException($"CashShop row count mismatch. Expected={table.Rows.Count:N0}, actual={actual:N0}.");
                }

                await tx.CommitAsync(cancellationToken);
                sw.Stop();
                Log($"COMMIT OK. Asset.CashShop={table.Rows.Count:N0} rows in {sw.Elapsed.TotalSeconds:N1}s.");
                return new CashShopDatabaseImportSummary { Rows=table.Rows.Count, Mapping=mapping, Elapsed=sw.Elapsed, LogFile=logFile };
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    Log("ROLLBACK OK. Database restored to the state before Cash Shop import.");
                }
                catch (Exception rollbackEx) { Log("ROLLBACK ERROR: " + rollbackEx.Message); }
                throw;
            }
        }

        private static DataTable BuildTable(IReadOnlyList<CashShopXmlDbRow> rows, CashShopDatabaseMapping mapping, bool idIsIdentity, bool activatedIsBit)
        {
            var table = new DataTable();
            if (!idIsIdentity) table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Unique_Id", typeof(long));
            table.Columns.Add("Item_Id", typeof(int));
            table.Columns.Add("Quanty", typeof(int));
            table.Columns.Add("Price", typeof(int));
            table.Columns.Add("Activated", activatedIsBit ? typeof(bool) : typeof(int));
            table.Columns.Add("ItemName", typeof(string));
            table.Columns.Add("CashShopId", typeof(int));

            foreach (CashShopXmlDbRow row in rows)
            {
                int quantity = mapping.QuantitySource == "nDispCount" ? row.DisplayCount : row.Amount;
                if (quantity <= 0) quantity = row.Amount > 0 ? row.Amount : 1;
                int price = mapping.PriceSource == "nStandardSellingPrice" ? row.StandardPrice : row.RealPrice;
                int activated = mapping.ActivatedSource == "bActive" ? row.BActive : row.Enabled;
                string itemName = mapping.ItemNameSource switch
                {
                    "CashName" => row.CashName,
                    "ItemList.Name" => row.ItemListName,
                    _ => row.Name
                };
                object activeValue = activatedIsBit ? activated != 0 : activated;

                if (idIsIdentity)
                    table.Rows.Add(row.UniqueId, row.ItemId, quantity, price, activeValue, itemName, row.CashShopId);
                else
                    table.Rows.Add(row.PhysicalId, row.UniqueId, row.ItemId, quantity, price, activeValue, itemName, row.CashShopId);
            }
            return table;
        }

        private static async Task ValidateSchemaAsync(SqlConnection connection, CancellationToken token)
        {
            const string sql = "SELECT TOP (0) [Id],[Unique_Id],[Item_Id],[Quanty],[Price],[Activated],[ItemName],[CashShopId] FROM [dmo].[Asset].[CashShop];";
            await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync(token);
        }

        private static async Task<bool> IsIdentityAsync(SqlConnection connection, CancellationToken token)
        {
            const string sql = "SELECT CONVERT(int, COLUMNPROPERTY(OBJECT_ID(N'dmo.Asset.CashShop'), N'Id', 'IsIdentity'));";
            await using var cmd = new SqlCommand(sql, connection);
            object? value = await cmd.ExecuteScalarAsync(token);
            return value != null && value != DBNull.Value && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
        }

        private static async Task<bool> ActivatedIsBitAsync(SqlConnection connection, CancellationToken token)
        {
            const string sql = "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='Asset' AND TABLE_NAME='CashShop' AND COLUMN_NAME='Activated';";
            await using var cmd = new SqlCommand(sql, connection);
            string type = Convert.ToString(await cmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) ?? string.Empty;
            return type.Equals("bit", StringComparison.OrdinalIgnoreCase);
        }
    }
}
