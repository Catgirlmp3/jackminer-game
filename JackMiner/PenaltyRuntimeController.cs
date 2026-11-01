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

    public PenaltyRuntimeController(PenaltyProcessHost host) => _host = host;

    public async Task StartAsync(string executablePath, int cpuTarget, CancellationToken cancellationToken = default)
    {
        if (_host.IsRunning) throw new InvalidOperationException("The resource session is already running.");

        var runtimeDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("The runtime directory could not be resolved.");
        var configPath = Path.Combine(runtimeDirectory, "session-config.json");
        var port = GetAvailableLoopbackPort();
        _apiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _apiEndpoint = new Uri($"http://{PenaltyNetworkConstants.LoopbackAddress}:{port}/");
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        _activeConfiguration = new JsonObject
        {
            ["autosave"] = false,
            ["donate-level"] = 1,
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
                    ["url"] = $"{PenaltyNetworkConstants.PoolHost}:{PenaltyNetworkConstants.PoolPort}",
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

        // Give the local API a short window to bind. The miner can still continue
        // running if the API is unavailable; the initial target is already in config.
        try { await WaitForApiAsync(cancellationToken); }
        catch
        {
            _host.Stop();
            ResetSession();
            throw;
        }
    }

    public async Task UpdateCpuTargetAsync(int cpuTarget, CancellationToken cancellationToken = default)
    {
        if (!_host.IsRunning || _activeConfiguration is null || _apiEndpoint is null)
            return;

        _activeConfiguration["cpu"]!["max-threads-hint"] = Math.Clamp(cpuTarget, 10, 100);
        _host.SetCpuLimit(cpuTarget);
        using var content = new StringContent(_activeConfiguration.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _api.PutAsync(new Uri(_apiEndpoint, "1/config"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void ResetSession()
    {
        _activeConfiguration = null;
        _apiEndpoint = null;
        _apiToken = null;
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
