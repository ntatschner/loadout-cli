using Loadout.Core.Configuration;

namespace Loadout.Core.Updates;

/// <summary>What the daemon last learned about updates, for Studio to show.</summary>
/// <param name="Current">The version this daemon is.</param>
/// <param name="Available">The version on offer, or null when the feed offers none for this platform.</param>
/// <param name="IsNewer">Whether that is newer than this one, which is what the badge means.</param>
/// <param name="Notes">The release notes, when the feed carries any.</param>
/// <param name="CheckedAt">When the feed was last read, or null when it has not been yet.</param>
/// <param name="Problem">Why the last check failed, or null when it did not.</param>
/// <param name="Channel">How this copy was installed: archive or installer.</param>
/// <param name="CanRollBack">Whether an archive copy has the binary its last update replaced.</param>
/// <param name="Automatic">Whether the daemon checks by itself; off when updates-check-automatically is.</param>
public sealed record UpdateStatus(
    string Current,
    string? Available,
    bool IsNewer,
    string? Notes,
    DateTimeOffset? CheckedAt,
    string? Problem,
    string Channel,
    bool CanRollBack,
    bool Automatic);

/// <summary>
/// The daemon's look at the release feed: once shortly after it starts, then
/// once a day, and whenever somebody presses Check now.
/// </summary>
/// <remarks>
/// <para>
/// Kept in memory rather than on disk. The daemon is the one thing running
/// long enough for a daily check to mean anything, and a cached answer on
/// disk would be one more thing that could disagree with the feed.
/// </para>
/// <para>
/// Never an error. A feed that cannot be reached is a <see cref="UpdateStatus.Problem"/>
/// for Settings to mention, not a reason for the daemon to say anything.
/// </para>
/// </remarks>
public sealed class UpdateWatch(IUpdateService updates, IConfigurationService configuration, TimeProvider time)
{
    /// <summary>How long after starting the first look is taken, so starting is not held up by the network.</summary>
    internal TimeSpan FirstLook { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often after that.</summary>
    internal TimeSpan Every { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The least time between two looks somebody asked for: Check now pressed
    /// repeatedly reads the feed once.
    /// </summary>
    internal TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Loadout.Models.Updates.UpdateCheck? _check;
    private DateTimeOffset? _checkedAt;
    private string? _problem;
    private bool _automatic = true;

    /// <summary>What is known now, without reading the feed.</summary>
    public UpdateStatus Status
    {
        get
        {
            var current = _check?.CurrentVersion
                ?? typeof(UpdateWatch).Assembly.GetName().Version?.ToString(3)
                ?? "0.0.0";
            var channel = updates.Channel;

            return new UpdateStatus(
                current,
                _check?.AvailableVersion,
                _check?.IsNewer ?? false,
                _check?.IsNewer == true ? _check.Notes : null,
                _checkedAt,
                _problem,
                channel == InstallChannel.Installer ? "installer" : "archive",
                channel == InstallChannel.Archive && File.Exists(InstallChannels.PreviousOf(updates.Executable)),
                _automatic);
        }
    }

    /// <summary>Raised when a look finds something different from the last one.</summary>
    public event Action? Changed;

    /// <summary>Looks at the feed on the daemon's clock until the daemon stops.</summary>
    public async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstLook, time, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                if (await AutomaticAsync(ct).ConfigureAwait(false))
                {
                    await LookAsync(ct).ConfigureAwait(false);
                }

                await Task.Delay(Every, time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The daemon stopping.
        }
    }

    /// <summary>Reads the feed now, unless it was read a moment ago, and says what it found.</summary>
    public async Task<UpdateStatus> CheckNowAsync(CancellationToken ct = default)
    {
        if (_checkedAt is not { } last || time.GetUtcNow() - last >= Patience)
        {
            await LookAsync(ct).ConfigureAwait(false);
        }

        return Status;
    }

    private async Task<bool> AutomaticAsync(CancellationToken ct)
    {
        var config = await configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        // Off means off, as it does for the launcher's own corner notice.
        _automatic = config.Succeeded && config.Value!.Updates.CheckAutomatically;

        return _automatic;
    }

    private async Task LookAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var before = Status;
            var result = await updates.CheckAsync(ct).ConfigureAwait(false);

            _checkedAt = time.GetUtcNow();

            if (result.Succeeded)
            {
                _check = result.Value;
                _problem = null;
            }
            else
            {
                _problem = result.Error;
            }

            var after = Status;

            if (after.Available != before.Available || after.IsNewer != before.IsNewer || after.Problem != before.Problem)
            {
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _checkedAt = time.GetUtcNow();
            _problem = ex.Message;
        }
        finally
        {
            _gate.Release();
        }
    }
}
