using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace JackMiner;

public sealed class JackMinerUpdateService
{
    private const string ReleaseApi = "https://api.github.com/repos/Catgirlmp3/jackminer-game/releases/tags/alpha-latest";
    private const string PackageName = "JackMiner-alpha-win-x64.zip";
    private const string ChecksumName = PackageName + ".sha256";
    private const long MaximumPackageBytes = 300L * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JackMiner-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public sealed record UpdateInfo(string Tag, string Commit, string PackageUrl, string ChecksumUrl);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The alpha release has no tag.");
        if (tag != "alpha-latest") throw new InvalidDataException("The updater received an unexpected release tag.");
        var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        var commitLine = body.Split('\n').FirstOrDefault(x => x.StartsWith("Build commit: ", StringComparison.Ordinal));
        var commit = commitLine?["Build commit: ".Length..].Trim() ?? string.Empty;
        if (commit.Length != 40 || !commit.All(Uri.IsHexDigit))
            throw new InvalidDataException("The release does not contain a valid build commit.");

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == PackageName) packageUrl = url;
            if (name == ChecksumName) checksumUrl = url;
        }
        ValidateReleaseAssetUrl(packageUrl);
        ValidateReleaseAssetUrl(checksumUrl);
        if (string.Equals(ReadInstalledCommit(), commit, StringComparison.OrdinalIgnoreCase)) return null;
        return new UpdateInfo(tag, commit, packageUrl!, checksumUrl!);
    }

    public async Task LaunchVerifiedUpdateAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(Path.GetTempPath(), "JackMinerUpdate", Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(updateRoot, "payload");
        Directory.CreateDirectory(payload);
        try
        {
            var packagePath = Path.Combine(updateRoot, PackageName);
            var checksumPath = Path.Combine(updateRoot, ChecksumName);
            await DownloadLimitedAsync(update.PackageUrl, packagePath, MaximumPackageBytes, cancellationToken);
            await DownloadLimitedAsync(update.ChecksumUrl, checksumPath, 4096, cancellationToken);
            var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
            var expectedHash = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("The release checksum file is malformed.");

            await using (var packageStream = File.OpenRead(packagePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash.ToLowerInvariant())))
                    throw new InvalidDataException("The update package SHA-256 does not match the release checksum. Nothing was changed.");
            }

            await ExtractSafelyAsync(packagePath, payload, cancellationToken);
            var buildInfo = Path.Combine(payload, "build-info.json");
            if (!File.Exists(buildInfo) || !string.Equals(ReadCommitFromFile(buildInfo), update.Commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded package does not match the release commit.");

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file);
                hashes[Path.GetRelativePath(payload, file)] =
                    Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            }
            var manifest = Path.Combine(updateRoot, "update-manifest.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new { files = hashes }), cancellationToken);

            var target = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var dataRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JackMiner")).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(target, dataRoot, StringComparison.OrdinalIgnoreCase) ||
                dataRoot.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JackMiner is running from a folder that contains its persistent user data. Move the application into its own folder before using automatic updates.");
            var executable = Path.GetFileName(Environment.ProcessPath ?? "JackMiner.exe");
            var scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildPowerShellScript(target, payload, manifest, executable, Environment.ProcessId), cancellationToken);

            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var updaterProcess = Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File " + Quote(scriptPath),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (updaterProcess is null)
                throw new InvalidOperationException("Windows did not start the update helper.");
        }
        catch
        {
            try { if (Directory.Exists(updateRoot)) Directory.Delete(updateRoot, true); } catch { }
            throw;
        }
    }

    private static string ReadInstalledCommit()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            return File.Exists(path) ? ReadCommitFromFile(path) : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string ReadCommitFromFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("commit").GetString() ?? string.Empty;
    }

    private static void ValidateReleaseAssetUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/Catgirlmp3/jackminer-game/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("The alpha release asset URL did not pass validation.");
    }

    private static async Task DownloadLimitedAsync(string url, string path, long limit, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > limit))
            throw new InvalidDataException("A release download exceeded its permitted size.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > limit) throw new InvalidDataException("A release download exceeded its permitted size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total == 0) throw new InvalidDataException("A release download was empty.");
    }

    private static async Task ExtractSafelyAsync(string packagePath, string destination, CancellationToken cancellationToken)
    {
        long expanded = 0;
        using var archive = ZipFile.OpenRead(packagePath);
        var prefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            expanded += entry.Length;
            if (expanded > 1024L * 1024 * 1024) throw new InvalidDataException("The update expands beyond the permitted size.");
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update archive contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
        }
        if (!File.Exists(Path.Combine(destination, "JackMiner.exe")))
            throw new InvalidDataException("The update package does not contain JackMiner.exe.");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string BuildPowerShellScript(string target, string payload, string manifest, string executable, int parentPid)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        var backup = target + ".jackminer-backup";
        return $@"$ErrorActionPreference = 'Stop'
$target = {Q(target)}
$payload = {Q(payload)}
$manifestPath = {Q(manifest)}
$backup = {Q(backup)}
$waitPid = {parentPid}
$exe = {Q(executable)}
try {{
    $deadline = (Get-Date).AddMinutes(3)
    while (Get-Process -Id $waitPid -ErrorAction SilentlyContinue) {{
        if ((Get-Date) -gt $deadline) {{ throw 'JackMiner did not close within the update timeout.' }}
        Start-Sleep -Milliseconds 250
    }}
    $manifestData = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (Test-Path -LiteralPath $backup) {{ Remove-Item -LiteralPath $backup -Recurse -Force }}
    if (Test-Path -LiteralPath $target) {{ Move-Item -LiteralPath $target -Destination $backup }}
    try {{
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $payload '*') -Destination $target -Recurse -Force
        foreach ($entry in $manifestData.files.PSObject.Properties) {{
            $installed = Join-Path $target $entry.Name
            if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {{ throw ""Missing installed file: $($entry.Name)"" }}
            $actual = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $entry.Value.ToLowerInvariant()) {{ throw ""Installed hash mismatch: $($entry.Name)"" }}
        }}
        if (-not (Test-Path -LiteralPath (Join-Path $target $exe))) {{ throw 'The application executable is missing.' }}
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show('Update installed and every file hash verified. Thank you for keeping JackMiner current. Mwah. 💋','JackMiner — Update Complete','OK','Information') | Out-Null
        Start-Process -FilePath (Join-Path $target $exe)
        if (Test-Path -LiteralPath $backup) {{ try {{ Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop }} catch {{ }} }}
    }} catch {{
        if (Test-Path -LiteralPath $target) {{ Remove-Item -LiteralPath $target -Recurse -Force }}
        if (Test-Path -LiteralPath $backup) {{ Move-Item -LiteralPath $backup -Destination $target }}
        throw
    }}
}} catch {{
    try {{ Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show(('Update failed. Your previous installation was restored when possible.' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message),'JackMiner — Update Failed','OK','Error') | Out-Null }} catch {{ }}
    exit 1
}}";
    }
}
