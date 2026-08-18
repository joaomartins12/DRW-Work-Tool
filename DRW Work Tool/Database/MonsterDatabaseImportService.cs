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
    public sealed class MonsterDatabaseImportSummary
    {
        public int MonsterBaseInfoRows { get; init; }
        public int MonsterSkillRows { get; init; }
        public int MonsterSkillInfoRows { get; init; }
        public int MissingMonsterReferences { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    /// <summary>
    /// Imports Monster.xml + MonstersSkill.xml into:
    ///
    ///   Asset.MonsterBaseInfo
    ///   Asset.MonsterSkill
    ///   Asset.MonsterSkillInfo
    ///
    /// Both XML files are parsed and validated completely BEFORE any database
    /// table is modified. All DELETE/INSERT/VERIFY work is then performed in
    /// ONE SQL transaction. Any failure/cancellation causes ROLLBACK.
    ///
    /// Confirmed XML -> DB mapping from the supplied XML + SQL samples:
    ///
    /// Monster.xml:
    ///   sequential physical row (1..N) -> MonsterBaseInfo.Id
    ///   MonsterID   -> MonsterBaseInfo.Type
    ///   ModelDigimon-> Model
    ///   Name        -> Name
    ///   Level       -> Level
    ///   HuntRange   -> ViewRange AND HuntRange
    ///   (constant)  -> ReactionType = 1
    ///   (no XML equivalents) -> Attribute/Element/Family1/2/3 = 0
    ///   AS/AR/AT/CT/DE/DS/EV/HP/HT/MS/WS -> matching *Value columns
    ///   BLValue     -> 0 (Monster.xml has no BL field)
    ///   Class       -> Class
    ///
    /// NOTE: Monster.xml also contains Sight, Scale, Battle, EXP, icons,
    /// comments/titles and other fields that do NOT exist in the three target
    /// table schemas supplied by the user; they are intentionally NOT invented
    /// into unrelated DB columns.
    ///
    /// MonstersSkill.xml:
    ///   sequential physical row (1..N) -> MonsterSkill.Id
    ///   Skill_IDX   -> MonsterSkill.SkillId
    ///   MonsterID   -> MonsterSkill.Type
    ///
    ///   sequential physical row (1..N) -> MonsterSkillInfo.Id
    ///   Skill_IDX   -> MonsterSkillInfo.SkillId
    ///   Eff_Val_Min -> MinValue
    ///   Eff_Val_Max -> MaxValue
    ///   CastTime    -> CastingTime
    ///   CoolTime    -> Cooldown
    ///   Target_Cnt  -> TargetCount
    ///   Target_MinCnt -> TargetMin
    ///   Target_MaxCnt -> TargetMax
    ///   UseTerms    -> UseTerms
    ///   RangeIDX    -> RangeId
    ///   Ani_Delay   -> AnimationDelay
    ///   Activetype  -> ActiveType
    ///   Skill_Type  -> SkillType
    ///   NoticeTime  -> NoticeTime
    ///   MonsterID   -> Type
    ///   Eff_Factor/2/3 -> EffFactor/2/3
    ///
    /// Fields with no target DB column in the supplied schema are validated as
    /// XML values but are deliberately not stored: unk, CastCheck, unk2,
    /// SequenceID, Valocity, Accel, Eff_Fact_Val/2/3, TalkID, NoticeEffname.
    /// </summary>
    public sealed class MonsterDatabaseImportService
    {
        private const string MonsterBaseInfoTable =
            "[dmo].[Asset].[MonsterBaseInfo]";

        private const string MonsterSkillTable =
            "[dmo].[Asset].[MonsterSkill]";

        private const string MonsterSkillInfoTable =
            "[dmo].[Asset].[MonsterSkillInfo]";

        public static string ImportLogFolder =>
            Path.Combine(
                AppPaths.Logs,
                "ImportToDatabase");

        public async Task<MonsterDatabaseImportSummary> ImportAsync(
            string connectionString,
            string monsterXml,
            string monsterSkillXml,
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
                    $"MonsterCore_{started:yyyy-MM-dd_HH-mm-ss}.log");

            void Log(string message)
            {
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

                File.AppendAllText(
                    logPath,
                    line + Environment.NewLine);

                progress?.Report(line);
            }

            Log("MONSTER CORE -> DATABASE iniciado.");
            Log("Ordem: Monster.xml -> MonstersSkill.xml.");
            Log("FASE 0/3 - validação completa antes de tocar na database.");

            EnsureFile(
                monsterXml,
                "Monster.xml");

            EnsureFile(
                monsterSkillXml,
                "MonstersSkill.xml");

            PreparedImport prepared =
                await Task.Run(
                    () =>
                        PrepareAndValidate(
                            monsterXml,
                            monsterSkillXml,
                            Log,
                            cancellationToken),
                    cancellationToken);

            Log(
                "VALIDAÇÃO XML CONCLUÍDA. Nenhuma tabela foi alterada durante a validação.");

            Log(
                $"Resumo preparado: MonsterBaseInfo={prepared.Monsters.Count:N0}, " +
                $"MonsterSkill={prepared.Skills.Count:N0}, " +
                $"MonsterSkillInfo={prepared.SkillInfos.Count:N0}.");

            if (prepared.MissingMonsterReferences.Count > 0)
            {
                Log(
                    $"WARNING: MonstersSkill.xml contém {prepared.MissingMonsterReferences.Count:N0} " +
                    "MonsterID distintos que não existem em Monster.xml. " +
                    "As skills serão preservadas porque o XML original contém essas referências.");

                foreach (int id in prepared.MissingMonsterReferences.Take(30))
                    Log($"WARNING: MonsterID referenciado apenas por MonstersSkill.xml: {id}.");
            }

            Log("A validar ligação SQL Server...");

            await using var connection =
                new SqlConnection(
                    connectionString);

            await connection.OpenAsync(
                cancellationToken);

            Log("Ligação SQL estabelecida.");

            // Schema check is deliberately performed before the transaction
            // and before any DELETE.
            await ValidateDatabaseSchemaAsync(
                connection,
                cancellationToken);

            Log("Schema SQL validado: as três tabelas/colunas esperadas existem.");

            await using (var xactAbort =
                new SqlCommand(
                    "SET XACT_ABORT ON;",
                    connection))
            {
                await xactAbort.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            Log("SQL safety: SET XACT_ABORT ON ativo.");

            await using SqlTransaction transaction =
                (SqlTransaction)
                await connection.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                Log("Transação SQL iniciada.");
                Log("A limpar tabelas Monster pela ordem segura das dependências...");

                await ClearTablesAsync(
                    connection,
                    transaction,
                    cancellationToken);

                Log("Tabelas limpas. Sem DBCC CHECKIDENT; os Ids são importados explicitamente.");

                Log("FASE 1/2 - Monster.xml -> Asset.MonsterBaseInfo.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    MonsterBaseInfoTable,
                    BuildMonsterBaseInfoTable(
                        prepared.Monsters),
                    cancellationToken);

                Log(
                    $"MonsterBaseInfo concluído: {prepared.Monsters.Count:N0} rows.");

                Log(
                    "FASE 2/2 - MonstersSkill.xml -> Asset.MonsterSkill -> Asset.MonsterSkillInfo.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    MonsterSkillTable,
                    BuildMonsterSkillTable(
                        prepared.Skills),
                    cancellationToken);

                Log(
                    $"MonsterSkill concluído: {prepared.Skills.Count:N0} rows.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    MonsterSkillInfoTable,
                    BuildMonsterSkillInfoTable(
                        prepared.SkillInfos),
                    cancellationToken);

                Log(
                    $"MonsterSkillInfo concluído: {prepared.SkillInfos.Count:N0} rows.");

                await VerifyInsertedCountsAsync(
                    connection,
                    transaction,
                    prepared,
                    Log,
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                TimeSpan elapsed =
                    DateTime.Now - started;

                Log("COMMIT concluído com sucesso.");

                Log(
                    $"SUCESSO FINAL: MonsterBaseInfo={prepared.Monsters.Count:N0}, " +
                    $"MonsterSkill={prepared.Skills.Count:N0}, " +
                    $"MonsterSkillInfo={prepared.SkillInfos.Count:N0}, " +
                    $"missing Monster refs={prepared.MissingMonsterReferences.Count:N0}, " +
                    $"tempo={elapsed.TotalSeconds:N1}s.");

                return new MonsterDatabaseImportSummary
                {
                    MonsterBaseInfoRows =
                        prepared.Monsters.Count,
                    MonsterSkillRows =
                        prepared.Skills.Count,
                    MonsterSkillInfoRows =
                        prepared.SkillInfos.Count,
                    MissingMonsterReferences =
                        prepared.MissingMonsterReferences.Count,
                    Elapsed = elapsed,
                    LogFile = logPath
                };
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);

                    Log(
                        "ROLLBACK concluído. A database voltou ao estado anterior ao import.");
                }
                catch (Exception rollbackEx)
                {
                    Log(
                        "ERRO durante ROLLBACK: " +
                        rollbackEx.Message);

                    try
                    {
                        SqlConnection.ClearPool(
                            connection);

                        await connection.CloseAsync();

                        Log(
                            "SQL safety: ligação com erro removida do pool e fechada.");
                    }
                    catch (Exception closeEx)
                    {
                        Log(
                            "WARNING: falha adicional ao fechar ligação após erro de rollback: " +
                            closeEx.Message);
                    }
                }

                throw;
            }
        }

        private static PreparedImport PrepareAndValidate(
            string monsterXml,
            string monsterSkillXml,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            log("A carregar Monster.xml...");

            XDocument monsterDocument =
                XDocument.Load(
                    monsterXml,
                    LoadOptions.None);

            List<MonsterBaseRow> monsters =
                ReadMonsters(
                    monsterDocument,
                    log,
                    cancellationToken);

            log(
                $"Monster.xml OK: {monsters.Count:N0} monsters únicos.");

            log("A carregar MonstersSkill.xml...");

            XDocument skillDocument =
                XDocument.Load(
                    monsterSkillXml,
                    LoadOptions.None);

            ReadMonsterSkills(
                skillDocument,
                monsters,
                log,
                cancellationToken,
                out List<MonsterSkillRow> skills,
                out List<MonsterSkillInfoRow> infos,
                out List<int> missingMonsterRefs);

            log(
                $"MonstersSkill.xml OK: MonsterSkill={skills.Count:N0}, " +
                $"MonsterSkillInfo={infos.Count:N0}.");

            log(
                "Campos XML sem coluna equivalente na DB serão preservados apenas no XML e NÃO serão inventados em outras colunas: " +
                "Monster(Sight, Scale, Battle, EXP, icons, comments/titles...) | " +
                "MonsterSkill(unk, CastCheck, unk2, SequenceID, Valocity, Accel, Eff_Fact_Val1/2/3, TalkID, NoticeEffname).");

            return new PreparedImport
            {
                Monsters = monsters,
                Skills = skills,
                SkillInfos = infos,
                MissingMonsterReferences = missingMonsterRefs
            };
        }

        private static List<MonsterBaseRow> ReadMonsters(
            XDocument document,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Monster.xml não possui root.");

            if (!root.Name.LocalName.Equals(
                    "Monsters",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Monster.xml root inválido: <{root.Name.LocalName}>. Esperado <Monsters>.");
            }

            List<XElement> nodes =
                root.Elements("Monster")
                    .ToList();

            if (nodes.Count == 0)
            {
                throw new InvalidDataException(
                    "Monster.xml não contém <Monster>.");
            }

            var result =
                new List<MonsterBaseRow>(
                    nodes.Count);

            var seenIds =
                new HashSet<int>();

            int identity = 0;

            foreach (XElement node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int id =
                    ReadInt(
                        node,
                        "MonsterID",
                        "Monster");

                if (id <= 0)
                {
                    throw new InvalidDataException(
                        $"Monster.xml contém MonsterID inválido: {id}.");
                }

                if (!seenIds.Add(id))
                {
                    throw new InvalidDataException(
                        $"Monster.xml contém MonsterID duplicado: {id}.");
                }

                string name =
                    node.Element("Name")?.Value
                    ?? string.Empty;

                if (name.Length > 255)
                {
                    throw new InvalidDataException(
                        $"MonsterID {id}: Name possui {name.Length} caracteres. " +
                        "O importer recusa truncar nomes silenciosamente.");
                }

                int huntRange =
                    ReadInt(
                        node,
                        "HuntRange",
                        $"Monster {id}");

                identity++;

                result.Add(
                    new MonsterBaseRow
                    {
                        Id = identity,
                        Type = id,
                        Model =
                            ReadInt(
                                node,
                                "ModelDigimon",
                                $"Monster {id}"),
                        Name = name,
                        Level =
                            ReadInt(
                                node,
                                "Level",
                                $"Monster {id}"),
                        // This is the mapping visible in the supplied SQL
                        // sample: e.g. MonsterID 10000 has Sight=300 and
                        // HuntRange=3500 while DB ViewRange=3500.
                        ViewRange = huntRange,
                        HuntRange = huntRange,
                        ReactionType = 1,
                        Attribute = 0,
                        Element = 0,
                        Family1 = 0,
                        Family2 = 0,
                        Family3 = 0,
                        ASValue =
                            ReadInt(node, "AS", $"Monster {id}"),
                        ARValue =
                            ReadInt(node, "AR", $"Monster {id}"),
                        ATValue =
                            ReadInt(node, "AT", $"Monster {id}"),
                        BLValue = 0,
                        CTValue =
                            ReadInt(node, "CT", $"Monster {id}"),
                        DEValue =
                            ReadInt(node, "DE", $"Monster {id}"),
                        DSValue =
                            ReadInt(node, "DS", $"Monster {id}"),
                        EVValue =
                            ReadInt(node, "EV", $"Monster {id}"),
                        HPValue =
                            ReadInt(node, "HP", $"Monster {id}"),
                        HTValue =
                            ReadInt(node, "HT", $"Monster {id}"),
                        MSValue =
                            ReadInt(node, "MS", $"Monster {id}"),
                        WSValue =
                            ReadInt(node, "WS", $"Monster {id}"),
                        Class =
                            ReadInt(node, "Class", $"Monster {id}")
                    });
            }

            log(
                "MonsterBaseInfo mapping validado contra o sample SQL fornecido: " +
                "Type=MonsterID (CONFIRMADO), Model=ModelDigimon, Name, Level, " +
                "ViewRange=HuntRange, HuntRange=HuntRange, ReactionType=1, " +
                "Attribute/Element/Family1/2/3=0, BLValue=0, " +
                "AS/AR/AT/CT/DE/DS/EV/HP/HT/MS/WS=campos homónimos, Class=Class.");

            return result;
        }

        private static void ReadMonsterSkills(
            XDocument document,
            IReadOnlyCollection<MonsterBaseRow> monsters,
            Action<string> log,
            CancellationToken cancellationToken,
            out List<MonsterSkillRow> skills,
            out List<MonsterSkillInfoRow> infos,
            out List<int> missingMonsterRefs)
        {
            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "MonstersSkill.xml não possui root.");

            if (!root.Name.LocalName.Equals(
                    "MonsterSkills",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"MonstersSkill.xml root inválido: <{root.Name.LocalName}>. Esperado <MonsterSkills>.");
            }

            List<XElement> nodes =
                root.Elements("MonsterSkill")
                    .ToList();

            if (nodes.Count == 0)
            {
                throw new InvalidDataException(
                    "MonstersSkill.xml não contém <MonsterSkill>.");
            }

            skills =
                new List<MonsterSkillRow>(
                    nodes.Count);

            infos =
                new List<MonsterSkillInfoRow>(
                    nodes.Count);

            var seenSkillIds =
                new HashSet<int>();

            HashSet<int> monsterIds =
                monsters
                    .Select(x => x.Type)
                    .ToHashSet();

            var missing =
                new HashSet<int>();

            int identity = 0;

            foreach (XElement node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int skillId =
                    ReadInt(
                        node,
                        "Skill_IDX",
                        "MonsterSkill");

                if (skillId < 0)
                {
                    throw new InvalidDataException(
                        $"MonstersSkill.xml contém Skill_IDX negativo: {skillId}.");
                }

                if (!seenSkillIds.Add(skillId))
                {
                    throw new InvalidDataException(
                        $"MonstersSkill.xml contém Skill_IDX duplicado: {skillId}.");
                }

                int monsterId =
                    ReadInt(
                        node,
                        "MonsterID",
                        $"MonsterSkill {skillId}");

                if (monsterId != 0 &&
                    !monsterIds.Contains(monsterId))
                {
                    missing.Add(monsterId);
                }

                // Validate numeric XML-only fields too. They are intentionally
                // not stored, but malformed values should still stop import
                // before DELETE begins.
                _ = ReadInt(node, "unk", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "CastCheck", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "unk2", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "SequenceID", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "Valocity", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "Accel", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "Eff_Fact_Val", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "Eff_Fact_Val2", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "Eff_Fact_Val3", $"MonsterSkill {skillId}");
                _ = ReadInt(node, "TalkID", $"MonsterSkill {skillId}");

                identity++;

                skills.Add(
                    new MonsterSkillRow
                    {
                        Id = identity,
                        Type = monsterId,
                        SkillId = skillId
                    });

                infos.Add(
                    new MonsterSkillInfoRow
                    {
                        Id = identity,
                        SkillId = skillId,
                        MinValue =
                            ReadInt(node, "Eff_Val_Min", $"MonsterSkill {skillId}"),
                        MaxValue =
                            ReadInt(node, "Eff_Val_Max", $"MonsterSkill {skillId}"),
                        CastingTime =
                            ReadDecimal(node, "CastTime", $"MonsterSkill {skillId}"),
                        Cooldown =
                            ReadInt(node, "CoolTime", $"MonsterSkill {skillId}"),
                        TargetCount =
                            ReadInt(node, "Target_Cnt", $"MonsterSkill {skillId}"),
                        TargetMin =
                            ReadInt(node, "Target_MinCnt", $"MonsterSkill {skillId}"),
                        TargetMax =
                            ReadInt(node, "Target_MaxCnt", $"MonsterSkill {skillId}"),
                        UseTerms =
                            ReadInt(node, "UseTerms", $"MonsterSkill {skillId}"),
                        RangeId =
                            ReadInt(node, "RangeIDX", $"MonsterSkill {skillId}"),
                        AnimationDelay =
                            ReadDecimal(node, "Ani_Delay", $"MonsterSkill {skillId}"),
                        ActiveType =
                            ReadInt(node, "Activetype", $"MonsterSkill {skillId}"),
                        SkillType =
                            ReadInt(node, "Skill_Type", $"MonsterSkill {skillId}"),
                        NoticeTime =
                            ReadDecimal(node, "NoticeTime", $"MonsterSkill {skillId}"),
                        Type = monsterId,
                        EffFactor =
                            ReadInt(node, "Eff_Factor", $"MonsterSkill {skillId}"),
                        EffFactor2 =
                            ReadInt(node, "Eff_Factor2", $"MonsterSkill {skillId}"),
                        EffFactor3 =
                            ReadInt(node, "Eff_Factor3", $"MonsterSkill {skillId}")
                    });
            }

            missingMonsterRefs =
                missing
                    .OrderBy(x => x)
                    .ToList();

            log(
                "MonsterSkill mapping validado: MonsterSkill.Type=MonsterID, MonsterSkill.SkillId=Skill_IDX.");

            log(
                "MonsterSkillInfo mapping validado: SkillId=Skill_IDX, " +
                "Min/Max=Eff_Val_Min/Max, CastingTime=CastTime, Cooldown=CoolTime, " +
                "Target*=Target_Cnt/MinCnt/MaxCnt, UseTerms, RangeId=RangeIDX, " +
                "AnimationDelay=Ani_Delay, ActiveType=Activetype, SkillType=Skill_Type, " +
                "NoticeTime, Type=MonsterID, EffFactor*=Eff_Factor*.");
        }

        private static async Task ValidateDatabaseSchemaAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            const string sql =
                """
                SELECT TOP (0)
                    [Id],[Type],[Model],[Name],[Level],[ViewRange],[HuntRange],
                    [ReactionType],[Attribute],[Element],[Family1],[Family2],[Family3],
                    [ASValue],[ARValue],[ATValue],[BLValue],[CTValue],[DEValue],
                    [DSValue],[EVValue],[HPValue],[HTValue],[MSValue],[WSValue],[Class]
                FROM [dmo].[Asset].[MonsterBaseInfo];

                SELECT TOP (0)
                    [Id],[Type],[SkillId]
                FROM [dmo].[Asset].[MonsterSkill];

                SELECT TOP (0)
                    [Id],[SkillId],[MinValue],[MaxValue],[CastingTime],[Cooldown],
                    [TargetCount],[TargetMin],[TargetMax],[UseTerms],[RangeId],
                    [AnimationDelay],[ActiveType],[SkillType],[NoticeTime],[Type],
                    [EffFactor],[EffFactor2],[EffFactor3]
                FROM [dmo].[Asset].[MonsterSkillInfo];
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection)
                {
                    CommandTimeout = 30
                };

            await using SqlDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            do
            {
                // Advancing through all result sets forces SQL Server to
                // resolve every referenced table and column.
            }
            while (await reader.NextResultAsync(
                cancellationToken));
        }

        private static async Task ClearTablesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            // IMPORTANT:
            // Do not call DBCC CHECKIDENT here.
            //
            // MonsterBaseInfo.Id is a normal column in the supplied schema,
            // not an IDENTITY column. The importer writes deterministic Id
            // values explicitly, so reseeding is not required for this import.
            const string sql =
                """
                DELETE FROM [dmo].[Asset].[MonsterSkillInfo];
                DELETE FROM [dmo].[Asset].[MonsterSkill];
                DELETE FROM [dmo].[Asset].[MonsterBaseInfo];
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection,
                    transaction)
                {
                    CommandTimeout = 180
                };

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        private static DataTable BuildMonsterBaseInfoTable(
            IReadOnlyCollection<MonsterBaseRow> rows)
        {
            var table =
                new DataTable();

            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("Model", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Level", typeof(int));
            table.Columns.Add("ViewRange", typeof(int));
            table.Columns.Add("HuntRange", typeof(int));
            table.Columns.Add("ReactionType", typeof(int));
            table.Columns.Add("Attribute", typeof(int));
            table.Columns.Add("Element", typeof(int));
            table.Columns.Add("Family1", typeof(int));
            table.Columns.Add("Family2", typeof(int));
            table.Columns.Add("Family3", typeof(int));
            table.Columns.Add("ASValue", typeof(int));
            table.Columns.Add("ARValue", typeof(int));
            table.Columns.Add("ATValue", typeof(int));
            table.Columns.Add("BLValue", typeof(int));
            table.Columns.Add("CTValue", typeof(int));
            table.Columns.Add("DEValue", typeof(int));
            table.Columns.Add("DSValue", typeof(int));
            table.Columns.Add("EVValue", typeof(int));
            table.Columns.Add("HPValue", typeof(int));
            table.Columns.Add("HTValue", typeof(int));
            table.Columns.Add("MSValue", typeof(int));
            table.Columns.Add("WSValue", typeof(int));
            table.Columns.Add("Class", typeof(int));

            foreach (MonsterBaseRow row in rows)
            {
                table.Rows.Add(
                    row.Id,
                    row.Type,
                    row.Model,
                    row.Name,
                    row.Level,
                    row.ViewRange,
                    row.HuntRange,
                    row.ReactionType,
                    row.Attribute,
                    row.Element,
                    row.Family1,
                    row.Family2,
                    row.Family3,
                    row.ASValue,
                    row.ARValue,
                    row.ATValue,
                    row.BLValue,
                    row.CTValue,
                    row.DEValue,
                    row.DSValue,
                    row.EVValue,
                    row.HPValue,
                    row.HTValue,
                    row.MSValue,
                    row.WSValue,
                    row.Class);
            }

            return table;
        }

        private static DataTable BuildMonsterSkillTable(
            IReadOnlyCollection<MonsterSkillRow> rows)
        {
            var table =
                new DataTable();

            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("SkillId", typeof(int));

            foreach (MonsterSkillRow row in rows)
                table.Rows.Add(row.Id, row.Type, row.SkillId);

            return table;
        }

        private static DataTable BuildMonsterSkillInfoTable(
            IReadOnlyCollection<MonsterSkillInfoRow> rows)
        {
            var table =
                new DataTable();

            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("SkillId", typeof(int));
            table.Columns.Add("MinValue", typeof(int));
            table.Columns.Add("MaxValue", typeof(int));
            table.Columns.Add("CastingTime", typeof(decimal));
            table.Columns.Add("Cooldown", typeof(int));
            table.Columns.Add("TargetCount", typeof(int));
            table.Columns.Add("TargetMin", typeof(int));
            table.Columns.Add("TargetMax", typeof(int));
            table.Columns.Add("UseTerms", typeof(int));
            table.Columns.Add("RangeId", typeof(int));
            table.Columns.Add("AnimationDelay", typeof(decimal));
            table.Columns.Add("ActiveType", typeof(int));
            table.Columns.Add("SkillType", typeof(int));
            table.Columns.Add("NoticeTime", typeof(decimal));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("EffFactor", typeof(int));
            table.Columns.Add("EffFactor2", typeof(int));
            table.Columns.Add("EffFactor3", typeof(int));

            foreach (MonsterSkillInfoRow row in rows)
            {
                table.Rows.Add(
                    row.Id,
                    row.SkillId,
                    row.MinValue,
                    row.MaxValue,
                    row.CastingTime,
                    row.Cooldown,
                    row.TargetCount,
                    row.TargetMin,
                    row.TargetMax,
                    row.UseTerms,
                    row.RangeId,
                    row.AnimationDelay,
                    row.ActiveType,
                    row.SkillType,
                    row.NoticeTime,
                    row.Type,
                    row.EffFactor,
                    row.EffFactor2,
                    row.EffFactor3);
            }

            return table;
        }

        private static async Task BulkInsertAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string destination,
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
                    DestinationTableName = destination,
                    BatchSize = 1500,
                    BulkCopyTimeout = 240,
                    EnableStreaming = true
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

        private static async Task VerifyInsertedCountsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PreparedImport prepared,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            int monsters =
                await CountAsync(
                    connection,
                    transaction,
                    MonsterBaseInfoTable,
                    cancellationToken);

            int skills =
                await CountAsync(
                    connection,
                    transaction,
                    MonsterSkillTable,
                    cancellationToken);

            int infos =
                await CountAsync(
                    connection,
                    transaction,
                    MonsterSkillInfoTable,
                    cancellationToken);

            if (monsters != prepared.Monsters.Count ||
                skills != prepared.Skills.Count ||
                infos != prepared.SkillInfos.Count)
            {
                throw new InvalidDataException(
                    "Verificação pós-import falhou: " +
                    $"MonsterBaseInfo {monsters}/{prepared.Monsters.Count}, " +
                    $"MonsterSkill {skills}/{prepared.Skills.Count}, " +
                    $"MonsterSkillInfo {infos}/{prepared.SkillInfos.Count}.");
            }

            log(
                $"VERIFY OK: MonsterBaseInfo={monsters:N0}, " +
                $"MonsterSkill={skills:N0}, MonsterSkillInfo={infos:N0}.");
        }

        private static async Task<int> CountAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string table,
            CancellationToken cancellationToken)
        {
            await using var command =
                new SqlCommand(
                    $"SELECT COUNT_BIG(*) FROM {table};",
                    connection,
                    transaction);

            object? value =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            long count =
                Convert.ToInt64(
                    value,
                    CultureInfo.InvariantCulture);

            return checked((int)count);
        }

        private static void EnsureFile(
            string path,
            string displayName)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"{displayName} não foi encontrado.",
                    path);
            }
        }

        private static int ReadInt(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                parent.Element(name)?.Value?.Trim()
                ?? string.Empty;

            if (!long.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é um inteiro válido.");
            }

            if (value < int.MinValue ||
                value > int.MaxValue)
            {
                throw new OverflowException(
                    $"{context}: <{name}>={value} não cabe em Int32.");
            }

            return (int)value;
        }

        private static decimal ReadDecimal(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                parent.Element(name)?.Value?.Trim()
                ?? string.Empty;

            if (!decimal.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é decimal válido.");
            }

            return value;
        }

        private sealed class PreparedImport
        {
            public required List<MonsterBaseRow> Monsters { get; init; }
            public required List<MonsterSkillRow> Skills { get; init; }
            public required List<MonsterSkillInfoRow> SkillInfos { get; init; }
            public required List<int> MissingMonsterReferences { get; init; }
        }

        private sealed class MonsterBaseRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int Model { get; init; }
            public string Name { get; init; } = string.Empty;
            public int Level { get; init; }
            public int ViewRange { get; init; }
            public int HuntRange { get; init; }
            public int ReactionType { get; init; }
            public int Attribute { get; init; }
            public int Element { get; init; }
            public int Family1 { get; init; }
            public int Family2 { get; init; }
            public int Family3 { get; init; }
            public int ASValue { get; init; }
            public int ARValue { get; init; }
            public int ATValue { get; init; }
            public int BLValue { get; init; }
            public int CTValue { get; init; }
            public int DEValue { get; init; }
            public int DSValue { get; init; }
            public int EVValue { get; init; }
            public int HPValue { get; init; }
            public int HTValue { get; init; }
            public int MSValue { get; init; }
            public int WSValue { get; init; }
            public int Class { get; init; }
        }

        private sealed class MonsterSkillRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int SkillId { get; init; }
        }

        private sealed class MonsterSkillInfoRow
        {
            public int Id { get; init; }
            public int SkillId { get; init; }
            public int MinValue { get; init; }
            public int MaxValue { get; init; }
            public decimal CastingTime { get; init; }
            public int Cooldown { get; init; }
            public int TargetCount { get; init; }
            public int TargetMin { get; init; }
            public int TargetMax { get; init; }
            public int UseTerms { get; init; }
            public int RangeId { get; init; }
            public decimal AnimationDelay { get; init; }
            public int ActiveType { get; init; }
            public int SkillType { get; init; }
            public decimal NoticeTime { get; init; }
            public int Type { get; init; }
            public int EffFactor { get; init; }
            public int EffFactor2 { get; init; }
            public int EffFactor3 { get; init; }
        }
    }
}
