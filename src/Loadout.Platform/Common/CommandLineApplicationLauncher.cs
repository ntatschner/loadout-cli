using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Hands paths and URLs to the desktop environment (spec sections 72, 73)
/// using the platform's opener: explorer on Windows, open on macOS,
/// xdg-open on Linux.
/// </summary>
public sealed class CommandLineApplicationLauncher : IApplicationLauncher
{
    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;
    private readonly string _openerName;

    public CommandLineApplicationLauncher(
        IProcessLauncher processes,
        IExecutableResolver resolver,
        string openerName)
    {
        _processes = processes;
        _resolver = resolver;
        _openerName = openerName;
    }

    /// <summary>True when the platform opener is present.</summary>
    public bool IsAvailable => _resolver.Resolve(_openerName) is not null;

    /// <inheritdoc />
    public Task<OperationResult> OpenInFileManagerAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail($"Nothing exists at '{path}'."));
        }

        return OpenAsync(path, ct);
    }

    /// <inheritdoc />
    public Task<OperationResult> OpenUrlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            // Handing an arbitrary string to the platform opener would let a
            // malformed or hostile workspace value invoke something unintended.
            return Task.FromResult(OperationResult.Fail(
                $"Only http and https URLs can be opened; got '{url}'."));
        }

        return OpenAsync(parsed.AbsoluteUri, ct);
    }

    private async Task<OperationResult> OpenAsync(string target, CancellationToken ct)
    {
        var opener = _resolver.Resolve(_openerName);
        if (opener is null)
        {
            return OperationResult.Fail(
                $"'{_openerName}' was not found, so nothing can be opened from here. "
                + "This is normal on a headless machine.");
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(opener, [target]),
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? $"'{_openerName}' could not be run.");
        }

        // Windows explorer.exe returns a non-zero code even when it succeeds,
        // so its exit status is not a reliable signal.
        if (OperatingSystem.IsWindows())
        {
            return OperationResult.Ok();
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail($"'{_openerName}' failed: {result.Value.StandardError.Trim()}");
    }
}
