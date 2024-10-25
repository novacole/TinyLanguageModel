using System.Text.Json;
using System.Text.RegularExpressions;

namespace TinyLanguageModel;
public class LanguageModel
{
    private Dictionary<int, Dictionary<string, Dictionary<string, float>>> _probabilities;
    private static Dictionary<string, Dictionary<string, int>> _meanings = new();
    private string _corpus = string.Empty;
    public LanguageModel()
    {
        _probabilities = new Dictionary<int, Dictionary<string, Dictionary<string, float>>>();
    }
    public void BuildAndSave(string corpus, int maxNGramSize)
    {
        _corpus = corpus;
        if (TryLoadExistingData(out _probabilities, out _meanings))
        {
            Console.WriteLine("Loaded probabilities and meanings from existing files.");
            return;
        }
        if (File.Exists("checkpoint_20.json"))
        {
            LoadAndProcessDataFromCheckpoints(20);
            return;
        }
        var sentences = Regex.Split(corpus, @"(\r?\n|\r|\n)").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var total = sentences.Length;
        int checkpointSize = total / 20;

        if (total % 20 != 0)
            checkpointSize++;

        int currentCheckpoint = 1;

        // Initialize data structure for counts
        var counts = new Dictionary<int, Dictionary<string, Dictionary<string, int>>>(); // Keep counts outside the loop

        for (int s = 0, curCount = 1; s < sentences.Length; s++, curCount++)
        {
            var words = Regex.Split(sentences[s].ToLower(), @"\s+|(?=\p{P})|(?<=\p{P})").Where(w => w != string.Empty).ToArray();
            var wordLength = words.Length;
            Console.WriteLine($"{curCount} of {total}");

            for (int n = 1; n <= maxNGramSize; n++)
            {
                if (!counts.TryGetValue(n, out var nGramDictionary))
                {
                    nGramDictionary = new Dictionary<string, Dictionary<string, int>>();
                    counts[n] = nGramDictionary;
                }

                for (int i = 0; i <= wordLength - n; i++)
                {
                    string context = String.Join(" ", words, i, n - 1);
                    string word = words[i + n - 1];
                    if (!nGramDictionary.TryGetValue(context, out var contextDictionary))
                    {
                        contextDictionary = new Dictionary<string, int>();
                        nGramDictionary[context] = contextDictionary;
                    }

                    contextDictionary.TryGetValue(word, out int wordCount);
                    contextDictionary[word] = wordCount + 1;
                }
            }

            // Save and clear data at each checkpoint
            if ((curCount % checkpointSize == 0 || s == sentences.Length - 1) && curCount != 1)
            {
                Console.WriteLine($"Checkpoint {currentCheckpoint}: Saving partial data...");
                SaveIntermediateData($"checkpoint_{currentCheckpoint}.json", counts);
                currentCheckpoint++;
                counts.Clear();  // Clear the data after saving
                counts = new Dictionary<int, Dictionary<string, Dictionary<string, int>>>(); // Reinitialize counts
            }
        }

        // Load data from all checkpoints, aggregate it, and process it
        LoadAndProcessDataFromCheckpoints(currentCheckpoint - 1);
    }

