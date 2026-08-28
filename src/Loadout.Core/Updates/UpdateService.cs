using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Updates;

/// <inheritdoc />
internal sealed class UpdateService : IUpdateService
{
    private static readonly JsonSerializerOptions FeedOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Refuses anything implausibly large. A release archive is tens of
    /// megabytes; a gigabyte means the source is wrong or hostile, and finding
    /// out after filling the disk is too late.
    /// </summary>
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;

    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;
    private readonly IFilePermissions _permissions;
    private readonly HttpClient _http;
    private readonly Func<string> _currentExecutable;
    private readonly string _currentVersion;

    public UpdateService(
        IConfigurationService configuration,
        IPlatformPaths paths,
        IFilePermissions permissions,
        HttpClient http,
        Func<string>? currentExecutable = null,
        string? currentVersion = null)
    {
        _configuration = configuration;
        _paths = paths;
        _permissions = permissions;
        _http = http;

        _currentExecutable = currentExecutable ?? (() => Environment.ProcessPath ?? string.Empty);

        _currentVersion = currentVersion
            ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    /// <inheritdoc />
    public async Task<OperationResult<UpdateCheck>> CheckAsync(CancellationToken ct = default)
    {
        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);
        if (configResult.Failed)
        {
            return OperationResult<UpdateCheck>.Fail(configResult.Error!, configResult.ExitCode);
        }

        var source = configResult.Value!.Updates.Source;

        if (string.IsNullOrWhiteSpace(source))
        {
            return OperationResult<UpdateCheck>.Fail(
                "No release source is configured. Set one with: "
                + "loadout config set updates-source <url>",
                ExitCode.ConfigurationInvalid);
        }

        var feedResult = await ReadFeedAsync(source, ct).ConfigureAwait(false);
        if (feedResult.Failed)
        {
            return OperationResult<UpdateCheck>.Fail(feedResult.Error!, feedResult.ExitCode);
        }

        var feed = feedResult.Value!;
        var rid = _paths.Host.RuntimeIdentifier;

        // A feed with no build for this platform is a perfectly ordinary state,
        // not a failure: a release may simply not cover every architecture yet.
        feed.Artifacts.TryGetValue(rid, out var artifact);

        var isNewer = artifact is not null && IsNewer(feed.Version, _currentVersion);

        return OperationResult<UpdateCheck>.Ok(new UpdateCheck(
            _currentVersion,
            artifact is null ? null : feed.Version,
            isNewer,
            artifact,
            feed.Notes));
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> ApplyAsync(
        UpdateCheck check,
        CancellationToken ct = default)
    {
        if (check.Artifact is null)
        {
            return OperationResult<string>.Fail(
                $"The release source has no build for {_paths.Host.RuntimeIdentifier}.",
                ExitCode.ConfigurationInvalid);
        }

        if (string.IsNullOrWhiteSpace(check.Artifact.Sha256))
        {
            // Without a hash there is nothing to check the download against,
            // and this download becomes the binary the user runs next time.
            return OperationResult<string>.Fail(
                "The release source published no SHA-256 for this build, so the download cannot be "
                + "verified. Refusing to update.",
                ExitCode.PolicyViolation);
        }

        var executable = _currentExecutable();

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return OperationResult<string>.Fail(
                "The running executable could not be located, so it cannot be replaced.",
                ExitCode.GeneralFailure);
        }

        var staging = Path.Combine(_paths.Paths.Cache, "update-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            var archive = Path.Combine(staging, "download");

            var downloadResult = await DownloadAsync(check.Artifact.Url, archive, ct)
                .ConfigureAwait(false);

            if (downloadResult.Failed)
            {
                return OperationResult<string>.Fail(downloadResult.Error!, downloadResult.ExitCode);
            }

            var verifyResult = await VerifyAsync(archive, check.Artifact, ct).ConfigureAwait(false);
            if (verifyResult.Failed)
            {
                return OperationResult<string>.Fail(verifyResult.Error!, verifyResult.ExitCode);
            }

            var extracted = ExtractBinary(archive, staging, Path.GetFileName(executable));
            if (extracted.Failed)
            {
                return OperationResult<string>.Fail(extracted.Error!, extracted.ExitCode);
            }

            return Swap(executable, extracted.Value!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail(
                $"The update could not be applied: {SecretRedactor.Redact(ex.Message)}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover staging directory in the cache is harmless; it is
                // in a location the system may reclaim anyway.
            }
        }
    }

    /// <summary>
    /// Reads the feed. A local path or file URL is supported as a first-class
    /// case so an internal release source can be a directory on a share
    /// (spec section 79).
    /// </summary>
    private async Task<OperationResult<ReleaseFeed>> ReadFeedAsync(
        string source,
        CancellationToken ct)
    {
        try
        {
            string json;

            if (IsLocal(source, out var localPath))
            {
                if (!File.Exists(localPath))
                {
                    return OperationResult<ReleaseFeed>.Fail(
                        $"No release feed at '{localPath}'.", ExitCode.ConfigurationInvalid);
                }

                json = await File.ReadAllTextAsync(localPath, ct).ConfigureAwait(false);
            }
            else
            {
                json = await _http.GetStringAsync(source, ct).ConfigureAwait(false);
            }

            var feed = JsonSerializer.Deserialize<ReleaseFeed>(json, FeedOptions);

            return feed is null
                ? OperationResult<ReleaseFeed>.Fail(
                    "The release feed was empty.", ExitCode.ConfigurationInvalid)
                : OperationResult<ReleaseFeed>.Ok(feed);
        }
        catch (JsonException ex)
        {
            return OperationResult<ReleaseFeed>.Fail(
                $"The release feed is not valid JSON: {ex.Message}", ExitCode.ConfigurationInvalid);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException)
        {
            return OperationResult<ReleaseFeed>.Fail(
                $"The release source could not be reached: {SecretRedactor.Redact(ex.Message)}",
                ExitCode.ConfigurationInvalid);
        }
    }

    private async Task<OperationResult> DownloadAsync(string url, string destination, CancellationToken ct)
    {
        if (IsLocal(url, out var localPath))
        {
            if (!File.Exists(localPath))
            {
                return OperationResult.Fail($"No release archive at '{localPath}'.");
            }

            File.Copy(localPath, destination, overwrite: true);

            return OperationResult.Ok();
        }

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return OperationResult.Fail(
                $"The release archive could not be downloaded: HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > MaximumArchiveBytes)
        {
            return OperationResult.Fail(
                $"The release archive is larger than {MaximumArchiveBytes / (1024 * 1024)}MB, "
                + "which is not plausible for this tool. Refusing to download.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        await source.CopyToAsync(target, ct).ConfigureAwait(false);

        return OperationResult.Ok();
    }

    private static async Task<OperationResult> VerifyAsync(
        string archive,
        ReleaseArtifact artifact,
        CancellationToken ct)
    {
        var info = new FileInfo(archive);

        if (artifact.Size is { } expectedSize && info.Length != expectedSize)
        {
            return OperationResult.Fail(
                $"The download is {info.Length} bytes but the feed said {expectedSize}. "
                + "Refusing to install it.",
                ExitCode.PolicyViolation);
        }

        await using var stream = File.OpenRead(archive);

        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hash);

        if (!string.Equals(actual, artifact.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // This is the check that stands between a compromised or corrupted
            // release source and the binary the user runs next time.
            return OperationResult.Fail(
                $"The download does not match the published SHA-256. Expected {artifact.Sha256}, "
                + $"got {actual}. Refusing to install it.",
                ExitCode.PolicyViolation);
        }

        return OperationResult.Ok();
    }

    /// <summary>Pulls the executable out of the release archive.</summary>
    private static OperationResult<string> ExtractBinary(
        string archive,
        string staging,
        string executableName)
    {
        var extracted = Path.Combine(staging, "extracted");
        Directory.CreateDirectory(extracted);

        try
        {
            // Zip is handled in-process; a tarball is left to the platform tar,
            // which is present on both Unix platforms and preserves the mode
            // bits that make the extracted binary runnable.
            if (IsZip(archive))
            {
                ZipFile.ExtractToDirectory(archive, extracted, overwriteFiles: true);
            }
            else
            {
                System.Formats.Tar.TarFile.ExtractToDirectory(
                    OpenDecompressed(archive), extracted, overwriteFiles: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail(
                $"The release archive could not be extracted: {ex.Message}");
        }

        var candidate = Directory
            .EnumerateFiles(extracted, "*", SearchOption.AllDirectories)
            .FirstOrDefault(f => string.Equals(
                Path.GetFileName(f), executableName, StringComparison.OrdinalIgnoreCase));

        return candidate is null
            ? OperationResult<string>.Fail(
                $"The release archive does not contain '{executableName}'.")
            : OperationResult<string>.Ok(candidate);
    }

    private static Stream OpenDecompressed(string archive) =>
        new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);

    private static bool IsZip(string path)
    {
        using var stream = File.OpenRead(path);

        // Read the signature rather than trusting the name: the feed chooses
        // the URL, and a mislabelled archive should not decide how it is parsed.
        return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
    }

    /// <summary>
    /// Puts the new binary in place, keeping the old one.
    /// </summary>
    private OperationResult<string> Swap(string executable, string replacement)
    {
        var kept = executable + ".previous";

        try
        {
            if (File.Exists(kept))
            {
                File.Delete(kept);
            }

            // Windows refuses to overwrite a running image but allows it to be
            // renamed, which is what makes an in-place update possible at all.
            // Keeping the old binary rather than deleting it also means a bad
            // update can be undone by hand.
            File.Move(executable, kept);

            try
            {
                File.Copy(replacement, executable, overwrite: true);
            }
            catch
            {
                // Put the working binary back rather than leaving the user with
                // nothing to run.
                File.Move(kept, executable, overwrite: true);
                throw;
            }

            _permissions.MakeExecutable(executable);

            return OperationResult<string>.Ok(kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail(
                $"The new binary could not be put in place: {ex.Message}. "
                + "If the launcher is installed system-wide this may need elevated permissions.");
        }
    }

    /// <summary>
    /// Whether a source is a local path rather than something to fetch over the
    /// network. Both a plain path and a file URL count.
    /// </summary>
    internal static bool IsLocal(string source, out string path)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                path = uri.LocalPath;
                return true;
            }

            // A Windows path like D:\feed.json parses as an absolute URI whose
            // scheme is the drive letter, so anything not http(s) and not a file
            // URL is treated as a path.
            if (uri.Scheme is "http" or "https")
            {
                path = string.Empty;
                return false;
            }
        }

        path = source;
        return true;
    }

    /// <summary>
    /// Compares two dotted versions. Anything unparseable is treated as not
    /// newer, so a malformed feed cannot talk the launcher into replacing
    /// itself.
    /// </summary>
    internal static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate, out var parsedCandidate)
        && Version.TryParse(current, out var parsedCurrent)
        && parsedCandidate > parsedCurrent;
}
