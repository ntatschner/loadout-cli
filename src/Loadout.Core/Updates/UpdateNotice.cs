using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Updates;

/// <summary>
/// Whether a newer launcher is available, answered quietly and at most once a
/// day, for a screen to mention in a corner.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IUpdateService.CheckAsync"/> is the honest check: it goes to the
/// release source every time, because <c>loadout update</c> is asked a fresh
/// question and deserves a fresh answer. The launcher is not asking that. It
/// opens many times a day, it must never wait on the network, and it must never
/// turn a failed lookup into something on screen. So this sits in front of the
/// service with a cache and a rule that nothing here is ever an error.
/// </para>
/// </remarks>
public interface IUpdateNotice
{
    /// <summary>
    /// The newer version on offer, or null when there is none, when checking is
    /// switched off, or when the answer could not be had. Never throws.
    /// </summary>
    Task<string?> AvailableAsync(CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class UpdateNotice : IUpdateNotice
{
    /// <summary>
    /// How long an answer is trusted. A day: releases are not more frequent
    /// than that, and a launcher that phoned home on every start would be
    /// doing so dozens of times for one possible change of answer.
    /// </summary>
    internal static readonly TimeSpan Freshness = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IConfigurationService _configuration;
    private readonly IUpdateService _updates;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly string _currentVersion;

    public UpdateNotice(
        IConfigurationService configuration,
        IUpdateService updates,
        IPlatformPaths paths,
        TimeProvider time,
        string? currentVersion = null)
    {
        _configuration = configuration;
        _updates = updates;
        _paths = paths;
        _time = time;

        // The same reading UpdateService takes, so the two agree about what is
        // running — and so a cache written by one build is not believed by the
        // next.
        _currentVersion = currentVersion
            ?? typeof(UpdateNotice).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    /// <summary>Where the last answer is kept.</summary>
    internal string Path => System.IO.Path.Combine(_paths.Paths.Cache, "update-notice.json");

    /// <inheritdoc />
    public async Task<string?> AvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

            // Off means off. Somebody who switched automatic checking off is
            // not asking to be told about updates in a corner instead.
            if (config.Failed || !config.Value!.Updates.CheckAutomatically)
            {
                return null;
            }

            var now = _time.GetUtcNow();

            if (Read() is { } cached
                && string.Equals(cached.Current, _currentVersion, StringComparison.Ordinal)
                && now - cached.CheckedAt < Freshness)
            {
                return cached.Available;
            }

            var check = await _updates.CheckAsync(ct).ConfigureAwait(false);

            // A failed check is recorded as "nothing" with the time, so the
            // next start does not ask again. A machine that cannot reach the
            // source would otherwise try on every launch; the cost of being
            // wrong is an update mentioned a day late, which is what the cache
            // costs anyway.
            var available = check.Succeeded && check.Value!.IsNewer
                ? check.Value.AvailableVersion
                : null;

            Write(new Answer(_currentVersion, now, available));

            return available;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private Answer? Read()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Answer>(File.ReadAllText(Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable cache is a missing cache. It is rewritten below.
            return null;
        }
    }

    private void Write(Answer answer)
    {
        try
        {
            Directory.CreateDirectory(_paths.Paths.Cache);
            File.WriteAllText(Path, JsonSerializer.Serialize(answer, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the answer costs one more lookup next
            // time, and this is a corner of a screen, not a command somebody
            // ran. Nothing to report.
        }
    }

    /// <summary>What was found, for which build, and when.</summary>
    internal sealed record Answer(string Current, DateTimeOffset CheckedAt, string? Available);
}
