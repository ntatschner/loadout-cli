using System.Text.Json;
using Loadout.Core.Security;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Guards the last line of defence before text reaches a log or the console
/// (spec sections 52 and 80). The launcher does not put secrets into messages,
/// but git and agent subprocesses echo their own input back, and that text is
/// not under the launcher's control.
/// </summary>
/// <remarks>
/// Every case lives in <c>tests/shared/redaction-cases.json</c>, as an input
/// and its exact output. Studio's port of the redactor reads the same file, so
/// the two cannot drift apart without one suite failing.
/// </remarks>
public sealed class SecretRedactorTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, SharedCase>> Cases = new(Load);

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var name in Cases.Value.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Each_shared_case_gives_its_exact_output(string name)
    {
        var shared = Cases.Value[name];

        SecretRedactor.Redact(shared.Input).Should().Be(shared.Expected, shared.Note ?? name);
    }

    [Fact]
    public void The_shared_file_has_cases_and_no_duplicate_names() =>
        // Load throws on a duplicate name; this keeps an empty or unreadable
        // file from passing as a theory with nothing in it.
        Cases.Value.Should().HaveCountGreaterThan(40);

    private static IReadOnlyDictionary<string, SharedCase> Load()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "shared", "redaction-cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new Dictionary<string, SharedCase>(StringComparer.Ordinal);

        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var input = item.GetProperty("input");
            var note = item.TryGetProperty("note", out var given) ? given.GetString() : null;

            cases.Add(name, new SharedCase(
                input.ValueKind == JsonValueKind.Null ? null : input.GetString(),
                item.GetProperty("expected").GetString()!,
                note));
        }

        return cases;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        // A directory in an ordinary checkout, a file in a git worktree.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }

    private sealed record SharedCase(string? Input, string Expected, string? Note);
}
