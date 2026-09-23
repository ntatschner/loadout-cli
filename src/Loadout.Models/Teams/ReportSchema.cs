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
    /// <summary>How much of a summary is shown where one is printed.</summary>
    /// <remarks>
    /// <para>
    /// A display length, and nothing more. The schema used to hold summary to
    /// 1500 characters, which meant a lead that wrote more had its whole report
    /// refused and wrote the thing again - and the retry is inside the agent's
    /// own turn, where nothing here can reach it. Watched live: 3163 characters,
    /// refused; 1790, refused; 1609, refused. Three rewrites of a full report,
    /// about forty-five seconds apart, shaving a little each time and still
    /// over.
    /// </para>
    /// <para>
    /// Length is not structure. The schema says what the fields are and a
    /// summary that runs long is not a malformed report, so the brief asks for
    /// a short one and this is what gets printed - the journal and the report
    /// file keep every word the lead wrote.
    /// </para>
    /// </remarks>
    public const int SummaryShown = 1500;

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
            "summary": { "type": "string" },
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
            "next": { "type": "string" },
            "proposed_done_when": { "type": "array", "items": { "type": "string" } },
            "goal_understood": { "type": "string" },
            "coverage": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["criterion", "verdict"],
              "properties": {
                "criterion": { "type": "string" },
                "verdict": { "enum": ["met", "unmet", "not-attempted"] },
                "because": { "type": "string" },
                "understood": { "type": "string" } } } }
          }
        }
        """;
}
