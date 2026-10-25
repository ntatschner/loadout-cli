using FluentAssertions;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// The directory a command run in the daemon is told it is in, against what a
/// process started there is told.
/// </summary>
public sealed class InvocationDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-invocation-" + Guid.NewGuid().ToString("N"));

    public InvocationDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [UnixFact]
    public void A_directory_reached_through_a_link_is_named_as_the_kernel_names_it()
    {
        var real = Directory.CreateDirectory(Path.Combine(_root, "real", "inner")).FullName;
        var link = Path.Combine(_root, "link");

        Directory.CreateSymbolicLink(link, Path.Combine(_root, "real"));

        var invocation = new Invocation(TextWriter.Null, TextWriter.Null, TextReader.Null, Path.Combine(link, "inner"));

        // What a process started there is told, which is what a child of the
        // daemon used to say. Asked of a real process rather than by moving
        // this one, which every other test running beside it would feel.
        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh", ["-c", "pwd -P"])
        {
            WorkingDirectory = Path.Combine(link, "inner"),
            RedirectStandardOutput = true,
        };

        using var child = System.Diagnostics.Process.Start(start)!;
        var told = child.StandardOutput.ReadToEnd().Trim();

        child.WaitForExit();

        invocation.WorkingDirectory.Should().Be(told);
        told.Should().EndWith(Path.Combine("real", "inner"), "the link is resolved");
        real.Should().EndWith("inner");
    }

    [WindowsFact]
    public void On_Windows_a_directory_is_named_as_it_was_given() =>
        new Invocation(TextWriter.Null, TextWriter.Null, TextReader.Null, _root).WorkingDirectory.Should().Be(_root);
}
