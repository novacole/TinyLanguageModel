using System.Text.RegularExpressions;

namespace TinyLanguageModel;
public class LanguageModel
{
    private Dictionary<int, Dictionary<string, Dictionary<string, float>>> _probabilities;
    private static Dictionary<string, Dictionary<string, int>> _meanings = new();
    public LanguageModel()
    {
        _probabilities = new Dictionary<int, Dictionary<string, Dictionary<string, float>>>();
    }

    public void Build(string corpus, int maxNGramSize)
    {
        var counts = new Dictionary<int, Dictionary<string, Dictionary<string, int>>>();

        var sentences = Regex.Split(corpus, @"(\r?\n|\r|\n)");

        foreach (var sentence in sentences)
        {
            var words = Regex.Split(sentence.ToLower(), @"\s+|(?=\p{P})|(?<=\p{P})").Where(w => w != string.Empty).ToArray();

            for (int n = 1; n <= maxNGramSize; n++)
            {
                for (int i = 0; i <= words.Length - n; i++)
                {
                    string context = String.Join(" ", words.Skip(i).Take(n - 1));
                    string word = words[i + n - 1];

                    if (!counts.ContainsKey(n))
                    {
                        counts[n] = new Dictionary<string, Dictionary<string, int>>();
                    }

                    if (!counts[n].ContainsKey(context))
                    {
                        counts[n][context] = new Dictionary<string, int>();
                    }

                    if (!counts[n][context].ContainsKey(word))
                    {
                        counts[n][context][word] = 0;
                    }

                    counts[n][context][word]++;
                }
            }
        }

        var probs = GenerateProbabilities(counts);

        _probabilities = probs;
        _meanings = ProcessMeanings(corpus);
    }

    private static Dictionary<int, Dictionary<string, Dictionary<string, float>>> GenerateProbabilities(
        Dictionary<int, Dictionary<string, Dictionary<string, int>>> counts)
    {
        var probabilities = new Dictionary<int, Dictionary<string, Dictionary<string, float>>>();

        foreach (var nGramEntry in counts)
        {
            int nGramSize = nGramEntry.Key;
            var contexts = nGramEntry.Value;

            var nGramProbabilities = new Dictionary<string, Dictionary<string, float>>();

            foreach (var contextEntry in contexts)
            {
                string context = contextEntry.Key;
                var wordCounts = contextEntry.Value;
                int contextTotalCount = wordCounts.Values.Sum();

                var contextProbabilities = new Dictionary<string, float>();
                foreach (var wordCount in wordCounts)
                {
                    string word = wordCount.Key;
                    int count = wordCount.Value;
                    float probability = (float)count / contextTotalCount;
                    contextProbabilities[word] = probability;
                }

                nGramProbabilities[context] = contextProbabilities;
            }

            probabilities[nGramSize] = nGramProbabilities;
        }

        return probabilities;
    }


    public string Predict(string[] context, int maxNGramSize, float temperature, bool stream)
    {
        var candidates = new Dictionary<string, float>();
        int attentionSpan = 100; // About 1.5 sentences. So we shouldnt expect long range coherence.
        Dictionary<string, float> productOfAttention = new Dictionary<string, float>();
        int startLimit = context.Length;
        int endLimit = context.Length >= attentionSpan ? context.Length - attentionSpan : context.Length - context.Length;

        productOfAttention = AttendTo(context.TakeLast(attentionSpan).ToList());

        for (int n = maxNGramSize; n > 1; n--)
        {
            string currentContext = string.Join(" ", context.TakeLast(n - 1));

            if (_probabilities.ContainsKey(n) && _probabilities[n].ContainsKey(currentContext))
            {
                float bias = (float)(maxNGramSize - n + 1);

                foreach (var wordProbability in _probabilities[n][currentContext])
                {
                    string word = wordProbability.Key;
                    float probability = wordProbability.Value;
                    if (!candidates.ContainsKey(word))
                        candidates[word] = probability;

                    if (productOfAttention.ContainsKey(word))
                    {
                        candidates[word] += productOfAttention[word];
                    }
                    else
                    {
                        candidates[word] -= .05f;
                    }
                }

                break;
            }
        }

        if (stream)
        {
            var lastToken = context.TakeLast(1).Single();
            Console.Write($"{lastToken} ");
            if (lastToken == ".")
            {
                Console.WriteLine();
            }
        }

        foreach (var i in candidates.Keys)
        {
            if (context.TakeLast(attentionSpan / 2).Contains(i))
            {
                candidates[i] -= .1f;
            }
        }
        // Normalize scores
        if (candidates.Count > 0)
        {
            float maxScore = candidates.Values.Max();
            var normalizedScores = candidates.OrderByDescending(kvp => kvp.Value / maxScore).ToDictionary(kvp => kvp.Key, kvp => kvp.Value / maxScore);

            if (temperature == 0)
            {
                return normalizedScores.First().Key;
            }
            else
            {
                return WeightedRandomSelection(normalizedScores, temperature);
            }
        }
        return string.Empty;
    }

