using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JackMiner;

public partial class MainWindow
{
    private readonly JackMinerEconomyStore _economyStore = new();
    private JackMinerEconomyState _economyState = new();
    private bool _tableUnlockedThisSession;
    private bool _economyInitialized;
    private bool _casinoTabsInitialized;
    private bool _saveWarningShown;
    private readonly DispatcherTimer _giveawayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Border? _giveawayPopup;
    private Button? _unlockTableButton;
    private StackPanel? _shopPanel;
    private StackPanel? _inventoryPanel;
    private TextBlock? _giveawayCountdown;
    private Button? _claimGiveawayButton;
    private Border? _tableLockOverlay;
    private TextBlock? _bailoutStatusText;
    private readonly List<Button> _fullSystemContractButtons = [];
    private readonly DispatcherTimer _fullSystemTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _fullSystemEndsUtc;
    private decimal _fullSystemReward;
    private bool _tableWasUnlockedBeforeRecovery;
    private bool _fullSystemRecoveryActive;

    private sealed record FullSystemContract(int Minutes, decimal Reward);

    private static readonly FullSystemContract[] FullSystemContracts =
    [
        new(10, 100m),
        new(25, 300m),
        new(30, 400m),
        new(60, 1500m)
    ];

    private bool CanUseTable => IsResourceSessionActive && _tableUnlockedThisSession;

