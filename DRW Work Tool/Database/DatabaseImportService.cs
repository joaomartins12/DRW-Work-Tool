using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class DatabaseImportSummary
    {
        public int ItemInfoRows { get; init; }
        public int AccessoryRollRows { get; init; }
        public int AccessoryStatusRows { get; init; }
        public int SkippedAccessoryDefinitions { get; init; }
        public int DuplicateAccessoryDefinitions { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    internal sealed class ItemInfoImportRow
    {
        public int ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Class { get; init; }
        public int Type { get; init; }
        public int Section { get; init; }
        public int SellType { get; init; }
        public int BoundType { get; init; }
        public int UseTimeType { get; init; }
        public int SkillCode { get; init; }
        public int TamerMinLevel { get; init; }
        public int TamerMaxLevel { get; init; }
        public int DigimonMinLevel { get; init; }
        public int DigimonMaxLevel { get; init; }
        public int SellPrice { get; init; }
        public int ScanPrice { get; init; }
        public int DigicorePrice { get; init; }
        public int UsageTimeMinutes { get; init; }
        public int Overlap { get; init; }
        public int Target { get; init; }
        public int EventPriceAmount { get; init; }
        public int EventPriceId { get; init; }
        public int TypeN { get; init; }
        public int ApplyValueMax { get; init; }
        public int ApplyValueMin { get; init; }
        public int ApplyElement { get; init; }
    }

    internal sealed class AccessoryImportRow
    {
        public required ItemAccessoryRecord Definition { get; init; }
        public required ItemAccessoryLinkedItem Item { get; init; }
    }

    public sealed class DatabaseImportService
    {
        private const string ItemInfoTable =
            "[dmo].[Asset].[ItemInfo]";

        private const string AccessoryRollTable =
            "[dmo].[Asset].[AccessoryRoll]";

        private const string AccessoryRollStatusTable =
            "[dmo].[Asset].[AccessoryRollStatus]";

        public static string ImportLogFolder =>
            Path.Combine(
                AppPaths.Logs,
                "ImportToDatabase");

        public async Task TestConnectionAsync(
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            await using var connection =
                new SqlConnection(
                    connectionString);

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                new SqlCommand(
                    "SELECT 1;",
                    connection);

            command.CommandTimeout = 15;

            _ =
                await command.ExecuteScalarAsync(
                    cancellationToken);
        }

        public async Task<DatabaseImportSummary> ImportAllAsync(
            string connectionString,
            string itemListXml,
            string itemAccessoryXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started =
                DateTime.Now;

            Directory.CreateDirectory(
                ImportLogFolder);

            string logPath =
                Path.Combine(
                    ImportLogFolder,
                    $"Import_{started:yyyy-MM-dd_HH-mm-ss}.log");

            void Log(
                string message)
            {
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

                File.AppendAllText(
                    logPath,
                    line + Environment.NewLine);

                progress?.Report(line);
            }

            Log(
                "IMPORT TO DATABASE iniciado.");

            Log(
                "A validar connection string e ligação ao SQL Server...");

            await TestConnectionAsync(
                connectionString,
                cancellationToken);

            Log(
                "Ligação SQL estabelecida com sucesso.");

            if (!File.Exists(
                itemListXml))
            {
                throw new FileNotFoundException(
                    "ItemList.xml não foi encontrado.",
                    itemListXml);
            }

            if (!File.Exists(
                itemAccessoryXml))
            {
                throw new FileNotFoundException(
                    "ItemAcessorys.xml não foi encontrado.",
                    itemAccessoryXml);
            }

            Log(
                $"A carregar ItemList.xml: {itemListXml}");

            List<ItemInfoImportRow> items =
                await Task.Run(
                    () => ReadItemInfoRows(
                        itemListXml),
                    cancellationToken);

            Log(
                $"ItemList.xml carregado: {items.Count:N0} ItemInfo rows.");

            Log(
                $"A carregar ItemAcessorys.xml: {itemAccessoryXml}");

            var accessoryService =
                new ItemAccessoryEditorService();

            accessoryService.Load(
                itemAccessoryXml,
                itemListXml);

            List<AccessoryImportRow> accessoryRows =
                BuildAccessoryRows(
                    accessoryService,
                    Log,
                    out int skippedDefinitions,
                    out int duplicateDefinitions);

            Log(
                $"Accessory mapping preparado: {accessoryRows.Count:N0} AccessoryRoll rows.");

            await using var connection =
                new SqlConnection(
                    connectionString);

            await connection.OpenAsync(
                cancellationToken);

            await using SqlTransaction transaction =
                (SqlTransaction)
                await connection.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                Log(
                    "Transação SQL iniciada.");

                Log(
                    "A limpar tabelas atuais...");

                await ClearTablesAsync(
                    connection,
                    transaction,
                    Log,
                    cancellationToken);

                Log(
                    "Tabelas limpas.");

                Log(
                    $"A importar {items.Count:N0} rows para {ItemInfoTable}...");

                await BulkInsertItemInfoAsync(
                    connection,
                    transaction,
                    items,
                    cancellationToken);

                Log(
                    $"ItemInfo concluído: {items.Count:N0} rows.");

                int accessoryRollCount = 0;
                int statusCount = 0;

                if (accessoryRows.Count > 0)
                {
                    Log(
                        $"A importar AccessoryRoll + AccessoryRollStatus para {accessoryRows.Count:N0} ItemIDs...");

                    (accessoryRollCount, statusCount) =
                        await InsertAccessoriesAsync(
                            connection,
                            transaction,
                            accessoryRows,
                            Log,
                            cancellationToken);
                }

                await transaction.CommitAsync(
                    cancellationToken);

                TimeSpan elapsed =
                    DateTime.Now - started;

                Log(
                    "COMMIT concluído com sucesso.");

                Log(
                    $"RESUMO: ItemInfo={items.Count:N0}, " +
                    $"AccessoryRoll={accessoryRollCount:N0}, " +
                    $"AccessoryRollStatus={statusCount:N0}, " +
                    $"Accessory definitions skipped={skippedDefinitions:N0}, " +
                    $"duplicate definitions resolved={duplicateDefinitions:N0}, " +
                    $"tempo={elapsed.TotalSeconds:N1}s.");

                return new DatabaseImportSummary
                {
                    ItemInfoRows = items.Count,
                    AccessoryRollRows = accessoryRollCount,
                    AccessoryStatusRows = statusCount,
                    SkippedAccessoryDefinitions = skippedDefinitions,
                    DuplicateAccessoryDefinitions = duplicateDefinitions,
                    Elapsed = elapsed,
                    LogFile = logPath
                };
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    Log(
                        "ROLLBACK executado. A database não ficou parcialmente importada.");
                }
                catch (Exception rollbackEx)
                {
                    Log(
                        "ERRO durante ROLLBACK: " +
                        rollbackEx.Message);
                }

                Log(
                    "IMPORT FALHOU: " +
                    ex);

                throw;
            }
        }

        private static List<ItemInfoImportRow> ReadItemInfoRows(
            string itemListXml)
        {
            XDocument document =
                XDocument.Load(
                    itemListXml,
                    LoadOptions.None);

            XElement root =
                document.Root
                ?? throw new InvalidDataException(
                    "ItemList.xml não possui root.");

            IEnumerable<XElement> rows =
                root.Descendants("sINFO");

            var result =
                new List<ItemInfoImportRow>();

            foreach (XElement item in rows)
            {
                result.Add(
                    new ItemInfoImportRow
                    {
                        ItemId =
                            ReadInt(item, "s_dwItemID"),
                        Name =
                            item.Element("s_szName")?.Value
                            ?? string.Empty,
                        Class =
                            ReadInt(item, "s_nClass"),
                        Type =
                            ReadInt(item, "s_nType_L"),
                        Section =
                            ReadInt(item, "s_nSection"),
                        SellType =
                            ReadInt(item, "s_nSellType"),
                        BoundType =
                            ReadInt(item, "s_nBelonging"),
                        UseTimeType =
                            ReadInt(item, "s_btUseTimeType"),
                        SkillCode =
                            ReadInt(item, "s_dwSkill"),
                        TamerMinLevel =
                            ReadInt(item, "s_nTamerReqMinLevel"),
                        TamerMaxLevel =
                            ReadInt(item, "s_nTamerReqMaxLevel"),
                        DigimonMinLevel =
                            ReadInt(item, "s_nDigimonReqMinLevel"),
                        DigimonMaxLevel =
                            ReadInt(item, "s_nDigimonReqMaxLevel"),
                        SellPrice =
                            ReadInt(item, "s_dwSale"),
                        ScanPrice =
                            ReadInt(item, "s_dwScanPrice"),
                        DigicorePrice =
                            ReadInt(item, "s_dwDigiCorePrice"),
                        UsageTimeMinutes =
                            ReadInt(item, "s_nUseTime_Min"),
                        Overlap =
                            ReadInt(item, "s_nOverlap"),
                        Target =
                            ReadInt(item, "s_nUseCharacter"),
                        EventPriceAmount =
                            ReadInt(item, "s_dwEventItemPrice"),
                        EventPriceId =
                            ReadInt(item, "s_nEventItemType"),
                        TypeN =
                            ReadInt(item, "s_nTypeValue"),
                        ApplyValueMax =
                            ReadInt(item, "s_btApplyRateMax"),
                        ApplyValueMin =
                            ReadInt(item, "s_btApplyRateMin"),
                        ApplyElement =
                            ReadInt(item, "s_btApplyElement")
                    });
            }

            return result;
        }

        private static List<AccessoryImportRow> BuildAccessoryRows(
            ItemAccessoryEditorService service,
            Action<string> log,
            out int skippedDefinitions,
            out int duplicateDefinitions)
        {
            skippedDefinitions = 0;
            duplicateDefinitions = 0;

            var result =
                new List<AccessoryImportRow>();

            // ItemAcessorys.xml can contain the same Accessory ID more than once.
            // ItemList only carries one numeric reference (s_dwSkill), therefore
            // there is no second discriminator that tells us which duplicate
            // definition belongs to an ItemID.
            //
            // To avoid duplicating AccessoryRoll rows for one ItemID, use the
            // FIRST physical definition for each Accessory ID, which matches the
            // client/export order and gives deterministic behavior.
            foreach (IGrouping<uint, ItemAccessoryRecord> group
                     in service.Records
                         .GroupBy(x => x.AccessoryId))
            {
                ItemAccessoryRecord definition =
                    group.First();

                if (group.Count() > 1)
                {
                    duplicateDefinitions +=
                        group.Count() - 1;

                    log(
                        $"WARNING: Accessory ID {group.Key} possui {group.Count()} definitions no XML. " +
                        "A database só possui ItemId como chave funcional; será usada a primeira definition física.");
                }

                List<ItemAccessoryLinkedItem> linkedItems =
                    definition.LinkedItems
                        .Where(x => x.SkillCodeType == 2)
                        .GroupBy(x => x.ItemId)
                        .Select(x => x.First())
                        .ToList();

                if (linkedItems.Count == 0)
                {
                    skippedDefinitions++;

                    continue;
                }

                foreach (ItemAccessoryLinkedItem item
                         in linkedItems)
                {
                    result.Add(
                        new AccessoryImportRow
                        {
                            Definition = definition,
                            Item = item
                        });
                }
            }

            return result;
        }

        private static async Task ClearTablesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            string sql =
                $"""
                DELETE FROM {AccessoryRollStatusTable};
                DELETE FROM {AccessoryRollTable};
                DELETE FROM {ItemInfoTable};

                DBCC CHECKIDENT ('dmo.Asset.AccessoryRollStatus', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.AccessoryRoll', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.ItemInfo', RESEED, 0);
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection,
                    transaction);

            command.CommandTimeout = 120;

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            log(
                "DELETE + identity reseed concluído para ItemInfo, AccessoryRoll e AccessoryRollStatus.");
        }

        private static async Task BulkInsertItemInfoAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            IReadOnlyCollection<ItemInfoImportRow> rows,
            CancellationToken cancellationToken)
        {
            DataTable table =
                CreateItemInfoDataTable();

            foreach (ItemInfoImportRow row in rows)
            {
                table.Rows.Add(
                    row.ItemId,
                    row.Name,
                    row.Class,
                    row.Type,
                    row.Section,
                    row.SellType,
                    row.BoundType,
                    row.UseTimeType,
                    row.SkillCode,
                    row.TamerMinLevel,
                    row.TamerMaxLevel,
                    row.DigimonMinLevel,
                    row.DigimonMaxLevel,
                    row.SellPrice,
                    row.ScanPrice,
                    row.DigicorePrice,
                    row.UsageTimeMinutes,
                    row.Overlap,
                    row.Target,
                    row.EventPriceAmount,
                    row.EventPriceId,
                    row.TypeN,
                    row.ApplyValueMax,
                    row.ApplyValueMin,
                    row.ApplyElement);
            }

            using var bulk =
                new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.CheckConstraints |
                    SqlBulkCopyOptions.KeepNulls,
                    transaction)
                {
                    DestinationTableName =
                        ItemInfoTable,
                    BatchSize = 2000,
                    BulkCopyTimeout = 180,
                    EnableStreaming = true
                };

            foreach (DataColumn column
                     in table.Columns)
            {
                bulk.ColumnMappings.Add(
                    column.ColumnName,
                    column.ColumnName);
            }

            await bulk.WriteToServerAsync(
                table,
                cancellationToken);
        }

        private static DataTable CreateItemInfoDataTable()
        {
            var table =
                new DataTable();

            table.Columns.Add("ItemId", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Class", typeof(int));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("Section", typeof(int));
            table.Columns.Add("SellType", typeof(int));
            table.Columns.Add("BoundType", typeof(int));
            table.Columns.Add("UseTimeType", typeof(int));
            table.Columns.Add("SkillCode", typeof(int));
            table.Columns.Add("TamerMinLevel", typeof(int));
            table.Columns.Add("TamerMaxLevel", typeof(int));
            table.Columns.Add("DigimonMinLevel", typeof(int));
            table.Columns.Add("DigimonMaxLevel", typeof(int));
            table.Columns.Add("SellPrice", typeof(int));
            table.Columns.Add("ScanPrice", typeof(int));
            table.Columns.Add("DigicorePrice", typeof(int));
            table.Columns.Add("UsageTimeMinutes", typeof(int));
            table.Columns.Add("Overlap", typeof(int));
            table.Columns.Add("Target", typeof(int));
            table.Columns.Add("EventPriceAmount", typeof(int));
            table.Columns.Add("EventPriceId", typeof(int));
            table.Columns.Add("TypeN", typeof(int));
            table.Columns.Add("ApplyValueMax", typeof(int));
            table.Columns.Add("ApplyValueMin", typeof(int));
            table.Columns.Add("ApplyElement", typeof(int));

            return table;
        }

        private static async Task<(int RollCount, int StatusCount)> InsertAccessoriesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            IReadOnlyList<AccessoryImportRow> rows,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            const string insertRoll =
                """
                INSERT INTO [dmo].[Asset].[AccessoryRoll]
                    ([ItemId], [StatusAmount], [RerollAmount])
                OUTPUT INSERTED.[Id]
                VALUES
                    (@ItemId, @StatusAmount, @RerollAmount);
                """;

            var statusRows =
                new DataTable();

            statusRows.Columns.Add("Type", typeof(int));
            statusRows.Columns.Add("MinValue", typeof(int));
            statusRows.Columns.Add("MaxValue", typeof(int));
            statusRows.Columns.Add("AccessoryRollAssetId", typeof(int));

            int rollCount = 0;

            await using var command =
                new SqlCommand(
                    insertRoll,
                    connection,
                    transaction);

            SqlParameter itemIdParam =
                command.Parameters.Add(
                    "@ItemId",
                    SqlDbType.Int);

            SqlParameter statusAmountParam =
                command.Parameters.Add(
                    "@StatusAmount",
                    SqlDbType.Int);

            SqlParameter rerollParam =
                command.Parameters.Add(
                    "@RerollAmount",
                    SqlDbType.Int);

            command.CommandTimeout = 60;

            foreach (AccessoryImportRow row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                itemIdParam.Value =
                    checked(
                        (int)row.Item.ItemId);

                statusAmountParam.Value =
                    row.Definition.GainOption;

                rerollParam.Value =
                    row.Definition.RenewalChanges;

                object? scalar =
                    await command.ExecuteScalarAsync(
                        cancellationToken);

                int accessoryRollId =
                    Convert.ToInt32(
                        scalar,
                        CultureInfo.InvariantCulture);

                rollCount++;

                // The database screenshots supplied by the user show one row
                // per UNIQUE Type even though ItemAcessorys.xml has 16 physical
                // slots and repeats the same s_nOptIdx several times.
                //
                // Preserve XML order: first occurrence wins.
                foreach (ItemAccessoryStatSlot slot in
                         row.Definition.Slots
                             .Where(x => x.StatId != 0)
                             .GroupBy(x => x.StatId)
                             .Select(x => x.First()))
                {
                    statusRows.Rows.Add(
                        slot.StatId,
                        slot.MinRaw,
                        slot.MaxRaw,
                        accessoryRollId);
                }

                if (rollCount % 250 == 0)
                {
                    log(
                        $"AccessoryRoll progress: {rollCount:N0}/{rows.Count:N0}");
                }
            }

            int statusCount =
                statusRows.Rows.Count;

            if (statusCount > 0)
            {
                using var bulk =
                    new SqlBulkCopy(
                        connection,
                        SqlBulkCopyOptions.CheckConstraints |
                        SqlBulkCopyOptions.KeepNulls,
                        transaction)
                    {
                        DestinationTableName =
                            AccessoryRollStatusTable,
                        BatchSize = 2000,
                        BulkCopyTimeout = 180,
                        EnableStreaming = true
                    };

                foreach (DataColumn column
                         in statusRows.Columns)
                {
                    bulk.ColumnMappings.Add(
                        column.ColumnName,
                        column.ColumnName);
                }

                await bulk.WriteToServerAsync(
                    statusRows,
                    cancellationToken);
            }

            log(
                $"Accessory import concluído: {rollCount:N0} rolls, {statusCount:N0} unique status rows.");

            return (
                rollCount,
                statusCount);
        }

        private static int ReadInt(
            XElement item,
            string tag)
        {
            string raw =
                item.Element(tag)?.Value?.Trim()
                ?? string.Empty;

            if (!long.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long value))
            {
                throw new InvalidDataException(
                    $"ItemList.xml: <{tag}>='{raw}' não é um inteiro válido.");
            }

            if (value < int.MinValue ||
                value > int.MaxValue)
            {
                throw new OverflowException(
                    $"ItemList.xml: <{tag}>={value} não cabe em SQL Int32.");
            }

            return (int)value;
        }
    }
}
