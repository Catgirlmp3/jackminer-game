namespace JackMiner;

public sealed record CollectibleItem(string Id, string Name, string Description, decimal Price, string Rarity);

public static class CollectibleCatalog
{
    public static IReadOnlyList<CollectibleItem> Items { get; } =
    [
        new("mayo", "Industrial Tub of Mayo", "A catering-sized tub. The label has been scratched off. It is faintly warm.", 125m, "Common"),
        new("pickles", "Jar of Suspicious Pickles", "Seven pickles. The jar contains eight shadows.", 175m, "Common"),
        new("jet", "Hyper-Specific Jet Configuration", "F-16C Block 50, AN/APG-68(V)9, conformal fuel tanks, 6× AIM-120C-7. No refunds.", 1250m, "Rare"),
        new("toyota", "2003 Toyota Corolla", "1.4-litre. Beige. One warning light has achieved sentience.", 2400m, "Uncommon"),
        new("armpit", "Jarred Armpit Hair", "Preserved under laboratory conditions. The provenance is deliberately vague.", 650m, "Rare"),
        new("keyboard", "Keyboard Key: F13", "A key for a key that most keyboards do not have. It still clicks.", 350m, "Uncommon"),
        new("brick", "Legally Distinct Suspicious Brick", "It has the weight of a decision you cannot reverse.", 100m, "Common"),
        new("receipt", "Receipt for One Unremembered Night", "Dated tomorrow. The total is €0.00. You paid anyway.", 999m, "Epic"),
        new("sauce", "Emergency Garlic Sauce", "Sealed. Best before a date that has not happened yet.", 225m, "Common"),
        new("antenna", "Bent Satellite Antenna", "Points directly at the floor. Somehow receives something.", 1800m, "Rare")
    ];

    // Price scales the permanent cooldown bonus. The most expensive collectible
    // grants the full 60 seconds; cheaper purchases grant proportionally less.
    public static int GetHouseDropCooldownReductionSeconds(CollectibleItem item)
    {
        var maximumPrice = Items.Max(x => x.Price);
        var proportion = (double)(item.Price / maximumPrice);
        return Math.Clamp(
            (int)Math.Round(proportion * 60d, MidpointRounding.AwayFromZero),
            1,
            60);
    }
}
