using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Nothing asks the process which directory it was started in, or whether its
/// streams are redirected, except through <c>Invocation</c>.
/// </summary>
/// <remarks>
/// The daemon runs Studio's commands inside itself, several at once. The
/// process has one working directory and one pair of streams, and they are
/// the daemon's: a command asking for them directly would act on whichever
/// repository the daemon was started from, or offer a prompt to a terminal
/// that is not the caller's. <c>Invocation</c> answers for the call, and as
/// the process does on the command line.
/// </remarks>
public sealed partial class InvocationStateTests
{
    [GeneratedRegex(@"(?<![\w.])(?:System\.)?(?:Console\.Is(?:Output|Input)Redirected|(?:IO\.)?Directory\.GetCurrentDirectory\(\)|Environment\.CurrentDirectory)")]
    private static partial Regex ProcessState();

    [Fact]
    public void Process_wide_state_is_read_only_through_Invocation()
    {
        var root = RepositoryRoot();

        var offenders = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !string.Equals(Path.GetFileName(file), "Invocation.cs", StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(one => !one.line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && ProcessState().IsMatch(one.line))
            .Select(one => $"{Path.GetRelativePath(root, one.file)}:{one.number}: {one.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "a command running inside the daemon reads the daemon's directory and streams through these; "
            + "use Invocation.CurrentDirectory, Invocation.IsOutputRedirected and Invocation.IsInputRedirected");
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
}
