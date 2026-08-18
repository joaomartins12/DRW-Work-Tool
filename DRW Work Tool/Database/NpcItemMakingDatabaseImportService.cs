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
    public sealed class ItemMakingDatabaseImportSummary
    {
        public int CraftRows { get; init; }
        public int MaterialRows { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    public sealed class NpcDatabaseImportSummary
    {
        public int NpcRows { get; init; }
        public int NpcItemRows { get; init; }
        public int PortalRows { get; init; }
        public int PortalAmountRows { get; init; }
        public int PortalRequirementRows { get; init; }
        public int ColiseumRows { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    public sealed class NpcItemMakingDatabaseImportService
    {
        private const string ItemCraftTable = "[dmo].[Asset].[ItemCraft]";
        private const string ItemCraftMaterialTable = "[dmo].[Asset].[ItemCraftMaterial]";

        private const string NpcTable = "[dmo].[Asset].[Npc]";
        private const string NpcItemTable = "[dmo].[Asset].[NpcItem]";
        private const string NpcPortalTable = "[dmo].[Asset].[NpcPortal]";
        private const string NpcPortalsAmountTable = "[dmo].[Asset].[NpcPortalsAmount]";
        private const string NpcPortalsTable = "[dmo].[Asset].[NpcPortals]";
        private const string NpcColiseumTable = "[dmo].[Asset].[NpcColiseum]";

        public static string ImportLogFolder =>
            Path.Combine(AppPaths.Logs, "ImportToDatabase");

        public async Task TestConnectionAsync(
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand("SELECT 1;", connection)
            {
                CommandTimeout = 15
            };

            _ = await command.ExecuteScalarAsync(cancellationToken);
        }

        public async Task<ItemMakingDatabaseImportSummary> ImportItemMakingAsync(
            string connectionString,
            string itemMakingXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            Directory.CreateDirectory(ImportLogFolder);

            string logPath = Path.Combine(
                ImportLogFolder,
                $"ItemMaking_{started:yyyy-MM-dd_HH-mm-ss}.log");

            void Log(string message)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logPath, line + Environment.NewLine);
                progress?.Report(line);
            }

            if (!File.Exists(itemMakingXml))
                throw new FileNotFoundException("ItemMaking.xml não foi encontrado.", itemMakingXml);

            Log("ITEMMAKING -> DATABASE iniciado.");
            Log("A validar ligação SQL...");
            await TestConnectionAsync(connectionString, cancellationToken);
            Log("Ligação SQL OK.");

            XDocument document = await Task.Run(
                () => XDocument.Load(itemMakingXml, LoadOptions.PreserveWhitespace),
                cancellationToken);

            var crafts = new List<CraftRow>();
            var materials = new List<CraftMaterialRow>();

            foreach (XElement npc in document.Descendants("NPC"))
            {
                int npcId = ReadInt(npc, "m_dwNpcIdx");

                foreach (XElement craft in npc.Descendants("itemMake"))
                {
                    int uniqueId = ReadInt(craft, "m_nUniqueIdx");
                    int rawRate = ReadInt(craft, "m_nProbabilityofSuccess");

                    if (uniqueId <= 0)
                        throw new InvalidDataException("ItemMaking contém m_nUniqueIdx <= 0.");

                    if (rawRate < 0 || rawRate > 10000)
                        throw new InvalidDataException(
                            $"Craft {uniqueId}: m_nProbabilityofSuccess={rawRate} fora de 0..10000.");

                    crafts.Add(new CraftRow
                    {
                        Id = uniqueId,
                        SequencialId = uniqueId,
                        ItemId = ReadInt(craft, "m_dwItemIdx"),
                        NpcId = npcId,
                        SuccessRate = rawRate / 100,
                        Price = ReadInt(craft, "Valor"),
                        Amount = ReadInt(craft, "m_nItemNum")
                    });

                    foreach (XElement material in
                        craft.Element("index")?.Elements("MaterialList")
                        ?? Enumerable.Empty<XElement>())
                    {
                        materials.Add(new CraftMaterialRow
                        {
                            ItemId = ReadInt(material, "m_dwItemIdx"),
                            Amount = ReadInt(material, "m_nItemNum"),
                            ItemCraftId = uniqueId
                        });
                    }

                    int declared = ReadInt(craft, "m_dwItemCost");
                    int actual = craft.Element("index")?.Elements("MaterialList").Count() ?? 0;
                    if (declared != actual)
                    {
                        throw new InvalidDataException(
                            $"Craft {uniqueId}: m_dwItemCost={declared}, mas existem {actual} MaterialList.");
                    }
                }
            }

            if (crafts.GroupBy(x => x.Id).Any(g => g.Count() > 1))
                throw new InvalidDataException("Existem m_nUniqueIdx duplicados em ItemMaking.xml.");

            Log($"XML preparado: {crafts.Count:N0} crafts | {materials.Count:N0} materials.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction tx =
                (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                Log("A limpar ItemCraftMaterial e ItemCraft...");
                await ExecuteAsync(connection, tx,
                    $"DELETE FROM {ItemCraftMaterialTable}; " +
                    $"DELETE FROM {ItemCraftTable}; " +
                    $"DBCC CHECKIDENT ('dmo.Asset.ItemCraftMaterial', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.ItemCraft', RESEED, 0);",
                    cancellationToken);

                Log("A importar ItemCraft...");
                await BulkInsertCraftsAsync(connection, tx, crafts, cancellationToken);

                Log("A importar ItemCraftMaterial...");
                await BulkInsertMaterialsAsync(connection, tx, materials, cancellationToken);

                await tx.CommitAsync(cancellationToken);

                TimeSpan elapsed = DateTime.Now - started;
                Log($"SUCESSO. ItemCraft={crafts.Count:N0}, ItemCraftMaterial={materials.Count:N0}, Tempo={elapsed}.");

                return new ItemMakingDatabaseImportSummary
                {
                    CraftRows = crafts.Count,
                    MaterialRows = materials.Count,
                    Elapsed = elapsed,
                    LogFile = logPath
                };
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                Log("ERRO: transação revertida por ROLLBACK.");
                throw;
            }
        }

        public async Task<NpcDatabaseImportSummary> ImportNpcAsync(
            string connectionString,
            string npcXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            Directory.CreateDirectory(ImportLogFolder);

            string logPath = Path.Combine(
                ImportLogFolder,
                $"Npc_{started:yyyy-MM-dd_HH-mm-ss}.log");

            void Log(string message)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logPath, line + Environment.NewLine);
                progress?.Report(line);
            }

            if (!File.Exists(npcXml))
                throw new FileNotFoundException("Npc.xml não foi encontrado.", npcXml);

            Log("NPC -> DATABASE iniciado.");
            Log("A validar ligação SQL...");
            await TestConnectionAsync(connectionString, cancellationToken);
            Log("Ligação SQL OK.");

            XDocument document = await Task.Run(
                () => XDocument.Load(npcXml, LoadOptions.PreserveWhitespace),
                cancellationToken);

            List<XElement> allNpcs = document.Root?.Elements("NPC").ToList()
                ?? throw new InvalidDataException("Npc.xml sem root NPCs.");

            // A tabela Asset.Npc representa apenas NPCs com loja/item ou portal.
            List<XElement> assetNpcs = allNpcs
                .Where(x =>
                    x.Element("ItemIDs")?.Elements("ItemID").Any() == true ||
                    x.Element("Portals")?.Elements("Portal").Any() == true)
                .ToList();

            List<XElement> coliseumNpcs = allNpcs
                .Where(x => ReadInt(x, "NPCType") == 22)
                .ToList();

            int expectedItems = assetNpcs.Sum(x =>
                x.Element("ItemIDs")?.Elements("ItemID").Count() ?? 0);

            int expectedPortals = assetNpcs.Sum(x =>
                x.Element("Portals")?.Elements("Portal").Count() ?? 0);

            int expectedPortalTypes = assetNpcs.Sum(x =>
                x.Descendants("PortalType").Count());

            int expectedRequirements = assetNpcs.Sum(x =>
                x.Descendants("ReqItem").Count());

            Log(
                $"XML mapping: Asset.Npc={assetNpcs.Count:N0}, " +
                $"NpcItem={expectedItems:N0}, NpcPortal={expectedPortals:N0}, " +
                $"NpcPortalsAmount={expectedPortalTypes:N0}, " +
                $"NpcPortals={expectedRequirements:N0}, Coliseum={coliseumNpcs.Count:N0}.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction tx =
                (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            int npcItemRows = 0;
            int portalRows = 0;
            int amountRows = 0;
            int requirementRows = 0;

            try
            {
                Log("A limpar tabelas NPC pela ordem das foreign keys...");
                await ExecuteAsync(connection, tx,
                    $"DELETE FROM {NpcPortalsTable}; " +
                    $"DELETE FROM {NpcPortalsAmountTable}; " +
                    $"DELETE FROM {NpcPortalTable}; " +
                    $"DELETE FROM {NpcItemTable}; " +
                    $"DELETE FROM {NpcColiseumTable}; " +
                    $"DELETE FROM {NpcTable}; " +
                    $"DBCC CHECKIDENT ('dmo.Asset.NpcPortals', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.NpcPortalsAmount', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.NpcPortal', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.NpcItem', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.NpcColiseum', RESEED, 0); " +
                    $"DBCC CHECKIDENT ('dmo.Asset.Npc', RESEED, 0);",
                    cancellationToken);

                Log("A importar Asset.Npc e relações...");

                foreach (XElement npc in assetNpcs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int npcId = ReadInt(npc, "NpcID");
                    int mapId = ReadInt(npc, "MapID");

                    int npcAssetId = await InsertAndReturnIdAsync(
                        connection, tx,
                        $"INSERT INTO {NpcTable} ([NpcId],[MapId]) OUTPUT INSERTED.[Id] VALUES (@NpcId,@MapId);",
                        cancellationToken,
                        ("@NpcId", npcId),
                        ("@MapId", mapId));

                    foreach (XElement item in
                        npc.Element("ItemIDs")?.Elements("ItemID")
                        ?? Enumerable.Empty<XElement>())
                    {
                        await ExecuteInsertAsync(
                            connection, tx,
                            $"INSERT INTO {NpcItemTable} ([ItemId],[NpcAssetId]) VALUES (@ItemId,@NpcAssetId);",
                            cancellationToken,
                            ("@ItemId", ParseInt(item.Value)),
                            ("@NpcAssetId", npcAssetId));
                        npcItemRows++;
                    }

                    foreach (XElement portal in
                        npc.Element("Portals")?.Elements("Portal")
                        ?? Enumerable.Empty<XElement>())
                    {
                        int portalType = ReadInt(portal, "s_nPortalType");
                        List<XElement> portalTypes =
                            portal.Element("PortalsType")?.Elements("PortalType").ToList()
                            ?? new List<XElement>();

                        int declaredCount = ReadInt(portal, "s_nPortalCount");
                        if (declaredCount != portalTypes.Count)
                        {
                            throw new InvalidDataException(
                                $"NpcID {npcId}: s_nPortalCount={declaredCount}, " +
                                $"mas existem {portalTypes.Count} PortalType.");
                        }

                        int npcPortalId = await InsertAndReturnIdAsync(
                            connection, tx,
                            $"INSERT INTO {NpcPortalTable} ([PortalType],[PortalCount],[NpcAssetId]) " +
                            $"OUTPUT INSERTED.[Id] VALUES (@PortalType,@PortalCount,@NpcAssetId);",
                            cancellationToken,
                            ("@PortalType", portalType),
                            ("@PortalCount", declaredCount),
                            ("@NpcAssetId", npcAssetId));

                        portalRows++;

                        foreach (XElement portalTypeNode in portalTypes)
                        {
                            int portalAmountId = await InsertAndReturnIdAsync(
                                connection, tx,
                                $"INSERT INTO {NpcPortalsAmountTable} ([NpcAssetId]) " +
                                $"OUTPUT INSERTED.[Id] VALUES (@NpcAssetId);",
                                cancellationToken,
                                ("@NpcAssetId", npcPortalId));

                            amountRows++;

                            List<XElement> reqs =
                                portalTypeNode.Element("Req")?.Elements("ReqItem").ToList()
                                ?? new List<XElement>();

                            if (reqs.Count != 3)
                            {
                                throw new InvalidDataException(
                                    $"NpcID {npcId}: cada PortalType deve conter exatamente 3 ReqItem. Encontrado={reqs.Count}.");
                            }

                            foreach (XElement req in reqs)
                            {
                                await ExecuteInsertAsync(
                                    connection, tx,
                                    $"INSERT INTO {NpcPortalsTable} " +
                                    $"([Type],[ResourceAmount],[NpcAssetId],[ItemId]) " +
                                    $"VALUES (@Type,@ResourceAmount,@NpcAssetId,@ItemId);",
                                    cancellationToken,
                                    ("@Type", ReadInt(req, "s_eEnableType")),
                                    ("@ResourceAmount", ReadInt(req, "s_nEnableCount")),
                                    ("@NpcAssetId", portalAmountId),
                                    ("@ItemId", ReadInt(req, "s_nEnableID")));

                                requirementRows++;
                            }
                        }
                    }
                }

                Log("A importar NpcColiseum (NPCType 22)...");
                foreach (XElement npc in coliseumNpcs)
                {
                    await ExecuteInsertAsync(
                        connection, tx,
                        $"INSERT INTO {NpcColiseumTable} ([NpcId]) VALUES (@NpcId);",
                        cancellationToken,
                        ("@NpcId", ReadInt(npc, "NpcID")));
                }

                if (npcItemRows != expectedItems ||
                    portalRows != expectedPortals ||
                    amountRows != expectedPortalTypes ||
                    requirementRows != expectedRequirements)
                {
                    throw new InvalidDataException(
                        "Contagens geradas não correspondem ao XML. " +
                        $"NpcItem {npcItemRows}/{expectedItems}, " +
                        $"NpcPortal {portalRows}/{expectedPortals}, " +
                        $"NpcPortalsAmount {amountRows}/{expectedPortalTypes}, " +
                        $"NpcPortals {requirementRows}/{expectedRequirements}.");
                }

                await tx.CommitAsync(cancellationToken);

                TimeSpan elapsed = DateTime.Now - started;
                Log(
                    $"SUCESSO. Npc={assetNpcs.Count:N0}, NpcItem={npcItemRows:N0}, " +
                    $"NpcPortal={portalRows:N0}, NpcPortalsAmount={amountRows:N0}, " +
                    $"NpcPortals={requirementRows:N0}, NpcColiseum={coliseumNpcs.Count:N0}.");

                return new NpcDatabaseImportSummary
                {
                    NpcRows = assetNpcs.Count,
                    NpcItemRows = npcItemRows,
                    PortalRows = portalRows,
                    PortalAmountRows = amountRows,
                    PortalRequirementRows = requirementRows,
                    ColiseumRows = coliseumNpcs.Count,
                    Elapsed = elapsed,
                    LogFile = logPath
                };
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                Log("ERRO: transação revertida por ROLLBACK.");
                throw;
            }
        }

        private static async Task BulkInsertCraftsAsync(
            SqlConnection connection,
            SqlTransaction tx,
            IReadOnlyList<CraftRow> rows,
            CancellationToken cancellationToken)
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("SequencialId", typeof(int));
            table.Columns.Add("ItemId", typeof(int));
            table.Columns.Add("NpcId", typeof(int));
            table.Columns.Add("SuccessRate", typeof(int));
            table.Columns.Add("Price", typeof(int));
            table.Columns.Add("Amount", typeof(int));

            foreach (CraftRow row in rows)
                table.Rows.Add(row.Id, row.SequencialId, row.ItemId, row.NpcId,
                    row.SuccessRate, row.Price, row.Amount);

            using var bulk = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.KeepIdentity |
                SqlBulkCopyOptions.CheckConstraints,
                tx)
            {
                DestinationTableName = ItemCraftTable,
                BatchSize = 1000,
                BulkCopyTimeout = 120
            };

            foreach (DataColumn c in table.Columns)
                bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

            await bulk.WriteToServerAsync(table, cancellationToken);
        }

        private static async Task BulkInsertMaterialsAsync(
            SqlConnection connection,
            SqlTransaction tx,
            IReadOnlyList<CraftMaterialRow> rows,
            CancellationToken cancellationToken)
        {
            var table = new DataTable();
            table.Columns.Add("ItemId", typeof(int));
            table.Columns.Add("Amount", typeof(int));
            table.Columns.Add("ItemCraftId", typeof(int));

            foreach (CraftMaterialRow row in rows)
                table.Rows.Add(row.ItemId, row.Amount, row.ItemCraftId);

            using var bulk = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.CheckConstraints,
                tx)
            {
                DestinationTableName = ItemCraftMaterialTable,
                BatchSize = 2000,
                BulkCopyTimeout = 120
            };

            foreach (DataColumn c in table.Columns)
                bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

            await bulk.WriteToServerAsync(table, cancellationToken);
        }

        private static async Task<int> InsertAndReturnIdAsync(
            SqlConnection connection,
            SqlTransaction tx,
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] values)
        {
            await using var command = new SqlCommand(sql, connection, tx);
            foreach (var pair in values)
                command.Parameters.AddWithValue(pair.Name, pair.Value);

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private static async Task ExecuteInsertAsync(
            SqlConnection connection,
            SqlTransaction tx,
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] values)
        {
            await using var command = new SqlCommand(sql, connection, tx);
            foreach (var pair in values)
                command.Parameters.AddWithValue(pair.Name, pair.Value);

            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task ExecuteAsync(
            SqlConnection connection,
            SqlTransaction tx,
            string sql,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(sql, connection, tx)
            {
                CommandTimeout = 120
            };
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static int ReadInt(XElement parent, string name) =>
            ParseInt(parent.Element(name)?.Value);

        private static int ParseInt(string? value)
        {
            if (!int.TryParse(
                value?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException($"Valor inteiro inválido: '{value}'.");
            }

            return result;
        }

        private sealed class CraftRow
        {
            public int Id { get; init; }
            public int SequencialId { get; init; }
            public int ItemId { get; init; }
            public int NpcId { get; init; }
            public int SuccessRate { get; init; }
            public int Price { get; init; }
            public int Amount { get; init; }
        }

        private sealed class CraftMaterialRow
        {
            public int ItemId { get; init; }
            public int Amount { get; init; }
            public int ItemCraftId { get; init; }
        }
    }
}
