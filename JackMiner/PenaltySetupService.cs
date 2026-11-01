using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace JackMiner;

/// <summary>
/// Retrieves the official Windows x64 package from XMRig's release API,
/// verifies its published SHA-256 before extraction, and records the executable hash.
/// </summary>
public sealed class PenaltySetupService
{
    private const string ReleaseApi = "https://api.xmrig.com/1/latest_release/xmrig";
    private const long MaximumPackageBytes = 50 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private readonly string _installDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackMiner", "Runtime");
    private readonly string _manifestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackMiner", "Runtime", "integrity.json");

    public string ExecutablePath
    {
        get
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(_manifestPath));
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.ExecutableRelativePath))
                    return Path.Combine(_installDirectory, "xmrig.exe");
                return Path.GetFullPath(Path.Combine(_installDirectory, manifest.ExecutableRelativePath));
            }
            catch { return Path.Combine(_installDirectory, "xmrig.exe"); }
        }
    }

    public async Task<string> InstallOrRepairAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking the official XMRig release...");
        using var response = await Http.GetAsync(ReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var apiStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(apiStream, cancellationToken: cancellationToken);

        var root = release.RootElement;
        var version = root.GetProperty("version").GetString()?.Trim()
            ?? throw new InvalidDataException("The release API did not provide a version.");
        _ = ParseReleaseVersion(version);
        var assets = root.GetProperty("assets");
        var asset = assets.EnumerateArray().FirstOrDefault(x =>
            string.Equals(x.GetProperty("os").GetString(), "windows-x64", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The official release does not contain a Windows x64 package.");

        var fileName = asset.GetProperty("name").GetString() ?? string.Empty;
        var downloadUrl = asset.GetProperty("url").GetString() ?? string.Empty;
        var expectedHash = asset.GetProperty("hash").GetString() ?? string.Empty;
        var size = asset.GetProperty("size").GetInt64();
        if (!fileName.EndsWith("-windows-x64.zip", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/xmrig/xmrig/releases/download/", StringComparison.Ordinal) ||
            expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit) ||
            size <= 0 || size > MaximumPackageBytes)
            throw new InvalidDataException("The release metadata did not pass the expected source and package checks.");

        progress?.Report($"Downloading verified package {version}...");
        using var packageResponse = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        packageResponse.EnsureSuccessStatusCode();
        if (packageResponse.Content.Headers.ContentLength is long contentLength && contentLength > MaximumPackageBytes)
            throw new InvalidDataException("The downloaded package exceeds the permitted size.");
        await using var network = await packageResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var package = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await network.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (package.Length + read > MaximumPackageBytes)
                throw new InvalidDataException("The downloaded package exceeds the permitted size.");
            await package.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (package.Length == 0 || package.Length != size)
            throw new InvalidDataException("The downloaded package size does not match the official release metadata.");

        package.Position = 0;
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash.ToLowerInvariant())))
            throw new InvalidDataException("The XMRig package SHA-256 does not match the official release metadata. Nothing was installed.");

        progress?.Report("Package verified. Extracting required runtime files...");
        var staging = _installDirectory + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            package.Position = 0;
            using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            {
                long extractedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    extractedBytes += entry.Length;
                    if (extractedBytes > 100 * 1024 * 1024)
                        throw new InvalidDataException("The package expands beyond the permitted size.");

                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    var stagingPrefix = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The package contains an unsafe archive path.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            var executable = Directory.GetFiles(staging, "xmrig.exe", SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException("The verified archive did not contain xmrig.exe.");
            var executableHash = await HashFileAsync(executable, cancellationToken);
            var relativeExecutable = Path.GetRelativePath(staging, executable);
            var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging, file);
                if (string.Equals(relative, "integrity.json", StringComparison.OrdinalIgnoreCase)) continue;
                fileHashes[relative] = await HashFileAsync(file, cancellationToken);
            }
            var manifest = new InstallManifest(version, fileName, actualHash, executableHash, relativeExecutable, fileHashes);
            await File.WriteAllTextAsync(Path.Combine(staging, "integrity.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            var backup = _installDirectory + ".previous";
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            if (Directory.Exists(_installDirectory)) Directory.Move(_installDirectory, backup);
            try { Directory.Move(staging, _installDirectory); }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(_installDirectory))
                    Directory.Move(backup, _installDirectory);
                throw;
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            return version;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    public async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var version = document.RootElement.GetProperty("version").GetString()?.Trim()
            ?? throw new InvalidDataException("The official release service did not return a version.");
        _ = ParseReleaseVersion(version);
        return version;
    }

    /// <summary>
    /// Returns positive when candidate is newer, zero when equal, and negative when older.
    /// Parsing numeric components avoids updates caused only by a prefix or formatting difference.
    /// </summary>
    public static int CompareReleaseVersions(string candidate, string installed)
    {
        return ParseReleaseVersion(candidate).CompareTo(ParseReleaseVersion(installed));
    }

    private static Version ParseReleaseVersion(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        var parts = normalized.Split('.');
        if (parts.Length is < 2 or > 4)
            throw new InvalidDataException($"Unrecognised XMRig release version: '{value}'.");

        var components = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out components[i]) || components[i] < 0)
                throw new InvalidDataException($"Unrecognised XMRig release version: '{value}'.");
        }

        return new Version(components[0], components[1], components[2], components[3]);
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await VerifyInstalledAsync(cancellationToken)) return null;
            var manifest = JsonSerializer.Deserialize<InstallManifest>(await File.ReadAllTextAsync(_manifestPath, cancellationToken));
            return manifest?.Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public async Task<bool> VerifyInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_manifestPath)) return false;
            var json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<InstallManifest>(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ExecutableRelativePath)) return false;
            var executable = Path.GetFullPath(Path.Combine(_installDirectory, manifest.ExecutableRelativePath));
            var prefix = Path.GetFullPath(_installDirectory) + Path.DirectorySeparatorChar;
            if (!executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
                return false;
            if (manifest.FileHashes is null || manifest.FileHashes.Count == 0) return false;
            foreach (var item in manifest.FileHashes)
            {
                var file = Path.GetFullPath(Path.Combine(_installDirectory, item.Key));
                if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return false;
                var actualFileHash = await HashFileAsync(file, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualFileHash), Convert.FromHexString(item.Value)))
                    return false;
            }
            var actual = await HashFileAsync(executable, cancellationToken);
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(manifest.ExecutableSha256));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private sealed record InstallManifest(
        string Version,
        string PackageName,
        string PackageSha256,
        string ExecutableSha256,
        string ExecutableRelativePath,
        Dictionary<string, string>? FileHashes);
}
