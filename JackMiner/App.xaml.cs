using System.Windows;

namespace JackMiner;

public partial class App : Application
{
    // Shared application-owned process host. The main window and any future
    // mining controls must use this instance rather than spawning detached helpers.
    public MinerProcessHost MinerHost { get; } = new();

    protected override void OnExit(ExitEventArgs e)
    {
        MinerHost.Dispose();
        base.OnExit(e);
    }
}
