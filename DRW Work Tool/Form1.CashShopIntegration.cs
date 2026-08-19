using System;
using System.Linq;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private bool _cashShopIntegrationReady;
        private bool _cashShopRefreshPending;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_cashShopIntegrationReady)
                return;

            _cashShopIntegrationReady = true;

            BeginInvoke(new Action(() =>
            {
                if (editorTabs == null || editorTabs.IsDisposed)
                    return;

                editorTabs.SelectedIndexChanged += (_, _) => QueueCashShopIntegrationRefresh();
                editorTabs.ControlAdded += (_, _) => QueueCashShopIntegrationRefresh();
                QueueCashShopIntegrationRefresh();
            }));
        }

        private void QueueCashShopIntegrationRefresh()
        {
            if (_cashShopRefreshPending || IsDisposed || !IsHandleCreated)
                return;

            _cashShopRefreshPending = true;
            BeginInvoke(new Action(() =>
            {
                _cashShopRefreshPending = false;
                RefreshCashShopIntegration();
            }));
        }

        private void RefreshCashShopIntegration()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            foreach (TabPage page in editorTabs.TabPages)
            {
                if (page.IsDisposed)
                    continue;

                if (page.Tag is EntityTabState state &&
                    state.Entity.Equals("CashShop", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureCashShopDirectOpenButton(page);
                }
            }
        }

        private void EnsureCashShopDirectOpenButton(TabPage page)
        {
            if (page.Controls.Find("CashShopVisualOpenButton", true).Length > 0)
                return;

            Button? oldOpen = EnumerateCashShopControls(page)
                .OfType<Button>()
                .FirstOrDefault(x => x.Text.Equals("OPEN", StringComparison.OrdinalIgnoreCase));

            if (oldOpen == null || oldOpen.Parent == null)
                return;

            Control parent = oldOpen.Parent;
            var open = CreateEditorActionButton("OPEN");
            open.Name = "CashShopVisualOpenButton";
            open.Location = oldOpen.Location;
            open.Size = oldOpen.Size;
            open.Anchor = oldOpen.Anchor;
            open.TabIndex = oldOpen.TabIndex;

            editorToolTip.SetToolTip(
                open,
                "Open the complete visual Cash Shop editor. Numbered duplicate XML trees are ignored; only the canonical complete set is used.");

            open.Click += async (_, _) => await OpenCashShopVisualEditorAsync();

            parent.Controls.Remove(oldOpen);
            oldOpen.Dispose();
            parent.Controls.Add(open);
            open.BringToFront();
        }

        private static System.Collections.Generic.IEnumerable<Control> EnumerateCashShopControls(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control nested in EnumerateCashShopControls(child))
                    yield return nested;
        }
    }
}
