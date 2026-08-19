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
    public sealed class SkillDatabaseDiagnosticSummary
    {
        public int XmlSkills { get; init; }
        public int DatabaseSkillInfoRows { get; init; }
        public int DatabaseSkillCodeRows { get; init; }
        public int DatabaseSkillCodeApplyRows { get; init; }
        public int DatabaseDigimonSkillRows { get; init; }
        public int MissingXmlSkillsInDatabase { get; init; }
        public int MissingDatabaseSkillsInXml { get; init; }
        public string OutputFolder { get; init; } = string.Empty;
        public string MainLog { get; init; } = string.Empty;
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>
    /// READ-ONLY diagnostic. It never executes INSERT/UPDATE/DELETE.
    /// It compares the canonical Skill.xml and Digimon_List.xml with the
    /// CURRENT database, which is useful when the database has been restored
    /// to a known-good state. Reports are deliberately verbose so mappings can
    /// be inferred from evidence instead of guessed.
    /// </summary>
    public sealed class SkillDatabaseDiagnosticService
    {
        private static readonly string[] ApplyFields =
        {
            "s_nA",
            "s_nInvoke_Rate",
            "s_nB",
            "s_nC",
            "s_nBuffCode",
            "s_nID",
            "s_nIncrease_B_Point"
        };

        private static readonly string[] SkillScalarFields =
        {
            "s_nLevelupPoint",
            "s_nMaxLevel",
            "s_nAttributeType",
            "s_nNatureType",
            "s_nFamilyType",
            "s_nUseHP",
            "s_nUseDS",
            "s_nIcon",
            "s_nTarget",
            "s_nAttType",
            "s_fAttRange",
            "s_fAttRange_MinDmg",
            "s_fAttRange_NorDmg",
            "s_fAttRange_MaxDmg",
            "s_nAttSphere",
            "s_fCastingTime",
            "s_fDamageTime",
            "s_nDamageDay",
            "ink",
            "s_nDistanceTime",
            "s_fCooldownTime",
            "s_nCooldownDay",
            "unk",
            "s_fSkill_Velocity",
            "s_fSkill_Accel",
            "s_nSkillType",
            "s_nLimitLevel",
            "s_nSkillGroup",
            "s_nSkillRank",
            "s_nMemorySkill",
            "s_nReq_Item",
            "unk2"
        };

        public async Task<SkillDatabaseDiagnosticSummary> CompareAsync(
            string connectionString,
            string skillXml,
            string digimonListXml,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            string folder = Path.Combine(
                AppPaths.Logs,
                "SkillDatabaseDiagnostic",
                started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));

            Directory.CreateDirectory(folder);
            string logPath = Path.Combine(folder, "diagnostic.log");

            void Log(string text)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}";
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
                progress?.Report(line);
            }

            Log("SKILL DATABASE DIAGNOSTIC iniciado em modo READ-ONLY.");
            Log("Nenhum INSERT/UPDATE/DELETE será executado.");
            Log("A carregar Skill.xml...");

            Dictionary<int, XmlSkill> xmlSkills = LoadSkills(skillXml, cancellationToken);
            Dictionary<int, List<DigimonSkillRef>> xmlDigimonRefs = LoadDigimonSkillRefs(digimonListXml, cancellationToken);

            Log($"Skill.xml: {xmlSkills.Count:N0} Skill IDs únicos.");
            Log($"Digimon_List.xml: {xmlDigimonRefs.Sum(x => x.Value.Count):N0} associações de skills não-zero.");
            Log("A ler snapshot atual da database...");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            List<DbSkillInfo> skillInfos = await ReadSkillInfoAsync(connection, cancellationToken);
            List<DbSkillCode> skillCodes = await ReadSkillCodeAsync(connection, cancellationToken);
            List<DbSkillApply> skillApplies = await ReadSkillCodeApplyAsync(connection, cancellationToken);
            List<DbDigimonSkill> digimonSkills = await ReadDigimonSkillAsync(connection, cancellationToken);

            Log($"DB snapshot: SkillInfo={skillInfos.Count:N0}, SkillCode={skillCodes.Count:N0}, SkillCodeApply={skillApplies.Count:N0}, DigimonSkill={digimonSkills.Count:N0}.");

            cancellationToken.ThrowIfCancellationRequested();

            WriteSkillInfoRaw(folder, xmlSkills, skillInfos);
            Log("Gerado SkillInfo_RawComparison.csv.");

            WriteSkillInfoFieldMatchSummary(folder, xmlSkills, skillInfos);
            Log("Gerado SkillInfo_FieldMatchSummary.csv.");

            WriteApplyRaw(folder, xmlSkills, skillCodes, skillApplies);
            Log("Gerado SkillCodeApply_RawComparison.csv.");

            WriteApplyFieldMatchSummary(folder, xmlSkills, skillCodes, skillApplies);
            Log("Gerado SkillCodeApply_FieldMatchSummary.csv.");

            WriteSkillCodeComparison(folder, xmlSkills, skillCodes);
            Log("Gerado SkillCode_Comparison.csv.");

            WriteDigimonSkillComparison(folder, xmlDigimonRefs, digimonSkills);
            Log("Gerado DigimonSkill_Comparison.csv.");

            HashSet<int> xmlIds = xmlSkills.Keys.ToHashSet();
            HashSet<int> dbIds = skillInfos.Select(x => x.SkillId).ToHashSet();

            int missingXmlInDb = xmlIds.Count(x => !dbIds.Contains(x));
            int missingDbInXml = dbIds.Count(x => !xmlIds.Contains(x));

            Log($"Skill IDs XML sem SkillInfo correspondente: {missingXmlInDb:N0}.");
            Log($"SkillInfo IDs sem Skill.xml correspondente: {missingDbInXml:N0}.");

            WriteHighSignalReport(folder, xmlSkills, skillInfos, skillCodes, skillApplies, xmlDigimonRefs, digimonSkills);
            Log("Gerado HIGH_SIGNAL_REPORT.txt com os resultados mais úteis.");

            TimeSpan elapsed = DateTime.Now - started;
            Log($"DIAGNOSTIC concluído em {elapsed.TotalSeconds:N1}s. Pasta: {folder}");

            return new SkillDatabaseDiagnosticSummary
            {
                XmlSkills = xmlSkills.Count,
                DatabaseSkillInfoRows = skillInfos.Count,
                DatabaseSkillCodeRows = skillCodes.Count,
                DatabaseSkillCodeApplyRows = skillApplies.Count,
                DatabaseDigimonSkillRows = digimonSkills.Count,
                MissingXmlSkillsInDatabase = missingXmlInDb,
                MissingDatabaseSkillsInXml = missingDbInXml,
                OutputFolder = folder,
                MainLog = logPath,
                Elapsed = elapsed
            };
        }

        private static Dictionary<int, XmlSkill> LoadSkills(string path, CancellationToken token)
        {
            XDocument doc = XDocument.Load(path, LoadOptions.None);
            XElement root = doc.Root ?? throw new InvalidDataException("Skill.xml sem root.");
            var result = new Dictionary<int, XmlSkill>();

            foreach (XElement node in root.Elements("SkillData"))
            {
                token.ThrowIfCancellationRequested();
                int id = ReadInt(node, "s_dwID");
                if (result.ContainsKey(id))
                    continue;

                List<XElement> applies = node.Element("SkillApply")?.Elements("IncreaseApply").ToList()
                    ?? new List<XElement>();

                var applyValues = new List<Dictionary<string, decimal>>();
                foreach (XElement apply in applies)
                {
                    var dict = new Dictionary<string, decimal>(StringComparer.Ordinal);
                    foreach (string field in ApplyFields)
                        dict[field] = ReadDecimalOptional(apply, field);
                    applyValues.Add(dict);
                }

                var scalars = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (string field in SkillScalarFields)
                    scalars[field] = ReadDecimalOptional(node, field);

                result[id] = new XmlSkill
                {
                    Id = id,
                    Name = node.Element("s_szName")?.Value ?? string.Empty,
                    Comment = node.Element("s_szComment")?.Value ?? string.Empty,
                    Scalars = scalars,
                    Applies = applyValues
                };
            }

            return result;
        }

        private static Dictionary<int, List<DigimonSkillRef>> LoadDigimonSkillRefs(string path, CancellationToken token)
        {
            XDocument doc = XDocument.Load(path, LoadOptions.None);
            XElement root = doc.Root ?? throw new InvalidDataException("Digimon_List.xml sem root.");
            var result = new Dictionary<int, List<DigimonSkillRef>>();

            foreach (XElement digimon in root.Elements("Digimon"))
            {
                token.ThrowIfCancellationRequested();
                int type = ParseInt(digimon.Attribute("ID")?.Value ?? "0");
                foreach (XElement skill in digimon.Element("Skills")?.Elements("Skill") ?? Enumerable.Empty<XElement>())
                {
                    int skillId = ParseInt(skill.Attribute("ID")?.Value ?? "0");
                    int slot = ParseInt(skill.Attribute("Slot")?.Value ?? "0");
                    if (skillId == 0)
                        continue;

                    if (!result.TryGetValue(skillId, out List<DigimonSkillRef>? list))
                    {
                        list = new List<DigimonSkillRef>();
                        result.Add(skillId, list);
                    }

                    if (!list.Any(x => x.Type == type && x.Slot == slot))
                        list.Add(new DigimonSkillRef { Type = type, Slot = slot });
                }
            }

            return result;
        }

        private static async Task<List<DbSkillInfo>> ReadSkillInfoAsync(SqlConnection c, CancellationToken token)
        {
            const string sql = "SELECT Id,SkillId,Name,DSUsage,HPUsage,Value,CastingTime,Cooldown,MaxLevel,RequiredPoints,Target,AreaOfEffect,AoEMinDamage,AoEMaxDamage,Range,UnlockLevel,MemoryChips,FirstConditionCode,SecondConditionCode,ThirdConditionCode,Type,Description,FamilyType,SkillType FROM [dmo].[Asset].[SkillInfo] ORDER BY Id;";
            var result = new List<DbSkillInfo>();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                result.Add(new DbSkillInfo
                {
                    Id = I(r,0), SkillId = I(r,1), Name = S(r,2), DSUsage = I(r,3), HPUsage = I(r,4), Value = I(r,5), CastingTime = D(r,6), Cooldown = I(r,7), MaxLevel = I(r,8), RequiredPoints = I(r,9), Target = I(r,10), AreaOfEffect = I(r,11), AoEMinDamage = I(r,12), AoEMaxDamage = I(r,13), Range = I(r,14), UnlockLevel = I(r,15), MemoryChips = I(r,16), FirstConditionCode = I(r,17), SecondConditionCode = I(r,18), ThirdConditionCode = I(r,19), Type = I(r,20), Description = S(r,21), FamilyType = I(r,22), SkillType = I(r,23)
                });
            }
            return result;
        }

        private static async Task<List<DbSkillCode>> ReadSkillCodeAsync(SqlConnection c, CancellationToken token)
        {
            const string sql = "SELECT Id,SkillCode,Comment FROM [dmo].[Asset].[SkillCode] ORDER BY Id;";
            var result = new List<DbSkillCode>();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                result.Add(new DbSkillCode { Id = I(r,0), SkillCode = I(r,1), Comment = S(r,2) });
            return result;
        }

        private static async Task<List<DbSkillApply>> ReadSkillCodeApplyAsync(SqlConnection c, CancellationToken token)
        {
            const string sql = "SELECT Id,Type,Attribute,Value,AdditionalValue,SkillCodeAssetId,IncreaseValue,Chance FROM [dmo].[Asset].[SkillCodeApply] ORDER BY SkillCodeAssetId,Id;";
            var result = new List<DbSkillApply>();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                result.Add(new DbSkillApply { Id=I(r,0), Type=I(r,1), Attribute=I(r,2), Value=I(r,3), AdditionalValue=I(r,4), SkillCodeAssetId=I(r,5), IncreaseValue=I(r,6), Chance=I(r,7) });
            return result;
        }

        private static async Task<List<DbDigimonSkill>> ReadDigimonSkillAsync(SqlConnection c, CancellationToken token)
        {
            const string sql = "SELECT Id,Type,Slot,SkillId FROM [dmo].[Asset].[DigimonSkill] ORDER BY Id;";
            var result = new List<DbDigimonSkill>();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                result.Add(new DbDigimonSkill { Id=I(r,0), Type=I(r,1), Slot=I(r,2), SkillId=I(r,3) });
            return result;
        }

        private static void WriteSkillInfoRaw(string folder, Dictionary<int, XmlSkill> xml, List<DbSkillInfo> db)
        {
            string path = Path.Combine(folder, "SkillInfo_RawComparison.csv");
            using var w = NewCsv(path);
            var header = new List<string> { "DB_Id","SkillId","DB_Name","XML_Name","DB_DSUsage","DB_HPUsage","DB_Value","DB_CastingTime","DB_Cooldown","DB_MaxLevel","DB_RequiredPoints","DB_Target","DB_AreaOfEffect","DB_AoEMinDamage","DB_AoEMaxDamage","DB_Range","DB_UnlockLevel","DB_MemoryChips","DB_FirstConditionCode","DB_SecondConditionCode","DB_ThirdConditionCode","DB_Type","DB_FamilyType","DB_SkillType" };
            header.AddRange(SkillScalarFields.Select(x => "XML_" + x));
            for (int a=0;a<3;a++) header.AddRange(ApplyFields.Select(x => $"XML_A{a+1}_{x}"));
            Csv(w, header);

            foreach (DbSkillInfo d in db)
            {
                xml.TryGetValue(d.SkillId, out XmlSkill? x);
                var row = new List<object?> { d.Id,d.SkillId,d.Name,x?.Name,d.DSUsage,d.HPUsage,d.Value,d.CastingTime,d.Cooldown,d.MaxLevel,d.RequiredPoints,d.Target,d.AreaOfEffect,d.AoEMinDamage,d.AoEMaxDamage,d.Range,d.UnlockLevel,d.MemoryChips,d.FirstConditionCode,d.SecondConditionCode,d.ThirdConditionCode,d.Type,d.FamilyType,d.SkillType };
                foreach (string f in SkillScalarFields) row.Add(x != null && x.Scalars.TryGetValue(f,out decimal v) ? v : null);
                for (int a=0;a<3;a++) foreach (string f in ApplyFields) row.Add(x != null && x.Applies.Count>a && x.Applies[a].TryGetValue(f,out decimal v) ? v : null);
                Csv(w,row);
            }
        }

        private static void WriteSkillInfoFieldMatchSummary(string folder, Dictionary<int, XmlSkill> xml, List<DbSkillInfo> db)
        {
            string path = Path.Combine(folder, "SkillInfo_FieldMatchSummary.csv");
            using var w = NewCsv(path);
            Csv(w, new object[] { "DB_Field","XML_Candidate","Transform","Compared","ExactMatches","MatchPercent" });

            var dbFields = new Dictionary<string, Func<DbSkillInfo, decimal>>
            {
                ["DSUsage"] = x=>x.DSUsage, ["HPUsage"] = x=>x.HPUsage, ["Value"] = x=>x.Value,
                ["CastingTime"] = x=>x.CastingTime, ["Cooldown"] = x=>x.Cooldown, ["MaxLevel"] = x=>x.MaxLevel,
                ["RequiredPoints"] = x=>x.RequiredPoints, ["Target"] = x=>x.Target, ["AreaOfEffect"] = x=>x.AreaOfEffect,
                ["AoEMinDamage"] = x=>x.AoEMinDamage, ["AoEMaxDamage"] = x=>x.AoEMaxDamage, ["Range"] = x=>x.Range,
                ["UnlockLevel"] = x=>x.UnlockLevel, ["MemoryChips"] = x=>x.MemoryChips,
                ["FirstConditionCode"] = x=>x.FirstConditionCode, ["SecondConditionCode"] = x=>x.SecondConditionCode, ["ThirdConditionCode"] = x=>x.ThirdConditionCode,
                ["Type"] = x=>x.Type, ["FamilyType"] = x=>x.FamilyType, ["SkillType"] = x=>x.SkillType
            };

            var candidates = new List<(string Name, Func<XmlSkill, decimal> Get, string Transform)>();
            foreach (string f in SkillScalarFields)
            {
                string local = f;
                candidates.Add((local, x=>x.Scalars.TryGetValue(local,out decimal v)?v:0m, "raw"));
                candidates.Add((local, x=>decimal.Truncate(x.Scalars.TryGetValue(local,out decimal v)?v:0m), "truncate"));
            }
            for (int a=0;a<3;a++)
            {
                int ai=a;
                foreach (string f in ApplyFields)
                {
                    string local=f;
                    candidates.Add(($"Apply{a+1}.{local}", x=>x.Applies.Count>ai && x.Applies[ai].TryGetValue(local,out decimal v)?v:0m, "raw"));
                    if (f=="s_nInvoke_Rate") candidates.Add(($"Apply{a+1}.{local}", x=>x.Applies.Count>ai && x.Applies[ai].TryGetValue(local,out decimal v)?decimal.Truncate(v/100m):0m, "/100 truncate"));
                }
            }

            foreach (var dbf in dbFields)
            {
                var ranked = new List<(string Name,string Transform,int Compared,int Matches,double Percent)>();
                foreach (var candidate in candidates)
                {
                    int compared=0, matches=0;
                    foreach (DbSkillInfo d in db)
                    {
                        if (!xml.TryGetValue(d.SkillId,out XmlSkill? x)) continue;
                        compared++;
                        if (dbf.Value(d)==candidate.Get(x)) matches++;
                    }
                    double pct = compared==0 ? 0 : matches*100.0/compared;
                    ranked.Add((candidate.Name,candidate.Transform,compared,matches,pct));
                }
                foreach (var r in ranked.OrderByDescending(x=>x.Percent).ThenByDescending(x=>x.Matches).Take(12))
                    Csv(w,new object[]{dbf.Key,r.Name,r.Transform,r.Compared,r.Matches,r.Percent.ToString("F4",CultureInfo.InvariantCulture)});
            }
        }

        private static void WriteApplyRaw(string folder, Dictionary<int, XmlSkill> xml, List<DbSkillCode> codes, List<DbSkillApply> db)
        {
            string path=Path.Combine(folder,"SkillCodeApply_RawComparison.csv");
            using var w=NewCsv(path);
            Csv(w,new object[]{"DB_Id","SkillCodeAssetId","SkillId","ApplyIndex","DB_Type","DB_Attribute","DB_Value","DB_AdditionalValue","DB_IncreaseValue","DB_Chance","XML_s_nA","XML_s_nInvoke_Rate","XML_InvokeRateDiv100","XML_s_nB","XML_s_nC","XML_s_nBuffCode","XML_s_nID","XML_s_nIncrease_B_Point"});
            var codeById=codes.ToDictionary(x=>x.Id,x=>x.SkillCode);
            foreach (var group in db.GroupBy(x=>x.SkillCodeAssetId))
            {
                if (!codeById.TryGetValue(group.Key,out int skillId)) skillId=0;
                int index=0;
                foreach (DbSkillApply d in group.OrderBy(x=>x.Id))
                {
                    XmlSkill? x = xml.TryGetValue(skillId,out XmlSkill? found)?found:null;
                    Dictionary<string,decimal>? a = x!=null && x.Applies.Count>index ? x.Applies[index] : null;
                    decimal G(string n)=>a!=null && a.TryGetValue(n,out decimal v)?v:0m;
                    Csv(w,new object[]{d.Id,d.SkillCodeAssetId,skillId,index+1,d.Type,d.Attribute,d.Value,d.AdditionalValue,d.IncreaseValue,d.Chance,G("s_nA"),G("s_nInvoke_Rate"),decimal.Truncate(G("s_nInvoke_Rate")/100m),G("s_nB"),G("s_nC"),G("s_nBuffCode"),G("s_nID"),G("s_nIncrease_B_Point")});
                    index++;
                }
            }
        }

        private static void WriteApplyFieldMatchSummary(string folder, Dictionary<int, XmlSkill> xml, List<DbSkillCode> codes, List<DbSkillApply> db)
        {
            string path=Path.Combine(folder,"SkillCodeApply_FieldMatchSummary.csv");
            using var w=NewCsv(path);
            Csv(w,new object[]{"DB_Field","XML_Candidate","Transform","Compared","ExactMatches","MatchPercent"});
            var codeById=codes.ToDictionary(x=>x.Id,x=>x.SkillCode);
            var samples=new List<(DbSkillApply Db, XmlSkill Xml, int ApplyIndex)>();
            foreach(var g in db.GroupBy(x=>x.SkillCodeAssetId))
            {
                if(!codeById.TryGetValue(g.Key,out int sid) || !xml.TryGetValue(sid,out XmlSkill? xs)) continue;
                int i=0;
                foreach(var d in g.OrderBy(x=>x.Id)) { if(i<xs.Applies.Count) samples.Add((d,xs,i)); i++; }
            }
            var dbFields=new Dictionary<string,Func<DbSkillApply,decimal>>{{"Type",x=>x.Type},{"Attribute",x=>x.Attribute},{"Value",x=>x.Value},{"AdditionalValue",x=>x.AdditionalValue},{"IncreaseValue",x=>x.IncreaseValue},{"Chance",x=>x.Chance}};
            foreach(var dbf in dbFields)
            {
                var ranked=new List<(string Name,string Transform,int C,int M,double P)>();
                foreach(string f in ApplyFields)
                {
                    foreach(string transform in f=="s_nInvoke_Rate"?new[]{"raw","/100 truncate"}:new[]{"raw"})
                    {
                        int c=0,m=0;
                        foreach(var s in samples)
                        {
                            decimal v=s.Xml.Applies[s.ApplyIndex].TryGetValue(f,out decimal raw)?raw:0m;
                            if(transform=="/100 truncate") v=decimal.Truncate(v/100m);
                            c++; if(dbf.Value(s.Db)==v)m++;
                        }
                        double p=c==0?0:m*100.0/c;
                        ranked.Add((f,transform,c,m,p));
                    }
                }
                foreach(var r in ranked.OrderByDescending(x=>x.P).ThenByDescending(x=>x.M))
                    Csv(w,new object[]{dbf.Key,r.Name,r.Transform,r.C,r.M,r.P.ToString("F4",CultureInfo.InvariantCulture)});
            }
        }

        private static void WriteSkillCodeComparison(string folder, Dictionary<int, XmlSkill> xml, List<DbSkillCode> db)
        {
            using var w=NewCsv(Path.Combine(folder,"SkillCode_Comparison.csv"));
            Csv(w,new object[]{"DB_Id","DB_SkillCode","ExistsInXML","DB_Comment","XML_Comment","CommentExactMatch"});
            foreach(var d in db)
            {
                bool ok=xml.TryGetValue(d.SkillCode,out XmlSkill? x);
                Csv(w,new object[]{d.Id,d.SkillCode,ok,d.Comment,x?.Comment,ok && string.Equals(d.Comment,x!.Comment,StringComparison.Ordinal)});
            }
        }

        private static void WriteDigimonSkillComparison(string folder, Dictionary<int,List<DigimonSkillRef>> xml, List<DbDigimonSkill> db)
        {
            using var w=NewCsv(Path.Combine(folder,"DigimonSkill_Comparison.csv"));
            Csv(w,new object[]{"DB_Id","SkillId","DB_Type","DB_Slot","XML_HasExactAssociation","XML_AssociationsForSkill"});
            foreach(var d in db)
            {
                bool has=xml.TryGetValue(d.SkillId,out List<DigimonSkillRef>? refs);
                bool exact=has && refs!.Any(x=>x.Type==d.Type && x.Slot==d.Slot);
                string all=has?string.Join(" | ",refs!.Select(x=>$"{x.Type}:{x.Slot}")):string.Empty;
                Csv(w,new object[]{d.Id,d.SkillId,d.Type,d.Slot,exact,all});
            }
        }

        private static void WriteHighSignalReport(string folder, Dictionary<int,XmlSkill> xml, List<DbSkillInfo> infos, List<DbSkillCode> codes, List<DbSkillApply> applies, Dictionary<int,List<DigimonSkillRef>> xmlRefs, List<DbDigimonSkill> dbRefs)
        {
            var sb=new StringBuilder();
            sb.AppendLine("SKILL DATABASE DIAGNOSTIC - HIGH SIGNAL REPORT");
            sb.AppendLine("==============================================");
            sb.AppendLine("This report is READ-ONLY evidence from the currently restored database.");
            sb.AppendLine();
            sb.AppendLine($"XML unique skills: {xml.Count}");
            sb.AppendLine($"DB SkillInfo: {infos.Count}");
            sb.AppendLine($"DB SkillCode: {codes.Count}");
            sb.AppendLine($"DB SkillCodeApply: {applies.Count}");
            sb.AppendLine($"DB DigimonSkill: {dbRefs.Count}");
            sb.AppendLine();

            int[] probes={7700511,7700521,7700531,7700541,7700611,7700631,7700711,7700741,7700911,7700921};
            sb.AppendLine("PROBE SKILLS");
            sb.AppendLine("------------");
            foreach(int id in probes)
            {
                DbSkillInfo? d=infos.FirstOrDefault(x=>x.SkillId==id);
                if(d==null || !xml.TryGetValue(id,out XmlSkill? x)) continue;
                sb.AppendLine($"Skill {id} {d.Name}");
                sb.AppendLine($"  DB: Value={d.Value} Conditions={d.FirstConditionCode}/{d.SecondConditionCode}/{d.ThirdConditionCode} Type={d.Type} Family={d.FamilyType} SkillType={d.SkillType}");
                sb.AppendLine($"  XML: NorDmg={Get(x,"s_fAttRange_NorDmg")} AttType={Get(x,"s_nAttType")} Family={Get(x,"s_nFamilyType")} SkillType={Get(x,"s_nSkillType")}");
                for(int a=0;a<x.Applies.Count;a++) sb.AppendLine($"  A{a+1}: A={GA(x,a,"s_nA")} Rate={GA(x,a,"s_nInvoke_Rate")} B={GA(x,a,"s_nB")} C={GA(x,a,"s_nC")} Buff={GA(x,a,"s_nBuffCode")} ID={GA(x,a,"s_nID")} Inc={GA(x,a,"s_nIncrease_B_Point")}");
            }
            sb.AppendLine();
            int exactRefs=dbRefs.Count(d=>xmlRefs.TryGetValue(d.SkillId,out List<DigimonSkillRef>? r) && r.Any(x=>x.Type==d.Type && x.Slot==d.Slot));
            sb.AppendLine($"DigimonSkill exact XML associations: {exactRefs}/{dbRefs.Count} ({(dbRefs.Count==0?0:exactRefs*100.0/dbRefs.Count):F2}%).");
            sb.AppendLine();
            sb.AppendLine("Use SkillInfo_FieldMatchSummary.csv and SkillCodeApply_FieldMatchSummary.csv first.");
            sb.AppendLine("For every SQL field they rank XML candidates by exact-match percentage across the entire restored database.");
            File.WriteAllText(Path.Combine(folder,"HIGH_SIGNAL_REPORT.txt"),sb.ToString(),Encoding.UTF8);
        }

        private static decimal Get(XmlSkill x,string f)=>x.Scalars.TryGetValue(f,out decimal v)?v:0m;
        private static decimal GA(XmlSkill x,int a,string f)=>x.Applies.Count>a && x.Applies[a].TryGetValue(f,out decimal v)?v:0m;
        private static StreamWriter NewCsv(string path)=>new(path,false,new UTF8Encoding(true));
        private static void Csv(StreamWriter w,IEnumerable<object?> values)=>w.WriteLine(string.Join(",",values.Select(CsvValue)));
        private static string CsvValue(object? value)
        {
            string s=value switch { null=>string.Empty, IFormattable f=>f.ToString(null,CultureInfo.InvariantCulture)??string.Empty, _=>value.ToString()??string.Empty };
            return "\""+s.Replace("\"","\"\"")+"\"";
        }
        private static int I(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt32(r.GetValue(i),CultureInfo.InvariantCulture);
        private static decimal D(SqlDataReader r,int i)=>r.IsDBNull(i)?0m:Convert.ToDecimal(r.GetValue(i),CultureInfo.InvariantCulture);
        private static string S(SqlDataReader r,int i)=>r.IsDBNull(i)?string.Empty:Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??string.Empty;
        private static int ReadInt(XElement p,string n)=>ParseInt(p.Element(n)?.Value??"0");
        private static int ParseInt(string raw)=>int.TryParse(raw.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int v)?v:0;
        private static decimal ReadDecimalOptional(XElement p,string n)=>decimal.TryParse((p.Element(n)?.Value??"0").Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out decimal v)?v:0m;

        private sealed class XmlSkill { public int Id {get;init;} public string Name {get;init;}=string.Empty; public string Comment {get;init;}=string.Empty; public required Dictionary<string,decimal> Scalars {get;init;} public required List<Dictionary<string,decimal>> Applies {get;init;} }
        private sealed class DigimonSkillRef { public int Type {get;init;} public int Slot {get;init;} }
        private sealed class DbSkillCode { public int Id {get;init;} public int SkillCode {get;init;} public string Comment {get;init;}=string.Empty; }
        private sealed class DbSkillApply { public int Id {get;init;} public int Type {get;init;} public int Attribute {get;init;} public int Value {get;init;} public int AdditionalValue {get;init;} public int SkillCodeAssetId {get;init;} public int IncreaseValue {get;init;} public int Chance {get;init;} }
        private sealed class DbDigimonSkill { public int Id {get;init;} public int Type {get;init;} public int Slot {get;init;} public int SkillId {get;init;} }
        private sealed class DbSkillInfo
        {
            public int Id {get;init;} public int SkillId {get;init;} public string Name {get;init;}=string.Empty; public int DSUsage {get;init;} public int HPUsage {get;init;} public int Value {get;init;} public decimal CastingTime {get;init;} public int Cooldown {get;init;} public int MaxLevel {get;init;} public int RequiredPoints {get;init;} public int Target {get;init;} public int AreaOfEffect {get;init;} public int AoEMinDamage {get;init;} public int AoEMaxDamage {get;init;} public int Range {get;init;} public int UnlockLevel {get;init;} public int MemoryChips {get;init;} public int FirstConditionCode {get;init;} public int SecondConditionCode {get;init;} public int ThirdConditionCode {get;init;} public int Type {get;init;} public string Description {get;init;}=string.Empty; public int FamilyType {get;init;} public int SkillType {get;init;}
        }
    }
}
