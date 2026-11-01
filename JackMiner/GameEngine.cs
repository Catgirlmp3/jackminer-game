using System.Security.Cryptography;

namespace JackMiner;

public sealed record PlayingCard(string Rank, string Suit)
{
    public int Value => Rank switch
    {
        "A" => 11,
        "K" or "Q" or "J" => 10,
        _ => int.Parse(Rank)
    };

    public string Symbol => Suit switch
    {
        "Hearts" => "♥",
        "Diamonds" => "♦",
        "Clubs" => "♣",
        _ => "♠"
    };

    public bool IsRed => Suit is "Hearts" or "Diamonds";
    public override string ToString() => $"{Rank}{Symbol}";
}

public sealed class BlackjackHand
{
    public List<PlayingCard> Cards { get; } = [];
    public decimal Bet { get; set; }
    public bool IsFinished { get; set; }
    public bool IsDoubled { get; set; }
    public bool IsSplitHand { get; set; }

    public int Total
    {
        get
        {
            var total = Cards.Sum(c => c.Value);
            var aces = Cards.Count(c => c.Rank == "A");
            while (total > 21 && aces-- > 0) total -= 10;
            return total;
        }
    }

    public bool IsSoft
    {
        get
        {
            var hardTotal = Cards.Sum(c => c.Rank == "A" ? 1 : c.Value);
            return Cards.Any(c => c.Rank == "A") && hardTotal + 10 <= 21;
        }
    }

    public bool IsBust => Total > 21;
    public bool IsBlackjack => Cards.Count == 2 && Total == 21 && !IsSplitHand;
    public bool CanSplit => Cards.Count == 2 && Cards[0].Value == Cards[1].Value;
}

public sealed class GameEngine
{
    private readonly List<PlayingCard> _shoe = [];
    private int _nextCard;
    private readonly List<BlackjackHand> _hands = [];

    public decimal Balance { get; private set; } = 1000m;
    public IReadOnlyList<BlackjackHand> Hands => _hands;
    public BlackjackHand Dealer { get; } = new();
    public int ActiveHandIndex { get; private set; }
    public bool RoundInProgress { get; private set; }
    public bool DealerRevealed { get; private set; }
    public string Status { get; private set; } = "Place your bet to begin.";
    public decimal LastNet { get; private set; }

    public BlackjackHand? ActiveHand => RoundInProgress && ActiveHandIndex < _hands.Count ? _hands[ActiveHandIndex] : null;
    public bool CanHit => ActiveHand is { IsFinished: false, IsBust: false, IsBlackjack: false };
    public bool CanStand => CanHit;
    public bool CanDouble => ActiveHand is { Cards.Count: 2, IsFinished: false } hand && Balance >= hand.Bet;
    public bool CanSplit => ActiveHand is { CanSplit: true, IsFinished: false } hand && _hands.Count == 1 && Balance >= hand.Bet;

    public void RestoreBalance(decimal balance)
    {
        if (RoundInProgress) throw new InvalidOperationException("Cannot restore a balance during a round.");
        Balance = Math.Clamp(balance, 0m, 1_000_000_000m);
    }

    public bool TrySpend(decimal amount)
    {
        if (amount <= 0 || Balance < amount || RoundInProgress) return false;
        Balance -= amount;
        return true;
    }

    public void AwardBonus(decimal amount)
    {
        if (amount <= 0 || RoundInProgress) return;
        Balance = Math.Min(1_000_000_000m, Balance + amount);
    }

    public bool StartRound(decimal wager)
    {
        if (RoundInProgress || wager <= 0 || wager > 300 || Balance < wager || wager % 5 != 0) return false;
        if (_shoe.Count - _nextCard < 52) BuildShoe();

        Balance -= wager;
        LastNet = 0;
        Dealer.Cards.Clear();
        Dealer.IsFinished = false;
        _hands.Clear();
        ActiveHandIndex = 0;
        DealerRevealed = false;

        var hand = new BlackjackHand { Bet = wager };
        hand.Cards.Add(Draw());
        Dealer.Cards.Add(Draw());
        hand.Cards.Add(Draw());
        Dealer.Cards.Add(Draw());
        _hands.Add(hand);
        RoundInProgress = true;

        if (Dealer.Total == 21)
        {
            DealerRevealed = true;
            if (hand.IsBlackjack)
            {
                Balance += wager;
                Status = "Push — both you and the dealer have blackjack.";
                LastNet = 0;
            }
            else
            {
                Status = "Dealer blackjack. Hand lost.";
                LastNet = -wager;
            }
            EndRound();
        }
        else if (hand.IsBlackjack)
        {
            DealerRevealed = true;
            Balance += wager * 2.5m;
            LastNet = wager * 1.5m;
            Status = "BLACKJACK! Paid 3:2.";
            EndRound();
        }
        else
        {
            Status = "Your move. Hit, stand, double or split.";
        }
        return true;
    }

