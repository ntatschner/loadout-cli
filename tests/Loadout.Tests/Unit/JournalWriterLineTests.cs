using FluentAssertions;
using Loadout.Agents.Teams.Steps;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The shape of one journal line, now that two assemblies write them.
/// </summary>
/// <remarks>
/// The runner writes through <see cref="JournalWriter"/> and closing a dead
/// run writes through <see cref="Core.Teams.RunJournalLine"/> directly. Every
/// journal already on a disk was written in this shape, and the fold reads
/// them all, so the shape is pinned as text rather than read back.
/// </remarks>
public sealed class JournalWriterLineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-line-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Line_shape_is_unchanged()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "journal.jsonl");
        var at = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var writer = new JournalWriter(path, "20261023-1200-abcd", TimeProvider.System);

        await writer.WriteAsync("node.ended", "implementer/1", new { exit = 0, killed = false, stderr = "" }, CancellationToken.None, at);
        await writer.WriteAsync("run.finished", null, new { ended = "done", cost = 0.25m, rounds = 2 }, CancellationToken.None, at);

        (await File.ReadAllTextAsync(path)).Should().Be(
            """{"at":"2026-10-23T12:00:00+00:00","run":"20261023-1200-abcd","node":"implementer/1","kind":"node.ended","data":{"exit":0,"killed":false,"stderr":""}}"""
            + "\n"
            + """{"at":"2026-10-23T12:00:00+00:00","run":"20261023-1200-abcd","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.25,"rounds":2}}"""
            + "\n");
    }
}
