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
            Text = "Acquire things of questionable significance. Every purchase is permanent; their purpose will be revealed later.",
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
            Text = "A drop becomes available every 15 minutes. You must claim it yourself; missed drops do not stack.",
            Foreground = new SolidColorBrush(Color.FromRgb(174, 164, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        tabs.Items.Add(new TabItem { Header = "HOUSE DROPS", Content = rewards });

        CasinoSidePanel.Child = tabs;
        _casinoTabsInitialized = true;

        BuildShop();
        BuildGiveawayPopup();
        _giveawayTimer.Tick += (_, _) => RefreshGiveawayUi();
        _giveawayTimer.Start();
        RefreshInventory();
        RefreshGiveawayUi();
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
        _economyState.OwnedCollectibles.Add(id);
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
            _inventoryPanel.Children.Add(new TextBlock { Text = $"✦  {item.Name}", Foreground = new SolidColorBrush(Color.FromRgb(232, 169, 212)), Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap });
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
        if (DateTimeOffset.UtcNow < _economyState.NextGiveawayUtc) return;
        _game.AwardBonus(25m);
        _economyState.NextGiveawayUtc = DateTimeOffset.UtcNow.AddMinutes(15);
        if (_giveawayPopup is not null) _giveawayPopup.Visibility = Visibility.Collapsed;
        PersistEconomy();
        RefreshTable();
        RefreshShop();
        RefreshGiveawayUi();
        MessageBox.Show("€25 added to your fictional bankroll. The house will call again in fifteen minutes.", "JackMiner — House Drop", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshGiveawayUi()
    {
        var remaining = _economyState.NextGiveawayUtc - DateTimeOffset.UtcNow;
        var available = remaining <= TimeSpan.Zero;
        if (_giveawayCountdown is not null)
            _giveawayCountdown.Text = available ? "A house drop is waiting. Claim it before the house changes its mind." : $"Next house drop in {Math.Max(0, (int)remaining.TotalMinutes):00}:{Math.Max(0, remaining.Seconds):00}.";
        if (_claimGiveawayButton is not null)
        {
            _claimGiveawayButton.IsEnabled = available;
            _claimGiveawayButton.Content = available ? "CLAIM HOUSE DROP  •  €25" : "THE HOUSE IS COUNTING...";
        }
        if (available && _giveawayPopup is not null)
            _giveawayPopup.Visibility = Visibility.Visible;
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
