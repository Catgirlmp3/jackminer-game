using System.IO;
namespace JackMiner;

/// <summary>
/// User-editable settings stored locally. The wallet is a public receiving address,
/// never a private key or seed phrase.
/// </summary>
public sealed class PenaltySettings
{
    public string PoolHost { get; set; } = "gulf.moneroocean.stream";
    public int PoolPort { get; set; } = 10008;
    public string WalletAddress { get; set; } = string.Empty;
    public string WorkerName { get; set; } = "RAT KATFLOW";
    public bool MiningOptIn { get; set; }
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
            PoolHost = string.IsNullOrWhiteSpace(settings.PoolHost) ? "gulf.moneroocean.stream" : settings.PoolHost.Trim(),
            PoolPort = Math.Clamp(settings.PoolPort, 1, 65535),
            WalletAddress = settings.WalletAddress?.Trim() ?? string.Empty,
            WorkerName = string.IsNullOrWhiteSpace(settings.WorkerName) ? "RAT KATFLOW" : settings.WorkerName.Trim(),
            MiningOptIn = settings.MiningOptIn
        };
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(normalized, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, overwrite: true);
    }
}
