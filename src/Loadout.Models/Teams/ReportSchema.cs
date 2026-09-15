namespace Loadout.Models.Teams;

/// <summary>
/// The JSON Schema a node's final answer is held to, handed to the agent as
/// its structured output.
/// </summary>
/// <remarks>
/// Written by hand rather than generated from <see cref="Report"/>, because
/// the schema is a promise to other people's roles and a generator's choices
/// about nullability and additional properties would change it without
/// anyone deciding to. A test keeps the two in step by round-tripping a
/// report that uses every field.
/// </remarks>
public static class ReportSchema
{
    /// <summary>The schema for <see cref="Report.Version"/>.</summary>
    /// <remarks>
    /// No <c>$schema</c> key. Claude Code's validator refuses one naming
    /// draft 2020-12 ("no schema with key or ref"), and the first real run
    /// ended in under a second on exactly that. Without the key the schema
    /// is read as the validator's default draft, which supports everything
    /// used here.
    /// </remarks>
    public const string Version1 = """
        {
          "title": "report/1",
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "node", "status", "summary", "deliverables", "evidence", "outward_taken"],
          "properties": {
            "contract": { "const": "report/1" },
            "node": { "type": "string" },
            "status": { "enum": ["done", "blocked", "failed", "needs-decision"] },
            "summary": { "type": "string", "maxLength": 1500 },
            "deliverables": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["kind", "ref"],
              "properties": {
                "kind": { "enum": ["commit", "branch", "file", "document", "plan", "decision", "answer"] },
                "ref": { "type": "string" },
                "note": { "type": "string" } } } },
            "evidence": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["kind", "ref", "result"],
              "properties": {
                "kind": { "enum": ["test", "command", "observation", "review"] },
                "ref": { "type": "string" },
                "result": { "enum": ["pass", "fail", "n/a"] },
                "note": { "type": "string" } } } },
            "blocker": { "type": "object", "additionalProperties": false, "required": ["what", "unblocked_by"],
              "properties": { "what": { "type": "string" }, "unblocked_by": { "type": "string" } } },
            "questions": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["question", "options", "recommendation"],
              "properties": {
                "question": { "type": "string" },
                "options": { "type": "array", "items": { "type": "string" }, "minItems": 2 },
                "recommendation": { "type": "string" } } } },
            "requests": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["node", "task", "deliverable"],
              "properties": {
                "node": { "type": "string" },
                "task": { "type": "string" },
                "deliverable": { "enum": ["commit", "branch", "file", "document", "plan", "decision", "answer"] },
                "inputs": { "type": "array", "items": { "type": "string" } } } } },
            "outward_taken": { "type": "array", "items": { "type": "string" } },
            "outward_requested": { "type": "array", "items": { "type": "string" } },
            "next": { "type": "string" }
          }
        }
        """;
}
