using Loadout.Platform.Common;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// Exercises path comparison against the real filesystem (spec section 84).
/// <para>
/// These deliberately assert behaviour rather than a hardcoded expectation per
/// operating system, because the whole point is that the answer comes from the
/// volume, not from the OS. A developer on case-sensitive APFS and one on the
/// default install must both pass.
/// </para>
/// </summary>
public sealed class PathSemanticsTests : IDisposable
{
    private readonly string _root;
    private readonly PathSemantics _semantics = new();

    public PathSemanticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    [Fact]
    public void A_path_equals_itself()
    {
        var path = Path.Combine(_root, "repo");
        Directory.CreateDirectory(path);

        _semantics.PathsEqual(path, path).Should().BeTrue();
    }

    [Fact]
    public void Trailing_separators_do_not_change_identity()
    {
        var path = Path.Combine(_root, "repo");
        Directory.CreateDirectory(path);

        _semantics.PathsEqual(path, path + Path.DirectorySeparatorChar).Should().BeTrue();
    }

    [Fact]
    public void Case_comparison_follows_the_volume_rather_than_the_operating_system()
    {
        var path = Path.Combine(_root, "MixedCaseRepo");
        Directory.CreateDirectory(path);

        var flipped = Path.Combine(_root, "mixedcaserepo");

        var volumeIsCaseInsensitive = _semantics.IsCaseInsensitive(_root);

        // On a case-insensitive volume these are one directory, so they must
        // compare equal or the same clone registers twice. On a case-sensitive
        // volume they are two different directories and must not.
        _semantics.PathsEqual(path, flipped).Should().Be(volumeIsCaseInsensitive);
    }

    [Fact]
    public void The_probe_agrees_with_what_the_filesystem_actually_does()
    {
        var path = Path.Combine(_root, "ProbeCheck");
        Directory.CreateDirectory(path);

        var reportedInsensitive = _semantics.IsCaseInsensitive(_root);
        var actuallyInsensitive = Directory.Exists(Path.Combine(_root, "probecheck"));

        reportedInsensitive.Should().Be(actuallyInsensitive);
    }

    [Fact]
    public void Unicode_normalisation_differences_resolve_to_one_path()
    {
        // macOS has historically stored filenames decomposed, so the same
        // visible name arrives as NFD from a directory listing and NFC from a
        // config file. Without normalisation that is two projects.
        // Written as escapes so the two forms survive being saved to disk.
        const string Composed = "café";        // e-acute as one code point
        const string Decomposed = "café";     // e followed by combining acute

        Composed.Should().NotBe(Decomposed, "the two forms must differ before normalisation");

        Directory.CreateDirectory(Path.Combine(_root, Composed));

        _semantics.PathsEqual(
            Path.Combine(_root, Composed),
            Path.Combine(_root, Decomposed))
            .Should().BeTrue();
    }

    [Fact]
    public void Different_directories_never_compare_equal()
    {
        var first = Path.Combine(_root, "alpha");
        var second = Path.Combine(_root, "beta");

        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        _semantics.PathsEqual(first, second).Should().BeFalse();
    }

    [Fact]
    public void Canonicalisation_is_stable_across_repeated_calls()
    {
        var path = Path.Combine(_root, "stable");
        Directory.CreateDirectory(path);

        // Used as a dictionary key during discovery, so an unstable result
        // would let the same repository be reported twice in one scan.
        _semantics.Canonicalise(path).Should().Be(_semantics.Canonicalise(path));
    }

    [Fact]
    public void A_relative_path_resolves_against_the_current_directory()
    {
        var path = Path.Combine(_root, "relative-check");
        Directory.CreateDirectory(path);

        var previous = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(_root);

            _semantics.PathsEqual("relative-check", path).Should().BeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
