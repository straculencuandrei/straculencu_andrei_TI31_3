using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace WordFrequency
{
    /// <summary>
    /// Reprezinta un document / articol procesat in cadrul etapei de extragere a trasaturilor (VSM).
    /// Contine informatii despre identificator, cale, folder (Training/Testing), clase (topics),
    /// frecventele termenilor si reprezentarea ca vector rar.
    /// </summary>
    public class Document
    {
        private static readonly Regex TopicRegex = new Regex("<code\\s+code=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WordRegex = new Regex(@"[a-zA-Z]+", RegexOptions.Compiled);

        public string Id { get; set; }
        public string FilePath { get; set; }
        public string RelativePath { get; set; }
        public string DatasetFolder { get; set; }
        public List<string> Topics { get; set; }
        public List<string> StemmedLines { get; set; }
        public Dictionary<string, int> TermFrequencies { get; set; }
        public SortedDictionary<int, int> SparseVector { get; set; }

        public Document()
        {
            Topics = new List<string>();
            StemmedLines = new List<string>();
            TermFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            SparseVector = new SortedDictionary<int, int>();
        }

        /// <summary>
        /// Incarca si proceseaza un fisier text extras anterior.
        /// Identifica etichetele de topic (&lt;code code="..."&gt;) la finalul fisierului,
        /// elimina semnele de punctuatie, cifrele, cuvintele de legatura si aplica Porter Stemmer.
        /// </summary>
        public static Document LoadFromTextFile(string filePath, HashSet<string> stopWords, PorterStemmer stemmer, string baseDir = null)
        {
            Document doc = new Document
            {
                FilePath = filePath,
                Id = Path.GetFileNameWithoutExtension(filePath)
            };

            if (!string.IsNullOrEmpty(baseDir))
            {
                doc.RelativePath = GetRelativePath(baseDir, filePath);
            }
            else
            {
                doc.RelativePath = Path.GetFileName(filePath);
            }

            // Identificare folder: Training sau Testing conform specificatiei din PDF (Etapa 1, pag. 5)
            string normalizedPath = filePath.Replace('/', '\\');
            if (normalizedPath.IndexOf("\\Training\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf("Training", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doc.DatasetFolder = "Training";
            }
            else if (normalizedPath.IndexOf("\\Testing\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     normalizedPath.IndexOf("Testing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                doc.DatasetFolder = "Testing";
            }
            else
            {
                string parentDir = Path.GetFileName(Path.GetDirectoryName(filePath));
                doc.DatasetFolder = string.IsNullOrEmpty(parentDir) ? "Unknown" : parentDir;
            }

            string[] lines = File.ReadAllLines(filePath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    doc.StemmedLines.Add(string.Empty);
                    continue;
                }

                // Verificare daca linia contine tag de categorie topic: <code code="...">
                Match topicMatch = TopicRegex.Match(line);
                if (topicMatch.Success)
                {
                    string topicCode = topicMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(topicCode) && !doc.Topics.Contains(topicCode))
                    {
                        doc.Topics.Add(topicCode);
                    }
                    continue;
                }

                // Daca linia incepe cu tag generic dar nu a potrivit regex-ul de mai sus
                if (line.StartsWith("<code", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("</code", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Procesare continut text (title si paragrafe text)
                MatchCollection matches = WordRegex.Matches(line);
                List<string> lineTokens = new List<string>(matches.Count);

                foreach (Match m in matches)
                {
                    string word = m.Value.ToLowerInvariant();

                    // Filtrare cuvinte scurte sau stopwords
                    if (word.Length <= 1 || stopWords.Contains(word))
                    {
                        continue;
                    }

                    // Extragere radacina cuvant (Porter Stemmer)
                    string stemmed = stemmer.StemWord(word);

                    if (!string.IsNullOrWhiteSpace(stemmed) && stemmed.Length > 1 && !stopWords.Contains(stemmed))
                    {
                        lineTokens.Add(stemmed);

                        if (doc.TermFrequencies.ContainsKey(stemmed))
                        {
                            doc.TermFrequencies[stemmed]++;
                        }
                        else
                        {
                            doc.TermFrequencies[stemmed] = 1;
                        }
                    }
                }

                doc.StemmedLines.Add(string.Join(" ", lineTokens));
            }

            // Daca nu am gasit topicuri in fisierul .txt, incercam fallback pe fisierul .XML original din Reuters
            if (doc.Topics.Count == 0)
            {
                TryExtractTopicsFromOriginalXml(doc);
            }

            return doc;
        }

        /// <summary>
        /// Fallback in cazul in care fisierul .txt a fost generat inainte de extragerea etichetelor de topics.
        /// </summary>
        private static void TryExtractTopicsFromOriginalXml(Document doc)
        {
            try
            {
                string xmlName = doc.Id + ".XML";
                string currentDir = Directory.GetCurrentDirectory();
                string reutersDir = Path.Combine(currentDir, "Reuters");

                if (Directory.Exists(reutersDir))
                {
                    string[] matches = Directory.GetFiles(reutersDir, xmlName, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        string xmlContent = File.ReadAllText(matches[0]);
                        int idx = xmlContent.IndexOf("bip:topics:1.0", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            int openCodes = xmlContent.LastIndexOf("<codes", idx, StringComparison.OrdinalIgnoreCase);
                            int closeCodes = xmlContent.IndexOf("</codes>", idx, StringComparison.OrdinalIgnoreCase);
                            if (openCodes >= 0 && closeCodes > openCodes)
                            {
                                string section = xmlContent.Substring(openCodes, closeCodes - openCodes);
                                MatchCollection m = TopicRegex.Matches(section);
                                foreach (Match match in m)
                                {
                                    string code = match.Groups[1].Value.Trim();
                                    if (!string.IsNullOrEmpty(code) && !doc.Topics.Contains(code))
                                    {
                                        doc.Topics.Add(code);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignoram eventualele erori la fallback
            }
        }

        /// <summary>
        /// Salveaza fisierul stemmed cu textul procesat si etichetele <code code="..."> la final.
        /// </summary>
        public void SaveStemmedFile(string outputPath)
        {
            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            List<string> linesToWrite = new List<string>(StemmedLines);

            // Adaugam etichetele de topic la finalul fisierului stemmed
            foreach (string topic in Topics)
            {
                linesToWrite.Add(string.Format("<code code=\"{0}\">", topic));
            }

            File.WriteAllLines(outputPath, linesToWrite);
        }

        /// <summary>
        /// Construieste reprezentarea vectoriala rara (Sparse Vector)
        /// mapand cuvintele pe indicii din dictionarul global.
        /// </summary>
        public void BuildSparseVector(Dictionary<string, int> globalWordToIndex)
        {
            SparseVector.Clear();
            int index;
            foreach (KeyValuePair<string, int> kvp in TermFrequencies)
            {
                if (globalWordToIndex.TryGetValue(kvp.Key, out index))
                {
                    SparseVector[index] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Returneaza vectorul rar sub forma de string "pos:frecventa pos:frecventa ...",
        /// ordonat crescator dupa indexul cuvantului in dictionarul global.
        /// </summary>
        public string ToSparseVectorString()
        {
            if (SparseVector.Count == 0) return string.Empty;

            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (KeyValuePair<int, int> kvp in SparseVector)
            {
                if (!first) sb.Append(" ");
                sb.Append(kvp.Key);
                sb.Append(":");
                sb.Append(kvp.Value);
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formateaza linia de date conform specificatiei din PDF:
        /// {Id} # {sparse_vector} # {topics} # {DatasetFolder}
        /// Exemplu din PDF: D1 # 0:1 1:1 2:1 3:1 # ml cla clu # Training
        /// </summary>
        public string ToDataLine()
        {
            string sparseStr = ToSparseVectorString();
            string topicsStr = string.Join(" ", Topics);
            return string.Format("{0} # {1} # {2} # {3}", Id, sparseStr, topicsStr, DatasetFolder);
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            Uri uriRelativeTo = new Uri(Path.GetFullPath(relativeTo).TrimEnd('\\') + "\\");
            Uri uriPath = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(uriRelativeTo.MakeRelativeUri(uriPath).ToString().Replace('/', '\\'));
        }
    }
}
