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
    public sealed class DigimonBookDatabaseDiagnosticSummary
    {
        public int BookInfoXmlRows { get; init; }
        public int DeckOptionXmlRows { get; init; }
        public int DeckBookInfoDbRows { get; init; }
        public int DeckBuffDbRows { get; init; }
        public int DeckBuffOptionDbRows { get; init; }
        public string OutputFolder { get; init; } = string.Empty;
        public string HighSignalReport { get; init; } = string.Empty;
        public TimeSpan Elapsed { get; init; }
    }

    internal sealed record DeckBookXml(int OptionId, string Name, string Explain);
    internal sealed record DeckBookDb(int Id, int OptionId, int Type, string Name, string Explain);
    internal sealed record DeckBuffXml(int GroupId, string Name, string Explain, int[] Condition, int[] AtType, int[] Option, int[] Value, int[] Prob, int[] Time);
    internal sealed record DeckBuffDb(int Id, int GroupId, string Name, string Explain);
    internal sealed record DeckBuffOptionDb(int Id, int GroupId, int Condition, int AtType, int Value, int Prob, int Time, int OptionId);

    public sealed class DigimonBookDatabaseDiagnosticService
    {
        public async Task<DigimonBookDatabaseDiagnosticSummary> CompareAsync(
            string connectionString,
            string digimonBookFolder,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DateTime started = DateTime.Now;
            string output = Path.Combine(AppPaths.Logs, "DigimonBookDatabaseDiagnostic", started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            string logFile = Path.Combine(output, "diagnostic.log");

            void Log(string text)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}";
                File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
                progress?.Report(line);
            }

            string bookPath = Path.Combine(digimonBookFolder, "BookInfo.xml");
            string deckPath = Path.Combine(digimonBookFolder, "DeckOption.xml");
            if (!File.Exists(bookPath) || !File.Exists(deckPath))
                throw new FileNotFoundException("BookInfo.xml and DeckOption.xml are required for the database diagnostic.");

            Log("DIGIMON BOOK DATABASE DIAGNOSTIC started in READ-ONLY mode.");
            Log("No INSERT / UPDATE / DELETE is executed.");

            List<DeckBookXml> books = LoadBookInfo(bookPath);
            List<DeckBuffXml> decks = LoadDeckOptions(deckPath);
            Log($"XML loaded: BookInfo={books.Count:N0}, DeckOption={decks.Count:N0}.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            List<DeckBookDb> dbBooks = await ReadBookDb(connection, cancellationToken);
            List<DeckBuffDb> dbBuffs = await ReadBuffDb(connection, cancellationToken);
            List<DeckBuffOptionDb> dbOptions = await ReadOptionDb(connection, cancellationToken);
            Log($"DB snapshot: DeckBookInfo={dbBooks.Count:N0}, DeckBuff={dbBuffs.Count:N0}, DeckBuffOption={dbOptions.Count:N0}.");

            string bookReport = WriteBookComparison(output, books, dbBooks);
            string buffReport = WriteBuffComparison(output, decks, dbBuffs);
            OptionAnalysis options = WriteOptionAnalysis(output, decks, dbOptions);
            string high = WriteHighSignal(output, books, decks, dbBooks, dbBuffs, dbOptions, bookReport, buffReport, options);

            TimeSpan elapsed = DateTime.Now - started;
            Log($"Diagnostic completed in {elapsed.TotalSeconds:N1}s. Output: {output}");
            return new DigimonBookDatabaseDiagnosticSummary
            {
                BookInfoXmlRows = books.Count,
                DeckOptionXmlRows = decks.Count,
                DeckBookInfoDbRows = dbBooks.Count,
                DeckBuffDbRows = dbBuffs.Count,
                DeckBuffOptionDbRows = dbOptions.Count,
                OutputFolder = output,
                HighSignalReport = high,
                Elapsed = elapsed
            };
        }

        private static List<DeckBookXml> LoadBookInfo(string path) =>
            (XDocument.Load(path).Root?.Elements("BookInfo") ?? Enumerable.Empty<XElement>())
                .Select(x => new DeckBookXml(I(x,"s_dwOptID"),T(x,"s_szOptName"),T(x,"s_szOptExplain"))).ToList();

        private static List<DeckBuffXml> LoadDeckOptions(string path) =>
            (XDocument.Load(path).Root?.Elements("DeckOption") ?? Enumerable.Empty<XElement>())
                .Select(x => new DeckBuffXml(
                    I(x,"s_nGroupIdx"), T(x,"s_szGroupName"), T(x,"s_szExplain"),
                    A(x,"s_nCondition","condition"), A(x,"s_nAT_Type","atType"), A(x,"s_nOption","option"),
                    A(x,"s_nVal","value"), A(x,"s_nProb","prob"), A(x,"s_nTime","time")))
                .ToList();

        private static async Task<List<DeckBookDb>> ReadBookDb(SqlConnection c, CancellationToken token)
        {
            var rows = new List<DeckBookDb>();
            await using var cmd = new SqlCommand("SELECT [Id],[OptionId],[Type],[Name],[Explain] FROM [dmo].[Asset].[DeckBookInfo] ORDER BY [Id]", c);
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token)) rows.Add(new DeckBookDb(N(r,0),N(r,1),N(r,2),S(r,3),S(r,4)));
            return rows;
        }
        private static async Task<List<DeckBuffDb>> ReadBuffDb(SqlConnection c, CancellationToken token)
        {
            var rows = new List<DeckBuffDb>();
            await using var cmd = new SqlCommand("SELECT [Id],[GroupIdX],[GroupName],[Explain] FROM [dmo].[Asset].[DeckBuff] ORDER BY [Id]", c);
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token)) rows.Add(new DeckBuffDb(N(r,0),N(r,1),S(r,2),S(r,3)));
            return rows;
        }
        private static async Task<List<DeckBuffOptionDb>> ReadOptionDb(SqlConnection c, CancellationToken token)
        {
            var rows = new List<DeckBuffOptionDb>();
            await using var cmd = new SqlCommand("SELECT [Id],[GroupIdX],[Condition],[AtType],[Value],[Prob],[Time],[OptionId] FROM [dmo].[Asset].[DeckBuffOption] ORDER BY [Id]", c);
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token)) rows.Add(new DeckBuffOptionDb(N(r,0),N(r,1),N(r,2),N(r,3),N(r,4),N(r,5),N(r,6),N(r,7)));
            return rows;
        }

        private static string WriteBookComparison(string folder, List<DeckBookXml> xml, List<DeckBookDb> db)
        {
            string path = Path.Combine(folder, "DeckBookInfo_Comparison.csv");
            var candidates = new List<DeckBookXml> { new(0,"None","None.") };
            candidates.AddRange(xml);
            using var w = new StreamWriter(path,false,new UTF8Encoding(true));
            w.WriteLine("DB_Id,DB_OptionId,DB_Type,DB_Name,DB_Explain,XML_OptionId,XML_Name,XML_Explain,OptionIdMatch,TypeEqOptionId,NameMatch,ExplainMatch");
            foreach (DeckBookDb d in db)
            {
                DeckBookXml? x = candidates.FirstOrDefault(z => z.OptionId == d.OptionId);
                w.WriteLine(string.Join(",", new[] { d.Id.ToString(),d.OptionId.ToString(),d.Type.ToString(),Csv(d.Name),Csv(d.Explain),
                    (x?.OptionId??-1).ToString(),Csv(x?.Name),Csv(x?.Explain),B(x?.OptionId==d.OptionId),B(d.Type==d.OptionId),B(x?.Name==d.Name),B(x?.Explain==d.Explain) }));
            }
            return path;
        }

        private static string WriteBuffComparison(string folder, List<DeckBuffXml> xml, List<DeckBuffDb> db)
        {
            string path = Path.Combine(folder, "DeckBuff_Comparison.csv");
            using var w = new StreamWriter(path,false,new UTF8Encoding(true));
            w.WriteLine("DB_Id,DB_GroupId,XML_GroupId,DB_GroupName,XML_GroupName,DB_Explain,XML_Explain,GroupMatch,NameMatch,ExplainMatch");
            foreach (DeckBuffDb d in db)
            {
                DeckBuffXml? x = xml.FirstOrDefault(z=>z.GroupId==d.GroupId);
                w.WriteLine(string.Join(",",new[]{d.Id.ToString(),d.GroupId.ToString(),(x?.GroupId??-1).ToString(),Csv(d.Name),Csv(x?.Name),Csv(d.Explain),Csv(x?.Explain),B(x?.GroupId==d.GroupId),B(x?.Name==d.Name),B(x?.Explain==d.Explain)}));
            }
            return path;
        }

        private sealed record OptionCandidate(string DbField,string XmlField,string Order,double Percent,int Exact,int Compared);
        private sealed class OptionAnalysis
        {
            public List<OptionCandidate> Candidates { get; } = new();
            public string RawPath { get; set; } = string.Empty;
            public string CandidatePath { get; set; } = string.Empty;
        }

        private static OptionAnalysis WriteOptionAnalysis(string folder, List<DeckBuffXml> xml, List<DeckBuffOptionDb> db)
        {
            var result = new OptionAnalysis();
            result.RawPath = Path.Combine(folder,"DeckBuffOption_Raw.csv");
            using (var w = new StreamWriter(result.RawPath,false,new UTF8Encoding(true)))
            {
                w.WriteLine("DB_Id,GroupId,DB_Condition,DB_AtType,DB_Value,DB_Prob,DB_Time,DB_OptionId,XML_Slot0_Condition,XML_Slot0_AT,XML_Slot0_Option,XML_Slot0_Value,XML_Slot0_Prob,XML_Slot0_Time,XML_Slot1_Condition,XML_Slot1_AT,XML_Slot1_Option,XML_Slot1_Value,XML_Slot1_Prob,XML_Slot1_Time,XML_Slot2_Condition,XML_Slot2_AT,XML_Slot2_Option,XML_Slot2_Value,XML_Slot2_Prob,XML_Slot2_Time");
                foreach (DeckBuffOptionDb d in db)
                {
                    DeckBuffXml? x=xml.FirstOrDefault(z=>z.GroupId==d.GroupId);
                    if(x==null) continue;
                    var values=new List<string>{d.Id.ToString(),d.GroupId.ToString(),d.Condition.ToString(),d.AtType.ToString(),d.Value.ToString(),d.Prob.ToString(),d.Time.ToString(),d.OptionId.ToString()};
                    for(int i=0;i<3;i++) values.AddRange(new[]{At(x.Condition,i),At(x.AtType,i),At(x.Option,i),At(x.Value,i),At(x.Prob,i),At(x.Time,i)}.Select(v=>v.ToString(CultureInfo.InvariantCulture)));
                    w.WriteLine(string.Join(",",values));
                }
            }

            var groupedDb=db.GroupBy(x=>x.GroupId).ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.Id).Take(3).ToList());
            var pairs = new List<(DeckBuffOptionDb Db, DeckBuffXml Xml, int Position)>();
            foreach(DeckBuffXml x in xml)
                if(groupedDb.TryGetValue(x.GroupId,out List<DeckBuffOptionDb>? group))
                    for(int i=0;i<Math.Min(3,group.Count);i++) pairs.Add((group[i],x,i));

            string[] orders={"Direct","Reverse"};
            foreach(string order in orders)
            {
                int Slot((DeckBuffOptionDb Db,DeckBuffXml Xml,int Position) p)=>order=="Reverse"?2-p.Position:p.Position;
                Add("Condition","s_nCondition",p=>At(p.Xml.Condition,Slot(p)),p=>p.Db.Condition,order);
                Add("Condition","s_nAT_Type",p=>At(p.Xml.AtType,Slot(p)),p=>p.Db.Condition,order);
                Add("Condition","s_nOption",p=>At(p.Xml.Option,Slot(p)),p=>p.Db.Condition,order);
                Add("Condition","s_nVal",p=>At(p.Xml.Value,Slot(p)),p=>p.Db.Condition,order);
                Add("Condition","ActiveFlag",p=>Active(p.Xml,Slot(p)),p=>p.Db.Condition,order);
                Add("Condition","SlotOrdinal",p=>Slot(p)+1,p=>p.Db.Condition,order);

                foreach((string dbField,Func<(DeckBuffOptionDb Db,DeckBuffXml Xml,int Position),int> readDb) in new[]{
                    ("AtType",(Func<(DeckBuffOptionDb,DeckBuffXml,int),int>)(p=>p.Item1.AtType)),
                    ("Value",p=>p.Item1.Value),("Prob",p=>p.Item1.Prob),("Time",p=>p.Item1.Time),("OptionId",p=>p.Item1.OptionId)})
                {
                    Add(dbField,"s_nCondition",p=>At(p.Xml.Condition,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                    Add(dbField,"s_nAT_Type",p=>At(p.Xml.AtType,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                    Add(dbField,"s_nOption",p=>At(p.Xml.Option,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                    Add(dbField,"s_nVal",p=>At(p.Xml.Value,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                    Add(dbField,"s_nProb",p=>At(p.Xml.Prob,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                    Add(dbField,"s_nTime",p=>At(p.Xml.Time,Slot(p)),p=>readDb((p.Db,p.Xml,p.Position)),order);
                }
            }

            void Add(string dbField,string xmlField,Func<(DeckBuffOptionDb Db,DeckBuffXml Xml,int Position),int>a,Func<(DeckBuffOptionDb Db,DeckBuffXml Xml,int Position),int>b,string order)
            {
                int exact=pairs.Count(p=>a(p)==b(p));
                result.Candidates.Add(new OptionCandidate(dbField,xmlField,order,pairs.Count==0?0:exact*100.0/pairs.Count,exact,pairs.Count));
            }

            result.CandidatePath=Path.Combine(folder,"DeckBuffOption_Candidates.csv");
            using(var w=new StreamWriter(result.CandidatePath,false,new UTF8Encoding(true)))
            {
                w.WriteLine("DB_Field,XML_Candidate,SlotOrder,Compared,ExactMatches,MatchPercent");
                foreach(OptionCandidate c in result.Candidates.OrderBy(x=>x.DbField).ThenByDescending(x=>x.Percent))
                    w.WriteLine($"{c.DbField},{c.XmlField},{c.Order},{c.Compared},{c.Exact},{c.Percent.ToString("0.000",CultureInfo.InvariantCulture)}");
            }
            return result;
        }

        private static string WriteHighSignal(string folder,List<DeckBookXml> books,List<DeckBuffXml> decks,List<DeckBookDb> dbBooks,List<DeckBuffDb> dbBuffs,List<DeckBuffOptionDb> dbOptions,string bookCsv,string buffCsv,OptionAnalysis analysis)
        {
            string path=Path.Combine(folder,"HIGH_SIGNAL_REPORT.txt");
            var sb=new StringBuilder();
            sb.AppendLine("DIGIMON BOOK XML <-> DATABASE HIGH SIGNAL REPORT");
            sb.AppendLine("READ-ONLY"); sb.AppendLine();
            sb.AppendLine($"BookInfo.xml rows       : {books.Count:N0}");
            sb.AppendLine($"DeckOption.xml groups   : {decks.Count:N0}");
            sb.AppendLine($"Asset.DeckBookInfo rows : {dbBooks.Count:N0}");
            sb.AppendLine($"Asset.DeckBuff rows     : {dbBuffs.Count:N0}");
            sb.AppendLine($"Asset.DeckBuffOption    : {dbOptions.Count:N0}"); sb.AppendLine();

            int bookOption=dbBooks.Count(d=>books.Any(x=>x.OptionId==d.OptionId)||d.OptionId==0);
            int bookType=dbBooks.Count(d=>d.Type==d.OptionId);
            int buffId=dbBuffs.Count(d=>decks.Any(x=>x.GroupId==d.GroupId));
            int buffName=dbBuffs.Count(d=>decks.Any(x=>x.GroupId==d.GroupId&&x.Name==d.Name));
            int buffExplain=dbBuffs.Count(d=>decks.Any(x=>x.GroupId==d.GroupId&&x.Explain==d.Explain));
            sb.AppendLine("DECK BOOK INFO SIGNAL");
            sb.AppendLine($"OptionId present in XML (+ synthetic 0): {Pct(bookOption,dbBooks.Count):0.00}%");
            sb.AppendLine($"Type == OptionId: {Pct(bookType,dbBooks.Count):0.00}%");
            sb.AppendLine("The DB contains an OptionId=0/Type=0 'None' row in the supplied working snapshot; BookInfo.xml itself begins at 1."); sb.AppendLine();
            sb.AppendLine("DECK BUFF SIGNAL");
            sb.AppendLine($"GroupIdX == s_nGroupIdx: {Pct(buffId,dbBuffs.Count):0.00}%");
            sb.AppendLine($"GroupName == s_szGroupName: {Pct(buffName,dbBuffs.Count):0.00}%");
            sb.AppendLine($"Explain == s_szExplain: {Pct(buffExplain,dbBuffs.Count):0.00}%"); sb.AppendLine();
            sb.AppendLine("DECK BUFF OPTION — BEST CANDIDATES");
            foreach(string field in new[]{"Condition","AtType","Value","Prob","Time","OptionId"})
            {
                OptionCandidate? best=analysis.Candidates.Where(x=>x.DbField==field).OrderByDescending(x=>x.Percent).FirstOrDefault();
                if(best!=null) sb.AppendLine($"{field,-10} <- {best.XmlField,-14} order={best.Order,-7}  {best.Percent:0.00}% ({best.Exact}/{best.Compared})");
            }
            sb.AppendLine();
            sb.AppendLine("IMPORT STATUS: LOCKED until the candidate report establishes a safe mapping for all six DeckBuffOption fields.");
            sb.AppendLine("Run COMPARE DB against the known-good database and send HIGH_SIGNAL_REPORT.txt plus DeckBuffOption_Candidates.csv.");
            sb.AppendLine(); sb.AppendLine("FILES"); sb.AppendLine(bookCsv); sb.AppendLine(buffCsv); sb.AppendLine(analysis.RawPath); sb.AppendLine(analysis.CandidatePath);
            File.WriteAllText(path,sb.ToString(),new UTF8Encoding(true)); return path;
        }

        private static int Active(DeckBuffXml x,int i)=>At(x.Condition,i)!=0||At(x.AtType,i)!=0||At(x.Option,i)!=0||At(x.Value,i)!=0||At(x.Prob,i)!=0||At(x.Time,i)!=0?1:0;
        private static double Pct(int a,int b)=>b==0?0:a*100.0/b;
        private static int N(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt32(r.GetValue(i),CultureInfo.InvariantCulture);
        private static string S(SqlDataReader r,int i)=>r.IsDBNull(i)?string.Empty:Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??string.Empty;
        private static int I(XElement e,string n)=>int.TryParse(e.Element(n)?.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int v)?v:0;
        private static string T(XElement e,string n)=>e.Element(n)?.Value??string.Empty;
        private static int[] A(XElement e,string p,string c)=>(e.Element(p)?.Elements(c)??Enumerable.Empty<XElement>()).Select(x=>int.TryParse(x.Value,out int v)?v:0).ToArray();
        private static int At(int[] a,int i)=>i>=0&&i<a.Length?a[i]:0;
        private static string B(bool v)=>v?"1":"0";
        private static string Csv(string? value){string t=value??string.Empty;if(t.Contains('"'))t=t.Replace("\"","\"\"");return t.IndexOfAny(new[]{',','"','\r','\n'})>=0?"\""+t+"\"":t;}
    }
}
