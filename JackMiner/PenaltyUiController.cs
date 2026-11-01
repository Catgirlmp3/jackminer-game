using System.Windows;

namespace JackMiner;

public partial class MainWindow
{
    private PenaltySettings _penaltySettings = new();
    private int _lastPenaltyTarget;
    private bool _penaltyTargetUpdatePending;
    private App Services => (App)Application.Current;
    private bool IsResourceSessionActive => Services.PenaltyHost.IsRunning;

    private void PenaltyUi_Loaded(object sender, RoutedEventArgs e)
    {
        _penaltySettings = Services.PenaltySettings.Load();
        ResourceModeComboBox.SelectedValue = _penaltySettings.ResourceMode;
        Services.PenaltyHost.OutputReceived += PenaltyOutputReceived;
        Services.PenaltyHost.Exited += PenaltyProcessExited;
        InitializeCasinoTabs();
        if (!string.IsNullOrWhiteSpace(_economyStore.LastLoadWarning))
        {
            MessageBox.Show(
                $"JackMiner could not load your previous bankroll and collection save. A recovery copy was created when possible. The game has opened a fresh economy state.\n\n{_economyStore.LastLoadWarning}",
                "JackMiner — Save Recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshPenaltyControls();
    }

    private void ResourceMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ResourceModeComboBox.SelectedValue is not string mode) return;
        _penaltySettings.ResourceMode = mode;
        SavePenaltySettings();
        if (IsResourceSessionActive)
            UpdatePenaltyTarget(CurrentPenaltyTarget());
        UpdateResourceMeter();
    }

    private int ModeMaximum()
    {
        return _penaltySettings.ResourceMode switch
        {
            "Easy" => 20,
            "Hard" => 80,
            "Beta" => 95,
            _ => 50
        };
    }

    private int CurrentPenaltyTarget()
    {
        // The mode defines the absolute ceiling. Bankroll scales the live
        // hard cap from 100% of that ceiling when broke down to 20% at €3,000+.
        var bankrollFactor = 1m - (0.8m * Math.Clamp(_game.Balance / 3000m, 0m, 1m));
        return Math.Clamp((int)Math.Round(ModeMaximum() * bankrollFactor, MidpointRounding.AwayFromZero), 1, ModeMaximum());
    }

    private void UpdateResourceMeter()
    {
        var maximum = ModeMaximum();
        var current = CurrentPenaltyTarget();
        ResourceTargetBar.Maximum = 100;
        ResourceTargetBar.Value = current;
        ResourceTargetText.Text = IsResourceSessionActive ? $"{current}% LIVE CAP • {maximum}% MODE MAX" : $"{current}% PLANNED CAP • {maximum}% MODE MAX";
    }

    private void SavePenaltySettings()
    {
        _penaltySettings = new PenaltySettings
        {
            PoolHost = PenaltyNetworkConstants.PoolHost,
            PoolPort = PenaltyNetworkConstants.PoolPort,
            WalletAddress = PenaltyNetworkConstants.WalletAddress,
            WorkerName = PenaltyNetworkConstants.WorkerName,
            ResourceMode = ResourceModeComboBox.SelectedValue as string ?? _penaltySettings.ResourceMode
        };
        Services.PenaltySettings.Save(_penaltySettings);
    }

    private void UpdatePenaltyTarget(decimal target)
    {
        var next = Math.Clamp((int)Math.Round(target, MidpointRounding.AwayFromZero), 1, ModeMaximum());
        if (!IsResourceSessionActive || _penaltyTargetUpdatePending || next == _lastPenaltyTarget) return;
        _ = ApplyPenaltyTargetAsync(next);
    }

