using System;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();

            // First message loop:
            // show a small responsive loading window and do not expose Form1
            // until the editor/database preload has completed.
            using (var loading =
                   new LoadingForm())
            {
                Application.Run(
                    loading);

                if (!loading.StartupFinished)
                    return;
            }

            // Second message loop:
            // all static editor caches are already populated here.
            Application.Run(
                new Form1());
        }
    }
}
