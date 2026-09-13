using System.Text;
using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The questions retrieval has to keep answering, against a frozen store.
/// </summary>
/// <remarks>
/// <para>
/// Two fixtures rather than the live memory store, because a test that reads
/// the store measures the store: it would move every time somebody recorded a
/// fact, and go green or red for reasons that have nothing to do with the
/// ranking.
/// </para>
/// <para>
/// This is a capability net and not a sample. It says nothing about how often
/// retrieval is right — that needs labelled traffic, and the honest figure last
/// measured was that it offered something worth offering about a third of the
/// times it spoke. What it does say is that the cases an investigation turned
/// on keep working, which is the check that was missing when a confidence rule
/// was justified on two questions that still passed while silencing a third
/// outright.
/// </para>
/// </remarks>
public sealed class MemoryRetrievalRegressionTests
{
    /// <summary>
    /// How far down the answer may be. Two, because the caller offers two and a
    /// question whose answer is third is a question that was not answered.
    /// </summary>
    private const int Reach = 2;

    [Fact]
    public void Every_question_reaches_the_topic_it_is_about()
    {
        var topics = Topics();
        var failures = new StringBuilder();
        var asked = 0;

        foreach (var (question, expected) in Questions())
        {
            asked++;

            var ranked = MemorySearch.Rank(topics, question, limit: Reach)
                .Select(match => match.Topic.Name)
                .ToList();

            if (!ranked.Contains(expected, StringComparer.Ordinal))
            {
                failures.AppendLine(
                    $"  \"{question}\" wanted {expected}, got "
                    + (ranked.Count == 0 ? "nothing" : string.Join(", ", ranked)));
            }
        }

        asked.Should().BeGreaterThan(10, "the fixture should not have been emptied");

        failures.Length.Should().Be(
            0,
            "every question below is one somebody actually asked, and the topic is the "
            + $"one that answers it:\n{failures}");
    }

    [Fact]
    public void A_question_about_nothing_in_the_store_reaches_nothing()
    {
        // The other half of the capability. A ranking that answers everything
        // has not ranked, and the caller has no way to tell a real match from
        // a shrug.
        MemorySearch.Rank(Topics(), "what did you have for breakfast")
            .Should().BeEmpty();
    }

    private static IReadOnlyList<MemoryTopic> Topics()
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var facts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var parts in Rows("memory-topics.tsv"))
        {
            if (parts.Length < 3)
            {
                continue;
            }

            if (parts[0] == "TOPIC")
            {
                descriptions[parts[1]] = parts[2];
                facts.TryAdd(parts[1], []);
            }
            else if (parts[0] == "FACT")
            {
                facts.TryAdd(parts[1], []);
                facts[parts[1]].Add(parts[2]);
            }
        }

        return descriptions
            .Select(pair => new MemoryTopic(
                pair.Key,
                $"memory/{pair.Key}.md",
                pair.Value,
                MemoryKind.Lesson,
                facts[pair.Key],
                [],
                Bytes: 512,
                WrittenUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .ToList();
    }

    private static IEnumerable<(string Question, string Topic)> Questions()
    {
        foreach (var parts in Rows("memory-questions.tsv"))
        {
            if (parts.Length >= 2)
            {
                yield return (parts[0], parts[1]);
            }
        }
    }

    private static IEnumerable<string[]> Rows(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                yield return line.Split('\t');
            }
        }
    }
}
