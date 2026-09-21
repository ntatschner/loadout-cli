using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>
/// Reads a node's report from the JSON the agent produced, and says plainly
/// what is wrong when it cannot.
/// </summary>
/// <remarks>
/// The schema is enforced by the agent before this runs, so most of what
/// arrives here parses. What does not is still worth a clear sentence: an
/// agent that ignored the schema, a contract version this launcher has not
/// heard of, or a turn that ended with no structured output at all.
/// </remarks>
public static class ReportReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a report, or explains why the text is not one.</summary>
    public static OperationResult<Report> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<Report>.Fail(
                "The node ended its turn without a report. A node's final answer is a report/1 "
                + "document; text outside it is not read.");
        }

        string? contract;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<Report>.Fail("The node's report is not a JSON object.");
            }

            contract = document.RootElement.TryGetProperty("contract", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            return OperationResult<Report>.Fail($"The node's report is not valid JSON: {ex.Message}");
        }

        if (contract is null)
        {
            return OperationResult<Report>.Fail(
                "The node's report does not say which contract it follows. It must carry "
                + $"\"contract\": \"{Report.Version}\".");
        }

        if (!string.Equals(contract, Report.Version, StringComparison.Ordinal))
        {
            return OperationResult<Report>.Fail(
                $"The node's report follows '{contract}', which this launcher does not read. "
                + $"It reads {Report.Version}.");
        }

        try
        {
            var report = JsonSerializer.Deserialize<Report>(json, Options);

            return report is null
                ? OperationResult<Report>.Fail("The node's report is empty.")
                : OperationResult<Report>.Ok(report);
        }
        catch (JsonException ex)
        {
            return OperationResult<Report>.Fail($"The node's report does not fit {Report.Version}: {ex.Message}");
        }
    }

    /// <summary>Writes a report as the JSON the contract describes, for the journal and for tests.</summary>
    public static string Write(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return JsonSerializer.Serialize(report, Options);
    }

    /// <summary>Writes a brief as the JSON attached to a node's prompt.</summary>
    public static string Write(Brief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);

        return JsonSerializer.Serialize(brief, Options);
    }
}
