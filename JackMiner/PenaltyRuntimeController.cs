using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackMiner;

/// <summary>
/// Creates the miner configuration and controls the loopback-only runtime API.
/// The process itself remains owned by PenaltyProcessHost.
/// </summary>
public sealed class PenaltyRuntimeController : IDisposable
{
    private readonly PenaltyProcessHost _host;
    private readonly HttpClient _api = new() { Timeout = TimeSpan.FromSeconds(5) };
    private JsonObject? _activeConfiguration;
    private Uri? _apiEndpoint;
    private string? _apiToken;
    private string? _logPath;

    public PenaltyRuntimeController(PenaltyProcessHost host) => _host = host;

    public async Task<MinerRuntimeStatus> StartAsync(
        string executablePath,
        int cpuTarget,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_host.IsRunning) throw new InvalidOperationException("The resource session is already running.");

        var runtimeDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("The runtime directory could not be resolved.");
        var configPath = Path.Combine(runtimeDirectory, "session-config.json");
        var logPath = Path.Combine(runtimeDirectory, "session.log");
        _logPath = logPath;
        var port = GetAvailableLoopbackPort();
        var poolPort = PenaltyNetworkConstants.SelectPoolPort(cpuTarget);
        progress?.Report($"Preparing XMRig for {PenaltyNetworkConstants.PoolHost}:{poolPort}...");
        _apiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _apiEndpoint = new Uri($"http://{PenaltyNetworkConstants.LoopbackAddress}:{port}/");
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        _activeConfiguration = new JsonObject
        {
            ["autosave"] = false,
            ["donate-level"] = 1,
            ["log-file"] = logPath,
            ["cpu"] = new JsonObject
            {
                ["enabled"] = true,
                ["yield"] = true,
                ["huge-pages"] = false,
                ["max-threads-hint"] = Math.Clamp(cpuTarget, 10, 100)
            },
            ["randomx"] = new JsonObject { ["rdmsr"] = false, ["wrmsr"] = false },
            ["pools"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = $"{PenaltyNetworkConstants.PoolHost}:{poolPort}",
                    ["user"] = PenaltyNetworkConstants.WalletAddress,
                    ["pass"] = PenaltyNetworkConstants.WorkerName,
                    ["keepalive"] = true,
                    ["tls"] = false
                }
            },
            ["http"] = new JsonObject
            {
                ["enabled"] = true,
                ["host"] = PenaltyNetworkConstants.LoopbackAddress,
                ["port"] = port,
                ["access-token"] = _apiToken,
                ["restricted"] = false
            }
        };

        await File.WriteAllTextAsync(configPath, _activeConfiguration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        _host.StartVerified(executablePath, new[] { $"--config={configPath}", "--no-color" }, runtimeDirectory, cpuTarget);

        try
        {
            progress?.Report("XMRig started. Checking its local status API...");
            await WaitForApiAsync(cancellationToken);

            // A live process and a responding API do not prove that a pool job
            // was received. Require a real non-zero hashrate before declaring
            // the resource session active or allowing the table to unlock.
            progress?.Report($"Connected to XMRig. Waiting for pool work from {PenaltyNetworkConstants.PoolHost}:{poolPort}...");
            return await WaitForHashrateAsync(TimeSpan.FromSeconds(90), progress, cancellationToken);
        }
        catch
        {
            _host.Stop();
            ResetSession();
            throw;
        }
    }

    public async Task<MinerRuntimeStatus> GetMiningStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_host.IsRunning || _apiEndpoint is null)
            throw new InvalidOperationException("The XMRig process is not running.");

        using var response = await _api.GetAsync(new Uri(_apiEndpoint, "1/summary"), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseMiningStatus(document.RootElement);
    }

    private async Task<MinerRuntimeStatus> WaitForHashrateAsync(
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        MinerRuntimeStatus? latest = null;
        string? lastApiError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_host.IsRunning)
                throw new InvalidOperationException("XMRig exited before it began hashing. Check the runtime session.log for the startup error.");

            try
            {
                latest = await GetMiningStatusAsync(cancellationToken);
                lastApiError = null;
                if (latest.Hashrate > 0)
                    return latest;

                progress?.Report($"XMRig is running but has no hashrate yet. Algorithm: {latest.Algorithm}; pool: {latest.PoolStatus}.");
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or JsonException or IOException or TaskCanceledException) &&
                !cancellationToken.IsCancellationRequested)
            {
                lastApiError = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var detail = latest?.ToDisplayText() ?? lastApiError ?? "XMRig's summary API returned no status.";
        throw new TimeoutException(
            $"XMRig remained open but did not report any hashing after {timeout.TotalSeconds:0} seconds. " +
            $"{detail} The table has been left locked. See the runtime log at " +
            $"{_logPath ?? "the JackMiner runtime folder/session.log"}.");
    }

    private static MinerRuntimeStatus ParseMiningStatus(JsonElement root)
    {
        var algorithm = ReadString(root, "algo", "algorithm pending");
        var hashrate = 0d;
        if (root.TryGetProperty("hashrate", out var hashrateElement) &&
            hashrateElement.ValueKind == JsonValueKind.Object &&
            hashrateElement.TryGetProperty("total", out var totals))
        {
            if (totals.ValueKind == JsonValueKind.Array && totals.GetArrayLength() > 0)
                totals[0].TryGetDouble(out hashrate);
            else if (totals.ValueKind == JsonValueKind.Number)
                totals.TryGetDouble(out hashrate);
        }

        var pool = "not connected";
        if (root.TryGetProperty("connection", out var connection) &&
            connection.ValueKind == JsonValueKind.Object)
        {
            pool = ReadString(connection, "pool", pool);
        }

        long accepted = 0;
        long submitted = 0;
        if (root.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Object)
        {
            accepted = ReadInt64(results, "shares_good");
            submitted = ReadInt64(results, "shares_total");
        }

        var errors = new List<string>();
        AddErrorMessages(root, "connection", errors);
        AddErrorMessages(root, "results", errors);
        var lastError = errors.Count == 0 ? string.Empty : string.Join(" | ", errors.TakeLast(3));
        return new MinerRuntimeStatus(algorithm, Math.Max(0, hashrate), pool, accepted, submitted, lastError);
    }

    private static string ReadString(JsonElement element, string property, string fallback)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : fallback;
    }

    private static long ReadInt64(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt64()
            ? value.GetInt64()
            : 0;
    }

    private static void AddErrorMessages(JsonElement root, string section, List<string> errors)
    {
        if (!root.TryGetProperty(section, out var details) ||
            details.ValueKind != JsonValueKind.Object ||
            !details.TryGetProperty("error_log", out var log) ||
            log.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in log.EnumerateArray())
        {
            var message = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(message)) errors.Add(message);
        }
    }

    public async Task<bool> UpdateCpuTargetAsync(int cpuTarget, CancellationToken cancellationToken = default)
    {
        if (!_host.IsRunning || _activeConfiguration is null || _apiEndpoint is null)
            return false;

        // This is the actual safety boundary: apply the Windows Job Object hard
        // cap first. If Windows cannot enforce the new limit, let the caller stop
        // the session rather than continuing without the requested cap.
        _host.SetCpuLimit(cpuTarget);

        // XMRig's local API is only a secondary optimisation for its thread hint.
        // A transient API error must not terminate the whole session: the Windows
        // hard cap above is already active and continues to constrain CPU usage.
        _activeConfiguration["cpu"]!["max-threads-hint"] = Math.Clamp(cpuTarget, 10, 100);
        try
        {
            using var content = new StringContent(_activeConfiguration.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _api.PutAsync(new Uri(_apiEndpoint, "1/config"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void ResetSession()
    {
        _activeConfiguration = null;
        _apiEndpoint = null;
        _apiToken = null;
        _logPath = null;
        _api.DefaultRequestHeaders.Authorization = null;
    }

    private async Task WaitForApiAsync(CancellationToken cancellationToken)
    {
        if (_apiEndpoint is null) return;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_host.IsRunning) throw new InvalidOperationException("The resource process exited during startup.");
            try
            {
                using var response = await _api.GetAsync(new Uri(_apiEndpoint, "1/summary"), cancellationToken);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(500, cancellationToken);
        }
        throw new InvalidOperationException("The process started, but its local control API did not become available. Stop the session and check the runtime log.");
    }

    public void Dispose()
    {
        ResetSession();
        _api.Dispose();
    }

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
public sealed record MinerRuntimeStatus(
    string Algorithm,
    double Hashrate,
    string PoolStatus,
    long SharesAccepted,
    long SharesSubmitted,
    string LastError)
{
    public string ToDisplayText()
    {
        var rate = Hashrate >= 1_000_000
            ? $"{Hashrate / 1_000_000d:0.00} MH/s"
            : Hashrate >= 1_000
                ? $"{Hashrate / 1_000d:0.00} kH/s"
                : $"{Hashrate:0.00} H/s";
        var status = $"{Algorithm} • {rate} • pool: {PoolStatus} • shares: {SharesAccepted}/{SharesSubmitted} accepted";
        return string.IsNullOrWhiteSpace(LastError) ? status : $"{status} • pool error: {LastError}";
    }
}
