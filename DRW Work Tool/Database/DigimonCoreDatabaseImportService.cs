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
    public sealed class DigimonCoreDatabaseImportSummary
    {
        public int DigimonBaseInfoRows { get; init; }
        public int EvolutionRows { get; init; }
        public int EvolutionLineRows { get; init; }
        public int EvolutionStageRows { get; init; }
        public int SkillCodeRows { get; init; }
        public int SkillCodeApplyRows { get; init; }
        public int SkillInfoRows { get; init; }
        public int DigimonSkillRows { get; init; }
        public int DuplicateSkillIdsCollapsed { get; init; }
        public int MissingSkillReferences { get; init; }
        public int SharedSkillAssociations { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string LogFile { get; init; } = string.Empty;
    }

    public sealed class DigimonCoreDatabaseImportService
    {
        private const string DigimonBaseInfoTable =
            "[dmo].[Asset].[DigimonBaseInfo]";

        private const string EvolutionTable =
            "[dmo].[Asset].[Evolution]";

        private const string EvolutionStageTable =
            "[dmo].[Asset].[EvolutionStage]";

        private const string EvolutionLineTable =
            "[dmo].[Asset].[EvolutionLine]";

        private const string SkillCodeTable =
            "[dmo].[Asset].[SkillCode]";

        private const string SkillCodeApplyTable =
            "[dmo].[Asset].[SkillCodeApply]";

        private const string SkillInfoTable =
            "[dmo].[Asset].[SkillInfo]";

        private const string DigimonSkillTable =
            "[dmo].[Asset].[DigimonSkill]";

        public static string ImportLogFolder =>
            Path.Combine(
                AppPaths.Logs,
                "ImportToDatabase");

        public async Task<DigimonCoreDatabaseImportSummary> ImportAsync(
            string connectionString,
            string digimonListXml,
            string digimonEvoXml,
            string skillXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;

            Directory.CreateDirectory(
                ImportLogFolder);

            string logPath =
                Path.Combine(
                    ImportLogFolder,
                    $"DigimonCore_{started:yyyy-MM-dd_HH-mm-ss}.log");

            void Log(string message)
            {
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

                File.AppendAllText(
                    logPath,
                    line + Environment.NewLine);

                progress?.Report(line);
            }

            Log("DIGIMON CORE -> DATABASE iniciado.");
            Log("Ordem obrigatória: Digimon_List.xml -> DigimonEvo.xml -> Skill.xml.");
            Log("FASE 0/4 - validação completa antes de tocar na database.");

            EnsureFile(digimonListXml, "Digimon_List.xml");
            EnsureFile(digimonEvoXml, "DigimonEvo.xml");
            EnsureFile(skillXml, "Skill.xml");

            PreparedImport prepared =
                await Task.Run(
                    () =>
                        PrepareAndValidate(
                            digimonListXml,
                            digimonEvoXml,
                            skillXml,
                            Log,
                            cancellationToken),
                    cancellationToken);

            Log("VALIDAÇÃO CONCLUÍDA. Nenhuma tabela foi alterada durante a validação.");

            Log(
                $"Resumo preparado: DigimonBaseInfo={prepared.Digimons.Count:N0}, " +
                $"Evolution={prepared.Evolutions.Count:N0}, " +
                $"EvolutionLine={prepared.EvolutionLines.Count:N0}, " +
                $"EvolutionStage={prepared.EvolutionStages.Count:N0}, " +
                $"SkillCode={prepared.SkillCodes.Count:N0}, " +
                $"SkillCodeApply={prepared.SkillApplies.Count:N0}, " +
                $"SkillInfo={prepared.SkillInfos.Count:N0}, " +
                $"DigimonSkill={prepared.DigimonSkills.Count:N0}.");

            if (prepared.MissingSkillReferences.Count > 0)
            {
                Log(
                    $"WARNING: Digimon_List.xml referencia {prepared.MissingSkillReferences.Count:N0} " +
                    "Skill IDs que não existem em Skill.xml. Essas referências não podem gerar " +
                    "SkillCode/SkillInfo e foram apenas reportadas.");

                foreach (uint id in prepared.MissingSkillReferences.Take(30))
                    Log($"WARNING: Skill ID ausente em Skill.xml: {id}.");

                if (prepared.MissingSkillReferences.Count > 30)
                    Log($"WARNING: ... e mais {prepared.MissingSkillReferences.Count - 30:N0} IDs.");
            }

            Log("A validar ligação SQL Server...");

            await using var connection =
                new SqlConnection(connectionString);

            await connection.OpenAsync(cancellationToken);
            Log("Ligação SQL estabelecida.");

            await using (var xactAbort =
                new SqlCommand("SET XACT_ABORT ON;", connection))
            {
                await xactAbort.ExecuteNonQueryAsync(cancellationToken);
            }

            Log("SQL safety: SET XACT_ABORT ON ativo.");

            await using SqlTransaction transaction =
                (SqlTransaction)
                await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                Log("Transação SQL iniciada.");
                Log("A limpar as tabelas core pela ordem segura das dependências...");

                await ClearCoreTablesAsync(
                    connection,
                    transaction,
                    cancellationToken);

                Log(
                    "Tabelas core limpas + identity reseed concluído. " +
                    "EvolutionArmor foi preservada porque não é representada pelos três XMLs.");

                Log("FASE 1/3 - Digimon_List.xml -> Asset.DigimonBaseInfo.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    DigimonBaseInfoTable,
                    BuildDigimonBaseInfoTable(prepared.Digimons),
                    cancellationToken);

                Log($"FASE 1/3 concluída: DigimonBaseInfo={prepared.Digimons.Count:N0}.");

                Log("FASE 2/3 - DigimonEvo.xml -> Evolution -> EvolutionLine -> EvolutionStage.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    EvolutionTable,
                    BuildEvolutionTable(prepared.Evolutions),
                    cancellationToken);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    EvolutionLineTable,
                    BuildEvolutionLineTable(prepared.EvolutionLines),
                    cancellationToken);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    EvolutionStageTable,
                    BuildEvolutionStageTable(prepared.EvolutionStages),
                    cancellationToken);

                Log("FASE 2/3 concluída. EvolutionArmor preservada.");

                Log("FASE 3/3 - Skill.xml -> SkillCode -> SkillCodeApply -> SkillInfo -> DigimonSkill.");

                await BulkInsertAsync(
                    connection,
                    transaction,
                    SkillCodeTable,
                    BuildSkillCodeTable(prepared.SkillCodes),
                    cancellationToken);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    SkillCodeApplyTable,
                    BuildSkillCodeApplyTable(prepared.SkillApplies),
                    cancellationToken);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    SkillInfoTable,
                    BuildSkillInfoTable(prepared.SkillInfos),
                    cancellationToken);

                await BulkInsertAsync(
                    connection,
                    transaction,
                    DigimonSkillTable,
                    BuildDigimonSkillTable(prepared.DigimonSkills),
                    cancellationToken);

                await VerifyInsertedCountsAsync(
                    connection,
                    transaction,
                    prepared,
                    Log,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                TimeSpan elapsed = DateTime.Now - started;

                Log("COMMIT concluído com sucesso.");
                Log(
                    $"SUCESSO FINAL: DigimonBaseInfo={prepared.Digimons.Count:N0}, " +
                    $"Evolution={prepared.Evolutions.Count:N0}, " +
                    $"EvolutionLine={prepared.EvolutionLines.Count:N0}, " +
                    $"EvolutionStage={prepared.EvolutionStages.Count:N0}, " +
                    $"SkillCode={prepared.SkillCodes.Count:N0}, " +
                    $"SkillCodeApply={prepared.SkillApplies.Count:N0}, " +
                    $"SkillInfo={prepared.SkillInfos.Count:N0}, " +
                    $"DigimonSkill={prepared.DigimonSkills.Count:N0}, " +
                    $"tempo={elapsed.TotalSeconds:N1}s.");

                return new DigimonCoreDatabaseImportSummary
                {
                    DigimonBaseInfoRows = prepared.Digimons.Count,
                    EvolutionRows = prepared.Evolutions.Count,
                    EvolutionLineRows = prepared.EvolutionLines.Count,
                    EvolutionStageRows = prepared.EvolutionStages.Count,
                    SkillCodeRows = prepared.SkillCodes.Count,
                    SkillCodeApplyRows = prepared.SkillApplies.Count,
                    SkillInfoRows = prepared.SkillInfos.Count,
                    DigimonSkillRows = prepared.DigimonSkills.Count,
                    DuplicateSkillIdsCollapsed = prepared.DuplicateSkillIdsCollapsed,
                    MissingSkillReferences = prepared.MissingSkillReferences.Count,
                    SharedSkillAssociations = prepared.SharedSkillAssociations,
                    Elapsed = elapsed,
                    LogFile = logPath
                };
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    Log("ROLLBACK concluído. A database voltou ao estado anterior ao import.");
                }
                catch (Exception rollbackEx)
                {
                    Log("ERRO durante ROLLBACK: " + rollbackEx.Message);

                    try
                    {
                        SqlConnection.ClearPool(connection);
                        await connection.CloseAsync();
                        Log("SQL safety: ligação fatal removida do pool e fechada fisicamente.");
                    }
                    catch (Exception closeEx)
                    {
                        Log("WARNING: também não foi possível fechar/descartar a ligação após falha de rollback: " + closeEx.Message);
                    }
                }

                throw;
            }
        }

        private static PreparedImport PrepareAndValidate(
            string digimonListXml,
            string digimonEvoXml,
            string skillXml,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            log("A carregar Digimon_List.xml...");
            XDocument digimonDocument = XDocument.Load(digimonListXml, LoadOptions.None);

            List<DigimonBaseRow> digimons =
                ReadDigimonList(
                    digimonDocument,
                    log,
                    cancellationToken,
                    out Dictionary<uint, List<SkillAssociation>> skillAssociations);

            log(
                $"Digimon_List.xml OK: {digimons.Count:N0} Digimon, " +
                $"{skillAssociations.Sum(x => x.Value.Count):N0} skill associations físicas.");

            log("A carregar DigimonEvo.xml...");
            XDocument evoDocument = XDocument.Load(digimonEvoXml, LoadOptions.None);

            ReadDigimonEvo(
                evoDocument,
                digimons,
                log,
                cancellationToken,
                out List<EvolutionRow> evolutions,
                out List<EvolutionLineRow> evolutionLines,
                out List<EvolutionStageRow> evolutionStages);

            log(
                $"DigimonEvo.xml OK: Evolution={evolutions.Count:N0}, " +
                $"EvolutionLine={evolutionLines.Count:N0}, " +
                $"EvolutionStage={evolutionStages.Count:N0}.");

            log("A carregar Skill.xml...");
            XDocument skillDocument = XDocument.Load(skillXml, LoadOptions.None);

            ReadSkills(
                skillDocument,
                skillAssociations,
                log,
                cancellationToken,
                out List<SkillCodeRow> skillCodes,
                out List<SkillApplyRow> skillApplies,
                out List<SkillInfoRow> skillInfos,
                out List<DigimonSkillRow> digimonSkills,
                out int duplicateSkillIdsCollapsed,
                out List<uint> missingSkillReferences,
                out int sharedSkillAssociations);

            log(
                $"Skill.xml OK: SkillCode={skillCodes.Count:N0}, " +
                $"SkillCodeApply={skillApplies.Count:N0}, " +
                $"SkillInfo={skillInfos.Count:N0}, " +
                $"DigimonSkill={digimonSkills.Count:N0}.");

            log(
                "SkillInfo mapping restaurado a partir da DB funcional: " +
                "Value=0, CastingTime=0 e MemoryChips=0; Type segue DigimonSkill.Type.");

            return new PreparedImport
            {
                Digimons = digimons,
                Evolutions = evolutions,
                EvolutionLines = evolutionLines,
                EvolutionStages = evolutionStages,
                SkillCodes = skillCodes,
                SkillApplies = skillApplies,
                SkillInfos = skillInfos,
                DigimonSkills = digimonSkills,
                DuplicateSkillIdsCollapsed = duplicateSkillIdsCollapsed,
                MissingSkillReferences = missingSkillReferences,
                SharedSkillAssociations = sharedSkillAssociations
            };
        }

        private static List<DigimonBaseRow> ReadDigimonList(
            XDocument document,
            Action<string> log,
            CancellationToken cancellationToken,
            out Dictionary<uint, List<SkillAssociation>> skillAssociations)
        {
            XElement root = document.Root ?? throw new InvalidDataException("Digimon_List.xml não possui root.");

            if (!root.Name.LocalName.Equals("DigimonList", StringComparison.Ordinal))
                throw new InvalidDataException($"Digimon_List.xml root inválido: <{root.Name.LocalName}>. Esperado <DigimonList>.");

            string skillSlots = root.Attribute("SkillSlots")?.Value ?? string.Empty;

            if (skillSlots.Length != 0 && skillSlots != "5")
                throw new InvalidDataException($"Digimon_List.xml SkillSlots={skillSlots}. O importer foi preparado para exatamente 5 slots.");

            List<XElement> nodes = root.Elements("Digimon").ToList();

            if (nodes.Count == 0)
                throw new InvalidDataException("Digimon_List.xml não contém Digimon.");

            var result = new List<DigimonBaseRow>(nodes.Count);
            skillAssociations = new Dictionary<uint, List<SkillAssociation>>();
            var seenIds = new HashSet<uint>();
            int identity = 0;

            foreach (XElement node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint type = ReadUIntAttribute(node, "ID", "Digimon");

                if (type == 0)
                    throw new InvalidDataException("Digimon_List.xml contém Digimon ID=0.");

                if (!seenIds.Add(type))
                    throw new InvalidDataException($"Digimon_List.xml contém Digimon ID duplicado: {type}.");

                XElement stats = node.Element("Stats") ?? throw new InvalidDataException($"Digimon {type}: <Stats> ausente.");

                int[] families =
                    ReadCsvTriple(
                        ReadText(node, "FamilyTypes", $"Digimon {type}"),
                        $"Digimon {type} FamilyTypes");

                List<XElement> skillNodes =
                    node.Element("Skills")?.Elements("Skill").ToList()
                    ?? throw new InvalidDataException($"Digimon {type}: <Skills> ausente.");

                if (skillNodes.Count != 5)
                    throw new InvalidDataException($"Digimon {type}: esperado exatamente 5 <Skill>; encontrado {skillNodes.Count}.");

                var seenSlots = new HashSet<int>();

                foreach (XElement skill in skillNodes)
                {
                    int slot = ReadIntAttribute(skill, "Slot", $"Digimon {type} Skill");

                    if (slot < 0 || slot > 4)
                        throw new InvalidDataException($"Digimon {type}: Skill Slot={slot} fora de 0..4.");

                    if (!seenSlots.Add(slot))
                        throw new InvalidDataException($"Digimon {type}: Skill Slot={slot} duplicado.");

                    uint skillId = ReadUIntAttribute(skill, "ID", $"Digimon {type} Skill Slot {slot}");

                    if (skillId == 0)
                        continue;

                    if (!skillAssociations.TryGetValue(skillId, out List<SkillAssociation>? list))
                    {
                        list = new List<SkillAssociation>();
                        skillAssociations.Add(skillId, list);
                    }

                    if (!list.Any(x => x.DigimonType == type && x.Slot == slot))
                    {
                        list.Add(
                            new SkillAssociation
                            {
                                DigimonType = type,
                                Slot = slot
                            });
                    }
                }

                identity++;

                result.Add(
                    new DigimonBaseRow
                    {
                        Id = identity,
                        Type = CheckedInt(type, $"Digimon {type} ID"),
                        Model = ReadInt(node, "ModelID", $"Digimon {type}"),
                        Name = node.Attribute("Name")?.Value ?? string.Empty,
                        Level = ReadInt(node, "BaseLevel", $"Digimon {type}"),
                        ScaleType = ReadInt(node, "DigimonType", $"Digimon {type}"),
                        Attribute = ReadInt(node, "AttributeType", $"Digimon {type}"),
                        Element = ReadInt(node, "BaseNatureType", $"Digimon {type}"),
                        Family1 = families[0],
                        Family2 = families[1],
                        Family3 = families[2],
                        ASValue = ReadIntAttribute(stats, "AttSpeed", $"Digimon {type} Stats"),
                        ARValue = ReadIntAttribute(stats, "AttRange", $"Digimon {type} Stats"),
                        ATValue = ReadIntAttribute(stats, "AttPower", $"Digimon {type} Stats"),
                        BLValue = 0,
                        CTValue = ReadIntAttribute(stats, "CriticalRate", $"Digimon {type} Stats"),
                        DEValue = ReadIntAttribute(stats, "DefPower", $"Digimon {type} Stats"),
                        DSValue = ReadIntAttribute(stats, "DS", $"Digimon {type} Stats"),
                        EVValue = ReadIntAttribute(stats, "Evasion", $"Digimon {type} Stats"),
                        HPValue = ReadIntAttribute(stats, "HP", $"Digimon {type} Stats"),
                        HTValue = ReadIntAttribute(stats, "HitRate", $"Digimon {type} Stats"),
                        MSValue = ReadIntAttribute(stats, "MoveSpeed", $"Digimon {type} Stats"),
                        WSValue = ReadIntLikeDecimal(node, "WalkLen", $"Digimon {type}"),
                        EvolutionType = ReadInt(node, "EvolutionType", $"Digimon {type}")
                    });
            }

            log(
                "DigimonBaseInfo mapping: Type=ID, Model=ModelID, Level=BaseLevel, " +
                "ScaleType=DigimonType, Attribute=AttributeType, Element=BaseNatureType, " +
                "AS/AR/AT/CT/DE/DS/EV/HP/HT/MS=Stats, BL=0, WS=WalkLen.");

            return result;
        }

        private static void ReadDigimonEvo(
            XDocument document,
            IReadOnlyCollection<DigimonBaseRow> digimons,
            Action<string> log,
            CancellationToken cancellationToken,
            out List<EvolutionRow> evolutions,
            out List<EvolutionLineRow> evolutionLines,
            out List<EvolutionStageRow> evolutionStages)
        {
            XElement root = document.Root ?? throw new InvalidDataException("DigimonEvo.xml não possui root.");

            if (!root.Name.LocalName.Equals("DigimonList", StringComparison.Ordinal))
                throw new InvalidDataException($"DigimonEvo.xml root inválido: <{root.Name.LocalName}>. Esperado <DigimonList>.");

            List<XElement> trees = root.Elements("Digimon").ToList();

            if (trees.Count == 0)
                throw new InvalidDataException("DigimonEvo.xml não possui árvores.");

            evolutions = new List<EvolutionRow>();
            evolutionLines = new List<EvolutionLineRow>();
            evolutionStages = new List<EvolutionStageRow>();

            var digimonIds = digimons.Select(x => x.Type).ToHashSet();
            var seenRoots = new HashSet<int>();

            int evolutionId = 0;
            int evolutionLineId = 0;
            int stageId = 0;
            int unknownDigimonRefs = 0;

            foreach (XElement tree in trees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int rootType = ReadInt(tree, "digiId", "DigimonEvo tree");

                if (rootType <= 0)
                    throw new InvalidDataException("DigimonEvo.xml contém tree digiId <= 0.");

                if (!seenRoots.Add(rootType))
                    throw new InvalidDataException($"DigimonEvo.xml contém root digiId duplicado: {rootType}.");

                List<XElement> nodes = tree.Elements("Evolution").ToList();
                int declaredCount = ReadInt(tree, "CountEvo", $"DigimonEvo tree {rootType}");

                if (declaredCount != nodes.Count)
                    throw new InvalidDataException($"DigimonEvo tree {rootType}: CountEvo={declaredCount}, mas existem {nodes.Count} Evolution.");

                if (nodes.Count == 0)
                    throw new InvalidDataException($"DigimonEvo tree {rootType}: sem Evolution.");

                List<XElement> rootNodes =
                    nodes.Where(node => ReadInt(node, "digiId", $"DigimonEvo tree {rootType} Evolution") == rootType).ToList();

                if (rootNodes.Count != 1)
                    throw new InvalidDataException($"DigimonEvo tree {rootType}: esperado exatamente um root Evolution com digiId={rootType}; encontrado {rootNodes.Count}.");

                int rootLevel = ReadInt(rootNodes[0], "Level", $"DigimonEvo tree {rootType} root Evolution");

                if (rootLevel != 1)
                    throw new InvalidDataException($"DigimonEvo tree {rootType}: a Evolution root digiId={rootType} tem Level={rootLevel}; esperado Level=1.");

                int rootPhysicalIndex = nodes.IndexOf(rootNodes[0]);

                if (rootPhysicalIndex != 0)
                    log($"INFO: DigimonEvo tree {rootType}: root na posição física {rootPhysicalIndex + 1}/{nodes.Count}; ordem original preservada.");

                evolutionId++;

                evolutions.Add(
                    new EvolutionRow
                    {
                        Id = evolutionId,
                        Type = rootType,
                        EvolutionRank = ReadInt(tree, "BattleType", $"DigimonEvo tree {rootType}")
                    });

                foreach (XElement node in nodes)
                {
                    int type = ReadInt(node, "digiId", $"DigimonEvo tree {rootType}");

                    if (!digimonIds.Contains(type))
                        unknownDigimonRefs++;

                    int level = ReadInt(node, "Level", $"DigimonEvo {type}");

                    if (level < 0)
                        throw new InvalidDataException($"DigimonEvo {type}: Level={level} inválido.");

                    evolutionLineId++;

                    evolutionLines.Add(
                        new EvolutionLineRow
                        {
                            Id = evolutionLineId,
                            EvolutionId = evolutionId,
                            Type = type,
                            UnlockItemSection = ReadInt(node, "m_nOpenItemTypeS", $"DigimonEvo {type}"),
                            UnlockItemSectionAmount = ReadInt(node, "m_nOpenItemNum", $"DigimonEvo {type}"),
                            UnlockLevel = ReadInt(node, "m_nOpenLevel", $"DigimonEvo {type}"),
                            UnlockQuestId = ReadInt(node, "m_nOpenQuest", $"DigimonEvo {type}"),
                            SlotLevel = level,
                            RequiredAmount = ReadInt(node, "m_nUseItemNum", $"DigimonEvo {type}"),
                            RequiredItem = ReadInt(node, "m_nUseItem", $"DigimonEvo {type}")
                        });

                    List<XElement> links = node.Elements("EvolutionType").ToList();

                    if (links.Count != 9)
                        throw new InvalidDataException($"DigimonEvo {type}: esperado exatamente 9 EvolutionType; encontrado {links.Count}.");

                    foreach (XElement link in links)
                    {
                        stageId++;
                        evolutionStages.Add(
                            new EvolutionStageRow
                            {
                                Id = stageId,
                                Type = ReadInt(link, "dwDigimonID", $"DigimonEvo {type} EvolutionType"),
                                Value = ReadInt(link, "nSlot", $"DigimonEvo {type} EvolutionType"),
                                EvolutionLineId = evolutionLineId
                            });
                    }
                }
            }

            if (evolutionStages.Count != evolutionLines.Count * 9)
                throw new InvalidDataException("DigimonEvo validation interna: EvolutionStage != EvolutionLine * 9.");

            if (unknownDigimonRefs > 0)
                log($"WARNING: DigimonEvo contém {unknownDigimonRefs:N0} Evolution digiId que não existem em Digimon_List.xml. Os valores serão preservados.");

            log(
                "Evolution mapping validado: Evolution.Type=tree digiId, " +
                "Evolution.EvolutionRank=BattleType; EvolutionLine=uma row por <Evolution>; " +
                "EvolutionStage=9 rows por Evolution com Type=dwDigimonID e Value=nSlot.");
        }

        private static void ReadSkills(
            XDocument document,
            Dictionary<uint, List<SkillAssociation>> skillAssociations,
            Action<string> log,
            CancellationToken cancellationToken,
            out List<SkillCodeRow> skillCodes,
            out List<SkillApplyRow> skillApplies,
            out List<SkillInfoRow> skillInfos,
            out List<DigimonSkillRow> digimonSkills,
            out int duplicateSkillIdsCollapsed,
            out List<uint> missingSkillReferences,
            out int sharedSkillAssociations)
        {
            XElement root = document.Root ?? throw new InvalidDataException("Skill.xml não possui root.");

            if (!root.Name.LocalName.Equals("SkillDataArray", StringComparison.Ordinal))
                throw new InvalidDataException($"Skill.xml root inválido: <{root.Name.LocalName}>. Esperado <SkillDataArray>.");

            List<XElement> physical = root.Elements("SkillData").ToList();

            if (physical.Count == 0)
                throw new InvalidDataException("Skill.xml não contém SkillData.");

            var occurrences =
                physical.Select(
                    node =>
                        new SkillPhysicalOccurrence
                        {
                            Node = node,
                            SkillId = ReadUInt(node, "s_dwID", "SkillData")
                        }).ToList();

            if (occurrences.Any(x => x.SkillId == 0))
                throw new InvalidDataException("Skill.xml contém s_dwID=0.");

            List<XElement> unique =
                occurrences.GroupBy(x => x.SkillId).Select(g => g.First().Node).ToList();

            duplicateSkillIdsCollapsed = physical.Count - unique.Count;

            foreach (IGrouping<uint, SkillPhysicalOccurrence> duplicate in
                occurrences.GroupBy(x => x.SkillId).Where(g => g.Count() > 1))
            {
                log(
                    $"INFO: Skill.xml contém {duplicate.Count()} ocorrências físicas de s_dwID={duplicate.Key}. " +
                    "SkillCode/SkillCodeApply usam a primeira ocorrência; SkillInfo preserva todas as ocorrências físicas.");
            }

            skillCodes = new List<SkillCodeRow>(unique.Count);
            skillInfos = new List<SkillInfoRow>();
            skillApplies = new List<SkillApplyRow>(unique.Count * 3);
            digimonSkills = new List<DigimonSkillRow>();

            int skillCodeAssetId = 0;
            int applyId = 0;
            int skillInfoId = 0;
            int digimonSkillId = 0;
            sharedSkillAssociations = 0;

            var uniqueIds = unique.Select(x => ReadUInt(x, "s_dwID", "SkillData")).ToHashSet();

            missingSkillReferences =
                skillAssociations.Keys.Where(x => !uniqueIds.Contains(x)).OrderBy(x => x).ToList();

            foreach (XElement node in unique)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint skillId = ReadUInt(node, "s_dwID", "SkillData");
                int sqlSkillId = CheckedInt(skillId, $"Skill {skillId}");

                skillCodeAssetId++;

                skillCodes.Add(
                    new SkillCodeRow
                    {
                        Id = skillCodeAssetId,
                        SkillCode = sqlSkillId,
                        Comment = ReadOptionalText(node, "s_szComment")
                    });

                List<XElement> applies = GetSkillApplies(node, skillId);

                for (int i = 0; i < 3; i++)
                {
                    XElement apply = applies[i];
                    applyId++;

                    skillApplies.Add(
                        new SkillApplyRow
                        {
                            Id = applyId,
                            Type = ReadInt(apply, "s_nID", $"Skill {skillId} Apply {i + 1}"),
                            Attribute = ReadInt(apply, "s_nA", $"Skill {skillId} Apply {i + 1}"),
                            Value = ReadInt(apply, "s_nB", $"Skill {skillId} Apply {i + 1}"),
                            AdditionalValue = ReadInt(apply, "s_nC", $"Skill {skillId} Apply {i + 1}"),
                            SkillCodeAssetId = skillCodeAssetId,
                            IncreaseValue = ReadInt(apply, "s_nIncrease_B_Point", $"Skill {skillId} Apply {i + 1}"),
                            Chance = ReadInt(apply, "s_nInvoke_Rate", $"Skill {skillId} Apply {i + 1}") / 100
                        });
                }

                List<SkillAssociation> orderedAssociations =
                    skillAssociations.TryGetValue(skillId, out List<SkillAssociation>? associations) && associations.Count > 0
                        ? associations.Distinct(SkillAssociationComparer.Instance).OrderBy(x => x.DigimonType).ThenBy(x => x.Slot).ToList()
                        : new List<SkillAssociation>();

                if (orderedAssociations.Count == 0)
                {
                    digimonSkillId++;
                    digimonSkills.Add(
                        new DigimonSkillRow
                        {
                            Id = digimonSkillId,
                            Type = 0,
                            Slot = 0,
                            SkillId = sqlSkillId
                        });

                    skillInfoId++;
                    skillInfos.Add(BuildSkillInfoRow(skillInfoId, node, skillId, sqlSkillId, 0));
                }
                else
                {
                    if (orderedAssociations.Count > 1)
                        sharedSkillAssociations += orderedAssociations.Count - 1;

                    foreach (SkillAssociation association in orderedAssociations)
                    {
                        int digimonType = CheckedInt(association.DigimonType, $"DigimonSkill Skill {skillId}");

                        digimonSkillId++;
                        digimonSkills.Add(
                            new DigimonSkillRow
                            {
                                Id = digimonSkillId,
                                Type = digimonType,
                                Slot = association.Slot,
                                SkillId = sqlSkillId
                            });

                        skillInfoId++;
                        skillInfos.Add(BuildSkillInfoRow(skillInfoId, node, skillId, sqlSkillId, digimonType));
                    }
                }
            }

            var seenPhysicalIds = new HashSet<uint>();

            foreach (SkillPhysicalOccurrence occurrence in occurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (seenPhysicalIds.Add(occurrence.SkillId))
                    continue;

                uint skillId = occurrence.SkillId;
                int sqlSkillId = CheckedInt(skillId, $"Skill {skillId}");

                skillInfoId++;
                skillInfos.Add(BuildSkillInfoRow(skillInfoId, occurrence.Node, skillId, sqlSkillId, 0));
            }

            if (skillApplies.Count != skillCodes.Count * 3)
                throw new InvalidDataException("Skill validation interna: SkillCodeApply != SkillCode * 3.");

            int expectedSkillInfos = digimonSkills.Count + duplicateSkillIdsCollapsed;

            if (skillInfos.Count != expectedSkillInfos)
            {
                throw new InvalidDataException(
                    $"SkillInfo cardinality inválida: esperado {expectedSkillInfos:N0} " +
                    $"(DigimonSkill {digimonSkills.Count:N0} + physical duplicates {duplicateSkillIdsCollapsed:N0}), " +
                    $"gerado {skillInfos.Count:N0}.");
            }

            log(
                $"Skill physical={physical.Count:N0}, unique={unique.Count:N0}, physical duplicates={duplicateSkillIdsCollapsed:N0}.");

            log(
                $"DigimonSkill mapping: {digimonSkills.Count:N0} rows; shared extra associations={sharedSkillAssociations:N0}; " +
                "unassociated Skill.xml IDs use Type=0, Slot=0.");

            log(
                $"SkillInfo mapping: {skillInfos.Count:N0} rows = DigimonSkill {digimonSkills.Count:N0} + physical duplicates {duplicateSkillIdsCollapsed:N0}.");

            log(
                "SkillCodeApply ground truth: Type=s_nID, Attribute=s_nA, Value=s_nB, AdditionalValue=s_nC, " +
                "IncreaseValue=s_nIncrease_B_Point, Chance=s_nInvoke_Rate/100.");

            log(
                "SkillInfo ground truth: Value=0, CastingTime=0, MemoryChips=0; " +
                "First/Second/ThirdConditionCode=s_nB de IncreaseApply 1/2/3; Type=DigimonSkill.Type (ou 0 sem associação).");
        }

        private static List<XElement> GetSkillApplies(XElement node, uint skillId)
        {
            List<XElement> applies =
                node.Element("SkillApply")?.Elements("IncreaseApply").ToList()
                ?? throw new InvalidDataException($"Skill {skillId}: SkillApply ausente.");

            if (applies.Count != 3)
                throw new InvalidDataException($"Skill {skillId}: esperado exatamente 3 IncreaseApply; encontrado {applies.Count}.");

            return applies;
        }

        private static SkillInfoRow BuildSkillInfoRow(
            int id,
            XElement node,
            uint skillId,
            int sqlSkillId,
            int digimonType)
        {
            List<XElement> applies = GetSkillApplies(node, skillId);

            return new SkillInfoRow
            {
                Id = id,
                SkillId = sqlSkillId,
                Name = ReadOptionalText(node, "s_szName"),
                DSUsage = ReadInt(node, "s_nUseDS", $"Skill {skillId}"),
                HPUsage = ReadInt(node, "s_nUseHP", $"Skill {skillId}"),
                Value = 0,
                CastingTime = 0,
                Cooldown = ReadIntLikeDecimal(node, "s_fCooldownTime", $"Skill {skillId}"),
                MaxLevel = ReadInt(node, "s_nMaxLevel", $"Skill {skillId}"),
                RequiredPoints = ReadInt(node, "s_nLevelupPoint", $"Skill {skillId}"),
                Target = ReadInt(node, "s_nTarget", $"Skill {skillId}"),
                AreaOfEffect = ReadInt(node, "s_nAttSphere", $"Skill {skillId}"),
                AoEMinDamage = ReadIntLikeDecimal(node, "s_fAttRange_MinDmg", $"Skill {skillId}"),
                AoEMaxDamage = ReadIntLikeDecimal(node, "s_fAttRange_MaxDmg", $"Skill {skillId}"),
                Range = ReadIntLikeDecimal(node, "s_fAttRange", $"Skill {skillId}"),
                UnlockLevel = ReadInt(node, "s_nLimitLevel", $"Skill {skillId}"),
                MemoryChips = 0,
                FirstConditionCode = ReadInt(applies[0], "s_nB", $"Skill {skillId} Apply 1"),
                SecondConditionCode = ReadInt(applies[1], "s_nB", $"Skill {skillId} Apply 2"),
                ThirdConditionCode = ReadInt(applies[2], "s_nB", $"Skill {skillId} Apply 3"),
                Type = digimonType,
                Description = ReadOptionalText(node, "s_szComment"),
                FamilyType = ReadInt(node, "s_nFamilyType", $"Skill {skillId}"),
                SkillType = ReadInt(node, "s_nSkillType", $"Skill {skillId}")
            };
        }

        private static async Task ClearCoreTablesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            string sql =
                $"""
                DELETE FROM {DigimonSkillTable};
                DELETE FROM {SkillCodeApplyTable};
                DELETE FROM {SkillInfoTable};
                DELETE FROM {SkillCodeTable};

                DELETE FROM {EvolutionStageTable};
                DELETE FROM {EvolutionLineTable};
                DELETE FROM {EvolutionTable};

                DELETE FROM {DigimonBaseInfoTable};

                DBCC CHECKIDENT ('dmo.Asset.DigimonSkill', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.SkillCodeApply', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.SkillInfo', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.SkillCode', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.EvolutionStage', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.EvolutionLine', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.Evolution', RESEED, 0);
                DBCC CHECKIDENT ('dmo.Asset.DigimonBaseInfo', RESEED, 0);
                """;

            await using var command =
                new SqlCommand(sql, connection, transaction)
                {
                    CommandTimeout = 180
                };

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task BulkInsertAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string destination,
            DataTable table,
            CancellationToken cancellationToken)
        {
            if (table.Rows.Count == 0)
                return;

            using var bulk =
                new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.KeepIdentity |
                    SqlBulkCopyOptions.CheckConstraints |
                    SqlBulkCopyOptions.KeepNulls,
                    transaction)
                {
                    DestinationTableName = destination,
                    BatchSize = 2000,
                    BulkCopyTimeout = 240,
                    EnableStreaming = true
                };

            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            await bulk.WriteToServerAsync(table, cancellationToken);
        }

        private static async Task VerifyInsertedCountsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PreparedImport prepared,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var expected =
                new Dictionary<string, int>
                {
                    [DigimonBaseInfoTable] = prepared.Digimons.Count,
                    [EvolutionTable] = prepared.Evolutions.Count,
                    [EvolutionLineTable] = prepared.EvolutionLines.Count,
                    [EvolutionStageTable] = prepared.EvolutionStages.Count,
                    [SkillCodeTable] = prepared.SkillCodes.Count,
                    [SkillCodeApplyTable] = prepared.SkillApplies.Count,
                    [SkillInfoTable] = prepared.SkillInfos.Count,
                    [DigimonSkillTable] = prepared.DigimonSkills.Count
                };

            foreach ((string table, int count) in expected)
            {
                await using var command =
                    new SqlCommand($"SELECT COUNT_BIG(*) FROM {table};", connection, transaction);

                object? scalar = await command.ExecuteScalarAsync(cancellationToken);
                long actual = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);

                if (actual != count)
                    throw new InvalidDataException($"Verificação SQL falhou em {table}: esperado={count}, atual={actual}.");

                log($"VERIFY OK: {table} = {actual:N0} rows.");
            }
        }

        private static DataTable BuildDigimonBaseInfoTable(IEnumerable<DigimonBaseRow> rows)
        {
            DataTable t = CreateTable(
                ("Id", typeof(int)), ("Type", typeof(int)), ("Model", typeof(int)),
                ("Name", typeof(string)), ("Level", typeof(int)), ("ScaleType", typeof(int)),
                ("Attribute", typeof(int)), ("Element", typeof(int)), ("Family1", typeof(int)),
                ("Family2", typeof(int)), ("Family3", typeof(int)), ("ASValue", typeof(int)),
                ("ARValue", typeof(int)), ("ATValue", typeof(int)), ("BLValue", typeof(int)),
                ("CTValue", typeof(int)), ("DEValue", typeof(int)), ("DSValue", typeof(int)),
                ("EVValue", typeof(int)), ("HPValue", typeof(int)), ("HTValue", typeof(int)),
                ("MSValue", typeof(int)), ("WSValue", typeof(int)), ("EvolutionType", typeof(int)));

            foreach (DigimonBaseRow r in rows)
                t.Rows.Add(r.Id, r.Type, r.Model, r.Name, r.Level, r.ScaleType, r.Attribute, r.Element,
                    r.Family1, r.Family2, r.Family3, r.ASValue, r.ARValue, r.ATValue, r.BLValue,
                    r.CTValue, r.DEValue, r.DSValue, r.EVValue, r.HPValue, r.HTValue, r.MSValue,
                    r.WSValue, r.EvolutionType);

            return t;
        }

        private static DataTable BuildEvolutionTable(IEnumerable<EvolutionRow> rows)
        {
            DataTable t = CreateTable(("Id", typeof(int)), ("Type", typeof(int)), ("EvolutionRank", typeof(int)));
            foreach (EvolutionRow r in rows)
                t.Rows.Add(r.Id, r.Type, r.EvolutionRank);
            return t;
        }

        private static DataTable BuildEvolutionLineTable(IEnumerable<EvolutionLineRow> rows)
        {
            DataTable t = CreateTable(
                ("Id", typeof(int)), ("EvolutionId", typeof(int)), ("Type", typeof(int)),
                ("UnlockItemSection", typeof(int)), ("UnlockItemSectionAmount", typeof(int)),
                ("UnlockLevel", typeof(int)), ("UnlockQuestId", typeof(int)), ("SlotLevel", typeof(int)),
                ("RequiredAmount", typeof(int)), ("RequiredItem", typeof(int)));

            foreach (EvolutionLineRow r in rows)
                t.Rows.Add(r.Id, r.EvolutionId, r.Type, r.UnlockItemSection, r.UnlockItemSectionAmount,
                    r.UnlockLevel, r.UnlockQuestId, r.SlotLevel, r.RequiredAmount, r.RequiredItem);

            return t;
        }

        private static DataTable BuildEvolutionStageTable(IEnumerable<EvolutionStageRow> rows)
        {
            DataTable t = CreateTable(("Id", typeof(int)), ("Type", typeof(int)), ("Value", typeof(int)), ("EvolutionLineId", typeof(int)));
            foreach (EvolutionStageRow r in rows)
                t.Rows.Add(r.Id, r.Type, r.Value, r.EvolutionLineId);
            return t;
        }

        private static DataTable BuildSkillCodeTable(IEnumerable<SkillCodeRow> rows)
        {
            DataTable t = CreateTable(("Id", typeof(int)), ("SkillCode", typeof(int)), ("Comment", typeof(string)));
            foreach (SkillCodeRow r in rows)
                t.Rows.Add(r.Id, r.SkillCode, r.Comment);
            return t;
        }

        private static DataTable BuildSkillCodeApplyTable(IEnumerable<SkillApplyRow> rows)
        {
            DataTable t = CreateTable(
                ("Id", typeof(int)), ("Type", typeof(int)), ("Attribute", typeof(int)),
                ("Value", typeof(int)), ("AdditionalValue", typeof(int)),
                ("SkillCodeAssetId", typeof(int)), ("IncreaseValue", typeof(int)), ("Chance", typeof(int)));

            foreach (SkillApplyRow r in rows)
                t.Rows.Add(r.Id, r.Type, r.Attribute, r.Value, r.AdditionalValue, r.SkillCodeAssetId, r.IncreaseValue, r.Chance);

            return t;
        }

        private static DataTable BuildSkillInfoTable(IEnumerable<SkillInfoRow> rows)
        {
            DataTable t = CreateTable(
                ("Id", typeof(int)), ("SkillId", typeof(int)), ("Name", typeof(string)),
                ("DSUsage", typeof(int)), ("HPUsage", typeof(int)), ("Value", typeof(int)),
                ("CastingTime", typeof(int)), ("Cooldown", typeof(int)), ("MaxLevel", typeof(int)),
                ("RequiredPoints", typeof(int)), ("Target", typeof(int)), ("AreaOfEffect", typeof(int)),
                ("AoEMinDamage", typeof(int)), ("AoEMaxDamage", typeof(int)), ("Range", typeof(int)),
                ("UnlockLevel", typeof(int)), ("MemoryChips", typeof(int)),
                ("FirstConditionCode", typeof(int)), ("SecondConditionCode", typeof(int)),
                ("ThirdConditionCode", typeof(int)), ("Type", typeof(int)),
                ("Description", typeof(string)), ("FamilyType", typeof(int)), ("SkillType", typeof(int)));

            foreach (SkillInfoRow r in rows)
                t.Rows.Add(r.Id, r.SkillId, r.Name, r.DSUsage, r.HPUsage, r.Value, r.CastingTime,
                    r.Cooldown, r.MaxLevel, r.RequiredPoints, r.Target, r.AreaOfEffect, r.AoEMinDamage,
                    r.AoEMaxDamage, r.Range, r.UnlockLevel, r.MemoryChips, r.FirstConditionCode,
                    r.SecondConditionCode, r.ThirdConditionCode, r.Type, r.Description, r.FamilyType, r.SkillType);

            return t;
        }

        private static DataTable BuildDigimonSkillTable(IEnumerable<DigimonSkillRow> rows)
        {
            DataTable t = CreateTable(("Id", typeof(int)), ("Type", typeof(int)), ("Slot", typeof(int)), ("SkillId", typeof(int)));
            foreach (DigimonSkillRow r in rows)
                t.Rows.Add(r.Id, r.Type, r.Slot, r.SkillId);
            return t;
        }

        private static DataTable CreateTable(params (string Name, Type Type)[] columns)
        {
            var table = new DataTable();
            foreach ((string name, Type type) in columns)
                table.Columns.Add(name, type);
            return table;
        }

        private static void EnsureFile(string path, string displayName)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"{displayName} não foi encontrado.", path);
        }

        private static string ReadText(XElement parent, string name, string context)
        {
            XElement? node = parent.Element(name);
            if (node == null)
                throw new InvalidDataException($"{context}: <{name}> ausente.");
            return node.Value.Trim();
        }

        private static string ReadOptionalText(XElement parent, string name) =>
            parent.Element(name)?.Value ?? string.Empty;

        private static int ReadInt(XElement parent, string name, string context) =>
            ParseInt(ReadText(parent, name, context), $"{context} <{name}>");

        private static uint ReadUInt(XElement parent, string name, string context)
        {
            string raw = ReadText(parent, name, context);
            if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
                throw new InvalidDataException($"{context} <{name}>='{raw}' não é UInt32 válido.");
            return value;
        }

        private static uint ReadUIntAttribute(XElement node, string name, string context)
        {
            string raw = node.Attribute(name)?.Value?.Trim()
                ?? throw new InvalidDataException($"{context}: atributo {name} ausente.");
            if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
                throw new InvalidDataException($"{context} @{name}='{raw}' não é UInt32 válido.");
            return value;
        }

        private static int ReadIntAttribute(XElement node, string name, string context)
        {
            string raw = node.Attribute(name)?.Value?.Trim()
                ?? throw new InvalidDataException($"{context}: atributo {name} ausente.");
            return ParseInt(raw, $"{context} @{name}");
        }

        private static int ParseInt(string raw, string context)
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                throw new InvalidDataException($"{context}='{raw}' não é inteiro válido.");
            if (value < int.MinValue || value > int.MaxValue)
                throw new OverflowException($"{context}={value} não cabe em SQL Int32.");
            return (int)value;
        }

        private static int ReadIntLikeDecimal(XElement parent, string name, string context)
        {
            string raw = ReadText(parent, name, context);
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
                throw new InvalidDataException($"{context} <{name}>='{raw}' não é numérico válido.");
            if (value < int.MinValue || value > int.MaxValue)
                throw new OverflowException($"{context} <{name}>={value} não cabe em SQL Int32.");
            return decimal.ToInt32(decimal.Truncate(value));
        }

        private static int[] ReadCsvTriple(string raw, string context)
        {
            string[] parts = raw.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
                throw new InvalidDataException($"{context}: esperado A,B,C; recebido '{raw}'.");
            return parts.Select((x, i) => ParseInt(x, $"{context}[{i}]")).ToArray();
        }

        private static int CheckedInt(uint value, string context)
        {
            if (value > int.MaxValue)
                throw new OverflowException($"{context}={value} não cabe em SQL Int32.");
            return (int)value;
        }

        private sealed class PreparedImport
        {
            public required List<DigimonBaseRow> Digimons { get; init; }
            public required List<EvolutionRow> Evolutions { get; init; }
            public required List<EvolutionLineRow> EvolutionLines { get; init; }
            public required List<EvolutionStageRow> EvolutionStages { get; init; }
            public required List<SkillCodeRow> SkillCodes { get; init; }
            public required List<SkillApplyRow> SkillApplies { get; init; }
            public required List<SkillInfoRow> SkillInfos { get; init; }
            public required List<DigimonSkillRow> DigimonSkills { get; init; }
            public int DuplicateSkillIdsCollapsed { get; init; }
            public required List<uint> MissingSkillReferences { get; init; }
            public int SharedSkillAssociations { get; init; }
        }

        private sealed class DigimonBaseRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int Model { get; init; }
            public string Name { get; init; } = string.Empty;
            public int Level { get; init; }
            public int ScaleType { get; init; }
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
            public int EvolutionType { get; init; }
        }

        private sealed class EvolutionRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int EvolutionRank { get; init; }
        }

        private sealed class EvolutionLineRow
        {
            public int Id { get; init; }
            public int EvolutionId { get; init; }
            public int Type { get; init; }
            public int UnlockItemSection { get; init; }
            public int UnlockItemSectionAmount { get; init; }
            public int UnlockLevel { get; init; }
            public int UnlockQuestId { get; init; }
            public int SlotLevel { get; init; }
            public int RequiredAmount { get; init; }
            public int RequiredItem { get; init; }
        }

        private sealed class EvolutionStageRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int Value { get; init; }
            public int EvolutionLineId { get; init; }
        }

        private sealed class SkillCodeRow
        {
            public int Id { get; init; }
            public int SkillCode { get; init; }
            public string Comment { get; init; } = string.Empty;
        }

        private sealed class SkillApplyRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int Attribute { get; init; }
            public int Value { get; init; }
            public int AdditionalValue { get; init; }
            public int SkillCodeAssetId { get; init; }
            public int IncreaseValue { get; init; }
            public int Chance { get; init; }
        }

        private sealed class SkillInfoRow
        {
            public int Id { get; init; }
            public int SkillId { get; init; }
            public string Name { get; init; } = string.Empty;
            public int DSUsage { get; init; }
            public int HPUsage { get; init; }
            public int Value { get; init; }
            public int CastingTime { get; init; }
            public int Cooldown { get; init; }
            public int MaxLevel { get; init; }
            public int RequiredPoints { get; init; }
            public int Target { get; init; }
            public int AreaOfEffect { get; init; }
            public int AoEMinDamage { get; init; }
            public int AoEMaxDamage { get; init; }
            public int Range { get; init; }
            public int UnlockLevel { get; init; }
            public int MemoryChips { get; init; }
            public int FirstConditionCode { get; init; }
            public int SecondConditionCode { get; init; }
            public int ThirdConditionCode { get; init; }
            public int Type { get; init; }
            public string Description { get; init; } = string.Empty;
            public int FamilyType { get; init; }
            public int SkillType { get; init; }
        }

        private sealed class DigimonSkillRow
        {
            public int Id { get; init; }
            public int Type { get; init; }
            public int Slot { get; init; }
            public int SkillId { get; init; }
        }

        private sealed class SkillPhysicalOccurrence
        {
            public required XElement Node { get; init; }
            public uint SkillId { get; init; }
        }

        private sealed class SkillAssociation
        {
            public uint DigimonType { get; init; }
            public int Slot { get; init; }
        }

        private sealed class SkillAssociationComparer : IEqualityComparer<SkillAssociation>
        {
            public static readonly SkillAssociationComparer Instance = new();

            public bool Equals(SkillAssociation? x, SkillAssociation? y) =>
                x != null && y != null &&
                x.DigimonType == y.DigimonType &&
                x.Slot == y.Slot;

            public int GetHashCode(SkillAssociation obj) =>
                HashCode.Combine(obj.DigimonType, obj.Slot);
        }
    }
}
