namespace TinyLanguageModel;
public class Program
{
    public static void Main(string[] args)
    {
        var model = new LanguageModel();
        string corpus = File.ReadAllText("corpa/wikisent2.txt");
        var maxNGramSize = 3;
 
        model.BuildAndSave(corpus, maxNGramSize);

        string[] startingContext = "whenever it is".ToLower().Split(" ");
        string text = Generate(startingContext, model, maxNGramSize, 100000, 0.1f, true);
        Console.WriteLine($"\n----------------------------------------");
        Console.WriteLine($"Generated text:\n {text}");
        Console.WriteLine($"\n----------------------------------------");
    }


    private static string Generate(string[] startingContext, LanguageModel model, int maxNGramSize, int length, float temperature, bool stream)
    {
        var sentence = new List<string>(startingContext);
        var currentContext = new Queue<string>(startingContext);

        while (sentence.Count < length)
        {
            string[] contextArray = currentContext.ToArray();
            string nextWord = model.Predict(contextArray, maxNGramSize, temperature, stream);
            if (string.IsNullOrEmpty(nextWord))
                break;

            sentence.Add(nextWord);
            currentContext.Enqueue(nextWord);
            if (currentContext.Count == 1)
                currentContext.Dequeue();
        }

        return string.Join(" ", sentence);
    }
}

