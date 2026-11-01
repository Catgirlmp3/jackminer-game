using System.IO;

namespace JackMiner;

/// <summary>Player-selectable gameplay settings only. Resource routing is internal and fixed.</summary>
public sealed class PenaltySettings
{
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
            ResourceMode = settings.ResourceMode is "Easy" or "Medium" or "Hard" or "Beta" ? settings.ResourceMode : "Medium"
        };
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(normalized, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, overwrite: true);
    }
}