    private static string WeightedRandomSelection(Dictionary<string, float> weightedItems, float temperature)
    {
        int topTokens = (int)(temperature * 10);

        var adjustedWeights = weightedItems.Take(topTokens + 1).ToDictionary(item => item.Key, item => Math.Pow(item.Value, 1 / temperature));

        double totalWeight = adjustedWeights.Values.Sum();

        double randomValue = new Random().NextDouble() * totalWeight;
        foreach (var item in adjustedWeights)
        {
            randomValue -= item.Value;
            if (randomValue <= 0)
            {
                return item.Key;
            }
        }

        return adjustedWeights.Keys.Last();
    }
    private static Dictionary<string, Dictionary<string, int>> ProcessMeanings(string corpus)
    {
        var sentences = Regex.Split(corpus, @"\r\n|\r|\n");
        var wordMap = new Dictionary<string, Dictionary<string, int>>();

        foreach (var sentence in sentences)
        {
            var words = Regex.Split(sentence.ToLower(), @"\s+|(?=\p{P})|(?<=\p{P})");
            foreach (var keyWord in words)
            {
                if (!wordMap.ContainsKey(keyWord))
                {
                    wordMap[keyWord] = new Dictionary<string, int>();
                }

                foreach (var word in words)
                {
                    if (word != keyWord)
                    {
                        if (!wordMap[keyWord].ContainsKey(word))
                        {
                            wordMap[keyWord][word] = 0;
                        }

                        wordMap[keyWord][word]++;
                    }
                }
            }
        }

        var sortedWordMap = new Dictionary<string, Dictionary<string, int>>();
        foreach (var item in wordMap)
        {
            sortedWordMap[item.Key] = item.Value.OrderByDescending(pair => pair.Value)
                                                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return sortedWordMap;
    }

    private static Dictionary<string, float> AttendTo(List<string> targetWords)
    {
        var tempResults = new List<(string Key, float Probability, int MatchCount)>();
        int maxMatchCount = 0;
        Dictionary<string, Dictionary<string, float>> understading = new();
        int maxAssociations = 50;

        foreach (var meaning in _meanings)
        {

            if (!string.IsNullOrEmpty(meaning.Key))
            {
                var associatedMeanings = meaning.Value.OrderByDescending(pair => pair.Value)
                                                 .Take(maxAssociations)
                                                 .Select(pair => pair.Key)
                                                 .ToHashSet();

                int matchedCount = 0;

                foreach (var targetWord in targetWords)
                {
                    if (associatedMeanings.Contains(targetWord))
                    {
                        if (_meanings.TryGetValue(targetWord, out var wordCounts) && wordCounts.TryGetValue(meaning.Key, out var count))
                        {
                            matchedCount++;
                            if (understading.ContainsKey(meaning.Key))
                            {
                                understading[meaning.Key][targetWord] = count;
                            }
                            else
                            {
                                understading[meaning.Key] = new Dictionary<string, float>() { { targetWord, count } };
                            }
                        }
                    }
                }

                if (matchedCount > 0)
                {
                    if (matchedCount > maxMatchCount)
                    {
                        maxMatchCount = matchedCount;
                    }
                }
            }
        }

        Dictionary<string, float> finalResults = new();
        if (understading.Count > 0)
        {
            var maxCount = understading.Select(x => x.Value.Count).Max();
            finalResults = understading.Where(x => x.Value.Count >= maxCount -1).ToDictionary(x => x.Key, x => (float)(x.Value.Values.Average()));
            var maxAverage = finalResults.Values.Max();

            if (finalResults.Count > 0)
            {
                var max = finalResults.Values.Max();
                foreach (var result in finalResults)
                {
                    finalResults[result.Key] = ((max - result.Value) / maxCount) / maxAverage;
                }
            }
        }
        return finalResults;
    }

}
