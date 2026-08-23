using Loadout.Core.Security;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The scanner gates what gets written into the workspace repository, so both
/// halves of its behaviour are load-bearing: it has to catch real credentials,
/// and it has to leave ordinary prose alone. A scanner that cried wolf would be
/// switched off, and one that leaked what it found would defeat its own purpose.
/// </summary>
public sealed class SecretScannerTests
{
    [Theory]
    [InlineData("token is sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345", "Anthropic API key")]
    [InlineData("export GITHUB=ghp_abcdefghijklmnopqrstuvwxyz0123", "GitHub token")]
    [InlineData("xoxb-1234567890-abcdefghijkl", "Slack token")]
    [InlineData("AKIAIOSFODNN7EXAMPLE", "AWS access key id")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", "private key block")]
    [InlineData("clone https://user:hunter2pass@github.com/org/repo", "credentials in a URL")]
    public void Recognises_credential_shapes(string text, string expected) =>
        SecretScanner.Match(text).Should().Contain(expected);

    [Fact]
    public void Never_returns_the_matched_value()
    {
        const string secret = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";

        // The finding travels into console output, logs and audit records. If
        // the value came with it, every one of those becomes a second place the
        // credential is disclosed.
        var matched = SecretScanner.Match($"key: {secret}");

        matched.Should().NotBeEmpty();
        matched.Should().OnlyContain(name => !name.Contains(secret, StringComparison.Ordinal));
        matched.Should().OnlyContain(name => !name.Contains("abcdefghij", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("The build takes about four minutes on a cold cache.")]
    [InlineData("Set API_KEY in the environment before running the integration tests.")]
    [InlineData("See docs/authentication.md for how tokens are issued.")]
    [InlineData("password = <your password here>")]
    [InlineData("The commit sha is 4f2a1b9c8d3e7f6a5b4c3d2e1f0a9b8c7d6e5f4a.")]
    public void Leaves_ordinary_prose_alone(string text) =>
        SecretScanner.Match(text).Should().BeEmpty();

    [Fact]
    public void Empty_input_matches_nothing() =>
        SecretScanner.Match(null).Should().BeEmpty();
}
