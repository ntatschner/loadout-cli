namespace Loadout.Models.Ideas;

/// <summary>
/// The shape an agent's answer to a round of refinement must take.
/// </summary>
/// <remarks>
/// <para>
/// One shape for every round, rather than one for questions and one for the
/// plan. What the round is for is said in the prompt; what comes back is either
/// questions, or a plan, or both, and the record takes whatever is there. A
/// second schema would be a second thing to keep in step with the reader, for
/// no reason the agent would notice.
/// </para>
/// <para>
/// No <c>$schema</c> key, for the reason <c>ReportSchema</c> gives: Claude Code's
/// validator refuses one naming draft 2020-12.
/// </para>
/// </remarks>
public static class IdeaSchema
{
    /// <summary>The contract every answer names, so a stale one is refused rather than misread.</summary>
    public const string Version = "idea/2";

    /// <summary>
    /// The contract before a plan could propose done-when. An answer naming it
    /// is still read, as a plan with none: it is the same shape, less one field.
    /// </summary>
    public const string Previous = "idea/1";

    /// <summary>The schema for <see cref="Version"/>.</summary>
    /// <remarks>
    /// idea/1 with an optional <c>doneWhen</c> on the plan. Optional, so a plan
    /// without one is still a plan; the person adds conditions at acceptance.
    /// </remarks>
    public const string Version2 = """
        {
          "title": "idea/2",
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "understanding", "questions"],
          "properties": {
            "contract": { "const": "idea/2" },
            "understanding": { "type": "string" },
            "questions": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["question", "why", "options", "recommendation"],
              "properties": {
                "question": { "type": "string" },
                "why": { "type": "string" },
                "options": { "type": "array", "items": { "type": "string" } },
                "recommendation": { "type": "string" } } } },
            "plan": {
              "type": "object", "additionalProperties": false, "required": ["title", "layers", "additions", "project"],
              "properties": {
                "title": { "type": "string" },
                "layers": { "type": "array", "items": {
                  "type": "object", "additionalProperties": false, "required": ["name", "purpose", "options"],
                  "properties": {
                    "name": { "type": "string" },
                    "purpose": { "type": "string" },
                    "options": { "type": "array", "minItems": 1, "items": {
                      "type": "object", "additionalProperties": false, "required": ["title", "detail", "pros", "cons", "recommended"],
                      "properties": {
                        "title": { "type": "string" },
                        "detail": { "type": "string" },
                        "pros": { "type": "array", "items": { "type": "string" } },
                        "cons": { "type": "array", "items": { "type": "string" } },
                        "recommended": { "type": "boolean" } } } } } } },
                "additions": { "type": "array", "items": {
                  "type": "object", "additionalProperties": false, "required": ["title", "why"],
                  "properties": {
                    "title": { "type": "string" },
                    "why": { "type": "string" } } } },
                "project": {
                  "type": "object", "additionalProperties": false, "required": ["slug", "is_new", "name", "reason"],
                  "properties": {
                    "slug": { "type": "string" },
                    "is_new": { "type": "boolean" },
                    "name": { "type": "string" },
                    "reason": { "type": "string" } } },
                "doneWhen": { "type": "array", "items": { "type": "string" } } } }
          }
        }
        """;
}
