using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace PhoenixLedger.CSharpLab;

internal sealed record AvailableUpdate(Version Version, string DownloadUrl, string Sha256, long Size);

internal sealed class UpdatePreferences
{
    public bool CheckOnStartup { get; set; } = true;
    public bool AutomaticallyInstall { get; set; } = false;
}

internal static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/combatengineer88/PhoenixLedger/releases/latest";
    private const string ReleaseAssetPrefix =
        "https://github.com/combatengineer88/PhoenixLedger/releases/download/";
    private const string InstallerAssetName = "PhoenixLedger-CSharpLab-Setup.exe";
    private const string InstallRegistryKey = @"Software\PhoenixLedger\CSharpLab";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    public static Version CurrentVersion =>
        typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static string SettingsFile => Path.Combine(Store.Root, "update-preferences.json");

    private sealed record InstallationLocation(string Directory, bool AllUsers);

    public static string? InstalledDirectory => DetectInstallation()?.Directory;

    private static InstallationLocation? DetectInstallation()
    {
        string running = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(AppContext.BaseDirectory));

        // v0.2.0 registered the per-user install in HKCU (32-bit view).
        // v0.2.1 can install for a single user or for all users.
        // Read both registry views to support installations on either side
        // of a future transition to 64-bit installer registry entries.
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(InstallRegistryKey);
                    string? path = key?.GetValue("InstallDir") as string;
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    string installed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    if (string.Equals(installed, running, StringComparison.OrdinalIgnoreCase))
                        return new InstallationLocation(installed, hive == RegistryHive.LocalMachine);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                    or IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // A denied or malformed entry does not invalidate another installation.
                }
            }
        }
        return null;
    }

    public static UpdatePreferences LoadPreferences()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<UpdatePreferences>(
                    File.ReadAllText(SettingsFile)) ?? new UpdatePreferences();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An inaccessible or invalid user setting must not prevent startup.
        }
        return new UpdatePreferences();
    }

    public static void SavePreferences(UpdatePreferences preferences)
    {
        Directory.CreateDirectory(Store.Root);
        string temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
        File.Move(temporary, SettingsFile, overwrite: true);
    }

    public static async Task<AvailableUpdate?> FindUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("PhoenixLedgerCSharpLab-Updater/0.2");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // No published releases yet.
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        JsonElement release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() ||
            release.GetProperty("prerelease").GetBoolean())
            return null;

        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version))
            throw new InvalidDataException("The published release has an invalid version tag.");

        // Do not downgrade or reinstall an equal version.
        if (version <= CurrentVersion) return null;

        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(),
                InstallerAssetName, StringComparison.Ordinal))
                continue;

            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith(ReleaseAssetPrefix, StringComparison.Ordinal) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("The release installer URL was not trusted.");

            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release installer does not contain a SHA-256 digest.");

            string sha = digest["sha256:".Length..];
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
                throw new InvalidDataException("The published installer SHA-256 digest is invalid.");

            long size = asset.GetProperty("size").GetInt64();
            if (size < 1 || size > 600_000_000)
                throw new InvalidDataException("Unexpected installer download size.");

            return new AvailableUpdate(version, url, sha.ToUpperInvariant(), size);
        }

        throw new InvalidDataException("The latest release has no Windows installer asset.");
    }

    public static async Task<string> DownloadVerifiedInstallerAsync(
        AvailableUpdate update, IProgress<int>? progress, CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(Store.Root, "updates");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, $"PhoenixLedger-CSharpLab-Setup-v{update.Version}.exe");
        string partial = target + ".partial";
        try
        {
            using var response = await Client.GetAsync(update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != update.Size)
                throw new InvalidDataException("Installer size did not match the release.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(partial, FileMode.Create, FileAccess.Write,
                FileShare.None, 128 * 1024, useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                long downloaded = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    downloaded += count;
                    if (downloaded > update.Size)
                        throw new InvalidDataException("Installer exceeded the published size.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((int)(downloaded * 100L / update.Size));
                }

                if (downloaded != update.Size)
                    throw new InvalidDataException("Downloaded installer size did not match the release.");
            }

            // Scope the verification handle so Windows can rename the .partial
            // file. In v0.2.x/v0.3.0 this handle lived until method return and
            // caused "used by another process" on File.Move.
            await using (var downloadedFile = File.OpenRead(partial))
            {
                byte[] computed = await SHA256.HashDataAsync(downloadedFile, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                    computed, Convert.FromHexString(update.Sha256)))
                    throw new InvalidDataException("Installer checksum verification failed. No update was installed.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public static void LaunchInstallerAndExit(Form form, string installerPath, AvailableUpdate update)
    {
        InstallationLocation location = DetectInstallation() ??
            throw new InvalidOperationException(
                "This copy is not installed using the PhoenixLedger Windows installer. " +
                "Install a Windows release first.");

        if (!File.Exists(installerPath))
            throw new FileNotFoundException("The verified installer was not found.", installerPath);

        // Important: the updater process must NOT run from Program Files.
        // Copy the dedicated helper into per-user staging so that no updater
        // executable keeps the installed application folder locked.
        string bundledAgent = Path.Combine(AppContext.BaseDirectory, "PhoenixLedger.UpdateAgent.exe");
        if (!File.Exists(bundledAgent))
            throw new FileNotFoundException("The standalone update agent was not included with this build.", bundledAgent);

        string staging = Path.Combine(Store.Root, "updates");
        Directory.CreateDirectory(staging);
        string agentCopy = Path.Combine(staging,
            $"PhoenixLedger.UpdateAgent-{CurrentVersion.ToString(3)}-{Guid.NewGuid():N}.exe");
        File.Copy(bundledAgent, agentCopy);

        // The agent is independent of the main app and waits for this specific
        // parent process to EXIT before it launches Inno Setup. It verifies the
        // installer hash a second time, waits for setup and reopens the app.
        var start = new ProcessStartInfo
        {
            FileName = agentCopy,
            UseShellExecute = true,
            WorkingDirectory = staging
        };
        foreach (string argument in new[]
        {
            "--apply",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Path.GetFullPath(installerPath),
            location.Directory,
            location.AllUsers ? "/ALLUSERS" : "/CURRENTUSER",
            Path.Combine(staging, "last-update-install.log"),
            Path.Combine(location.Directory, "PhoenixLedger.CSharpLab.exe"),
            update.Sha256,
            update.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
        })
            start.ArgumentList.Add(argument);

        if (Process.Start(start) is null)
            throw new InvalidOperationException("Could not start the independent update agent.");

        // Close the application AFTER the agent has been started, but BEFORE
        // the agent touches the installed executable or DLLs.
        form.BeginInvoke(new Action(Application.Exit));
    }
}
