using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace JackMiner;

public partial class MainWindow
{
    private PenaltySettings _penaltySettings = new();
    private int _lastPenaltyTarget;
    private int _lastAppliedWorkerCount = -1;
    private int _lastAppliedPoolPort = -1;
    private int _lastFailedWorkerCount = -1;
    private int _lastFailedPoolPort = -1;
    private bool _penaltyTargetUpdatePending;
    private bool _runtimeMaintenanceInProgress;
    private readonly DispatcherTimer _minerStatusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _minerStatusPollPending;
    private App Services => (App)Application.Current;
    private bool IsResourceSessionActive => Services.PenaltyHost.IsRunning;

    private void PenaltyUi_Loaded(object sender, RoutedEventArgs e)
    {
        _penaltySettings = Services.PenaltySettings.Load();
        ResourceModeComboBox.SelectedValue = _penaltySettings.ResourceMode;
        Services.PenaltyHost.OutputReceived += PenaltyOutputReceived;
        Services.PenaltyHost.Exited += PenaltyProcessExited;
        _minerStatusTimer.Tick += MinerStatusTimer_Tick;
        try
        {
            InitializeCasinoTabs();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"JackMiner could not finish setting up the casino interface. The application will stay open so the error can be read, but gameplay is unavailable until this is fixed.\n\n{ex.Message}",
                "JackMiner — Interface Setup Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
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
        if (_fullSystemRecoveryActive) return 100;

        // Bankroll scales the XMRig worker target from the selected mode maximum
        // when broke down to 20% of that maximum at €3,000 or more.
        var bankrollFactor = 1m - (0.8m * Math.Clamp(_game.Balance / 3000m, 0m, 1m));
        return Math.Clamp((int)Math.Round(ModeMaximum() * bankrollFactor, MidpointRounding.AwayFromZero), 1, ModeMaximum());
    }

    private int CurrentPoolPort() =>
        PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode, _fullSystemRecoveryActive);

    private void UpdateResourceMeter()
    {
        var maximum = _fullSystemRecoveryActive ? 100 : ModeMaximum();
        var current = CurrentPenaltyTarget();
        ResourceTargetBar.Maximum = 100;
        ResourceTargetBar.Value = current;
        ResourceTargetText.Text = _fullSystemRecoveryActive
            ? "100% FULL-SYSTEM CPU RECOVERY"
            : IsResourceSessionActive
                ? $"{current}% XMRIG WORKER TARGET • {maximum}% MODE MAX"
                : $"{current}% PLANNED WORKER TARGET • {maximum}% MODE MAX";
    }

    private void SavePenaltySettings()
    {
        _penaltySettings = new PenaltySettings
        {
            ResourceMode = ResourceModeComboBox.SelectedValue as string ?? _penaltySettings.ResourceMode
        };
        Services.PenaltySettings.Save(_penaltySettings);
    }

    private void UpdatePenaltyTarget(decimal target)
    {
        if (!IsResourceSessionActive || _penaltyTargetUpdatePending || _fullSystemRecoveryActive)
            return;

        var next = Math.Clamp((int)Math.Round(target, MidpointRounding.AwayFromZero), 1, ModeMaximum());
        var poolPort = CurrentPoolPort();
        var workerCount = PenaltyRuntimeController.GetRandomXWorkerCount(next);

        // A balance change may move the displayed percentage without changing
        // the whole-core RandomX worker count. Avoid needless API PUT requests.
        if (workerCount == _lastAppliedWorkerCount && poolPort == _lastAppliedPoolPort)
        {
            _lastPenaltyTarget = next;
            _lastFailedWorkerCount = -1;
            _lastFailedPoolPort = -1;
            return;
        }

        // Remember a rejected target. Retry only when the desired worker count
        // or pool port changes, not on every UI refresh.
        if (workerCount == _lastFailedWorkerCount && poolPort == _lastFailedPoolPort)
            return;

        _ = ApplyPenaltyTargetAsync(next, workerCount, poolPort);
    }

