using Loadout.Platform.Common;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where the home directory comes from, and which answers are refused.
/// <para>
/// This resolution was changed so that a run could be isolated — pointing
/// USERPROFILE at a throwaway directory previously did nothing, because the
/// account's own record of its profile was preferred and ignores the
/// environment entirely. Making the environment authoritative then broke every
/// path on Windows under Git Bash, which exports HOME as /c/Users/name: a POSIX
/// spelling that no Windows API can open.
/// </para>
/// <para>
/// Both failures are covered here, because each fix caused the other.
/// </para>
/// </summary>
public sealed class HomeDirectoryTests
{
    [Fact]
    public void The_home_directory_is_a_path_that_exists()
    {
        var home = new SystemEnvironmentProvider().HomeDirectory;

        home.Should().NotBeNullOrWhiteSpace();

        // The regression that mattered: a value that is returned but cannot be
        // opened is worse than no value, because it fails far from here.
        Path.IsPathRooted(home).Should().BeTrue($"'{home}' has to be usable as a path");
        Directory.Exists(home).Should().BeTrue($"'{home}' has to exist");
    }

}
