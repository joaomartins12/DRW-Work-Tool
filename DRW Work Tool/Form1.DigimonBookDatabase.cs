using DRW_Work_Tool.Core;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private async Task RunDigimonBookCompareAsync()
        {
            string folder = Path.Combine(AppPaths.Xml, "Digimon_Book");
            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Digimon Book Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show("Configure and test the SQL Server connection in SETTINGS first.", "Digimon Book Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var page = CreateDarkTab("Digimon Book DB Compare");
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var title = new Label { Text = "Digimon Book XML ↔ DeckBookInfo / DeckBuff / DeckBuffOption", ForeColor = CText, Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), Location = new Point(8,8), AutoSize = true };
            var subtitle = new Label { Text = "READ-ONLY • mapping discovery • database is never modified", ForeColor = CMuted, Location = new Point(10,40), AutoSize = true };
            var status = new Label { Text = "Preparing...", ForeColor = CMuted, Location = new Point(10,68), Size = new Size(850,24), AutoEllipsis = true };
            var log = new TextBox { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Bottom, Height=440, BackColor=Color.FromArgb(10,10,10), ForeColor=CText, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Consolas",8.5F) };
            root.Controls.AddRange(new Control[]{title,subtitle,status,log}); page.Controls.Add(root); editorTabs.TabPages.Add(page); editorTabs.SelectedTab=page;

            var cts = new CancellationTokenSource();
            EventHandler disposed = (_,_) => { if(!cts.IsCancellationRequested) cts.Cancel(); };
            page.Disposed += disposed;
            var progress = new Progress<string>(line => { if(page.IsDisposed)return; status.Text=line; log.AppendText(line+Environment.NewLine); });

            try
            {
                var service = new DigimonBookDatabaseDiagnosticService();
                DigimonBookDatabaseDiagnosticSummary summary = await service.CompareAsync(connection,folder,progress,cts.Token);
                if(page.IsDisposed)return;
                status.Text=$"DONE • BookInfo XML {summary.BookInfoXmlRows:N0} • DeckOption XML {summary.DeckOptionXmlRows:N0} • DB Options {summary.DeckBuffOptionDbRows:N0}";
                status.ForeColor=Color.FromArgb(120,220,145);
                log.AppendText(Environment.NewLine+"HIGH SIGNAL REPORT: "+summary.HighSignalReport+Environment.NewLine);
                DialogResult open=MessageBox.Show(
                    "Digimon Book comparison completed.\r\n\r\n"+
                    $"DeckBookInfo DB rows: {summary.DeckBookInfoDbRows:N0}\r\n"+
                    $"DeckBuff DB rows: {summary.DeckBuffDbRows:N0}\r\n"+
                    $"DeckBuffOption DB rows: {summary.DeckBuffOptionDbRows:N0}\r\n\r\n"+
                    "The import remains locked until the option mapping is confirmed.\r\n\r\nOpen the diagnostic folder?",
                    "Digimon Book Compare DB",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
                if(open==DialogResult.Yes && Directory.Exists(summary.OutputFolder))
                    Process.Start(new ProcessStartInfo{FileName=summary.OutputFolder,UseShellExecute=true});
            }
            catch(OperationCanceledException)
            {
                if(!page.IsDisposed){status.Text="Cancelled.";status.ForeColor=Color.FromArgb(255,190,90);}
            }
            catch(Exception ex)
            {
                if(!page.IsDisposed){status.Text="FAILED — database was not modified.";status.ForeColor=Color.FromArgb(255,100,110);log.AppendText(Environment.NewLine+ex+Environment.NewLine);ShowEditorError("Digimon Book Compare DB",ex);}
            }
            finally
            {
                if(!page.IsDisposed) page.Disposed -= disposed;
                cts.Dispose();
            }
        }
    }
}
