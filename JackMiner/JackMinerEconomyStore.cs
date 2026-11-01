using System.IO;
using System.Text.Json;

namespace JackMiner;

public sealed class JackMinerEconomyState
{
    public decimal Balance { get; set; } = 1000m;
    public List<string> OwnedCollectibles { get; set; } = [];
    public DateTimeOffset NextGiveawayUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
}

public sealed class JackMinerEconomyStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackMiner", "economy.json");

    public JackMinerEconomyState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new JackMinerEconomyState();
            var state = JsonSerializer.Deserialize<JackMinerEconomyState>(File.ReadAllText(_path));
            if (state is null) return new JackMinerEconomyState();
            state.Balance = Math.Clamp(state.Balance, 0m, 1_000_000_000m);
            state.OwnedCollectibles ??= [];
            state.NextGiveawayUtc = state.NextGiveawayUtc == default
                ? DateTimeOffset.UtcNow.AddMinutes(15) : state.NextGiveawayUtc;
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new JackMinerEconomyState();
        }
    }

    public void Save(JackMinerEconomyState state)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
