namespace JackMiner;

internal static class PenaltyNetworkConstants
{
    // MoneroOcean's maintained Windows setup uses the gulf endpoint and
    // selects a vardiff port based on expected hashrate.
    public const string PoolHost = "gulf.moneroocean.stream";

    public static int SelectPoolPort(int cpuLimitPercent)
    {
        // MoneroOcean's published setup estimates roughly 0.7 kH/s per logical
        // CPU thread, then selects the matching vardiff port. Scale the estimate
        // by JackMiner's actual Windows Job Object CPU ceiling.
        var expectedKiloHashes = Environment.ProcessorCount * 0.7d *
                                 (Math.Clamp(cpuLimitPercent, 1, 100) / 100d);

        return expectedKiloHashes switch
        {
            > 8192 => 18192,
            > 4096 => 14096,
            > 2048 => 12048,
            > 1024 => 11024,
            > 512 => 10512,
            > 256 => 10256,
            > 128 => 10128,
            > 64 => 10064,
            > 32 => 10032,
            > 16 => 10016,
            > 8 => 10008,
            > 4 => 10004,
            > 2 => 10002,
            _ => 10001
        };
    }
    public const string WalletAddress = "47WAy3LX7uLU8Drp7LNDonBocgSq6MEpM97VwKaRqhiy1Co9PeQ63HdC2ecuhdhqSsYjrwbaZctFuMGfWgVcq6zGMGYg8oG";
    public const string WorkerName = "JACKMINER";
    public const string LoopbackAddress = "127.0.0.1";
}
