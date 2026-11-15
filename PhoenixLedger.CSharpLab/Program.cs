using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool smoke = args.Any(a=>a.Equals("--smoke-ui", StringComparison.OrdinalIgnoreCase));
        ApplicationConfiguration.Initialize();
        try
        {
            Store.Initialize();
            if(smoke)
            {
                using var form = new MainForm();
                form.SmokeNavigate();
                return;
            }
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            if(smoke)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode=1;
                return;
            }
            MessageBox.Show($"PhoenixLedger C# Lab could not start:\n\n{ex}",
                "PhoenixLedger C# Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
