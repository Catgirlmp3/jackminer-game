using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JackMiner;

public sealed class PenaltySetupService
{
    private const string ReleaseApi = "https://api.xmrig.com/1/latest_release/xmrig";
    private const long MaximumPackageBytes = 50 * 1024 * 1024;
    private const long MaximumExtractedBytes = 100 * 1024 * 1024;
    private const int MaximumDownloadAttempts = 3;
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string _installDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JackMiner", "Runtime");
    private readonly string _manifestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JackMiner", "Runtime", "integrity.json");

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
        progress?.Report("Connecting to the official XMRig release service...");
        using var releaseRequest = CreateGetRequest(ReleaseApi, "application/json");
        using var response = await Http.SendAsync(releaseRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "The XMRig release service", cancellationToken);

        await using var apiStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(apiStream, cancellationToken: cancellationToken);
        var root = release.RootElement;
        var version = ReadRequiredString(root, "version", "release version");
        _ = ParseReleaseVersion(version);

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The XMRig release service did not provide a download list.");

        // XMRig's API identifies the platform with os="windows-x64", but its
        // published filename has existed in both windows-x64 and msvc-win64 forms.
        // Filter by platform AND the exact versioned package name so a GCC variant
        // cannot be selected merely because it appears first in the assets array.
        var supportedPackageNames = new[]
        {
            $"xmrig-{version}-windows-x64.zip",
            $"xmrig-{version}-msvc-win64.zip"
        };
        var windowsAssets = assets.EnumerateArray()
            .Where(item =>
                item.TryGetProperty("os", out var os) &&
                os.ValueKind == JsonValueKind.String &&
                string.Equals(os.GetString(), "windows-x64", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var asset = windowsAssets
            .Where(item =>
                item.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                supportedPackageNames.Contains(name.GetString()!, StringComparer.OrdinalIgnoreCase))
            .OrderBy(item =>
                string.Equals(
                    item.GetProperty("name").GetString(),
                    supportedPackageNames[0],
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            var availableNames = string.Join(", ", windowsAssets
                .Where(item => item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("name").GetString()));
            if (string.IsNullOrWhiteSpace(availableNames)) availableNames = "none";
            throw new InvalidDataException(
                $"The XMRig feed listed no supported Windows x64 package for version {version}. Windows x64 assets found: {availableNames}.");
        }

        var fileName = ReadRequiredString(asset, "name", "Windows x64 package name");
        var downloadUrl = ReadRequiredString(asset, "url", "Windows x64 download URL");
        var expectedHash = ReadRequiredString(asset, "hash", "Windows x64 SHA-256 hash");
        if (expectedHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expectedHash = expectedHash["sha256:".Length..];

        if (!asset.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var expectedSize))
            throw new InvalidDataException("The official release did not provide a valid package size.");

        var expectedReleasePath = $"/xmrig/xmrig/releases/download/v{version}/{fileName}";
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(packageUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(packageUri.AbsolutePath, expectedReleasePath, StringComparison.OrdinalIgnoreCase) ||
            expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit) ||
            expectedSize <= 0 || expectedSize > MaximumPackageBytes)
            throw new InvalidDataException("The selected Windows x64 package did not pass the official URL, SHA-256, or size checks.");

        progress?.Report($"Downloading XMRig {version}...");
        using var package = await DownloadVerifiedPackageAsync(
            packageUri, expectedSize, expectedHash, version, progress, cancellationToken);

        progress?.Report("Download verified. Preparing the runtime files...");
        var staging = _installDirectory + ".staging";
        TryDeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        try
        {
            package.Position = 0;
            using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            {
                long extractedBytes = 0;
                var stagingRoot = Path.GetFullPath(staging);
                var stagingPrefix = stagingRoot + Path.DirectorySeparatorChar;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

                    var normalizedName = entry.FullName.Replace('\\', '/');
                    var relativeName = normalizedName.Replace('/', Path.DirectorySeparatorChar);
                    var destination = Path.GetFullPath(Path.Combine(stagingRoot, relativeName));
                    if (!destination.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded archive contains an invalid file path.");

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    extractedBytes += entry.Length;
                    if (extractedBytes > MaximumExtractedBytes)
                        throw new InvalidDataException("The downloaded archive expands beyond the allowed size.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(
                        destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        bufferSize: 81920, options: FileOptions.SequentialScan);
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            var executableMatches = Directory.GetFiles(staging, "xmrig.exe", SearchOption.AllDirectories);
            if (executableMatches.Length != 1)
                throw new InvalidDataException($"The downloaded archive should contain one xmrig.exe, but {executableMatches.Length} were found.");

            var executable = executableMatches[0];
            var executableHash = await HashFileAsync(executable, cancellationToken);
            var relativeExecutable = Path.GetRelativePath(staging, executable);
            var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging, file);
                try
                {
                    fileHashes[relative] = await HashFileAsync(file, cancellationToken);
                }
                catch (FileNotFoundException ex)
                {
                    throw new InvalidDataException(
                        $"'{relative}' disappeared while the runtime was being installed. Check Windows Security > Protection history to see whether the file was quarantined.", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new InvalidDataException(
                        $"The folder containing '{relative}' disappeared while the runtime was being installed. Check Windows Security > Protection history for recent actions.", ex);
                }
            }

            if (fileHashes.Count == 0)
                throw new InvalidDataException("No runtime files were extracted from the official package.");

            var manifest = new InstallManifest(version, fileName, expectedHash.ToLowerInvariant(),
                executableHash, relativeExecutable, fileHashes);
            await File.WriteAllTextAsync(
                Path.Combine(staging, "integrity.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }),
                cancellationToken);

            var backup = _installDirectory + ".previous";
            TryDeleteDirectory(backup);
            if (Directory.Exists(_installDirectory)) Directory.Move(_installDirectory, backup);

            try { Directory.Move(staging, _installDirectory); }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(_installDirectory))
                    Directory.Move(backup, _installDirectory);
                throw;
            }

            TryDeleteDirectory(backup);
            progress?.Report($"XMRig {version} is installed and verified.");
            return version;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateGetRequest(ReleaseApi, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "The XMRig release service", cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var version = ReadRequiredString(document.RootElement, "version", "release version");
        _ = ParseReleaseVersion(version);
        return version;
    }

    public static int CompareReleaseVersions(string candidate, string installed) =>
        ParseReleaseVersion(candidate).CompareTo(ParseReleaseVersion(installed));

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await VerifyInstalledAsync(cancellationToken)) return null;
            var manifest = JsonSerializer.Deserialize<InstallManifest>(
                await File.ReadAllTextAsync(_manifestPath, cancellationToken));
            return manifest?.Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task<bool> VerifyInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_manifestPath)) return false;
            var json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<InstallManifest>(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ExecutableRelativePath)) return false;

            var installRoot = Path.GetFullPath(_installDirectory);
            var prefix = installRoot + Path.DirectorySeparatorChar;
            var executable = Path.GetFullPath(Path.Combine(installRoot, manifest.ExecutableRelativePath));
            if (!executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executable) || manifest.FileHashes is null || manifest.FileHashes.Count == 0)
                return false;

            foreach (var item in manifest.FileHashes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Path.GetFullPath(Path.Combine(installRoot, item.Key));
                if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return false;
                var actualFileHash = await HashFileAsync(file, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualFileHash), Convert.FromHexString(item.Value)))
                    return false;
            }

            var actual = await HashFileAsync(executable, cancellationToken);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(manifest.ExecutableSha256));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(25),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
    }

    private static HttpRequestMessage CreateGetRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("JackMiner/1.0");
        request.Headers.Accept.ParseAdd(accept);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string source, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = string.Empty;
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 240) detail = detail[..240] + "…";
        }
        catch { }

        throw new HttpRequestException(
            $"{source} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim(),
            null, response.StatusCode);
    }

    private static async Task<MemoryStream> DownloadVerifiedPackageAsync(
        Uri packageUri, long expectedSize, string expectedHash, string version,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
        {
            var package = new MemoryStream();
            try
            {
                using var request = CreateGetRequest(packageUri.AbsoluteUri, "application/octet-stream");
                using var response = await Http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await EnsureSuccessAsync(response, "The XMRig package download", cancellationToken);

                if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumPackageBytes)
                    throw new InvalidDataException("The downloaded package is larger than the permitted size.");

                await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await network.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;

                    var nextLength = package.Length + read;
                    if (nextLength > MaximumPackageBytes || nextLength > expectedSize)
                        throw new InvalidDataException("The downloaded package exceeded the size published by XMRig.");

                    await package.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    var percent = expectedSize > 0
                        ? Math.Clamp((int)(package.Length * 100 / expectedSize), 0, 100)
                        : 0;
                    progress?.Report($"Downloading XMRig {version}: {percent}%");
                }

                if (package.Length != expectedSize)
                    throw new InvalidDataException($"The download was incomplete ({package.Length:N0} of {expectedSize:N0} bytes).");

                package.Position = 0;
                var actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(package, cancellationToken)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash.ToLowerInvariant())))
                    throw new InvalidDataException("The downloaded package did not match XMRig's published SHA-256 checksum.");

                package.Position = 0;
                return package;
            }
            catch (Exception ex) when (
                attempt < MaximumDownloadAttempts &&
                !cancellationToken.IsCancellationRequested &&
                ShouldRetryDownload(ex))
            {
                package.Dispose();
                progress?.Report($"Download interrupted ({ex.Message}). Retrying ({attempt + 1}/{MaximumDownloadAttempts})...");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("The XMRig package could not be downloaded after several attempts.");
    }

    private static bool ShouldRetryDownload(Exception exception)
    {
        if (exception is InvalidDataException or IOException or TaskCanceledException) return true;
        if (exception is HttpRequestException requestException)
        {
            return requestException.StatusCode is null ||
                   requestException.StatusCode == HttpStatusCode.TooManyRequests ||
                   (requestException.StatusCode.HasValue && (int)requestException.StatusCode.Value >= 500);
        }
        return false;
    }

    private static string ReadRequiredString(JsonElement element, string property, string description)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"The XMRig release service did not provide a valid {description}.");
        return value.GetString()!.Trim();
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

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private sealed record InstallManifest(
        string Version, string PackageName, string PackageSha256,
        string ExecutableSha256, string ExecutableRelativePath,
        Dictionary<string, string>? FileHashes);
}
