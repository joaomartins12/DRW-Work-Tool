namespace DRW_Work_Tool
{
    /// <summary>
    /// Resolves Timer references in partial WinForms files to the UI timer.
    /// This avoids ambiguity with System.Threading.Timer introduced by implicit usings.
    /// </summary>
    internal sealed class Timer : System.Windows.Forms.Timer
    {
    }
}