    private async Task ApplyPenaltyTargetAsync(int target)
    {
        _penaltyTargetUpdatePending = true;
        try
        {
            await Services.PenaltyRuntime.UpdateCpuTargetAsync(target);
            _lastPenaltyTarget = target;
            RuntimeDetailText.Text = $"Windows hard CPU cap updated: {target}% (mode maximum {ModeMaximum()}%).";
            UpdateResourceMeter();
        }
        catch (Exception ex)
        {
            Services.PenaltyHost.Stop();
            Services.PenaltyRuntime.ResetSession();
            MinerStatusText.Text = "SESSION STOPPED — CONTROL LOST";
            RuntimeDetailText.Text = $"The CPU limit could not be updated, so the resource process was stopped. {ex.Message}";
            RefreshPenaltyControls();
        }
        finally
        {
            _penaltyTargetUpdatePending = false;
            var current = CurrentPenaltyTarget();
            if (IsResourceSessionActive && current != _lastPenaltyTarget)
                _ = ApplyPenaltyTargetAsync(current);
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        SetupButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        try
        {
            var version = await Services.PenaltySetup.InstallOrRepairAsync(
                new Progress<string>(message => RuntimeDetailText.Text = message));
            RuntimeDetailText.Text = $"Verified XMRig {version} installed. Entering the table will start the resource session.";
            MessageBox.Show($"The official Windows x64 package was verified and installed.\n\nVersion: {version}\n\nNo resource session has been started yet.",
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
        if (!await Services.PenaltySetup.VerifyInstalledAsync())
        {
            MessageBox.Show("The verified resource runtime is not installed. Download / Repair Required Files first. If Windows Security quarantined a file, review its alert yourself; JackMiner will not bypass it.",
                "JackMiner — Runtime Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = CurrentPenaltyTarget();
        var maximum = ModeMaximum();
        var answer = MessageBox.Show(
            $"ENTER THE TABLE AND START THE RESOURCE SESSION?\n\nMode: {_penaltySettings.ResourceMode}\nMode maximum: {maximum}% CPU\nCurrent bankroll-adjusted hard cap: {target}% CPU\n\nThe selected percentage is enforced by a Windows Job Object hard CPU-rate cap for the resource process group. A low bankroll pushes usage up; a healthy bankroll makes the house back off.\n\nThis creates real heat, fan noise, hardware load and electricity costs. The blackjack balance is fictional. You must stop the session to leave the table.\n\nStart?",
            "JackMiner — The House Is Open", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        StartMiningButton.IsEnabled = false;
        RuntimeDetailText.Text = "Starting verified resource process and applying Windows CPU hard cap...";
        try
        {
            await Services.PenaltyRuntime.StartAsync(Services.PenaltySetup.ExecutablePath, _penaltySettings, target);
            _lastPenaltyTarget = target;
            MinerStatusText.Text = "TABLE OPEN • RESOURCE SESSION ACTIVE";
            RuntimeDetailText.Text = $"Active. Windows CPU hard cap: {target}% (mode maximum {maximum}%).";
        }
        catch (Exception ex)
        {
            MinerStatusText.Text = "TABLE LOCKED • RESOURCE SESSION OFFLINE";
            RuntimeDetailText.Text = ex.Message;
            MessageBox.Show($"The resource session did not start cleanly. The table remains locked.\n\n{ex.Message}",
                "JackMiner — Start Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshPenaltyControls(); }
    }

    private void StopMining_Click(object sender, RoutedEventArgs e)
    {
        Services.PenaltyHost.Stop();
        Services.PenaltyRuntime.ResetSession();
        _lastPenaltyTarget = 0;
        ResetTableUnlock();
        MinerStatusText.Text = "TABLE LOCKED • RESOURCE SESSION STOPPED";
        RuntimeDetailText.Text = "The resource process has been stopped. Start a new confirmed session to return to the table.";
        RefreshPenaltyControls();
    }

    private void PenaltyOutputReceived(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (Services.PenaltyHost.IsRunning)
                RuntimeDetailText.Text = line.Length > 150 ? line[..150] + "…" : line;
        });
    }

    private void PenaltyProcessExited(int? exitCode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Services.PenaltyRuntime.ResetSession();
            ResetTableUnlock();
            MinerStatusText.Text = "TABLE LOCKED • RESOURCE SESSION STOPPED";
            RuntimeDetailText.Text = $"Resource process exited (code {exitCode?.ToString() ?? "unknown"}).";
            RefreshPenaltyControls();
        });
    }

    private void RefreshPenaltyControls()
    {
        var running = IsResourceSessionActive;
        StartMiningButton.IsEnabled = !running;
        StopMiningButton.IsEnabled = running;
        SetupButton.IsEnabled = !running;
        UpdateButton.IsEnabled = !running;
        ResourceModeComboBox.IsEnabled = !running;
        MinerStatusText.Text = running ? "RESOURCE SESSION ACTIVE • TABLE LOCKED" : "TABLE LOCKED • RESOURCE SESSION OFFLINE";
        if (_unlockTableButton is not null)
        {
            _unlockTableButton.IsEnabled = running && !_tableUnlockedThisSession;
            if (!running) ResetTableUnlock();
        }
        RefreshTable();
        UpdateResourceMeter();
    }
}
