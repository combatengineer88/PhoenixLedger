using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Store.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PhoenixLedger C# Lab could not start:\n\n{ex}",
                "PhoenixLedger C# Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