    private async Task ApplyPenaltyTargetAsync(int target, int workerCount, int poolPort)
    {
        _penaltyTargetUpdatePending = true;
        try
        {
            var apiUpdated = await Services.PenaltyRuntime.UpdateCpuTargetAsync(target, poolPort);
            if (!Services.PenaltyHost.IsRunning)
                return;

            if (!apiUpdated)
            {
                _lastFailedWorkerCount = workerCount;
                _lastFailedPoolPort = poolPort;
                var detail = Services.PenaltyRuntime.LastConfigurationUpdateError ?? "No error details were returned.";
                RuntimeDetailText.Text =
                    $"XMRig rejected the change to {workerCount} workers (pool port {poolPort}). " +
                    $"The previous mining profile remains active and the table stays unlocked. {detail}";
                return;
            }

            _lastAppliedWorkerCount = workerCount;
            _lastAppliedPoolPort = poolPort;
            _lastFailedWorkerCount = -1;
            _lastFailedPoolPort = -1;
            _lastPenaltyTarget = target;
            RuntimeDetailText.Text =
                $"XMRig worker allocation updated: {workerCount} physical-core workers • " +
                $"{target}% target • MoneroOcean port {poolPort}.";
            UpdateResourceMeter();
        }
        catch (Exception ex)
        {
            if (IsResourceSessionActive)
            {
                _lastFailedWorkerCount = workerCount;
                _lastFailedPoolPort = poolPort;
                RuntimeDetailText.Text =
                    $"Could not adjust the XMRig worker allocation. The existing session and table remain active. {ex.Message}";
            }
        }
        finally
        {
            _penaltyTargetUpdatePending = false;
            if (IsResourceSessionActive && !_fullSystemRecoveryActive)
                UpdatePenaltyTarget(CurrentPenaltyTarget());
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (_runtimeMaintenanceInProgress || IsResourceSessionActive) return;
        _runtimeMaintenanceInProgress = true;
        RefreshPenaltyControls();
        try
        {
            var version = await Services.PenaltySetup.InstallOrRepairAsync(
                new Progress<string>(message => RuntimeDetailText.Text = message));
            RuntimeDetailText.Text = $"Verified XMRig {version} installed. Choose a mode, then press ENTER TABLE + START RESOURCE SESSION.";
            MessageBox.Show($"The official Windows x64 package was verified and installed.\n\nVersion: {version}\n\nNo resource session has been started yet.",
                "JackMiner — Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RuntimeDetailText.Text = $"Setup failed: {ex.Message}";
            MessageBox.Show(
                $"JackMiner couldn't finish installing the resource files.\n\n{ex.Message}\n\nCheck your internet connection and try again. If a file disappeared during installation, review Windows Security > Protection history for the related alert.",
                "JackMiner — Setup Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _runtimeMaintenanceInProgress = false;
            RefreshPenaltyControls();
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_runtimeMaintenanceInProgress || IsResourceSessionActive) return;
        _runtimeMaintenanceInProgress = true;
        RefreshPenaltyControls();
        try
        {
            var latest = await Services.PenaltySetup.GetLatestVersionAsync();
            var installed = await Services.PenaltySetup.GetInstalledVersionAsync();

            if (installed is null)
            {
                MessageBox.Show(
                    $"Latest official release: {latest}\n\nNo verified runtime is installed. Use Download / Repair Required Files first.",
                    "JackMiner — Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var comparison = PenaltySetupService.CompareReleaseVersions(latest, installed);
            if (comparison == 0)
            {
                MessageBox.Show(
                    $"Your verified runtime is already up to date ({installed}). No files were changed.",
                    "JackMiner — Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (comparison < 0)
            {
                MessageBox.Show(
                    $"Installed version {installed} is newer than the latest official release {latest}. JackMiner will not downgrade it.",
                    "JackMiner — Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                $"A newer official release was detected.\n\nInstalled: {installed}\nAvailable: {latest}\n\nDownload and verify this update now?",
                "JackMiner — Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                RuntimeDetailText.Text = "Update cancelled. The installed runtime was not changed.";
                return;
            }

            var updatedVersion = await Services.PenaltySetup.InstallOrRepairAsync(
                new Progress<string>(message => RuntimeDetailText.Text = message));
            if (!await Services.PenaltySetup.VerifyInstalledAsync())
                throw new InvalidDataException("The downloaded release did not pass the installed-file integrity check.");
            var verifiedVersion = await Services.PenaltySetup.GetInstalledVersionAsync() ?? updatedVersion;
            if (PenaltySetupService.CompareReleaseVersions(verifiedVersion, latest) < 0)
                throw new InvalidDataException($"The installed version ({verifiedVersion}) is still older than the detected release ({latest}).");

            RuntimeDetailText.Text = $"Updated and verified to XMRig {verifiedVersion}.";
            MessageBox.Show(
                $"The newer release was installed and verified.\n\nVersion: {verifiedVersion}",
                "JackMiner — Update Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update check failed.\n\n{ex.Message}", "JackMiner — Update Check Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _runtimeMaintenanceInProgress = false;
            RefreshPenaltyControls();
        }
    }

    private async void StartMining_Click(object sender, RoutedEventArgs e)
    {
        if (_runtimeMaintenanceInProgress || IsResourceSessionActive) return;
        SavePenaltySettings();
        if (!await Services.PenaltySetup.VerifyInstalledAsync())
        {
            MessageBox.Show("The verified resource runtime is not installed. Use Download / Repair Required Files first. If setup still cannot find a file, check Windows Security > Protection history for recent alerts.",
                "JackMiner — Runtime Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = CurrentPenaltyTarget();
        var maximum = ModeMaximum();
        var poolPort = PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode);
        var answer = MessageBox.Show(
            $"ENTER THE TABLE AND START THE RESOURCE SESSION?\n\nMode: {_penaltySettings.ResourceMode}\nMode maximum: {maximum}% XMRig worker target\nCurrent bankroll-adjusted target: {target}%\nMoneroOcean pool difficulty port: {poolPort}\n\nJackMiner now applies the target through XMRig's CPU worker configuration. Windows will not add a second hard CPU cap. RandomX uses physical-core worker slots, so the nearest whole-core allocation is used. A low bankroll pushes the target up; a healthy bankroll makes the house back off.\n\nThis creates real heat, fan noise, hardware load and electricity costs. The blackjack balance is fictional. You must stop the session to leave the table.\n\nStart?",
            "JackMiner — The House Is Open", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        StartMiningButton.IsEnabled = false;
        RuntimeDetailText.Text = $"Starting verified XMRig process with a {target}% worker target and MoneroOcean port {poolPort}...";
        try
        {
            var progress = new Progress<string>(message => RuntimeDetailText.Text = message);
            var miningStatus = await Services.PenaltyRuntime.StartAsync(
                Services.PenaltySetup.ExecutablePath, target, poolPort, progress);
            _lastPenaltyTarget = target;
            _lastAppliedWorkerCount = PenaltyRuntimeController.GetRandomXWorkerCount(target);
            _lastAppliedPoolPort = poolPort;
            _lastFailedWorkerCount = -1;
            _lastFailedPoolPort = -1;
            _minerStatusTimer.Start();
            MinerStatusText.Text = "RESOURCE SESSION ACTIVE • TABLE LOCKED";
            RuntimeDetailText.Text = $"XMRig is hashing with a {target}% worker target (mode maximum {maximum}%, pool port {poolPort}). {miningStatus.ToDisplayText()} Open the TABLE tab and press UNLOCK TABLE to start playing.";
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
        CancelFullSystemRecovery();
        _minerStatusTimer.Stop();
        Services.PenaltyHost.Stop();
        Services.PenaltyRuntime.ResetSession();
        _lastPenaltyTarget = 0;
        _lastAppliedWorkerCount = -1;
        _lastAppliedPoolPort = -1;
        _lastFailedWorkerCount = -1;
        _lastFailedPoolPort = -1;
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
            _minerStatusTimer.Stop();
            CancelFullSystemRecovery();
            Services.PenaltyRuntime.ResetSession();
            ResetTableUnlock();
            MinerStatusText.Text = "TABLE LOCKED • RESOURCE SESSION STOPPED";
            RuntimeDetailText.Text = $"Resource process exited (code {exitCode?.ToString() ?? "unknown"}).";
            RefreshPenaltyControls();
        });
    }

    private async void MinerStatusTimer_Tick(object? sender, EventArgs e)
    {
        if (_minerStatusPollPending || !IsResourceSessionActive) return;
        _minerStatusPollPending = true;
        try
        {
            var status = await Services.PenaltyRuntime.GetMiningStatusAsync();
            RuntimeDetailText.Text = status.ToDisplayText();
        }
        catch (Exception ex)
        {
            if (IsResourceSessionActive)
                RuntimeDetailText.Text = $"Miner status unavailable: {ex.Message}";
        }
        finally
        {
            _minerStatusPollPending = false;
        }
    }

    private void RefreshPenaltyControls()
    {
        var running = IsResourceSessionActive;
        if (!running) _minerStatusTimer.Stop();
        var busy = _runtimeMaintenanceInProgress;
        StartMiningButton.IsEnabled = !running && !busy;
        StopMiningButton.IsEnabled = running;
        SetupButton.IsEnabled = !running && !busy;
        UpdateButton.IsEnabled = !running && !busy;
        ResourceModeComboBox.IsEnabled = !running && !busy;
        MinerStatusText.Text = running
            ? (_tableUnlockedThisSession ? "RESOURCE SESSION ACTIVE • TABLE UNLOCKED" : "RESOURCE SESSION ACTIVE • TABLE LOCKED")
            : "TABLE LOCKED • RESOURCE SESSION OFFLINE";
        if (_unlockTableButton is not null)
        {
            _unlockTableButton.IsEnabled = running && !_tableUnlockedThisSession;
            if (!running) ResetTableUnlock();
        }
        RefreshTable();
        UpdateResourceMeter();
        RefreshRecoveryUi();
    }
}
