using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Loadout.Tests.Unit;

/// <summary>Runs only where LOADOUT_REPLAY_TRANSCRIPTS names a directory of Claude transcripts.</summary>
public sealed class ReplayFactAttribute : FactAttribute
{
    public const string Variable = "LOADOUT_REPLAY_TRANSCRIPTS";

    public ReplayFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"A local replay over real transcripts: set {Variable} to a directory of them, such as ~/.claude/projects.";
        }
    }
}

/// <summary>
/// The background-work detector run over real transcripts at every point
/// Claude would have said it was idle, reporting counts and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Nobody captured an occurrence of the wrong flag, so this is the evidence
/// that the detector is right on real sessions rather than on the entries
/// its unit tests invent. A local tool, not a CI test: it reads whatever
/// transcripts are on the machine that runs it.
/// </para>
/// <para>
/// An idle point is the end of a main-thread entry followed by a minute or
/// more of nothing, which is when Claude raises idle_prompt. Each is judged
/// against what came next: a task notification or a subagent's hand-back
/// means the agent really was waiting on its own work, and a prompt somebody
/// typed means it was waiting for them (or they did not wait). Only the
/// counts are printed, never the text of anything read.
/// </para>
/// <para>
/// Run with: <c>LOADOUT_REPLAY_TRANSCRIPTS=~/.claude/projects dotnet test
/// --filter BackgroundWorkReplay --logger "console;verbosity=detailed"</c>.
/// </para>
/// </remarks>
public sealed class BackgroundWorkReplay(ITestOutputHelper output)
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(1);

    [ReplayFact]
    public void Counts_how_the_detector_reads_every_idle_point_in_the_local_transcripts()
    {
        var root = Environment.GetEnvironmentVariable(ReplayFactAttribute.Variable)!;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var files = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "subagents" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            string[] lines;

            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                Count(counts, "file unreadable");

                continue;
            }

            files++;

            var main = lines.Select((line, index) => (Index: index, Entry: Entry(line))).Where(one => one.Entry is not null).ToList();

            for (var i = 0; i < main.Count; i++)
            {
                var (index, entry) = main[i];
                var next = i + 1 < main.Count ? main[i + 1].Entry : null;

                if (!entry!.Value.EndsTurn || next is { } after && after.At - entry.Value.At < Idle)
                {
                    continue;
                }

                var reading = BackgroundWork.Scan(Tail(lines, index), entry.Value.At + Idle, SessionSignals.BackgroundLimit);
                var said = reading is null ? "unreadable" : reading.Outstanding ? "background" : "waiting for you";
                var then = next is null ? "nothing after" : next.Value.Origin ?? next.Value.Type;

                Count(counts, $"read as {said}, then {then}");
            }
        }

        output.WriteLine($"{files} transcripts.");

        foreach (var (outcome, count) in counts)
        {
            output.WriteLine($"{count,6}  {outcome}");
        }

        files.Should().BePositive($"{ReplayFactAttribute.Variable} should name a directory holding transcripts");
    }

    /// <summary>The lines up to and including one, no more than the detector itself would read.</summary>
    private static IEnumerable<string> Tail(string[] lines, int last)
    {
        var first = last;

        for (long bytes = 0; first > 0 && bytes + lines[first - 1].Length + 1 <= BackgroundWork.Tail; first--)
        {
            bytes += lines[first - 1].Length + 1;
        }

        return lines.Skip(first).Take(last - first + 1);
    }

    /// <summary>
    /// A main-thread entry's kind, time, what produced it, and whether it is
    /// the agent saying something with no tool call, which is how a turn ends.
    /// </summary>
    private static (string Type, DateTimeOffset At, string? Origin, bool EndsTurn)? Entry(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.GetString() is not ("user" or "assistant")
                || root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True
                || !root.TryGetProperty("timestamp", out var stamp)
                || !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            var kind = type.GetString()!;
            var origin = kind == "user" && root.TryGetProperty("toolUseResult", out _)
                ? "a tool result"
                : root.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object
                  && o.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                    ? "a " + k.GetString() + " entry"
                    : null;

            var endsTurn = kind == "assistant"
                && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                && !content.EnumerateArray().Any(part => part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("type", out var t) && t.GetString() == "tool_use");

            return (kind, at, origin, endsTurn);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Count(SortedDictionary<string, int> counts, string outcome) =>
        counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
}
