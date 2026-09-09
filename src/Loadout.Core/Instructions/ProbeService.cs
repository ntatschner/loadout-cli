using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>
/// How often sessions did the thing a specialist asks for, week by week.
/// </summary>
/// <param name="Specialist">The specialist whose probe was measured.</param>
/// <param name="Weeks">One row per week that had any session in it, oldest first.</param>
public sealed record ProbeReport(SpecialistDocument Specialist, IReadOnlyList<ProbeWeek> Weeks);

/// <param name="Beginning">Monday of the week.</param>
/// <param name="Sessions">Sessions that used one of the probe's tools that week.</param>
/// <param name="Showed">How many of them showed the signature.</param>
public sealed record ProbeWeek(DateOnly Beginning, int Sessions, int Showed)
{
    /// <summary>Whole percent, for a report nobody should read as precise.</summary>
    public int Percent => Sessions == 0 ? 0 : Showed * 100 / Sessions;
}

/// <summary>
/// Measures whether the sessions an agent actually ran show what a specialist
/// asks of them.
/// </summary>
/// <remarks>
/// <para>
/// This answers a question the launcher could not answer before: whether any of
/// the guidance it composes changes what happens next. <c>instructions stats</c>
/// says which specialists launches reached, which is delivery and not effect —
/// a specialist could be in every launch and read by nobody, and cost tokens
/// every time.
/// </para>
/// <para>
/// It is a correlation and says so wherever it is shown. The launcher cannot
/// join a launch to the session it started — the agent chooses its own
/// identifier and never tells anyone — so this does not attempt that join.
/// What it reports instead is a rate over time for specialists that always
/// apply, where every session had the specialist and the only thing to look at
/// is whether the rate moved. For anything conditional there is no honest
/// comparison to draw here, and none is offered.
/// </para>
/// <para>
/// Best-effort by construction, like everything else that reads a transcript:
/// neither agent's format is a published contract, and a file that cannot be
/// understood costs that one session rather than the report.
/// </para>
/// </remarks>
public interface IProbeService
{
    /// <summary>
    /// Measures one specialist's probe against the transcripts on this machine.
    /// </summary>
    /// <param name="specialist">The specialist to measure.</param>
    /// <param name="roots">Directories holding agent transcripts.</param>
    /// <param name="since">Earliest day to report on.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<ProbeReport>> MeasureAsync(
        SpecialistDocument specialist,
        IReadOnlyList<string> roots,
        DateOnly since,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class ProbeService : IProbeService
{
    /// <summary>
    /// How long a probe pattern may run against one call's arguments.
    /// </summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async Task<OperationResult<ProbeReport>> MeasureAsync(
        SpecialistDocument specialist,
        IReadOnlyList<string> roots,
        DateOnly since,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specialist);
        ArgumentNullException.ThrowIfNull(roots);

        if (specialist.Probe is not { } probe)
        {
            return OperationResult<ProbeReport>.Fail(
                $"'{specialist.Id}' declares no probe, so there is nothing to measure. "
                + "Not every specialist can have one: a signature has to be something a "
                + "session visibly does.",
                Models.ExitCode.InvalidArguments);
        }

        var weeks = new Dictionary<DateOnly, (int Sessions, int Showed)>();

        foreach (var file in Transcripts(roots))
        {
            ct.ThrowIfCancellationRequested();

            var session = Read(file, probe, ct);

            if (session is not { } read || read.Day < since)
            {
                continue;
            }

            var beginning = read.Day.AddDays(
                -((int)read.Day.DayOfWeek + 6) % 7);

            var current = weeks.GetValueOrDefault(beginning);

            weeks[beginning] = (current.Sessions + 1, current.Showed + (read.Showed ? 1 : 0));
        }

        var rows = weeks
            .OrderBy(week => week.Key)
            .Select(week => new ProbeWeek(week.Key, week.Value.Sessions, week.Value.Showed))
            .ToList();

        return await Task.FromResult(
            OperationResult<ProbeReport>.Ok(new ProbeReport(specialist, rows)));
    }

    private static IEnumerable<string> Transcripts(IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Reads one transcript: the day it last ran, and whether it showed the
    /// signature.
    /// </summary>
    /// <remarks>
    /// A session that never used one of the probe's own tools is not counted
    /// either way — it never had the opportunity, and counting it as a failure
    /// would measure what the work happened to be rather than what the session
    /// did about it.
    /// </remarks>
    private static (DateOnly Day, bool Showed)? Read(
        string file,
        SpecialistProbe probe,
        CancellationToken ct)
    {
        var used = false;
        var showed = false;
        DateTimeOffset? last = null;

        try
        {
            foreach (var line in File.ReadLines(file))
            {
                ct.ThrowIfCancellationRequested();

                JsonDocument document;

                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    // One malformed line costs that line, never the session.
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // A sub-agent's transcript is not a session. It is one
                    // errand inside somebody else's, with a narrow brief and
                    // usually no shell at all, and counting sixty-five of them
                    // as sessions put the measured rate at less than a third of
                    // what the sessions themselves showed.
                    if (root.TryGetProperty("isSidechain", out var sidechain)
                        && sidechain.ValueKind == JsonValueKind.True)
                    {
                        return null;
                    }

                    if (root.TryGetProperty("timestamp", out var stamp)
                        && stamp.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(stamp.GetString(), out var when))
                    {
                        last = when;
                    }

                    foreach (var call in Calls(root))
                    {
                        // Only a call to one of the probe's own tools counts a
                        // session in. A session that never ran a shell command
                        // could not have backgrounded one, and counting it as
                        // having failed to would put the rate down by however
                        // many conversations happened to involve no shell —
                        // measuring what the work was rather than what the
                        // session did about it.
                        if (!Named(call, probe))
                        {
                            continue;
                        }

                        used = true;
                        showed |= Matches(call, probe);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return used && last is { } day
            ? (DateOnly.FromDateTime(day.UtcDateTime), showed)
            : null;
    }

    /// <summary>Tool calls in one transcript line, if it holds any.</summary>
    private static IEnumerable<JsonElement> Calls(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "tool_use")
            {
                yield return block;
            }
        }
    }

    /// <summary>Whether a call is to one of the tools the probe watches.</summary>
    private static bool Named(JsonElement call, SpecialistProbe probe) =>
        call.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
        && probe.Tools.Contains(name.GetString() ?? string.Empty, StringComparer.Ordinal);

    /// <summary>Whether one call is the thing the probe is looking for.</summary>
    private static bool Matches(JsonElement call, SpecialistProbe probe)
    {
        if (!call.TryGetProperty("input", out var input)
            || input.ValueKind != JsonValueKind.Object)
        {
            // The tool alone is the signature only when nothing more was asked
            // for. A probe naming an argument has not seen it.
            return probe.Argument is null && probe.Pattern is null;
        }

        if (probe.Argument is { Length: > 0 } argument
            && (!input.TryGetProperty(argument, out var value) || !IsSet(value)))
        {
            return false;
        }

        if (probe.Pattern is { Length: > 0 } pattern)
        {
            try
            {
                return Regex.IsMatch(input.GetRawText(), pattern, RegexOptions.None, PatternTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                // Nothing is claimed either way about a call the pattern could
                // not finish reading.
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether an argument counts as set.
    /// </summary>
    /// <remarks>
    /// A tool records an argument it was not given as absent, false or empty
    /// depending on the tool, and all three mean the same thing here.
    /// </remarks>
    private static bool IsSet(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => value.GetString() is { Length: > 0 },
        _ => true,
    };
}
