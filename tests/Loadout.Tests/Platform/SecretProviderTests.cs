using Loadout.Platform.Common;
using Loadout.Platform.Windows;
using Loadout.Tests.Fakes;
using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Platform;

// Each test carries the platform attribute that matches its Fact attribute.
// The Fact attribute enforces the restriction at run time by skipping; the
// platform attribute states the same guarantee to the compiler, so the
// platform-compatibility analyser can verify the calls inside.

/// <summary>
/// Exercises the native credential stores (spec section 54). Each is skipped
/// off its own platform rather than quietly passing, so the run summary shows
/// which platform checks did not apply.
/// </summary>
public sealed class SecretProviderTests
{
    /// <summary>
    /// A reference that no real installation would use, so a failed cleanup
    /// cannot collide with a developer's own stored credentials.
    /// </summary>
    private static string UniqueReference() =>
        "loadout-test/" + Guid.NewGuid().ToString("N");

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Windows_credential_manager_round_trips_a_secret()
    {
        var provider = new WindowsCredentialProvider();
        var reference = UniqueReference();
        const string Value = "sk-ant-not-a-real-key-0123456789";

        try
        {
            (await provider.SetAsync(reference, Value)).Succeeded.Should().BeTrue();

            var read = await provider.GetAsync(reference);

            read.Succeeded.Should().BeTrue();
            read.Value.Should().Be(Value);

            (await provider.TestAsync(reference)).Succeeded.Should().BeTrue();
        }
        finally
        {
            await provider.RemoveAsync(reference);
        }

        // Removal must actually remove; a lingering credential is a leak.
        (await provider.TestAsync(reference)).Failed.Should().BeTrue();
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Windows_reports_a_missing_credential_without_throwing()
    {
        var provider = new WindowsCredentialProvider();

        var result = await provider.GetAsync(UniqueReference());

        // An absent secret is an ordinary outcome, not an exception. Preflight
        // asks about secrets that may legitimately not be configured yet.
        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.AuthenticationRequired);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Windows_stores_a_value_containing_non_ascii_characters()
    {
        var provider = new WindowsCredentialProvider();
        var reference = UniqueReference();
        const string Value = "pässwörd-日本語-🔐";

        try
        {
            (await provider.SetAsync(reference, Value)).Succeeded.Should().BeTrue();

            // The blob is UTF-16 on the wire; a byte-length mistake here would
            // truncate exactly this kind of value.
            (await provider.GetAsync(reference)).Value.Should().Be(Value);
        }
        finally
        {
            await provider.RemoveAsync(reference);
        }
    }

        [Fact]
    public async Task The_environment_provider_still_reads_the_variable_the_old_name_used()
    {
        // A variable set in somebody's shell profile or CI configuration does
        // not rename itself when the tool does, and failing to find a secret
        // they had already provided would look like the store was broken.
        var provider = new EnvironmentSecretProvider(
            new FakeEnvironmentProvider(
                "/home/test",
                new Dictionary<string, string>
                {
                    ["AGENTCTL_SECRET_ANTHROPIC_DEFAULT"] = "value-from-the-old-name",
                }));

        var result = await provider.GetAsync("anthropic/default");

        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);
        result.Value.Should().Be("value-from-the-old-name");
    }

[Fact]
    public async Task The_environment_provider_resolves_a_reference_from_a_variable()
    {
        var reference = "anthropic/default";
        var variable = EnvironmentSecretProvider.ToVariableName(reference);

        variable.Should().Be("LOADOUT_SECRET_ANTHROPIC_DEFAULT");

        var provider = new EnvironmentSecretProvider(
            new FakeEnvironmentProvider(
                "/home/test",
                new Dictionary<string, string> { [variable] = "value-from-environment" }));

        var result = await provider.GetAsync(reference);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("value-from-environment");
    }

    [Fact]
    public async Task The_environment_provider_refuses_to_pretend_it_can_store_secrets()
    {
        var provider = new EnvironmentSecretProvider(new FakeEnvironmentProvider("/home/test"));

        // A process cannot durably set a variable for its parent. Reporting
        // success and losing the value would be far worse than refusing.
        var result = await provider.SetAsync("anthropic/default", "value");

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("cannot store");
    }
}
