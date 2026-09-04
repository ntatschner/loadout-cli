using System.Text;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>One topic a query reached, and why.</summary>
/// <param name="Topic">The topic.</param>
/// <param name="Score">
/// How well it answers the query, relative to the others in the same search.
/// Comparable within one result set and meaningless outside it, so it is never
/// shown as a quantity.
/// </param>
/// <param name="Terms">
/// How many distinct words of the query this topic carried. The score says how
/// well it answers relative to the others in the same search; this says how much
/// of the question it touched at all, which is the difference between a topic
/// about the same subject and one that shares a common word.
/// </param>
/// <param name="Matched">
/// The facts that carried query terms, so the reader can see what was matched
/// rather than take the ranking on trust. Empty when the match was on the name
/// or description alone, which is an ordinary and often better match.
/// </param>
public sealed record MemoryMatch(
    MemoryTopic Topic,
    double Score,
    IReadOnlyList<string> Matched,
    int Terms);

/// <summary>
/// Finds the topics that answer a question, without asking anything.
/// </summary>
/// <remarks>
/// <para>
/// The compiled context carries the memory index and not the topics themselves,
/// so a session decides what to open from one line of description each. That
/// works at fifteen topics. At two hundred — which the user and machine scopes
/// bring closer — the index is a wall, and the choice is between opening six
/// files and opening none.
/// </para>
/// <para>
/// Deliberately arithmetic rather than semantic. No model, no service, no index
/// to build and keep current: a memory store is small enough that scoring every
/// topic on every query costs nothing, and a search that runs offline and gives
/// the same answer twice is worth more here than one that understands synonyms.
/// It will not find "credential" from "secret", and says so rather than
/// pretending otherwise.
/// </para>
/// <para>
/// Rarity is what makes it work. A term in half the topics separates nothing, so
/// it counts for little; a term in one topic is almost the whole answer. Without
/// that, a query of three ordinary words ranks by how often a topic says
/// "the build".
/// </para>
/// </remarks>
public static class MemorySearch
{
    /// <summary>
    /// Words too common to separate one topic from another.
    /// </summary>
    /// <remarks>
    /// Kept short on purpose. Rarity already discounts a word that appears
    /// everywhere in this store, and a long fixed list would throw away terms
    /// that are ordinary in English but distinctive here — "when", "before" and
    /// "after" all carry meaning in a note about ordering.
    /// </remarks>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "from",
        "has", "have", "in", "is", "it", "its", "of", "on", "or", "that", "the",
        "there", "they", "this", "to", "was", "were", "will", "with",
    };

    /// <summary>The name and description are curated; a fact is prose.</summary>
    private const double NameWeight = 3.0;
    private const double DescriptionWeight = 2.0;
    private const double FactWeight = 1.0;

    /// <summary>
    /// The topics a query reaches, best first.
    /// </summary>
    /// <param name="topics">Everything to search.</param>
    /// <param name="query">What is being looked for.</param>
    /// <param name="limit">
    /// How many to return. A search that hands back everything has ranked but
    /// not chosen, which leaves the caller exactly where it started.
    /// </param>
    public static IReadOnlyList<MemoryMatch> Rank(
        IReadOnlyList<MemoryTopic> topics,
        string? query,
        int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(topics);

        var terms = Terms(query).Distinct(StringComparer.Ordinal).ToList();

        // Nothing to go on. Returning everything would look like a search that
        // matched everything rather than one that was asked nothing.
        if (terms.Count == 0 || topics.Count == 0 || limit <= 0)
        {
            return [];
        }

        var documents = topics
            .Select(topic => new Document(topic, Fields(topic)))
            .ToList();

        var matches = new List<MemoryMatch>();

        foreach (var document in documents)
        {
            var score = 0.0;
            var hits = 0;

            foreach (var term in terms)
            {
                var weight = document.Weight(term);

                if (weight <= 0)
                {
                    continue;
                }

                hits++;
                score += Saturate(weight) * Rarity(term, documents);
            }

            if (score <= 0)
            {
                continue;
            }

            matches.Add(new MemoryMatch(
                document.Topic,
                score,
                document.Topic.Facts
                    .Where(fact => terms.Any(term => Terms(fact).Contains(term, StringComparer.Ordinal)))
                    .ToList(),
                hits));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Topic.Name, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// What one term is worth to a topic that keeps saying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sixth mention of a word says far less than the first, and counting
    /// them evenly lets repetition beat relevance. Searching a real store for
    /// "build signs" put the topic that says "build" in its name, its
    /// description and its fact above the one whose fact is about signing,
    /// because six mentions of a common word outweighed one of a rare one.
    /// Rarity alone did not fix it: the gap in weight was larger than the gap in
    /// rarity.
    /// </para>
    /// <para>
    /// Saturating flattens that. A term worth 1 keeps most of its value, a term
    /// worth 6 is not worth six times as much, and the rare term decides. The
    /// constant is small because the weights here are small — a topic has one
    /// name, one description and a handful of facts, not a page of prose.
    /// </para>
    /// </remarks>
    private static double Saturate(double weight) => weight / (weight + 2.0);

    /// <summary>
    /// How much one term is worth, given how many topics use it.
    /// </summary>
    /// <remarks>
    /// A term in every topic scores zero and a term in one scores most. The
    /// logarithm keeps a store of five and a store of five hundred on the same
    /// scale, so a result set does not change character as memory grows.
    /// </remarks>
    private static double Rarity(string term, IReadOnlyList<Document> documents)
    {
        var appearances = documents.Count(document => document.Contains(term));

        return appearances == 0 ? 0 : Math.Log(1.0 + ((double)documents.Count / appearances));
    }

    /// <summary>
    /// The words of a piece of text, lowercased, with the ignorable ones gone.
    /// </summary>
    /// <remarks>
    /// Splitting on anything that is not a letter or a digit is what makes a
    /// topic name work as a query. Names here are slugs —
    /// <c>windows-restart-manager-disabled</c> — and somebody looking for that
    /// types "restart manager", which matches only if the slug has been taken
    /// apart the same way the question was.
    /// </remarks>
    private static List<string> Terms(string? text)
    {
        var terms = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return terms;
        }

        var word = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                word.Append(char.ToLowerInvariant(character));

                continue;
            }

            Take(word, terms);
        }

        Take(word, terms);

        return terms;

        static void Take(StringBuilder word, List<string> into)
        {
            if (word.Length == 0)
            {
                return;
            }

            var term = word.ToString();

            word.Clear();

            // One-character words carry nothing and match everywhere.
            if (term.Length > 1 && !Ignored.Contains(term))
            {
                into.Add(term);
            }
        }
    }

    private static IReadOnlyDictionary<string, double> Fields(MemoryTopic topic)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        Add(topic.Name, NameWeight);
        Add(topic.Description, DescriptionWeight);

        foreach (var fact in topic.Facts)
        {
            Add(fact, FactWeight);
        }

        return weights;

        void Add(string? text, double weight)
        {
            foreach (var term in Terms(text))
            {
                weights[term] = weights.GetValueOrDefault(term) + weight;
            }
        }
    }

    private sealed record Document(MemoryTopic Topic, IReadOnlyDictionary<string, double> Weights)
    {
        public bool Contains(string term) => Weights.ContainsKey(term);

        public double Weight(string term) => Weights.GetValueOrDefault(term);
    }
}
