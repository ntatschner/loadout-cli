using AgentWorkspace.Platform.Unix;
using AgentWorkspace.Platform.Windows;
using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Platform;

// Each test carries the platform attribute that matches its Fact attribute.
// The Fact attribute enforces the restriction at run time by skipping; the
// platform attribute states the same guarantee to the compiler, so the
// platform-compatibility analyser can verify the calls inside.

/// <summary>
/// Verifies that runtime and secret material is actually protected
/// (spec sections 82 and 83), on both permission models.
/// </summary>
public sealed class FilePermissionTests : IDisposable
{
    private readonly string _root;

    public FilePermissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentctl-perm-" + Guid.NewGuid().ToString("N"));
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
            // Not worth failing a run over a temp directory.
        }
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public void Unix_restricts_a_file_to_owner_read_and_write()
    {
        var file = Path.Combine(_root, "compiled-context.md");
        File.WriteAllText(file, "context");

        new UnixFilePermissions().RestrictToCurrentUser(file).Succeeded.Should().BeTrue();

        var mode = File.GetUnixFileMode(file);

        mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // The point of 0600: nothing outside the owner may read a compiled
        // context or a generated settings file.
        mode.Should().NotHaveFlag(UnixFileMode.GroupRead);
        mode.Should().NotHaveFlag(UnixFileMode.OtherRead);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public void Unix_restricts_a_directory_to_owner_access_only()
    {
        var directory = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(directory);

        new UnixFilePermissions().RestrictDirectoryToCurrentUser(directory)
            .Succeeded.Should().BeTrue();

        var mode = File.GetUnixFileMode(directory);

        // A directory also needs the execute bit to be traversable at all.
        mode.Should().Be(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public void Unix_adds_the_execute_bit_without_discarding_existing_permissions()
    {
        var script = Path.Combine(_root, "launch.sh");
        File.WriteAllText(script, "#!/bin/sh\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        new UnixFilePermissions().MakeExecutable(script).Succeeded.Should().BeTrue();

        var mode = File.GetUnixFileMode(script);

        mode.Should().HaveFlag(UnixFileMode.UserExecute);
        mode.Should().HaveFlag(UnixFileMode.UserRead);
        mode.Should().HaveFlag(UnixFileMode.UserWrite);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Windows_restricts_a_file_with_a_real_acl_rather_than_doing_nothing()
    {
        var file = Path.Combine(_root, "config.yaml");
        File.WriteAllText(file, "schema_version: 1");

        var result = new WindowsFilePermissions().RestrictToCurrentUser(file);

        result.Succeeded.Should().BeTrue();

        // Windows holds the same sensitive runtime content as Unix does, so
        // this must be a genuine restriction rather than a placeholder that
        // returns success.
        var security = new FileInfo(file).GetAccessControl();
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(System.Security.Principal.SecurityIdentifier));

        rules.Count.Should().Be(1, "only the current user should retain access");
        security.AreAccessRulesProtected.Should().BeTrue(
            "inheritance must be broken, or a permissive parent ACL would still grant access");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Windows_reports_a_missing_file_rather_than_throwing()
    {
        var result = new WindowsFilePermissions()
            .RestrictToCurrentUser(Path.Combine(_root, "does-not-exist"));

        result.Failed.Should().BeTrue();
    }
}