    public void Hit()
    {
        if (!CanHit) return;
        var hand = ActiveHand!;
        hand.Cards.Add(Draw());
        if (hand.IsBust)
        {
            hand.IsFinished = true;
            Status = $"Hand {ActiveHandIndex + 1} busts with {hand.Total}.";
            AdvanceHand();
        }
        else if (hand.Total == 21)
        {
            hand.IsFinished = true;
            Status = "21 — standing automatically.";
            AdvanceHand();
        }
        else Status = $"You have {hand.Total}.";
    }

    public void Stand()
    {
        if (!CanStand) return;
        var hand = ActiveHand!;
        hand.IsFinished = true;
        Status = $"Hand {ActiveHandIndex + 1} stands on {hand.Total}.";
        AdvanceHand();
    }

    public void DoubleDown()
    {
        if (!CanDouble) return;
        var hand = ActiveHand!;
        Balance -= hand.Bet;
        hand.Bet *= 2;
        hand.IsDoubled = true;
        hand.Cards.Add(Draw());
        hand.IsFinished = true;
        Status = hand.IsBust ? $"Double down — bust with {hand.Total}." : $"Double down — {hand.Total}.";
        AdvanceHand();
    }

    public void Split()
    {
        if (!CanSplit) return;
        var original = ActiveHand!;
        Balance -= original.Bet;
        var second = new BlackjackHand { Bet = original.Bet, IsSplitHand = true };
        var moved = original.Cards[1];
        original.Cards.RemoveAt(1);
        second.Cards.Add(moved);
        original.IsSplitHand = true;
        original.Cards.Add(Draw());
        second.Cards.Add(Draw());
        _hands.Add(second);
        Status = "Hands split. Play the first hand.";
        if (original.Total == 21) original.IsFinished = true;
        if (second.Total == 21) second.IsFinished = true;
        if (original.IsFinished) AdvanceHand();
    }

    private void AdvanceHand()
    {
        while (ActiveHandIndex < _hands.Count && _hands[ActiveHandIndex].IsFinished)
            ActiveHandIndex++;

        if (ActiveHandIndex >= _hands.Count)
            ResolveDealerAndPayouts();
        else
            Status = $"Playing hand {ActiveHandIndex + 1} of {_hands.Count}.";
    }

    private void ResolveDealerAndPayouts()
    {
        DealerRevealed = true;
        if (_hands.Any(h => !h.IsBust))
        {
            while (Dealer.Total < 17)
                Dealer.Cards.Add(Draw());
            // Dealer stands on soft 17 as well as hard 17.
        }

        decimal returned = 0;
        var summaries = new List<string>();
        foreach (var hand in _hands)
        {
            string result;
            if (hand.IsBust) result = "Bust";
            else if (Dealer.IsBust || hand.Total > Dealer.Total)
            {
                returned += hand.Bet * 2;
                result = "Win";
            }
            else if (hand.Total == Dealer.Total)
            {
                returned += hand.Bet;
                result = "Push";
            }
            else result = "Lose";
            summaries.Add($"Hand {summaries.Count + 1}: {result}");
        }

        Balance += returned;
        LastNet = returned - _hands.Sum(h => h.Bet);
        Status = string.Join("   •   ", summaries);
        EndRound();
    }

    private void EndRound()
    {
        RoundInProgress = false;
        foreach (var hand in _hands) hand.IsFinished = true;
    }

    private PlayingCard Draw()
    {
        if (_nextCard >= _shoe.Count) BuildShoe();
        return _shoe[_nextCard++];
    }

    private void BuildShoe()
    {
        string[] suits = ["Hearts", "Diamonds", "Clubs", "Spades"];
        string[] ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
        _shoe.Clear();
        for (var deck = 0; deck < 6; deck++)
            foreach (var suit in suits)
                foreach (var rank in ranks)
                    _shoe.Add(new PlayingCard(rank, suit));

        for (var i = _shoe.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (_shoe[i], _shoe[j]) = (_shoe[j], _shoe[i]);
        }
        _nextCard = 0;
    }
}