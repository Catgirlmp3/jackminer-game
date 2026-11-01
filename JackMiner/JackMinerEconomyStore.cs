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

    public string? LastLoadWarning { get; private set; }

    public JackMinerEconomyState Load()
    {
        LastLoadWarning = null;
        try
        {
            if (!File.Exists(_path)) return new JackMinerEconomyState();
            var state = JsonSerializer.Deserialize<JackMinerEconomyState>(File.ReadAllText(_path));
            if (state is null)
                return RecoverFromInvalidSave("The save file contained no usable data.");
            state.Balance = Math.Clamp(state.Balance, 0m, 1_000_000_000m);
            state.OwnedCollectibles ??= [];
            state.NextGiveawayUtc = state.NextGiveawayUtc == default
                ? DateTimeOffset.UtcNow.AddMinutes(15) : state.NextGiveawayUtc;
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return RecoverFromInvalidSave($"The save file could not be read: {ex.Message}");
        }
    }

    private JackMinerEconomyState RecoverFromInvalidSave(string reason)
    {
        string? backupPath = null;
        string? backupError = null;
        try
        {
            if (File.Exists(_path))
            {
                backupPath = _path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.bak";
                File.Copy(_path, backupPath, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            backupError = ex.Message;
        }

        LastLoadWarning = backupPath is not null
            ? $"{reason} The original file was preserved at: {backupPath}"
            : backupError is not null
                ? $"{reason} JackMiner could not create a backup: {backupError}"
                : reason;
        return new JackMinerEconomyState();
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
