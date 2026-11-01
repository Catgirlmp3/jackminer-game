using System.Windows;

namespace JackMiner;

public partial class MainWindow
{
    private PenaltySettings _penaltySettings = new();
    private int _lastPenaltyTarget;
    private bool _penaltyTargetUpdatePending;
    private App Services => (App)Application.Current;

    private void PenaltyUi_Loaded(object sender, RoutedEventArgs e)
    {
        _penaltySettings = Services.PenaltySettings.Load();
        WalletAddressBox.Text = _penaltySettings.WalletAddress;
        PoolHostBox.Text = _penaltySettings.PoolHost;
        WorkerNameBox.Text = _penaltySettings.WorkerName;
        MiningOptInCheckBox.IsChecked = _penaltySettings.MiningOptIn;
        Services.PenaltyHost.OutputReceived += PenaltyOutputReceived;
        Services.PenaltyHost.Exited += PenaltyProcessExited;
        RefreshPenaltyControls();
    }

    private void SavePenaltySettings()
    {
        _penaltySettings = new PenaltySettings
        {
            PoolHost = PoolHostBox.Text.Trim(),
            PoolPort = 10008,
            WalletAddress = WalletAddressBox.Text.Trim(),
            WorkerName = WorkerNameBox.Text.Trim(),
            MiningOptIn = MiningOptInCheckBox.IsChecked == true
        };
        Services.PenaltySettings.Save(_penaltySettings);
    }

    private int CurrentPenaltyTarget()
    {
        var value = Math.Clamp(75m - (_game.Balance / 3000m * 60m), 15m, 75m);
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private void UpdatePenaltyTarget(decimal target)
    {
        var next = (int)Math.Round(target, MidpointRounding.AwayFromZero);
        if (!Services.PenaltyHost.IsRunning || _penaltyTargetUpdatePending || next == _lastPenaltyTarget) return;
        _ = ApplyPenaltyTargetAsync(next);
    }

    private async Task ApplyPenaltyTargetAsync(int target)
    {
        _penaltyTargetUpdatePending = true;
        try
        {
            await Services.PenaltyRuntime.UpdateCpuTargetAsync(target);
            _lastPenaltyTarget = target;
            RuntimeDetailText.Text = $"Live CPU thread target updated to {target}% (XMRig hint).";
        }
        catch (Exception ex)
        {
            RuntimeDetailText.Text = $"Live target update failed: {ex.Message}";
        }
        finally
        {
            _penaltyTargetUpdatePending = false;
            var current = CurrentPenaltyTarget();
            if (Services.PenaltyHost.IsRunning && current != _lastPenaltyTarget)
                _ = ApplyPenaltyTargetAsync(current);
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        SavePenaltySettings();
        SetupButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        try
        {
            var version = await Services.PenaltySetup.InstallOrRepairAsync(
                new Progress<string>(message => RuntimeDetailText.Text = message));
            RuntimeDetailText.Text = $"Verified XMRig {version} installed. No session has been started.";
            MessageBox.Show($"The official Windows x64 package was verified and installed.\n\nVersion: {version}\n\nNo resource session has been started.",
                "JackMiner — Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RuntimeDetailText.Text = "Setup did not complete.";
            MessageBox.Show($"Required files could not be installed or verified. JackMiner does not bypass Windows security or restore quarantined files.\n\n{ex.Message}",
                "JackMiner — Setup Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshPenaltyControls(); }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            var latest = await Services.PenaltySetup.GetLatestVersionAsync();
            var installed = await Services.PenaltySetup.GetInstalledVersionAsync();
            if (installed is null)
                MessageBox.Show($"Latest official release: {latest}\n\nNo verified runtime is installed. Use Download / Repair Required Files first.",
                    "JackMiner — Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase))
                MessageBox.Show($"Your runtime is up to date: {installed}.", "JackMiner — Update Check",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else if (MessageBox.Show($"Installed: {installed}\nAvailable: {latest}\n\nDownload and verify the update now?",
                         "JackMiner — Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await Services.PenaltySetup.InstallOrRepairAsync(
                    new Progress<string>(message => RuntimeDetailText.Text = message));
                RuntimeDetailText.Text = $"Updated and verified to XMRig {latest}.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update check failed.\n\n{ex.Message}", "JackMiner — Update Check Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshPenaltyControls(); }
    }

    private async void StartMining_Click(object sender, RoutedEventArgs e)
    {
        SavePenaltySettings();
        if (!_penaltySettings.MiningOptIn)
        {
            MessageBox.Show("Tick the explicit opt-in box first. Accepting the first-run warning is not mining consent.",
                "JackMiner — Opt-in Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await Services.PenaltySetup.VerifyInstalledAsync())
        {
            MessageBox.Show("No verified runtime is installed. Use Download / Repair Required Files first. If Windows Security quarantined a file, review its alert yourself; JackMiner will not bypass it.",
                "JackMiner — Runtime Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = CurrentPenaltyTarget();
        var answer = MessageBox.Show(
            $"Start the optional CPU resource session?\n\nPool: {_penaltySettings.PoolHost}:{_penaltySettings.PoolPort}\nWorker: {_penaltySettings.WorkerName}\nCPU thread target hint: {target}% (not a guaranteed usage cap).\n\nThis creates real heat, fan noise, hardware load and electricity costs. The game balance is fictional. The session remains visible and can be stopped here.\n\nContinue?",
            "JackMiner — Confirm Resource Session", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        StartMiningButton.IsEnabled = false;
        RuntimeDetailText.Text = "Starting verified resource process...";
        try
        {
            await Services.PenaltyRuntime.StartAsync(Services.PenaltySetup.ExecutablePath, _penaltySettings, target);
            _lastPenaltyTarget = target;
            MinerStatusText.Text = "RESOURCE SESSION ACTIVE";
            RuntimeDetailText.Text = $"Active. CPU target hint: {target}%.";
        }
        catch (Exception ex)
        {
            MinerStatusText.Text = Services.PenaltyHost.IsRunning ? "RESOURCE PROCESS ACTIVE — CONTROL API ISSUE" : "RESOURCE SESSION OFFLINE";
            RuntimeDetailText.Text = ex.Message;
            MessageBox.Show($"The resource session did not start cleanly.\n\n{ex.Message}",
                "JackMiner — Start Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshPenaltyControls(); }
    }

    private void StopMining_Click(object sender, RoutedEventArgs e)
    {
        Services.PenaltyHost.Stop();
        Services.PenaltyRuntime.ResetSession();
        _lastPenaltyTarget = 0;
        MinerStatusText.Text = "RESOURCE SESSION STOPPED";
        RuntimeDetailText.Text = "The resource process has been stopped.";
        RefreshPenaltyControls();
    }

    private void PenaltyOutputReceived(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Services.PenaltyHost.IsRunning)
                RuntimeDetailText.Text = line.Length > 150 ? line[..150] + "…" : line;
        });
    }

    private void PenaltyProcessExited(int? exitCode)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Services.PenaltyRuntime.ResetSession();
            MinerStatusText.Text = "RESOURCE SESSION STOPPED";
            RuntimeDetailText.Text = $"Resource process exited (code {exitCode?.ToString() ?? "unknown"}).";
            RefreshPenaltyControls();
        });
    }

    private void RefreshPenaltyControls()
    {
        var running = Services.PenaltyHost.IsRunning;
        StartMiningButton.IsEnabled = !running;
        StopMiningButton.IsEnabled = running;
        SetupButton.IsEnabled = !running;
        UpdateButton.IsEnabled = !running;
        if (running) MinerStatusText.Text = "RESOURCE SESSION ACTIVE";
    }
}
