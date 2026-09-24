using System.Globalization;
using Loadout.Models.Teams;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Loadout.Core.Teams;

/// <summary>
/// Reads a team file's <c>budget:</c>, so that <c>usd: none</c> means no cap
/// rather than a file that fails to load.
/// </summary>
/// <remarks>
/// <para>
/// The figure is a <see cref="decimal" />, and YamlDotNet cannot make one of
/// "none": the whole file was dropped as unreadable, and a team of the same
/// name from a lower layer ran in its place with nothing said but a finding.
/// </para>
/// <para>
/// Read only. Team files are written by people and by <c>team new</c> as text,
/// never serialised from a definition.
/// </para>
/// </remarks>
internal sealed class TeamBudgetConverter : IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type) => type == typeof(TeamBudget);

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var budget = new TeamBudget();

        // "budget:" with nothing after it is a budget that says nothing.
        if (parser.TryConsume<Scalar>(out _))
        {
            return budget;
        }

        parser.Consume<MappingStart>();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();

            if (parser.Current is not Scalar value)
            {
                parser.SkipThisAndNestedEvents();

                continue;
            }

            parser.MoveNext();

            switch (key.Value)
            {
                case "usd":
                    Usd(budget, value);
                    break;

                case "turns_per_node" when value.Value.Length > 0:
                    budget.TurnsPerNode = int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var turns)
                        ? turns
                        : throw new YamlException(value.Start, value.End, $"budget.turns_per_node is a whole number, not '{value.Value}'.");
                    break;

                case "wall_clock":
                    budget.WallClock = value.Value.Length > 0 ? value.Value : null;
                    break;
            }
        }

        return budget;
    }

    private static void Usd(TeamBudget budget, Scalar value)
    {
        var said = value.Value.Trim();

        if (said.Length == 0 || said is "~" or "null")
        {
            return;
        }

        if (string.Equals(said, UsdCap.NoneWord, StringComparison.OrdinalIgnoreCase))
        {
            budget.Uncapped = true;

            return;
        }

        // Kept whatever its sign, so the catalogue's check can name a zero or
        // a negative figure as the mistake it is rather than it vanishing.
        budget.Usd = decimal.TryParse(said.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var usd)
            ? usd
            : throw new YamlException(
                value.Start, value.End, $"budget.usd is a figure in US dollars or '{UsdCap.NoneWord}', not '{said}'.");
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Team files are written as text, never serialised from a definition.");
}