    private void SaveIntermediateData(string filePath, Dictionary<int, Dictionary<string, Dictionary<string, int>>> data)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(data, options));
        Console.WriteLine($"Data saved to {filePath}");
    }

    private void LoadAndProcessDataFromCheckpoints(int checkpointCount)
    {
        var aggregatedCounts = new Dictionary<int, Dictionary<string, Dictionary<string, int>>>();

        for (int i = 1; i <= checkpointCount; i++)
        {
            string filePath = $"checkpoint_{i}.json";
            string dataJson = File.ReadAllText(filePath);
            var checkpointData = JsonSerializer.Deserialize<Dictionary<int, Dictionary<string, Dictionary<string, int>>>>(dataJson);
            Console.WriteLine($"loading checkpoint {i}");
            // Aggregate data
            foreach (var n in checkpointData.Keys)
            {
                if (!aggregatedCounts.TryGetValue(n, out var mainDict))
                {
                    mainDict = new Dictionary<string, Dictionary<string, int>>();
                    aggregatedCounts[n] = mainDict;
                }

                foreach (var context in checkpointData[n])
                {
                    if (!mainDict.TryGetValue(context.Key, out var wordDict))
                    {
                        wordDict = new Dictionary<string, int>();
                        mainDict[context.Key] = wordDict;
                    }

                    foreach (var word in context.Value)
                    {
                        if (!wordDict.TryGetValue(word.Key, out int count))
                            wordDict[word.Key] = word.Value;
                        else
                            wordDict[word.Key] += word.Value;
                    }
                }
            }
        }

        // Final processing and saving of the complete data
        ProcessAndSaveData(aggregatedCounts);
    }
    private void ProcessAndSaveData(Dictionary<int, Dictionary<string, Dictionary<string, int>>> counts)
    {
        _probabilities = GenerateProbabilities(counts.ToDictionary(x => x.Key, x => x.Value.ToDictionary(y => y.Key, y => y.Value.ToDictionary(z => z.Key, z => z.Value))));
        counts = null;
        _meanings = ProcessMeanings(_corpus);
        _corpus = null;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText("probabilities.json", JsonSerializer.Serialize(_probabilities, options));
        Console.WriteLine("Final data processed and saved.");
    }

    private bool TryLoadExistingData(out Dictionary<int, Dictionary<string, Dictionary<string, float>>> probabilities, out Dictionary<string, Dictionary<string, int>> meanings)
    {
        probabilities = new Dictionary<int, Dictionary<string, Dictionary<string, float>>>();
        meanings = new Dictionary<string, Dictionary<string, int>>();

        bool dataLoaded = false;

        if (File.Exists("probabilities.json") && new FileInfo("probabilities.json").Length > 0)
        {
            string probsJson = File.ReadAllText("probabilities.json");
            probabilities = JsonSerializer.Deserialize<Dictionary<int, Dictionary<string, Dictionary<string, float>>>>(probsJson);
            dataLoaded = true;
        }

        if (File.Exists("meanings.json") && new FileInfo("meanings.json").Length > 0)
        {
            string meaningsJson = File.ReadAllText("meanings.json");
            meanings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(meaningsJson);
            dataLoaded = true;
        }
        else
        {
            var options = new JsonSerializerOptions { WriteIndented = true };

            _meanings = ProcessMeanings(_corpus);
            // figure out what to do here. when using the allcombined dataset, the file is super large 10s of gigbytes. but also takes a long time to load into memory.
            // File.WriteAllText("meanings.json", JsonSerializer.Serialize(_meanings, options));
            dataLoaded = true;
        }

        return dataLoaded;
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
        int attentionSpan = 1500; 
        Dictionary<string, float> productOfAttention = new Dictionary<string, float>();
        int startLimit = context.Length;
        int endLimit = context.Length >= attentionSpan ? context.Length - attentionSpan : context.Length - context.Length;

        productOfAttention = AttendTo(context.TakeLast(attentionSpan).ToList());

        for (int n = maxNGramSize; n > 2; n--) // for now keep at 3, in future may use lower ngram to add options, but the allcombined.txt dataset is large enough that this doesn't seem necessary at this time.
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
                        candidates[word] = Math.Abs(probability);

                    if (productOfAttention.ContainsKey(word))
                    {
                        candidates[word] += Math.Abs(productOfAttention[word]);
                    }
                    else
                    {

                        if (candidates[word] - .05f < 0)
                        {
                            candidates[word] = 0;
                        } else
                        {
                            candidates[word] -= .05f;
                        }
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
                candidates[i] -= .05f;
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
        Dictionary<string, Dictionary<string, int>> wordMap = new Dictionary<string, Dictionary<string, int>>();
        var filePath = "meanings.json";
        // Check if file exists
       
            int count = 0;
            var sentences = Regex.Split(corpus, @"\r\n|\r|\n");
            using (var writer = new StreamWriter(filePath))
            {
                foreach (var sentence in sentences)
                {
                    Console.WriteLine($"Processing sentence {++count} of {sentences.Length}");
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
            
            }
        

        return wordMap;
    }

    private static Dictionary<string, float> AttendTo(List<string> targetWords)
    {
        var tempResults = new List<(string Key, float Probability, int MatchCount)>();
        int maxMatchCount = 0;
        Dictionary<string, Dictionary<string, float>> understading = new();
        int maxAssociations = 150;

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

            finalResults = understading.Where(x => x.Value.Count >= maxCount - 1).Select(x => new { Key = x.Key, Value = x.Value.Values.Average() })
                                       .ToDictionary(x => x.Key, x => x.Value);
            var min = finalResults.Values.Min();
            var max = finalResults.Values.Max();
            if (max > 0)
            {
                foreach (var result in finalResults.Keys.ToList())
                {
                    finalResults[result] /= max;

                }
            }
        }
        return finalResults;
    }

}
