namespace Loadout.Models.Teams;

/// <summary>
/// What a timed question does when nobody answers it in time.
/// </summary>
/// <remarks>
/// <para>
/// Taking the lead's recommendation straight away was the only thing a timer
/// could do, which made an unattended run's decisions the lead's own first
/// thought. Sending the question back once first makes it look again with a
/// round of evidence it did not have, and still ends in its recommendation if
/// that times out too.
/// </para>
/// <para>
/// Here rather than in the command line, for the reason
/// <see cref="TeamDuration"/> is: a team file says it too, and every reader has
/// to agree on the words.
/// </para>
/// </remarks>
public static class TeamTimeout
{
    /// <summary>Take the recommendation at once.</summary>
    public const string Recommend = "recommend";

    /// <summary>Send the question back once, then take the recommendation. The default.</summary>
    public const string ThinkAgainOnce = "think-again-once";

    /// <summary>Send it back every time; only the round limit and the budget end it.</summary>
    public const string ThinkAgain = "think-again";

    /// <summary>The setting, normalised, or null for anything that is not one.</summary>
    public static string? Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        Recommend or "take-recommendation" => Recommend,
        ThinkAgainOnce or "once" => ThinkAgainOnce,
        ThinkAgain or "always" => ThinkAgain,
        _ => null,
    };

    /// <summary>What to say about text that is not one.</summary>
    public static string Refusal(string text) =>
        $"'{text.Trim()}' is not something a timed question can do. Write {Recommend}, "
        + $"{ThinkAgainOnce} or {ThinkAgain}.";

    /// <summary>
    /// Whether a question timing out now is sent back rather than answered,
    /// given whether the timer has already sent this one back.
    /// </summary>
    public static bool SendsBack(string? policy, bool sentBackBefore) => (Parse(policy) ?? ThinkAgainOnce) switch
    {
        Recommend => false,
        ThinkAgain => true,
        _ => !sentBackBefore,
    };
}
