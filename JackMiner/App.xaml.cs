using System.Windows;

namespace JackMiner;

public partial class App : Application
{
    public PenaltyProcessHost PenaltyHost { get; } = new();
    public PenaltySettingsStore PenaltySettings { get; } = new();
    public PenaltySetupService PenaltySetup { get; } = new();
    public PenaltyRuntimeController PenaltyRuntime { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        PenaltyRuntime = new PenaltyRuntimeController(PenaltyHost);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PenaltyRuntime.Dispose();
        PenaltyHost.Dispose();
        base.OnExit(e);
    }
}
