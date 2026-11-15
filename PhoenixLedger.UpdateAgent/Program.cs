using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PhoenixLedger.UpdateAgent;

/// <summary>
/// Small, independent Windows executable. The main app copies it into LocalAppData
/// before starting it, so the program's install folder is not locked by the agent.
/// It waits for the main process to exit before running the verified installer.
/// </summary>
internal static class Program
{
    private const string ExpectedInstallerNamePrefix = "PhoenixLedger-CSharpLab-Setup-v";
    private const string ExpectedAppName = "PhoenixLedger.CSharpLab.exe";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint handle, string message, string title, uint type);

    private static int Main(string[] args)
    {
        if (args is ["--self-test"])
            return SelfTest();

        if (args.Length != 9 || args[0] != "--apply")
            return 2;

        string logPath = args[5];
        try
        {
            if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid)
                || parentPid <= 0)
                throw new InvalidDataException("Invalid parent process ID.");

            string installer = Path.GetFullPath(args[2]);
            string installationDirectory = Path.GetFullPath(args[3]);
            string mode = args[4];
            string application = Path.GetFullPath(args[6]);
            string expectedDigest = args[7];
            if (!long.TryParse(args[8], NumberStyles.None, CultureInfo.InvariantCulture, out long expectedSize)
                || expectedSize <= 0 || expectedSize > 600_000_000)
                throw new InvalidDataException("Invalid installer size.");

            if (mode is not ("/CURRENTUSER" or "/ALLUSERS"))
                throw new InvalidDataException("Invalid installation mode.");
            if (!Path.GetFileName(installer).StartsWith(ExpectedInstallerNamePrefix, StringComparison.OrdinalIgnoreCase)
                || !installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected installer filename.");
            if (!string.Equals(Path.GetFileName(application), ExpectedAppName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(application),
                    Path.TrimEndingDirectorySeparator(installationDirectory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The expected application is not in the installation directory.");

            // Verification is repeated in the separate process, before installation.
            VerifyInstaller(installer, expectedSize, expectedDigest);
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                if (!parent.WaitForExit(90_000))
                    throw new TimeoutException(
                        "PhoenixLedger did not close within 90 seconds. No files were replaced.");
            }
            catch (ArgumentException)
            {
                // The parent has already exited, so installing is safe.
            }

            // Give Windows a brief chance to release loaded DLLs after exit.
            Thread.Sleep(500);
            VerifyInstaller(installer, expectedSize, expectedDigest);

            var start = new ProcessStartInfo
            {
                FileName = installer,
                WorkingDirectory = Path.GetDirectoryName(installer)!,
                UseShellExecute = true
            };
            foreach (string argument in new[]
            {
                "/SP-", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                "/NORESTARTAPPLICATIONS", mode, "/DIR=" + installationDirectory,
                "/LOG=" + logPath
            })
                start.ArgumentList.Add(argument);

            // Inno Setup requests UAC as needed for /ALLUSERS. Do not elevate the
            // current-user mode, which is intended to stay per-user.
            using var setup = Process.Start(start) ??
                throw new InvalidOperationException("The Windows setup process could not be started.");

            if (!setup.WaitForExit(20 * 60 * 1000))
                throw new TimeoutException("Windows Setup did not finish within 20 minutes.");
            if (setup.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Windows Setup exited with code {setup.ExitCode}. The update may not have completed.");

            if (!File.Exists(application))
                throw new FileNotFoundException("Updated application was not found at the original location.", application);

            var reopen = new ProcessStartInfo
            {
                FileName = application,
                WorkingDirectory = installationDirectory,
                UseShellExecute = true
            };
            if (Process.Start(reopen) is null)
                throw new InvalidOperationException(
                    "Installation succeeded, but PhoenixLedger could not be reopened.");

            return 0;
        }
        catch (Exception error)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
                File.AppendAllText(Path.GetFullPath(logPath), "\nUpdateAgent: " + error + "\n");
            }
            catch (Exception) { /* Show error even if logging failed. */ }
            try
            {
                MessageBox(0,
                    error.Message + "\n\nThe installer was not confirmed successful." +
                    "\nUpdate log: " + logPath,
                    "PhoenixLedger update failed", 0x10);
            }
            catch (Exception) { /* Headless/test environments. */ }
            return 1;
        }
    }

    private static void VerifyInstaller(string file, long expectedSize, string expectedDigest)
    {
        if (expectedDigest.Length != 64 || !expectedDigest.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid expected SHA-256 digest.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expectedSize)
            throw new InvalidDataException("Installer size changed after download.");
        byte[] actual = SHA256.HashData(stream);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedDigest)))
            throw new InvalidDataException("Installer SHA-256 verification failed.");
    }

    private static int SelfTest()
    {
        string folder=Path.Combine(Path.GetTempPath(),"PhoenixLedgerAgentSelfTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder, ExpectedInstallerNamePrefix+"test.exe");
            byte[] data=System.Text.Encoding.UTF8.GetBytes("Test updater verification and file handle release.");
            File.WriteAllBytes(file, data);
            VerifyInstaller(file, data.Length, Convert.ToHexString(SHA256.HashData(data)));
            string renamed=file+".moved";
            File.Move(file,renamed);
            if (!File.Exists(renamed))
                throw new InvalidOperationException("Installer file handle was not released.");
            try
            {
                VerifyInstaller(renamed,data.Length,"0000000000000000000000000000000000000000000000000000000000000000");
                throw new InvalidOperationException("Checksum rejection did not work.");
            }
            catch (InvalidDataException) { }
            return 0;
        }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(folder,"test-error.log"),e.ToString()); }
            catch { }
            return 1;
        }
        finally
        {
            try { Directory.Delete(folder,recursive:true); }
            catch { }
        }
    }
}
