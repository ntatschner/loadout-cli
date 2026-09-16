using System.Runtime.Versioning;
using FluentAssertions;
using Loadout.Platform.Unix;
using Loadout.Platform.Windows;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// Saying something out loud, through whatever this machine has.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here makes a sound. A test suite that started talking would be a
/// surprise on somebody's machine, and what is worth proving is that the route
/// answers rather than that a sentence was audible — which no test can tell
/// you anyway.
/// </para>
/// <para>
/// So these check what can be checked without a person listening: that asking
/// the machine what it has does not throw, that an empty sentence is not worth
/// waking anything for, and that a sentence goes to the command as one
/// argument rather than as part of a string a shell would read.
/// </para>
/// </remarks>
public sealed class SpeechTests
{
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Asking_Windows_what_it_can_speak_with_answers_rather_than_throwing()
    {
        // Not "more than nought". A machine with no voices installed is a real
        // machine and this has to answer for it too, which is exactly the kind
        // of assumption that makes a suite pass here and fail on a runner.
        WindowsSpeech.Voices().Should().BeGreaterThanOrEqualTo(0);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Windows_says_which_voice_it_would_use_or_why_it_cannot()
    {
        var speech = new WindowsSpeech();

        var available = await speech.IsAvailableAsync();

        // Either way it explains itself. A launcher that offered speech and
        // then said nothing would look broken rather than unequipped.
        (available.Value ?? available.Error).Should().NotBeNullOrWhiteSpace();

        // NVDA where it is running, the system voice otherwise, and the name
        // is what doctor reports.
        speech.Name.Should().BeOneOf("nvda", "sapi");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Nothing_is_said_for_nothing()
    {
        // Silence costs nothing and an empty sentence is not worth a COM object.
        (await new WindowsSpeech().SayAsync("   ")).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_sentence_reaches_the_speaking_command_as_one_argument()
    {
        // The case that matters: anything in the text — a quote, a semicolon,
        // a path — must not become part of a command.
        var said = "implementer/1 wants Bash for 'git push; rm -rf /'";

        UnixSpeech.Arguments(said, interrupt: true, macOS: true).Should().Equal(said);
        UnixSpeech.Arguments(said, interrupt: true, macOS: false).Should().Contain(said);
    }

    [Fact]
    public void Interrupting_cancels_what_is_being_read_before_reading_the_next()
    {
        // Somebody holding the arrow key down would otherwise be minutes
        // behind their own cursor.
        UnixSpeech.Arguments("a row", interrupt: true, macOS: false)
            .Should().Equal("--cancel", "--wait", "a row");

        UnixSpeech.Arguments("a row", interrupt: false, macOS: false)
            .Should().Equal("a row");
    }

    [Fact]
    public void MacOS_has_no_cancel_because_each_sentence_is_its_own_process()
    {
        UnixSpeech.Arguments("a row", interrupt: true, macOS: true)
            .Should().NotContain("--cancel");
    }
}
