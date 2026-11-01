using System.Windows;

namespace JackMiner;

public partial class App : Application
{
    public PenaltyProcessHost PenaltyHost { get; } = new();
    public PenaltySettingsStore PenaltySettings { get; } = new();
    public PenaltySetupService PenaltySetup { get; } = new();

    protected override void OnExit(ExitEventArgs e)
    {
        PenaltyHost.Dispose();
        base.OnExit(e);
    }
}
