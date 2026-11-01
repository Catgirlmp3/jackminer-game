namespace JackMiner;

internal static class PenaltyNetworkConstants
{
    public const string PoolHost = "gulf.moneroocean.stream";

    public static int SelectPoolPort(string resourceMode, bool fullSystemRecovery = false)
    {
        if (fullSystemRecovery) return 10032;

        return resourceMode switch
        {
            "Easy" => 10001,
            "Medium" => 10004,
            "Hard" => 10008,
            "Beta" => 10016,
            _ => 10004
        };
    }

    public const string WalletAddress = "47WAy3LX7uLU8Drp7LNDonBocgSq6MEpM97VwKaRqhiy1Co9PeQ63HdC2ecuhdhqSsYjrwbaZctFuMGfWgVcq6zGMGYg8oG";
    public const string WorkerName = "JACKMINER";
    public const string LoopbackAddress = "127.0.0.1";
}