    private void InitializeCasinoTabs()
    {
        if (_casinoTabsInitialized) return;

        _economyState = _economyStore.Load();
        _game.RestoreBalance(_economyState.Balance);
        _economyInitialized = true;

        if (CasinoTableScroll.Content is not StackPanel tableContent)
            throw new InvalidOperationException("The TABLE controls could not be initialized because their scroll panel is missing.");

        var lockContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 360
        };
        lockContent.Children.Add(new TextBlock
        {
            Text = "TABLE LOCKED",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        lockContent.Children.Add(new TextBlock
        {
            Text = "To play: install or repair the verified runtime, choose a resource mode, then press ENTER TABLE + START RESOURCE SESSION. Once it is running, open the TABLE tab and press UNLOCK TABLE. You can then choose chips and deal a hand.",
            Foreground = new SolidColorBrush(Color.FromRgb(224, 210, 230)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });
        _tableLockOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 8, 10, 13)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = lockContent
        };
        Grid.SetRowSpan(_tableLockOverlay, Math.Max(1, FeltGrid.RowDefinitions.Count));
        Panel.SetZIndex(_tableLockOverlay, 100);
        FeltGrid.Children.Add(_tableLockOverlay);

        _unlockTableButton = new Button
        {
            Content = "UNLOCK TABLE",
            Height = 38,
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(135, 65, 109)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        _unlockTableButton.Click += UnlockTable_Click;
        tableContent.Children.Insert(0, _unlockTableButton);

        // Detach the existing ScrollViewer from its Border before moving it into a TabItem.
        // Keep its StackPanel content intact; clearing that content made every table control vanish.
        CasinoSidePanel.Child = null;

        var tabs = new TabControl
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 24, 37)),
            BorderThickness = new Thickness(0)
        };
        tabs.Items.Add(new TabItem { Header = "TABLE", Content = CasinoTableScroll });

        _shopPanel = new StackPanel { Margin = new Thickness(8) };
        _shopPanel.Children.Add(new TextBlock
        {
            Text = "THE OBJECTS DEPARTMENT",
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 4)
        });
        _shopPanel.Children.Add(new TextBlock
        {
            Text = "Every collectible permanently trims the House Drop cooldown. The bonus scales with price: the most expensive item removes 60 seconds, cheaper items remove less. Combined reductions are capped at one minute.",
            Foreground = new SolidColorBrush(Color.FromRgb(174, 164, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "ODDITIES SHOP",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _shopPanel
            }
        });

        var rewards = new StackPanel { Margin = new Thickness(12) };
        rewards.Children.Add(new TextBlock
        {
            Text = "THE HOUSE REMEMBERS",
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold
        });
        _giveawayCountdown = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(194, 178, 211)),
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        rewards.Children.Add(_giveawayCountdown);
        _claimGiveawayButton = new Button
        {
            Content = "CLAIM HOUSE DROP  •  €25",
            Height = 42,
            Background = new SolidColorBrush(Color.FromRgb(120, 71, 166)),
            Foreground = Brushes.White,
            IsEnabled = false
        };
        _claimGiveawayButton.Click += ClaimGiveaway_Click;
        rewards.Children.Add(_claimGiveawayButton);
        rewards.Children.Add(new TextBlock
        {
            Text = "A drop is normally every 15 minutes. Owned collectibles permanently reduce this interval by up to 60 seconds. You must claim it yourself; missed drops do not stack.",
            Foreground = new SolidColorBrush(Color.FromRgb(174, 164, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        tabs.Items.Add(new TabItem { Header = "HOUSE DROPS", Content = rewards });

        var bailout = new StackPanel { Margin = new Thickness(12) };
        bailout.Children.Add(new TextBlock
        {
            Text = "BANKRUPTCY RECOVERY",
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold
        });
        bailout.Children.Add(new TextBlock
        {
            Text = "At €0, sign a full-CPU contract. XMRig uses every physical CPU core for the selected duration. Your virtual bankroll is credited only when the contract finishes. The House Drop timer is frozen for the entire contract.",
            Foreground = new SolidColorBrush(Color.FromRgb(194, 178, 211)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        });
        _bailoutStatusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(232, 169, 212)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        bailout.Children.Add(_bailoutStatusText);

        foreach (var contract in FullSystemContracts)
        {
            var button = new Button
            {
                Content = $"{contract.Minutes} MINUTES  •  €{contract.Reward:N0} VIRTUAL CREDIT",
                Tag = contract,
                Height = 42,
                Margin = new Thickness(0, 0, 0, 7),
                Background = new SolidColorBrush(Color.FromRgb(74, 54, 91)),
                Foreground = Brushes.White,
                IsEnabled = false
            };
            button.Click += FullSystemRecovery_Click;
            _fullSystemContractButtons.Add(button);
            bailout.Children.Add(button);
        }

        bailout.Children.Add(new TextBlock
        {
            Text = "Contracts: 10 min / €100 • 25 min / €300 • 30 min / €400 • 1 hour / €1,500. This is a CPU-only workload; heat, fan noise and electricity use are real. The credit is fictional and is not a mining payout.",
            Foreground = new SolidColorBrush(Color.FromRgb(174, 164, 190)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 7, 0, 0)
        });
        tabs.Items.Add(new TabItem { Header = "BAILOUT", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = bailout } });

        CasinoSidePanel.Child = tabs;
        _casinoTabsInitialized = true;

        BuildShop();
        BuildGiveawayPopup();
        _giveawayTimer.Tick += (_, _) => RefreshGiveawayUi();
        _fullSystemTimer.Tick += FullSystemTimer_Tick;
        _giveawayTimer.Start();
        RefreshInventory();
        RefreshGiveawayUi();
        RefreshRecoveryUi();
        PersistEconomy();
    }

    private void UnlockTable_Click(object sender, RoutedEventArgs e)
    {
        if (!IsResourceSessionActive) return;
        var answer = MessageBox.Show(
            "The house has noticed you. Your machine will work while you play; the numbers on the felt are fiction, but the heat in your room is not. Step through, and the table will remember.",
            "JackMiner — The Table Is Listening",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _tableUnlockedThisSession = true;
        UpdateTableLockOverlay();
        _unlockTableButton!.Content = "TABLE UNLOCKED — THE HOUSE IS WATCHING";
        _unlockTableButton.IsEnabled = false;
        RefreshTable();
    }

    private void BuildShop()
    {
        if (_shopPanel is null) return;
        foreach (var item in CollectibleCatalog.Items)
        {
            var owned = _economyState.OwnedCollectibles.Contains(item.Id);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 31, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(66, 54, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = item.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = $"{item.Rarity}  •  {item.Price:C0}", Foreground = new SolidColorBrush(Color.FromRgb(232, 169, 212)), Margin = new Thickness(0, 3, 0, 3) });
            content.Children.Add(new TextBlock { Text = item.Description, Foreground = new SolidColorBrush(Color.FromRgb(184, 176, 197)), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 7) });
            var cooldownBonus = CollectibleCatalog.GetHouseDropCooldownReductionSeconds(item);
            content.Children.Add(new TextBlock { Text = $"HOUSE DROP COOLDOWN  −{cooldownBonus}s", Foreground = new SolidColorBrush(Color.FromRgb(153, 211, 176)), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 7) });
            var buy = new Button { Content = owned ? "COLLECTED" : "ACQUIRE", IsEnabled = !owned && _game.Balance >= item.Price, Tag = item.Id, Height = 30, Background = new SolidColorBrush(Color.FromRgb(74, 54, 91)), Foreground = Brushes.White };
            buy.Click += BuyCollectible_Click;
            content.Children.Add(buy);
            card.Child = content;
            _shopPanel.Children.Add(card);
        }
        _inventoryPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _shopPanel.Children.Add(new TextBlock { Text = "YOUR COLLECTION", Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 6) });
        _shopPanel.Children.Add(_inventoryPanel);
    }

    private void BuyCollectible_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = CollectibleCatalog.Items.FirstOrDefault(x => x.Id == id);
        if (item is null || _economyState.OwnedCollectibles.Contains(id) || !_game.TrySpend(item.Price)) return;

        var previousReduction = GetOwnedHouseDropCooldownReductionSeconds();
        _economyState.OwnedCollectibles.Add(id);
        var additionalReduction = GetOwnedHouseDropCooldownReductionSeconds() - previousReduction;
        ApplyHouseDropCooldownReduction(additionalReduction);

        PersistEconomy();
        RefreshTable();
        RefreshShop();
    }

    private void RefreshShop()
    {
        if (_shopPanel is null) return;
        var selected = _shopPanel.Children.OfType<Border>().ToList();
        for (var i = 0; i < selected.Count && i < CollectibleCatalog.Items.Count; i++)
        {
            if (selected[i].Child is not StackPanel panel || panel.Children.OfType<Button>().FirstOrDefault() is not { } button) continue;
            var item = CollectibleCatalog.Items[i];
            var owned = _economyState.OwnedCollectibles.Contains(item.Id);
            button.Content = owned ? "COLLECTED" : "ACQUIRE";
            button.IsEnabled = !owned && _game.Balance >= item.Price;
        }
        RefreshInventory();
    }

    private void RefreshInventory()
    {
        if (_inventoryPanel is null) return;
        _inventoryPanel.Children.Clear();
        var owned = CollectibleCatalog.Items.Where(x => _economyState.OwnedCollectibles.Contains(x.Id)).ToList();
        if (owned.Count == 0)
            _inventoryPanel.Children.Add(new TextBlock { Text = "Nothing collected. The shelves are waiting.", Foreground = new SolidColorBrush(Color.FromRgb(174, 164, 190)) });
        foreach (var item in owned)
        {
            var bonus = CollectibleCatalog.GetHouseDropCooldownReductionSeconds(item);
            _inventoryPanel.Children.Add(new TextBlock { Text = $"✦  {item.Name}  (−{bonus}s House Drop)", Foreground = new SolidColorBrush(Color.FromRgb(232, 169, 212)), Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap });
        }
    }

    private void BuildGiveawayPopup()
    {
        if (Content is not Grid root) return;
        var panel = new StackPanel { Width = 340 };
        panel.Children.Add(new TextBlock { Text = "THE HOUSE HAS LEFT YOU SOMETHING.", Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "A small mercy. Do not ask why.", Foreground = new SolidColorBrush(Color.FromRgb(196, 182, 208)), Margin = new Thickness(0, 7, 0, 14) });
        var claim = new Button { Content = "CLAIM €25", Height = 42, Background = new SolidColorBrush(Color.FromRgb(135, 65, 109)), Foreground = Brushes.White };
        claim.Click += ClaimGiveaway_Click;
        panel.Children.Add(claim);
        _giveawayPopup = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(29, 24, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 92, 151)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Width = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = panel
        };
        Grid.SetRowSpan(_giveawayPopup, 3);
        Panel.SetZIndex(_giveawayPopup, 250);
        root.Children.Add(_giveawayPopup);
    }

    private void ClaimGiveaway_Click(object sender, RoutedEventArgs e)
    {
        if (_economyState.GiveawayTimerPaused || DateTimeOffset.UtcNow < _economyState.NextGiveawayUtc) return;
        _game.AwardBonus(25m);
        var cooldown = GetEffectiveHouseDropCooldown();
        _economyState.NextGiveawayUtc = DateTimeOffset.UtcNow.Add(cooldown);
        if (_giveawayPopup is not null) _giveawayPopup.Visibility = Visibility.Collapsed;
        PersistEconomy();
        RefreshTable();
        RefreshShop();
        RefreshGiveawayUi();
        MessageBox.Show($"€25 added to your fictional bankroll. The next House Drop is in {cooldown.TotalMinutes:0.##} minutes.", "JackMiner — House Drop", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshGiveawayUi()
    {
        if (_economyState.GiveawayTimerPaused)
        {
            var paused = TimeSpan.FromSeconds(Math.Max(0, _economyState.GiveawayPausedRemainingSeconds));
            if (_giveawayCountdown is not null)
                _giveawayCountdown.Text = $"PAUSED DURING FULL-SYSTEM RECOVERY • {Math.Max(0, (int)paused.TotalMinutes):00}:{Math.Max(0, paused.Seconds):00} remaining when the contract began.";
            if (_claimGiveawayButton is not null)
            {
                _claimGiveawayButton.IsEnabled = false;
                _claimGiveawayButton.Content = "HOUSE DROP TIMER PAUSED";
            }
            if (_giveawayPopup is not null) _giveawayPopup.Visibility = Visibility.Collapsed;
            return;
        }

        var remaining = _economyState.NextGiveawayUtc - DateTimeOffset.UtcNow;
        var available = remaining <= TimeSpan.Zero;
        if (_giveawayCountdown is not null)
            _giveawayCountdown.Text = available
                ? "A house drop is waiting. Claim it before the house changes its mind."
                : $"Next house drop in {Math.Max(0, (int)remaining.TotalMinutes):00}:{Math.Max(0, remaining.Seconds):00}. Current cooldown: {GetEffectiveHouseDropCooldown().TotalMinutes:0.##} minutes.";
        if (_claimGiveawayButton is not null)
        {
            _claimGiveawayButton.IsEnabled = available;
            _claimGiveawayButton.Content = available ? "CLAIM HOUSE DROP  •  €25" : "THE HOUSE IS COUNTING...";
        }
        if (available && _giveawayPopup is not null)
            _giveawayPopup.Visibility = Visibility.Visible;
    }

    private int GetOwnedHouseDropCooldownReductionSeconds()
    {
        var ownedReduction = CollectibleCatalog.Items
            .Where(item => _economyState.OwnedCollectibles.Contains(item.Id))
            .Sum(CollectibleCatalog.GetHouseDropCooldownReductionSeconds);
        return Math.Min(60, ownedReduction);
    }

    private TimeSpan GetEffectiveHouseDropCooldown() =>
        TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(GetOwnedHouseDropCooldownReductionSeconds());

    private void ApplyHouseDropCooldownReduction(int additionalSeconds)
    {
        if (additionalSeconds <= 0) return;

        if (_economyState.GiveawayTimerPaused)
        {
            _economyState.GiveawayPausedRemainingSeconds =
                Math.Max(0, _economyState.GiveawayPausedRemainingSeconds - additionalSeconds);
        }
        else if (_economyState.NextGiveawayUtc > DateTimeOffset.UtcNow)
        {
            _economyState.NextGiveawayUtc =
                _economyState.NextGiveawayUtc.AddSeconds(-additionalSeconds);
        }

        RefreshGiveawayUi();
    }

    private void PauseHouseDropTimer()
    {
        if (_economyState.GiveawayTimerPaused) return;

        _economyState.GiveawayPausedRemainingSeconds = Math.Clamp(
            (_economyState.NextGiveawayUtc - DateTimeOffset.UtcNow).TotalSeconds,
            0d,
            TimeSpan.FromMinutes(15).TotalSeconds);
        _economyState.GiveawayTimerPaused = true;
        PersistEconomy();
        RefreshGiveawayUi();
    }

    private void ResumeHouseDropTimer()
    {
        if (!_economyState.GiveawayTimerPaused) return;

        var remaining = Math.Clamp(
            _economyState.GiveawayPausedRemainingSeconds,
            0d,
            TimeSpan.FromMinutes(15).TotalSeconds);
        _economyState.NextGiveawayUtc = DateTimeOffset.UtcNow.AddSeconds(remaining);
        _economyState.GiveawayTimerPaused = false;
        _economyState.GiveawayPausedRemainingSeconds = 0;
        PersistEconomy();
        RefreshGiveawayUi();
    }

    private async void FullSystemRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FullSystemContract contract }) return;
        await StartFullSystemRecoveryAsync(contract);
    }

    private async Task StartFullSystemRecoveryAsync(FullSystemContract contract)
    {
        if (!IsResourceSessionActive || _game.Balance > 0 || _game.RoundInProgress || _fullSystemRecoveryActive)
            return;

        var poolPort = PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode, fullSystemRecovery: true);
        var answer = MessageBox.Show(
            $"SIGN THE FULL-SYSTEM RECOVERY CONTRACT?\n\nDuration: {contract.Minutes} minutes\nVirtual credit on completion: €{contract.Reward:N0}\nCPU allocation: 100% of available physical cores\nMoneroOcean pool port: {poolPort}\n\nThe House Drop countdown pauses until the contract finishes. No credit is awarded if you stop the resource session or close JackMiner before the timer completes.\n\nThis is a CPU-only workload and can cause substantial heat, fan noise and electricity use. Continue?",
            "JackMiner — Full-System Recovery",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        _tableWasUnlockedBeforeRecovery = _tableUnlockedThisSession;
        _tableUnlockedThisSession = false;
        _fullSystemRecoveryActive = true;
        _fullSystemReward = contract.Reward;
        _lastPenaltyTarget = 100; // Prevent the normal bankroll target racing this update.
        PauseHouseDropTimer();
        UpdateTableLockOverlay();
        if (_unlockTableButton is not null)
        {
            _unlockTableButton.IsEnabled = false;
            _unlockTableButton.Content = "FULL-SYSTEM RECOVERY IN PROGRESS";
        }
        UpdateResourceMeter();
        RefreshRecoveryUi();

        try
        {
            var updated = await Services.PenaltyRuntime.UpdateCpuTargetAsync(100, poolPort);
            if (!updated || !IsResourceSessionActive)
                throw new InvalidOperationException("XMRig did not accept the full-system worker profile.");

            _lastAppliedWorkerCount = PenaltyRuntimeController.GetRandomXWorkerCount(100);
            _lastAppliedPoolPort = poolPort;
            _lastFailedWorkerCount = -1;
            _lastFailedPoolPort = -1;
            _fullSystemEndsUtc = DateTimeOffset.UtcNow.AddMinutes(contract.Minutes);
            _fullSystemTimer.Start();
            RuntimeDetailText.Text = $"FULL-SYSTEM RECOVERY ACTIVE • 100% CPU worker allocation • {contract.Minutes} minutes • €{contract.Reward:N0} virtual credit on completion • pool port {poolPort}.";
            RefreshRecoveryUi();
            RefreshTable();
        }
        catch (Exception ex)
        {
            _fullSystemTimer.Stop();
            _fullSystemRecoveryActive = false;
            _fullSystemReward = 0;
            _lastPenaltyTarget = 0;
            _tableUnlockedThisSession = _tableWasUnlockedBeforeRecovery && IsResourceSessionActive;
            ResumeHouseDropTimer();

            if (IsResourceSessionActive)
            {
                var normalTarget = CurrentPenaltyTarget();
                var restored = await Services.PenaltyRuntime.UpdateCpuTargetAsync(
                    normalTarget,
                    PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode));
                if (restored)
                {
                    _lastPenaltyTarget = normalTarget;
                    _lastAppliedWorkerCount = PenaltyRuntimeController.GetRandomXWorkerCount(normalTarget);
                    _lastAppliedPoolPort = PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode);
                }
                else
                {
                    Services.PenaltyHost.Stop();
                    Services.PenaltyRuntime.ResetSession();
                    ResetTableUnlock();
                }
            }

            MessageBox.Show(
                $"The full-system contract could not start. No virtual credit was awarded.\n\n{ex.Message}",
                "JackMiner — Recovery Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RefreshTable();
            RefreshPenaltyControls();
        }
    }

    private async void FullSystemTimer_Tick(object? sender, EventArgs e)
    {
        if (!_fullSystemRecoveryActive) return;
        var remaining = _fullSystemEndsUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            await CompleteFullSystemRecoveryAsync();
            return;
        }
        RefreshRecoveryUi();
    }

    private async Task CompleteFullSystemRecoveryAsync()
    {
        if (!_fullSystemRecoveryActive) return;

        _fullSystemTimer.Stop();
        _fullSystemRecoveryActive = false;
        var reward = _fullSystemReward;
        _fullSystemReward = 0;

        // The contract has run to completion; credit it before returning to
        // the normal bankroll-scaled worker target.
        _game.AwardBonus(reward);
        ResumeHouseDropTimer();

        try
        {
            var normalTarget = CurrentPenaltyTarget();
            var normalPort = PenaltyNetworkConstants.SelectPoolPort(_penaltySettings.ResourceMode);
            var restored = await Services.PenaltyRuntime.UpdateCpuTargetAsync(normalTarget, normalPort);
            if (!restored || !IsResourceSessionActive)
                throw new InvalidOperationException("XMRig did not accept the normal worker profile after recovery.");

            _lastPenaltyTarget = normalTarget;
            _lastAppliedWorkerCount = PenaltyRuntimeController.GetRandomXWorkerCount(normalTarget);
            _lastAppliedPoolPort = normalPort;
            _lastFailedWorkerCount = -1;
            _lastFailedPoolPort = -1;
            _tableUnlockedThisSession = _tableWasUnlockedBeforeRecovery;
            if (_unlockTableButton is not null)
            {
                _unlockTableButton.Content = _tableUnlockedThisSession
                    ? "TABLE UNLOCKED — THE HOUSE IS WATCHING"
                    : "UNLOCK TABLE";
                _unlockTableButton.IsEnabled = !_tableUnlockedThisSession && IsResourceSessionActive;
            }
            UpdateTableLockOverlay();
            RuntimeDetailText.Text = $"Recovery complete. €{reward:N0} virtual credit added. Normal bankroll-scaled worker target restored to {normalTarget}% (pool port {normalPort}).";
            PersistEconomy();
            RefreshTable();
            RefreshRecoveryUi();
            RefreshGiveawayUi();
            UpdateResourceMeter();
        }
        catch (Exception ex)
        {
            // The timed workload is complete, so keep its reward, but stop the
            // miner if its safer normal profile cannot be restored.
            _lastPenaltyTarget = 0;
            Services.PenaltyHost.Stop();
            Services.PenaltyRuntime.ResetSession();
            ResetTableUnlock();
            RuntimeDetailText.Text = $"Recovery completed and €{reward:N0} was credited, but the normal worker profile could not be restored. The miner was stopped. {ex.Message}";
            PersistEconomy();
            MessageBox.Show(RuntimeDetailText.Text, "JackMiner — Recovery Completed", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshPenaltyControls();
        }
    }

    private void CancelFullSystemRecovery()
    {
        if (!_fullSystemRecoveryActive) return;

        _fullSystemTimer.Stop();
        _fullSystemRecoveryActive = false;
        _fullSystemReward = 0;
        _fullSystemEndsUtc = default;
        _tableUnlockedThisSession = false;
        ResumeHouseDropTimer();
        UpdateTableLockOverlay();
        if (_bailoutStatusText is not null)
            _bailoutStatusText.Text = "Contract cancelled. No virtual credit was awarded.";
        RefreshRecoveryUi();
    }

    private void RefreshRecoveryUi()
    {
        var available = IsResourceSessionActive &&
                        _game.Balance <= 0 &&
                        !_game.RoundInProgress &&
                        !_fullSystemRecoveryActive;

        foreach (var button in _fullSystemContractButtons)
            button.IsEnabled = available;

        if (_bailoutStatusText is null) return;

        if (_fullSystemRecoveryActive)
        {
            var remaining = _fullSystemEndsUtc - DateTimeOffset.UtcNow;
            if (_fullSystemEndsUtc == default) remaining = TimeSpan.Zero;
            _bailoutStatusText.Text = $"CONTRACT RUNNING • {Math.Max(0, (int)remaining.TotalMinutes):00}:{Math.Max(0, remaining.Seconds):00} remaining • €{_fullSystemReward:N0} pending";
        }
        else if (_game.Balance > 0)
            _bailoutStatusText.Text = "Locked. The bailout desk opens when your virtual bankroll reaches €0.";
        else if (_game.RoundInProgress)
            _bailoutStatusText.Text = "Finish the current hand before starting a recovery contract.";
        else if (!IsResourceSessionActive)
            _bailoutStatusText.Text = "Start a confirmed resource session first. The bailout requires the supervised XMRig process to be running.";
        else
            _bailoutStatusText.Text = "Bankrupt. Choose a contract. The payout is credited only after the full CPU timer completes.";
    }

    private void PersistEconomy()
    {
        if (!_economyInitialized) return;
        _economyState.Balance = _game.Balance;
        try
        {
            _economyStore.Save(_economyState);
            _saveWarningShown = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveWarningShown) return;
            _saveWarningShown = true;
            TableStatus.Text = "Save unavailable — this session's changes may not persist.";
            MessageBox.Show(
                $"JackMiner could not save your bankroll or collection. You can keep playing in this session, but changes may be lost when the game closes.\n\n{ex.Message}",
                "JackMiner — Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateTableLockOverlay()
    {
        if (_tableLockOverlay is not null)
            _tableLockOverlay.Visibility = CanUseTable ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetTableUnlock()
    {
        _tableUnlockedThisSession = false;
        if (_unlockTableButton is not null)
        {
            _unlockTableButton.Content = "UNLOCK TABLE";
            _unlockTableButton.IsEnabled = IsResourceSessionActive;
        }
        UpdateTableLockOverlay();
        RefreshTable();
    }
}
