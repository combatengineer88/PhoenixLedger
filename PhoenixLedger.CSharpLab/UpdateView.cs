using System.Drawing;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed class UpdateView : UserControl
{
    private readonly Label status = new()
    {
        AutoSize = false, Width = 770, Height = 88, Font = new Font("Segoe UI", 11),
        ForeColor = Color.FromArgb(39, 55, 77),
        Text = "The update checker is ready."
    };
    private readonly Label installLocation = new()
    {
        AutoSize = false, Width = 770, Height = 74,
        ForeColor = Color.FromArgb(103, 119, 138)
    };
    private readonly Button check = new()
    {
        Text = "Check for updates now", Width = 210, Height = 43
    };
    private readonly Button install = new()
    {
        Text = "Download and install update", Width = 265, Height = 43, Enabled = false
    };
    private readonly CheckBox checkAutomatically = new()
    {
        Text = "Check for new releases when PhoenixLedger starts",
        AutoSize = true
    };
    private readonly CheckBox installAutomatically = new()
    {
        Text = "Automatically install newer releases on startup (opt-in)",
        AutoSize = true
    };
    private readonly UpdatePreferences preferences;
    private AvailableUpdate? available;
    private bool busy;

    public event EventHandler? UpdateAvailable;

    public UpdateView()
    {
        preferences = UpdateService.LoadPreferences();
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = Color.FromArgb(241, 246, 251);
        Padding = new Padding(12);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(8),
        };
        layout.Controls.Add(new Label
        {
            Text = "PhoenixLedger C# Lab — software updates",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            Width = 760, Height = 43
        });
        layout.Controls.Add(new Label
        {
            Text = $"Installed application version: {UpdateService.CurrentVersion.ToString(3)}",
            Width = 760, Height = 34
        });
        layout.Controls.Add(installLocation);
        layout.Controls.Add(checkAutomatically);
        layout.Controls.Add(installAutomatically);
        layout.Controls.Add(new Label
        {
            Text = "Automatic installs apply only when you launch the program. " +
                   "Your separate lab database is not stored in the application folder.",
            Width = 760, Height = 55,
            ForeColor = Color.FromArgb(103, 119, 138)
        });
        layout.Controls.Add(status);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Width = 760, Height = 58
        };
        actions.Controls.Add(check);
        actions.Controls.Add(install);
        layout.Controls.Add(actions);
        Controls.Add(layout);

        string? directory = UpdateService.InstalledDirectory;
        installLocation.Text = directory == null
            ? "Installation: portable / source build.\n" +
              "Install a Windows installer release first to enable in-place updates."
            : $"Installation folder (retained during updates):\n{directory}";

        checkAutomatically.Checked = preferences.CheckOnStartup;
        installAutomatically.Checked = preferences.AutomaticallyInstall;
        checkAutomatically.CheckedChanged += (_, _) => SavePreferences();
        installAutomatically.CheckedChanged += (_, _) => SavePreferences();
        check.Click += async (_, _) => await CheckAsync(showErrors: true);
        install.Click += async (_, _) => await InstallAsync();
    }

    private void SavePreferences()
    {
        preferences.CheckOnStartup = checkAutomatically.Checked;
        preferences.AutomaticallyInstall = installAutomatically.Checked;
        try { UpdateService.SavePreferences(preferences); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to save update preferences:\n{ex.Message}",
                "PhoenixLedger Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public async Task CheckOnStartupAsync()
    {
        if (preferences.CheckOnStartup)
            await CheckAsync(showErrors: false, fromStartup: true);
    }

    private async Task CheckAsync(bool showErrors, bool fromStartup = false)
    {
        if (busy) return;
        busy = true;
        check.Enabled = false;
        install.Enabled = false;
        status.Text = "Checking the latest public GitHub release...";
        bool automaticInstall = false;
        try
        {
            available = await UpdateService.FindUpdateAsync();
            if (available == null)
            {
                status.Text = "No newer stable installer release is available. You are up to date, " +
                              "or no installer release has been published yet.";
            }
            else
            {
                status.Text = $"Version {available.Version.ToString(3)} is available. " +
                              "Download its verified Windows installer to update PhoenixLedger.";
                if (UpdateService.InstalledDirectory == null)
                    status.Text += "\nAn installed copy is required for automatic deployment.";
                else
                    automaticInstall = fromStartup && preferences.AutomaticallyInstall;
                UpdateAvailable?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            status.Text = $"Unable to check updates: {ex.Message}";
            if (showErrors)
                MessageBox.Show(this, status.Text, "PhoenixLedger Updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            check.Enabled = true;
            install.Enabled = available != null && UpdateService.InstalledDirectory != null;
        }
        if (automaticInstall) await InstallAsync();
    }

    private async Task InstallAsync()
    {
        if (busy || available == null || UpdateService.InstalledDirectory == null)
            return;
        busy = true;
        check.Enabled = false;
        install.Enabled = false;
        var update = available;
        try
        {
            status.Text = "Downloading the Windows installer...";
            var progress = new Progress<int>(percent =>
                status.Text = $"Downloading verified update {update.Version.ToString(3)}: {percent}%");
            string installer = await UpdateService.DownloadVerifiedInstallerAsync(update, progress);
            status.Text = "Installer verified. Closing PhoenixLedger to install the update " +
                          "into the original selected folder...";
            var form = FindForm() ?? throw new InvalidOperationException("Application window not found.");
            UpdateService.LaunchInstallerAndExit(form, installer);
        }
        catch (Exception ex)
        {
            status.Text = $"Update was not installed: {ex.Message}";
            MessageBox.Show(this, status.Text, "PhoenixLedger Updates",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            check.Enabled = true;
            install.Enabled = available != null && UpdateService.InstalledDirectory != null;
        }
    }
}
