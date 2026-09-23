using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Speaks through NVDA if it is running, and otherwise through the voice
/// Windows already has.
/// </summary>
/// <remarks>
/// <para>
/// NVDA first because a person running a screen reader wants one voice, not
/// two talking over each other: its controller client answers only while NVDA
/// is running, so asking it is also how we find out. JAWS has an equivalent
/// COM object and is not reached here — it is on the list, and claiming it
/// without a copy to try it against would be claiming something untested.
/// </para>
/// <para>
/// SAPI is the fallback, for somebody who wants the launcher spoken and is not
/// running a screen reader at all. It is reached through the COM object by
/// name rather than a package reference, because that is one type on a
/// platform that already has it against a dependency on every platform that
/// does not.
/// </para>
/// <para>
/// <strong>Only the SAPI route is verified.</strong> This machine has no NVDA
/// installed, so the controller path is written from its documented entry
/// points and has never answered. It fails closed: no DLL, no NVDA, fall
/// through to the voice that is there.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSpeech : ISpeech
{
    /// <summary>
    /// NVDA's controller client, which ships with NVDA and sits beside it.
    /// </summary>
    /// <remarks>
    /// Loaded by name only. Present on a machine running NVDA and absent
    /// everywhere else, which is exactly the test that matters, so no attempt
    /// is made to ship or find a copy.
    /// </remarks>
    private const string Controller = "nvdaControllerClient64.dll";

    private bool? _nvda;

    /// <inheritdoc />
    public string Name => _nvda == true ? "nvda" : "sapi";

    [DllImport(Controller, EntryPoint = "nvdaController_testIfRunning")]
    private static extern int TestIfRunning();

    [DllImport(Controller, EntryPoint = "nvdaController_speakText", CharSet = CharSet.Unicode)]
    private static extern int SpeakText([MarshalAs(UnmanagedType.LPWStr)] string text);

    [DllImport(Controller, EntryPoint = "nvdaController_cancelSpeech")]
    private static extern int CancelSpeech();

    /// <summary>
    /// Whether NVDA is running and reachable.
    /// </summary>
    /// <remarks>
    /// Asked once. The answer can only change if somebody starts or stops a
    /// screen reader mid-session, and re-probing a missing DLL on every line
    /// spoken would cost more than it could ever discover.
    /// </remarks>
    private bool Nvda()
    {
        if (_nvda is { } known)
        {
            return known;
        }

        try
        {
            _nvda = TestIfRunning() == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException)
        {
            // No NVDA on this machine, which is the ordinary case.
            _nvda = false;
        }

        return _nvda.Value;
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> IsAvailableAsync(CancellationToken ct = default)
    {
        if (Nvda())
        {
            return Task.FromResult(OperationResult<string>.Ok("NVDA is running and will speak."));
        }

        var voices = Voices();

        return Task.FromResult(voices > 0
            ? OperationResult<string>.Ok(
                $"No screen reader answered; Windows has {voices} voice(s) and will speak instead.")
            : OperationResult<string>.Fail(
                "Nothing on this machine can speak: no screen reader answered and Windows has no voices installed."));
    }

    /// <inheritdoc />
    public Task<OperationResult> SayAsync(string text, bool interrupt = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(OperationResult.Ok());
        }

        if (Nvda())
        {
            try
            {
                if (interrupt)
                {
                    CancelSpeech();
                }

                return Task.FromResult(SpeakText(text) == 0
                    ? OperationResult.Ok()
                    : OperationResult.Fail("NVDA refused the text."));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // It went away mid-session. Fall through to the voice.
                _nvda = false;
            }
        }

        return Task.FromResult(Sapi(text, interrupt));
    }

    /// <inheritdoc />
    public Task<OperationResult> SilenceAsync(CancellationToken ct = default)
    {
        if (Nvda())
        {
            try
            {
                CancelSpeech();

                return Task.FromResult(OperationResult.Ok());
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _nvda = false;
            }
        }

        // Two is "purge before speaking", which with an empty string is how
        // SAPI is told to stop and say nothing.
        return Task.FromResult(Sapi(string.Empty, interrupt: true));
    }

    /// <summary>How many voices Windows has, or nought when SAPI is not there.</summary>
    internal static int Voices()
    {
        try
        {
            if (Type.GetTypeFromProgID("SAPI.SpVoice") is not { } type
                || Activator.CreateInstance(type) is not { } voice)
            {
                return 0;
            }

            try
            {
                dynamic speaker = voice;

                return (int)speaker.GetVoices().Count;
            }
            finally
            {
                Marshal.FinalReleaseComObject(voice);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException
            or MissingMemberException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Says something through the voice Windows has.
    /// </summary>
    /// <remarks>
    /// Asynchronous and purging, in SAPI's own flags: 1 means do not wait, 2
    /// means drop what is queued. A launcher that blocked until a sentence
    /// finished would be a launcher that stopped responding to the arrow key
    /// that started it, and one that queued every row somebody scrolled past
    /// would be minutes behind them.
    /// </remarks>
    private static OperationResult Sapi(string text, bool interrupt)
    {
        const int Async = 1;
        const int PurgeBeforeSpeak = 2;

        try
        {
            if (Type.GetTypeFromProgID("SAPI.SpVoice") is not { } type
                || Activator.CreateInstance(type) is not { } voice)
            {
                return OperationResult.Fail("Windows has no speech voice to use.");
            }

            try
            {
                dynamic speaker = voice;

                speaker.Speak(text, Async | (interrupt ? PurgeBeforeSpeak : 0));

                return OperationResult.Ok();
            }
            finally
            {
                Marshal.FinalReleaseComObject(voice);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException
            or MissingMemberException or NotSupportedException)
        {
            return OperationResult.Fail($"Windows would not speak: {ex.Message}");
        }
    }
}
