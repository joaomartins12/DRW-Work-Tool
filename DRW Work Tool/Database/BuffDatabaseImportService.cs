using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
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
    public sealed class BuffDatabaseImportSummary
    {
        public int BuffRows { get; init; }
        public int DuplicateBuffIds { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    /// <summary>
    /// Imports Buff.xml into dmo.Asset.Buff.
    ///
    /// Verified mapping from the supplied XML and SQL sample:
    ///   physical row 1..N       -> Id
    ///   s_dwID                  -> BuffId
    ///   s_szName                -> Name
    ///   s_dwDigimonSkillCode    -> DigimonSkillCode
    ///   s_dwSkillCode           -> SkillCode
    ///   s_nMinLv                -> MinLevel
    ///   s_nConditionLv          -> ConditionLevel
    ///   s_nBuffClass            -> Class
    ///   s_nBuffType             -> Type
    ///   s_nBuffLifeType         -> LifeType
    ///   s_nBuffTimeType         -> TimeType
    ///
    /// XML-only fields are intentionally not invented into DB columns:
    /// s_szComment, s_nBuffIcon, unknow, s_bDelete, s_szEffectFile, u.
    /// </summary>
    public sealed class BuffDatabaseImportService
    {
        public async Task<BuffDatabaseImportSummary> ImportAsync(
            string connectionString,
            string buffXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            string logDir =
                Path.Combine(
                    AppPaths.Logs,
                    "DatabaseImports");

            Directory.CreateDirectory(logDir);

            string logFile =
                Path.Combine(
                    logDir,
                    "buff_import_" +
                    DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture) +
                    ".log");

            void Log(string message)
            {
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

                File.AppendAllText(
                    logFile,
                    line + Environment.NewLine);

                progress?.Report(line);
            }

            Log("BUFF -> DATABASE iniciado.");
            Log("FASE 0/2 - validação completa de Buff.xml antes de tocar na database.");

            Prepared prepared =
                await Task.Run(
                    () => Prepare(
                        buffXml,
                        Log,
                        cancellationToken),
                    cancellationToken);

            Log(
                $"VALIDAÇÃO CONCLUÍDA: {prepared.Rows.Count:N0} Buff rows; " +
                $"BuffId duplicados físicos={prepared.DuplicateIdCount:N0}.");

            cancellationToken.ThrowIfCancellationRequested();

            await using var connection =
                new SqlConnection(
                    connectionString);

            await connection.OpenAsync(
                cancellationToken);

            Log("Ligação SQL estabelecida.");

            await ValidateSchemaAsync(
                connection,
                cancellationToken);

            Log("Schema SQL validado: dmo.Asset.Buff e colunas esperadas existem.");

            await using (var safety =
                new SqlCommand(
                    "SET XACT_ABORT ON;",
                    connection))
            {
                await safety.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using SqlTransaction transaction =
                (SqlTransaction)
                await connection.BeginTransactionAsync(
                    cancellationToken);

            Log("Transação SQL iniciada.");

            try
            {
                Log("FASE 1/2 - limpar Asset.Buff.");

                // No DBCC CHECKIDENT is required. The supplied DB sample shows
                // deterministic physical Id values, so Id is imported explicitly.
                await using (var clear =
                    new SqlCommand(
                        "DELETE FROM [dmo].[Asset].[Buff];",
                        connection,
                        transaction))
                {
                    clear.CommandTimeout = 120;
                    await clear.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                Log("Asset.Buff limpa.");

                cancellationToken.ThrowIfCancellationRequested();

                Log(
                    $"FASE 2/2 - importar {prepared.Rows.Count:N0} rows.");

                DataTable table =
                    BuildTable(
                        prepared.Rows);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    table,
                    cancellationToken);

                Log(
                    $"Buff bulk insert concluído: {prepared.Rows.Count:N0} rows.");

                await VerifyCountAsync(
                    connection,
                    transaction,
                    prepared.Rows.Count,
                    cancellationToken);

                Log("VERIFY OK - contagem Asset.Buff corresponde ao XML.");

                await transaction.CommitAsync(
                    cancellationToken);

                stopwatch.Stop();

                Log("COMMIT concluído. Importação Buff terminada com sucesso.");

                return new BuffDatabaseImportSummary
                {
                    BuffRows = prepared.Rows.Count,
                    DuplicateBuffIds = prepared.DuplicateIdCount,
                    Elapsed = stopwatch.Elapsed,
                    LogFile = logFile
                };
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);

                    Log("ROLLBACK concluído. A database voltou ao estado anterior ao import.");
                }
                catch (Exception rollbackEx)
                {
                    Log(
                        "ERRO durante ROLLBACK: " +
                        rollbackEx.Message);
                }

                throw;
            }
        }

        private static Prepared Prepare(
            string filePath,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Buff.xml não foi encontrado.",
                    filePath);
            }

            log("A carregar Buff.xml...");

            XDocument document =
                XDocument.Load(
                    filePath,
                    LoadOptions.None);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Buff.xml sem root.");

            if (!root.Name.LocalName.Equals(
                    "BuffDataArray",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Root inválido <{root.Name.LocalName}>. Esperado <BuffDataArray>.");
            }

            List<XElement> nodes =
                root.Elements("BuffData").ToList();

            if (nodes.Count == 0)
            {
                throw new InvalidDataException(
                    "Buff.xml não contém BuffData.");
            }

            var rows =
                new List<BuffRow>(
                    nodes.Count);

            int physicalId = 0;

            foreach (XElement node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                physicalId++;

                uint buffId =
                    RequiredUInt(
                        node,
                        "s_dwID",
                        physicalId);

                string name =
                    node.Element("s_szName")?.Value
                    ?? string.Empty;

                rows.Add(
                    new BuffRow
                    {
                        Id = physicalId,
                        BuffId = buffId,
                        Name = name,
                        DigimonSkillCode =
                            RequiredUInt(
                                node,
                                "s_dwDigimonSkillCode",
                                physicalId),
                        SkillCode =
                            RequiredUInt(
                                node,
                                "s_dwSkillCode",
                                physicalId),
                        MinLevel =
                            RequiredInt(
                                node,
                                "s_nMinLv",
                                physicalId),
                        ConditionLevel =
                            RequiredInt(
                                node,
                                "s_nConditionLv",
                                physicalId),
                        Class =
                            RequiredInt(
                                node,
                                "s_nBuffClass",
                                physicalId),
                        Type =
                            RequiredInt(
                                node,
                                "s_nBuffType",
                                physicalId),
                        LifeType =
                            RequiredInt(
                                node,
                                "s_nBuffLifeType",
                                physicalId),
                        TimeType =
                            RequiredInt(
                                node,
                                "s_nBuffTimeType",
                                physicalId)
                    });
            }

            int duplicates =
                rows
                    .GroupBy(x => x.BuffId)
                    .Count(x => x.Count() > 1);

            if (duplicates != 0)
            {
                foreach (IGrouping<uint, BuffRow> group in
                    rows.GroupBy(x => x.BuffId).Where(x => x.Count() > 1))
                {
                    log(
                        $"WARNING: BuffId {group.Key} aparece {group.Count()} vezes no XML. " +
                        "As ocorrências físicas serão preservadas com Id de database distintos.");
                }
            }

            log(
                "Buff mapping validado: Id=ordem física; BuffId=s_dwID; Name=s_szName; " +
                "DigimonSkillCode=s_dwDigimonSkillCode; SkillCode=s_dwSkillCode; " +
                "MinLevel=s_nMinLv; ConditionLevel=s_nConditionLv; Class=s_nBuffClass; " +
                "Type=s_nBuffType; LifeType=s_nBuffLifeType; TimeType=s_nBuffTimeType.");

            log(
                "Campos sem coluna DB equivalente preservados apenas no XML: " +
                "s_szComment, s_nBuffIcon, unknow, s_bDelete, s_szEffectFile, u.");

            return new Prepared
            {
                Rows = rows,
                DuplicateIdCount = duplicates
            };
        }

        private static async Task ValidateSchemaAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            const string sql =
                """
                SELECT TOP (0)
                    [Id],
                    [BuffId],
                    [Name],
                    [DigimonSkillCode],
                    [SkillCode],
                    [MinLevel],
                    [ConditionLevel],
                    [Class],
                    [Type],
                    [LifeType],
                    [TimeType]
                FROM [dmo].[Asset].[Buff];
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 60
                };

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        private static DataTable BuildTable(
            IReadOnlyList<BuffRow> rows)
        {
            var table = new DataTable();

            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("BuffId", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("DigimonSkillCode", typeof(int));
            table.Columns.Add("SkillCode", typeof(int));
            table.Columns.Add("MinLevel", typeof(int));
            table.Columns.Add("ConditionLevel", typeof(int));
            table.Columns.Add("Class", typeof(int));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("LifeType", typeof(int));
            table.Columns.Add("TimeType", typeof(int));

            foreach (BuffRow row in rows)
            {
                table.Rows.Add(
                    row.Id,
                    checked((int)row.BuffId),
                    row.Name,
                    checked((int)row.DigimonSkillCode),
                    checked((int)row.SkillCode),
                    row.MinLevel,
                    row.ConditionLevel,
                    row.Class,
                    row.Type,
                    row.LifeType,
                    row.TimeType);
            }

            return table;
        }

        private static async Task BulkInsertAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            DataTable table,
            CancellationToken cancellationToken)
        {
            using var bulk =
                new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.KeepIdentity |
                    SqlBulkCopyOptions.CheckConstraints |
                    SqlBulkCopyOptions.KeepNulls,
                    transaction)
                {
                    DestinationTableName =
                        "[dmo].[Asset].[Buff]",
                    BatchSize = 500,
                    BulkCopyTimeout = 180
                };

            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(
                    column.ColumnName,
                    column.ColumnName);
            }

            await bulk.WriteToServerAsync(
                table,
                cancellationToken);
        }

        private static async Task VerifyCountAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int expected,
            CancellationToken cancellationToken)
        {
            await using var command =
                new SqlCommand(
                    "SELECT COUNT_BIG(*) FROM [dmo].[Asset].[Buff];",
                    connection,
                    transaction);

            object? result =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            long actual =
                Convert.ToInt64(
                    result,
                    CultureInfo.InvariantCulture);

            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"Asset.Buff count inválido após import. Esperado={expected:N0}, atual={actual:N0}.");
            }
        }

        private static uint RequiredUInt(
            XElement node,
            string field,
            int row)
        {
            string? raw =
                node.Element(field)?.Value;

            if (!uint.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint value))
            {
                throw new InvalidDataException(
                    $"BuffData row {row}: <{field}> inválido: '{raw}'.");
            }

            return value;
        }

        private static int RequiredInt(
            XElement node,
            string field,
            int row)
        {
            string? raw =
                node.Element(field)?.Value;

            if (!int.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw new InvalidDataException(
                    $"BuffData row {row}: <{field}> inválido: '{raw}'.");
            }

            return value;
        }

        private sealed class Prepared
        {
            public List<BuffRow> Rows { get; init; } = new();
            public int DuplicateIdCount { get; init; }
        }

        private sealed class BuffRow
        {
            public int Id { get; init; }
            public uint BuffId { get; init; }
            public string Name { get; init; } = string.Empty;
            public uint DigimonSkillCode { get; init; }
            public uint SkillCode { get; init; }
            public int MinLevel { get; init; }
            public int ConditionLevel { get; init; }
            public int Class { get; init; }
            public int Type { get; init; }
            public int LifeType { get; init; }
            public int TimeType { get; init; }
        }
    }
}
