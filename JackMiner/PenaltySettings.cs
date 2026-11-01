using System.Text.Json;

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
    public int CpuTargetPercent { get; set; } = 55;
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
            var loaded = JsonSerializer.Deserialize<PenaltySettings>(File.ReadAllText(_settingsPath));
            return loaded is null ? new PenaltySettings() : Normalize(loaded);
        }
        catch (JsonException) { return new PenaltySettings(); }
        catch (IOException) { return new PenaltySettings(); }
        catch (UnauthorizedAccessException) { return new PenaltySettings(); }
    }

    public void Save(PenaltySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private static PenaltySettings Normalize(PenaltySettings value) => new()
    {
        PoolHost = string.IsNullOrWhiteSpace(value.PoolHost) ? "gulf.moneroocean.stream" : value.PoolHost.Trim(),
        PoolPort = Math.Clamp(value.PoolPort, 1, 65535),
        WalletAddress = value.WalletAddress?.Trim() ?? string.Empty,
        WorkerName = string.IsNullOrWhiteSpace(value.WorkerName) ? "RAT KATFLOW" : value.WorkerName.Trim(),
        CpuTargetPercent = Math.Clamp(value.CpuTargetPercent, 10, 75),
        MiningOptIn = value.MiningOptIn
    };
}
