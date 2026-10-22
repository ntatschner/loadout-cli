namespace Loadout.Models.Tasks;

/// <summary>
/// The reply contract for an agent proposing a task's done-when.
/// </summary>
public static class DoneWhenSchema
{
    /// <summary>The contract every answer names, so a stale one is refused rather than misread.</summary>
    public const string Version = "donewhen/1";

    /// <summary>The fewest conditions a proposal may have.</summary>
    public const int Fewest = 2;

    /// <summary>The most conditions a proposal may have.</summary>
    /// <remarks>
    /// A run's lead reports a verdict and evidence for every condition, so a
    /// long list makes a run hard to finish without making it any better judged.
    /// </remarks>
    public const int Most = 5;

    /// <summary>The schema for <see cref="Version"/>.</summary>
    public const string Version1 = """
        {
          "title": "donewhen/1",
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "doneWhen", "why"],
          "properties": {
            "contract": { "const": "donewhen/1" },
            "doneWhen": { "type": "array", "minItems": 2, "maxItems": 5, "items": { "type": "string" } },
            "why": { "type": "string" }
          }
        }
        """;
}
