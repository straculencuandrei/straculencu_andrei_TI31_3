using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WordFrequency
{
    class Program
    {
        private static HashSet<string> stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void Main(string[] args)
        {
            string inputDir = "Reuters_txt";
            string outputDir = "Reuters_stemmed";
            string stopWordsPath = "stopwords.txt";
            string vsmOutputFile = "dataset_vsm.txt";

            if (args.Length > 0)
            {
                if (args[0] == "-h" || args[0] == "--help" || args[0] == "/?")
                {
                    PrintUsage();
                    return;
                }
                inputDir = args[0];
            }

            if (args.Length > 1)
            {
                outputDir = args[1];
            }

            if (args.Length > 2)
            {
                stopWordsPath = args[2];
            }

            if (args.Length > 3)
            {
                vsmOutputFile = args[3];
            }

            LoadStopWords(stopWordsPath);

            if (!Directory.Exists(inputDir) && !File.Exists(inputDir))
            {
                Console.WriteLine("Error: Input path does not exist: " + inputDir);
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();

            // Prelucrare fisier individual
            if (File.Exists(inputDir))
            {
                string outPath = outputDir;
                if (Directory.Exists(outputDir) || string.IsNullOrEmpty(Path.GetExtension(outputDir)))
                {
                    Directory.CreateDirectory(outputDir);
                    outPath = Path.Combine(outputDir, Path.GetFileName(inputDir));
                }

                PorterStemmer stemmer = new PorterStemmer();
                Document singleDoc = Document.LoadFromTextFile(inputDir, stopWords, stemmer);
                singleDoc.SaveStemmedFile(outPath);

                Console.WriteLine("Processed single file: " + inputDir + " -> " + outPath);
                Console.WriteLine("Topics found: " + string.Join(", ", singleDoc.Topics));
                Console.WriteLine("Unique words: " + singleDoc.TermFrequencies.Count);
                return;
            }

            Console.WriteLine("=================================================");
            Console.WriteLine("   ETAPA 1: EXTRAGEREA TRASATURILOR (VSM)       ");
            Console.WriteLine("=================================================");
            Console.WriteLine("Director intrare:   " + Path.GetFullPath(inputDir));
            Console.WriteLine("Director stemmed:   " + Path.GetFullPath(outputDir));
            Console.WriteLine("Fisier stopwords:   " + Path.GetFullPath(stopWordsPath));
            Console.WriteLine("Fisier iesire VSM:  " + Path.GetFullPath(vsmOutputFile));
            Console.WriteLine("Stopwords incarcate:" + stopWords.Count);

            string[] files = Directory.GetFiles(inputDir, "*.txt", SearchOption.AllDirectories);
            Console.WriteLine("Fisiere gasite:     " + files.Length);
            Console.WriteLine("=================================================\n");

            if (files.Length == 0)
            {
                Console.WriteLine("Nu au fost gasite fisiere .txt.");
                return;
            }

            Document[] documents = new Document[files.Length];
            int processed = 0;
            object lockObj = new object();

            // Pasul 1: Procesare fisiere in paralel (filtrare stopwords, stemming, salvare fisiere stemmed)
            Console.WriteLine("Pas 1/3: Procesare documente si stemming...");
            Parallel.For(0, files.Length, () => new PorterStemmer(), (i, state, stemmer) =>
            {
                string file = files[i];
                Document doc = Document.LoadFromTextFile(file, stopWords, stemmer, inputDir);
                documents[i] = doc;

                // Salvare fisier stemmed cu aceeasi structura de directoare relative
                string outPath = Path.Combine(outputDir, doc.RelativePath);
                doc.SaveStemmedFile(outPath);

                int count = Interlocked.Increment(ref processed);
                if (count % 1000 == 0 || count == files.Length)
                {
                    Console.WriteLine("   [" + count + "/" + files.Length + "] documente procesate...");
                }

                return stemmer;
            }, _ => {});

            // Pasul 2: Construire dictionar global (Vocabulary) sortat alfabetic conform PDF
            Console.WriteLine("\nPas 2/3: Construire dictionar global (Vectorul global)...");
            SortedSet<string> globalVocabulary = new SortedSet<string>(StringComparer.Ordinal);
            HashSet<string> allTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Document doc in documents)
            {
                foreach (string term in doc.TermFrequencies.Keys)
                {
                    globalVocabulary.Add(term);
                }
                foreach (string t in doc.Topics)
                {
                    allTopics.Add(t);
                }
            }

            Dictionary<string, int> wordToIndex = new Dictionary<string, int>(globalVocabulary.Count, StringComparer.Ordinal);
            List<string> indexToWord = new List<string>(globalVocabulary.Count);

            int wordIndex = 0;
            foreach (string word in globalVocabulary)
            {
                wordToIndex[word] = wordIndex;
                indexToWord.Add(word);
                wordIndex++;
            }

            Console.WriteLine("   Dimensiune dictionar global: " + globalVocabulary.Count + " cuvinte unice");
            Console.WriteLine("   Total clase/topics gasite:    " + allTopics.Count);

            // Pasul 3: Construire reprezentare vectori rari si scriere fisier final VSM
            Console.WriteLine("\nPas 3/3: Generare reprezentare vectori rari si salvare fisier...");
            for (int i = 0; i < documents.Length; i++)
            {
                documents[i].BuildSparseVector(wordToIndex);
            }

            WriteVsmDatasetFile(vsmOutputFile, indexToWord, documents);

            sw.Stop();

            Console.WriteLine("\n================ Sumar Executie ================");
            Console.WriteLine("Total documente procesate: " + documents.Length);
            Console.WriteLine("Dimensiune spatiu VSM:     " + indexToWord.Count + " trasaturi (atribute)");
            Console.WriteLine("Clase (topics) extrase:    " + allTopics.Count + " (" + string.Join(" ", allTopics) + ")");
            Console.WriteLine("Fisier rezultat VSM:       " + Path.GetFullPath(vsmOutputFile));
            Console.WriteLine("Timp total scurs:          " + sw.Elapsed.TotalSeconds.ToString("F3") + " secunde");
            Console.WriteLine("================================================\n");

            // Afisare preview primele atribute si primele documente
            Console.WriteLine("--- Preview Dictionar Global (primele 10 cuvinte) ---");
            int previewCount = Math.Min(10, indexToWord.Count);
            for (int i = 0; i < previewCount; i++)
            {
                Console.WriteLine("  @" + i + ": " + indexToWord[i]);
            }

            Console.WriteLine("\n--- Preview Documente Vectorizate (primele 3) ---");
            int previewDocs = Math.Min(3, documents.Length);
            for (int i = 0; i < previewDocs; i++)
            {
                Console.WriteLine("  " + documents[i].ToDataLine());
            }
            Console.WriteLine("------------------------------------------------\n");
        }

        private static void WriteVsmDatasetFile(string outputPath, List<string> attributes, Document[] documents)
        {
            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            using (StreamWriter writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                // Antet: salvam cuvintele din dictionarul global, fiecare cuvant pe o linie conform pag. 6 din PDF
                foreach (string attr in attributes)
                {
                    writer.WriteLine("@attribute " + attr);
                }

                // Separator date
                writer.WriteLine("@data");

                // Randuri de date: <Id> # <sparse_vector> # <topics> # <folder>
                foreach (Document doc in documents)
                {
                    writer.WriteLine(doc.ToDataLine());
                }
            }
        }

        private static void LoadStopWords(string path)
        {
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    string word = rawLine.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(word) && !word.StartsWith("#"))
                    {
                        stopWords.Add(word);
                    }
                }
            }
            else
            {
                string[] defaultStops = new string[] {
                    "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "aren't",
                    "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but", "by", "can",
                    "can't", "cannot", "could", "couldn't", "did", "didn't", "do", "does", "doesn't", "doing", "don't",
                    "down", "during", "each", "few", "for", "from", "further", "had", "hadn't", "has", "hasn't", "have",
                    "haven't", "having", "he", "he'd", "he'll", "he's", "her", "here", "here's", "hers", "herself", "him",
                    "himself", "his", "how", "how's", "i", "i'd", "i'll", "i'm", "i've", "if", "in", "into", "is", "isn't",
                    "it", "it's", "its", "itself", "let's", "me", "more", "most", "mustn't", "my", "myself", "no", "nor",
                    "not", "of", "off", "on", "once", "only", "or", "other", "ought", "our", "ours", "ourselves", "out",
                    "over", "own", "same", "shan't", "she", "she'd", "she'll", "she's", "should", "shouldn't", "so", "some",
                    "such", "than", "that", "that's", "the", "their", "theirs", "them", "themselves", "then", "there",
                    "there's", "these", "they", "they'd", "they'll", "they're", "they've", "this", "those", "through", "to",
                    "too", "under", "until", "up", "very", "was", "wasn't", "we", "we'd", "we'll", "we're", "we've", "were",
                    "weren't", "what", "what's", "when", "when's", "where", "where's", "which", "while", "who", "who's",
                    "whom", "why", "why's", "with", "won't", "would", "wouldn't", "you", "you'd", "you'll", "you're",
                    "you've", "your", "yours", "yourself", "yourselves"
                };
                foreach (string w in defaultStops) stopWords.Add(w);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Utilizare:");
            Console.WriteLine("  PorterStemmerRunner.exe [director_intrare] [director_stemmed] [fisier_stopwords] [fisier_iesire_vsm]");
            Console.WriteLine("\nValori implicite:");
            Console.WriteLine("  director_intrare    = Reuters_txt");
            Console.WriteLine("  director_stemmed    = Reuters_stemmed");
            Console.WriteLine("  fisier_stopwords    = stopwords.txt");
            Console.WriteLine("  fisier_iesire_vsm   = dataset_vsm.txt");
            Console.WriteLine("\nExemple:");
            Console.WriteLine("  PorterStemmerRunner.exe");
            Console.WriteLine("  PorterStemmerRunner.exe Reuters_txt\\Reuters_34 Reuters_stemmed\\Reuters_34 stopwords.txt dataset_34_vsm.txt");
        }
    }
}
