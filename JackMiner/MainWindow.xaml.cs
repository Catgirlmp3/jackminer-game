using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JackMiner;

public partial class MainWindow : Window
{
    private readonly GameEngine _game = new();
    private decimal _selectedWager;
    private readonly decimal[] _chips = [5, 10, 25, 50, 75, 100, 200];
    private readonly string _firstRunMarker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackMiner", "first-run-acknowledged-v2.txt");

    public MainWindow()
    {
        InitializeComponent();
        FirstRunOverlay.Visibility = File.Exists(_firstRunMarker) ? Visibility.Collapsed : Visibility.Visible;
        BuildChips();
        RefreshTable();
    }

    private void AcceptFirstRun_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_firstRunMarker)!);
            File.WriteAllText(_firstRunMarker, $"Acknowledged {DateTimeOffset.UtcNow:O}");
            FirstRunOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"JackMiner could not save the first-run acknowledgement. The warning will appear again next launch.\n\n{ex.Message}",
                "JackMiner — Preference Not Saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            FirstRunOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void DeclineFirstRun_Click(object sender, RoutedEventArgs e) => Close();

    private void BuildChips()
    {
        foreach (var amount in _chips)
        {
            var button = new Button
            {
                Content = $"€{amount}",
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(3),
                MinWidth = 58,
                Background = new SolidColorBrush(Color.FromRgb(49, 43, 62))
            };
            button.Click += (_, _) =>
            {
                if (_game.RoundInProgress) return;
                if (_selectedWager + amount > 300 || _selectedWager + amount > _game.Balance)
                {
                    TableStatus.Text = "Maximum wager is €300 and cannot exceed your balance.";
                    return;
                }
                _selectedWager += amount;
                RefreshTable();
            };
            ChipPanel.Children.Add(button);
        }
    }

    private void Deal_Click(object sender, RoutedEventArgs e)
    {
        if (!CanUseTable) return;
        if (_selectedWager <= 0)
        {
            TableStatus.Text = "Choose at least one chip first.";
            return;
        }
        if (!_game.StartRound(_selectedWager))
        {
            TableStatus.Text = "Unable to deal. Check your wager and balance.";
            return;
        }
        _selectedWager = 0;
        RefreshTable();
    }

    private void Hit_Click(object sender, RoutedEventArgs e) { if (!IsResourceSessionActive) return; _game.Hit(); RefreshTable(); }
    private void Stand_Click(object sender, RoutedEventArgs e) { if (!IsResourceSessionActive) return; _game.Stand(); RefreshTable(); }
    private void Double_Click(object sender, RoutedEventArgs e) { if (!IsResourceSessionActive) return; _game.DoubleDown(); RefreshTable(); }
    private void Split_Click(object sender, RoutedEventArgs e) { if (!IsResourceSessionActive) return; _game.Split(); RefreshTable(); }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!IsResourceSessionActive) return;
        if (_game.RoundInProgress)
        {
            TableStatus.Text = "Finish the current round before clearing your bet.";
            return;
        }
        _selectedWager = 0;
        RefreshTable();
    }

    private void RefreshTable()
    {
        BalanceText.Text = _game.Balance.ToString("C2", CultureInfo.GetCultureInfo("en-IE"));
        WagerText.Text = _selectedWager.ToString("C0", CultureInfo.GetCultureInfo("en-IE"));
        TableStatus.Text = CanUseTable ? _game.Status : IsResourceSessionActive ? "The resource session is active. Unlock the table to begin." : "The table is closed. Start a confirmed resource session to play.";
        ResultText.Text = _game.RoundInProgress ? "Round in progress." :
            _game.LastNet > 0 ? $"You won {_game.LastNet:C2} this round." :
            _game.LastNet < 0 ? $"You lost {Math.Abs(_game.LastNet):C2} this round." : "Ready for the next hand.";

        var targetLoad = CurrentPenaltyTarget();
        UpdatePenaltyTarget(targetLoad);
        UpdateResourceMeter();

        DealerCards.Children.Clear();
        for (var i = 0; i < _game.Dealer.Cards.Count; i++)
        {
            var card = _game.Dealer.Cards[i];
            var hidden = _game.RoundInProgress && !_game.DealerRevealed && i == 1;
            DealerCards.Children.Add(CreateCard(hidden ? "?" : card.ToString(), hidden ? false : card.IsRed, hidden));
        }
        DealerTotal.Text = _game.DealerRevealed ? $"  •  {_game.Dealer.Total}" :
            _game.Dealer.Cards.Count > 0 ? $"  •  {_game.Dealer.Cards[0].Value} + ?" : "  •  —";

        PlayerCards.Children.Clear();
        if (_game.Hands.Count == 0)
        {
            PlayerLabel.Text = "YOUR HAND";
        }
        else
        {
            PlayerLabel.Text = _game.Hands.Count > 1 ? $"YOUR HANDS  •  ACTIVE: {Math.Min(_game.ActiveHandIndex + 1, _game.Hands.Count)}" : "YOUR HAND";
            for (var i = 0; i < _game.Hands.Count; i++)
            {
                var hand = _game.Hands[i];
                var handPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
                handPanel.Children.Add(new TextBlock
                {
                    Text = $"HAND {i + 1}  •  {hand.Total}  •  BET €{hand.Bet:0.00}",
                    Foreground = hand == _game.ActiveHand ? Brushes.White : new SolidColorBrush(Color.FromRgb(164, 157, 177)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 5)
                });
                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var card in hand.Cards) cards.Children.Add(CreateCard(card.ToString(), card.IsRed, false));
                handPanel.Children.Add(cards);
                PlayerCards.Children.Add(handPanel);
            }
        }

        var tableOpen = CanUseTable;
        HitButton.IsEnabled = tableOpen && _game.CanHit;
        StandButton.IsEnabled = tableOpen && _game.CanStand;
        DoubleButton.IsEnabled = tableOpen && _game.CanDouble;
        SplitButton.IsEnabled = tableOpen && _game.CanSplit;
        DealButton.IsEnabled = tableOpen && !_game.RoundInProgress && _selectedWager > 0 && _selectedWager <= _game.Balance;
        foreach (Button chip in ChipPanel.Children)
            chip.IsEnabled = tableOpen && !_game.RoundInProgress && _selectedWager < 300 && _selectedWager < _game.Balance;
    }

    private static Border CreateCard(string face, bool isRed, bool hidden)
    {
        return new Border
        {
            Width = 76,
            Height = 108,
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(9),
            Background = hidden ? new SolidColorBrush(Color.FromRgb(90, 58, 119)) : Brushes.White,
            BorderBrush = hidden ? new SolidColorBrush(Color.FromRgb(184, 137, 219)) : new SolidColorBrush(Color.FromRgb(218, 214, 224)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = face,
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Foreground = hidden ? Brushes.White : (isRed ? Brushes.Firebrick : Brushes.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }
}