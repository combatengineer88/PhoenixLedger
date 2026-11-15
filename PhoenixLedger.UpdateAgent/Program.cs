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
            EnsureNoOtherPhoenixLedgerProcesses();
            VerifyInstaller(installer, expectedSize, expectedDigest);

            var start = new ProcessStartInfo
            {
                FileName = installer,
                WorkingDirectory = Path.GetDirectoryName(installer)!,
                UseShellExecute = true
            };
            // A successful /SILENT setup needs no input, but shows progress.
            // Do NOT use /SUPPRESSMSGBOXES here: Inno Setup automatically
            // chooses Abort/Cancel for Retry dialogs, resulting in exit code 5
            // and hiding the underlying file lock/permission problem.
            foreach (string argument in BuildSetupArguments(mode, installationDirectory, logPath))
                start.ArgumentList.Add(argument);

            // Inno Setup requests UAC as needed for /ALLUSERS. Do not elevate the
            // current-user mode, which is intended to stay per-user.
            using var setup = Process.Start(start) ??
                throw new InvalidOperationException("The Windows setup process could not be started.");

            if (!setup.WaitForExit(20 * 60 * 1000))
                throw new TimeoutException("Windows Setup did not finish within 20 minutes.");
            if (setup.ExitCode != 0)
                throw new InvalidOperationException(
                    DescribeSetupExitCode(setup.ExitCode) +
                    RelevantLogExcerpt(logPath));

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
                    "\nUpdate log: " + logPath +
                    "\n\nIf the issue continues, share the relevant error lines from the log.",
                    "PhoenixLedger update failed", 0x10);
            }
            catch (Exception) { /* Headless/test environments. */ }
            return 1;
        }
    }

    private static IReadOnlyList<string> BuildSetupArguments(string mode,string dir,string logPath) =>
        new[]
        {
            "/SP-", "/SILENT", "/NORESTART", "/NORESTARTAPPLICATIONS",
            mode, "/DIR=" + dir, "/LOG=" + logPath
        };

    private static string DescribeSetupExitCode(int code) => code switch
    {
        2 => "Windows Setup was cancelled before installation began (exit code 2).",
        5 => "Windows Setup was cancelled or aborted after installation began (exit code 5). " +
             "This can happen if a file remains locked or a Retry/Abort error is shown.",
        4 => "Windows Setup encountered a fatal installation error (exit code 4).",
        7 => "Windows Setup could not proceed past pre-installation checks (exit code 7).",
        8 => "Windows Setup requires a restart before installation (exit code 8).",
        _ => $"Windows Setup stopped with exit code {code}."
    };

    private static string RelevantLogExcerpt(string path)
    {
        try
        {
            if(!File.Exists(path))return "\nSetup did not write a log file.";
            string[] details=File.ReadLines(path)
                .TakeLast(300)
                .Where(line=>line.Contains("error",StringComparison.OrdinalIgnoreCase)
                    ||line.Contains("abort",StringComparison.OrdinalIgnoreCase)
                    ||line.Contains("denied",StringComparison.OrdinalIgnoreCase)
                    ||line.Contains("cannot",StringComparison.OrdinalIgnoreCase)
                    ||line.Contains("failed",StringComparison.OrdinalIgnoreCase)
                    ||line.Contains("sharing violation",StringComparison.OrdinalIgnoreCase))
                .TakeLast(4).ToArray();
            return details.Length==0?"\nSee the installer log for details.":
                "\nInstaller log details:\n"+string.Join("\n",details.Select(x=>x.Length>240?x[..240]+"…":x));
        }
        catch(IOException){return "\nInstaller log could not be read.";}
        catch(UnauthorizedAccessException){return "\nInstaller log could not be accessed.";}
    }

    private static void EnsureNoOtherPhoenixLedgerProcesses()
    {
        // The launched update agent runs outside Program Files. Wait for all
        // copies of the application, not only the parent PID, to exit before
        // the installer tries to replace Windows EXEs/DLLs. Never force-kill.
        DateTime deadline=DateTime.UtcNow.AddSeconds(15);
        while(true)
        {
            Process[] running=Process.GetProcessesByName("PhoenixLedger.CSharpLab");
            try
            {
                if(running.Length==0)return;
            }
            finally
            {
                foreach(Process process in running)process.Dispose();
            }

            if(DateTime.UtcNow>=deadline)
            {
                int answer=MessageBox(0,
                    "Another PhoenixLedger C# Lab window is still running.\n\n" +
                    "Close all PhoenixLedger windows, then click Retry. " +
                    "Choose Cancel to stop this update; no files will be replaced.",
                    "PhoenixLedger — close other windows",
                    0x35); // MB_RETRYCANCEL | MB_ICONWARNING
                if(answer!=4) // IDRETRY
                    throw new InvalidOperationException("Update cancelled because PhoenixLedger is still running.");
                deadline=DateTime.UtcNow.AddSeconds(15);
            }
            Thread.Sleep(500);
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
            var arguments=BuildSetupArguments("/ALLUSERS",@"C:\Program Files\PhoenixLedger CSharpLab",
                Path.Combine(folder,"setup.log"));
            if(!arguments.Contains("/SILENT") || arguments.Contains("/SUPPRESSMSGBOXES")
                || arguments.Contains("/VERYSILENT") || !arguments.Contains("/ALLUSERS")
                || !arguments.Any(arg=>arg.StartsWith("/LOG=",StringComparison.Ordinal)))
                throw new InvalidOperationException("Installer argument regression check failed.");
            if(!DescribeSetupExitCode(5).Contains("aborted",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Setup exit code 5 should have a useful explanation.");
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
