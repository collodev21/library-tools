using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CatalogAuditDesk
{
    #region Enums & Data Models

    public enum ItemStatus
    {
        Available,
        CheckedOut,
        InRepair,
        Missing,
        ReferenceOnly
    }

    public enum AuditOutcome
    {
        VerifiedPresent,
        Misplaced,
        Uncataloged,
        StatusConflict,
        DuplicateScan
    }

    public class CatalogItem
    {
        public string Barcode { get; set; }
        public string Isbn { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Classification { get; set; }
        public string Cutter { get; set; }
        public string ShelfLocation { get; set; }
        public int CopyNumber { get; set; }
        public int PublicationYear { get; set; }
        public ItemStatus Status { get; set; }
        public DateTime? LastAudited { get; set; }
        public string Notes { get; set; }

        public CatalogItem()
        {
            Barcode = "";
            Isbn = "";
            Title = "";
            Author = "";
            Classification = "";
            Cutter = "";
            ShelfLocation = "UNASSIGNED";
            CopyNumber = 1;
            PublicationYear = DateTime.Today.Year;
            Status = ItemStatus.Available;
            LastAudited = null;
            Notes = "";
        }

        public string FullCallNumber
        {
            get
            {
                string c = (Classification ?? "").Trim();
                string cut = (Cutter ?? "").Trim();
                if (string.IsNullOrEmpty(cut)) return c;
                if (string.IsNullOrEmpty(c)) return cut;
                return c + " " + cut;
            }
        }

        public string CanonicalIsbn13
        {
            get
            {
                return IsbnTools.ToCanonicalIsbn13(Isbn);
            }
        }

        public CatalogItem Clone()
        {
            return new CatalogItem
            {
                Barcode = this.Barcode,
                Isbn = this.Isbn,
                Title = this.Title,
                Author = this.Author,
                Classification = this.Classification,
                Cutter = this.Cutter,
                ShelfLocation = this.ShelfLocation,
                CopyNumber = this.CopyNumber,
                PublicationYear = this.PublicationYear,
                Status = this.Status,
                LastAudited = this.LastAudited,
                Notes = this.Notes
            };
        }

        public string ToTsvLine()
        {
            string dateStr = LastAudited.HasValue ? LastAudited.Value.ToString("yyyy-MM-dd HH:mm") : "";
            return string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}",
                EscapeTsv(Barcode),
                EscapeTsv(Isbn),
                EscapeTsv(Title),
                EscapeTsv(Author),
                EscapeTsv(Classification),
                EscapeTsv(Cutter),
                EscapeTsv(ShelfLocation),
                CopyNumber,
                PublicationYear,
                Status.ToString(),
                dateStr,
                EscapeTsv(Notes));
        }

        public static CatalogItem FromTsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            string[] p = line.Split('\t');
            if (p.Length < 7) return null;

            CatalogItem item = new CatalogItem();
            item.Barcode = UnescapeTsv(p[0]);
            item.Isbn = UnescapeTsv(p[1]);
            item.Title = UnescapeTsv(p[2]);
            item.Author = UnescapeTsv(p[3]);
            item.Classification = UnescapeTsv(p[4]);
            item.Cutter = UnescapeTsv(p[5]);
            item.ShelfLocation = UnescapeTsv(p[6]);

            int cp = 1;
            if (p.Length > 7 && int.TryParse(p[7], out cp)) item.CopyNumber = cp;

            int yr = 2000;
            if (p.Length > 8 && int.TryParse(p[8], out yr)) item.PublicationYear = yr;

            if (p.Length > 9)
            {
                try
                {
                    item.Status = (ItemStatus)Enum.Parse(typeof(ItemStatus), p[9], true);
                }
                catch
                {
                    item.Status = ItemStatus.Available;
                }
            }

            if (p.Length > 10 && !string.IsNullOrWhiteSpace(p[10]))
            {
                DateTime dt;
                if (DateTime.TryParse(p[10], out dt)) item.LastAudited = dt;
            }

            if (p.Length > 11)
            {
                item.Notes = UnescapeTsv(p[11]);
            }

            return item;
        }

        private static string EscapeTsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", "").Replace("\n", " ");
        }

        private static string UnescapeTsv(string s)
        {
            return (s ?? "").Trim();
        }
    }

    public class AuditEvent
    {
        public DateTime Timestamp { get; set; }
        public string ScannedBarcode { get; set; }
        public CatalogItem MatchedItem { get; set; }
        public AuditOutcome Outcome { get; set; }
        public bool OutOfSequence { get; set; }
        public string Message { get; set; }

        public AuditEvent()
        {
            Timestamp = DateTime.Now;
            ScannedBarcode = "";
            Outcome = AuditOutcome.Uncataloged;
            OutOfSequence = false;
            Message = "";
        }
    }

    public class DuplicatePair
    {
        public CatalogItem ItemA { get; set; }
        public CatalogItem ItemB { get; set; }
        public string MatchType { get; set; }
        public double SimilarityScore { get; set; }
        public string Reason { get; set; }
    }

    #endregion

    #region ISBN & Dewey Utilities

    public static class IsbnTools
    {
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            char[] arr = raw.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').Select(char.ToUpperInvariant).ToArray();
            return new string(arr);
        }

        public static bool IsValidIsbn10(string s)
        {
            string norm = Normalize(s);
            if (norm.Length != 10) return false;
            int sum = 0;
            for (int i = 0; i < 9; i++)
            {
                if (!char.IsDigit(norm[i])) return false;
                sum += (10 - i) * (norm[i] - '0');
            }
            int last = norm[9] == 'X' ? 10 : (char.IsDigit(norm[9]) ? norm[9] - '0' : -1);
            if (last < 0) return false;
            sum += last;
            return sum % 11 == 0;
        }

        public static bool IsValidIsbn13(string s)
        {
            string norm = Normalize(s);
            if (norm.Length != 13) return false;
            int sum = 0;
            for (int i = 0; i < 13; i++)
            {
                if (!char.IsDigit(norm[i])) return false;
                int weight = (i % 2 == 0) ? 1 : 3;
                sum += (norm[i] - '0') * weight;
            }
            return sum % 10 == 0;
        }

        public static string ConvertIsbn10To13(string isbn10)
        {
            string norm = Normalize(isbn10);
            if (norm.Length != 10) return norm;
            string prefix = "978" + norm.Substring(0, 9);
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int weight = (i % 2 == 0) ? 1 : 3;
                sum += (prefix[i] - '0') * weight;
            }
            int remainder = sum % 10;
            int check = (remainder == 0) ? 0 : 10 - remainder;
            return prefix + check.ToString();
        }

        public static string ToCanonicalIsbn13(string raw)
        {
            string norm = Normalize(raw);
            if (norm.Length == 13 && IsValidIsbn13(norm)) return norm;
            if (norm.Length == 10 && IsValidIsbn10(norm)) return ConvertIsbn10To13(norm);
            return norm;
        }

        public static string FormatHyphenated(string isbn)
        {
            string norm = Normalize(isbn);
            if (norm.Length == 13)
            {
                return string.Format("{0}-{1}-{2}-{3}-{4}",
                    norm.Substring(0, 3),
                    norm.Substring(3, 1),
                    norm.Substring(4, 4),
                    norm.Substring(8, 4),
                    norm.Substring(12, 1));
            }
            if (norm.Length == 10)
            {
                return string.Format("{0}-{1}-{2}-{3}",
                    norm.Substring(0, 1),
                    norm.Substring(1, 4),
                    norm.Substring(5, 4),
                    norm.Substring(9, 1));
            }
            return isbn ?? "";
        }
    }

    public static class DeweyCallComparer
    {
        public static int Compare(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
            if (string.IsNullOrWhiteSpace(a)) return -1;
            if (string.IsNullOrWhiteSpace(b)) return 1;

            string cleanA = (a ?? "").Trim();
            string cleanB = (b ?? "").Trim();

            string numPartA = ExtractClassification(cleanA);
            string numPartB = ExtractClassification(cleanB);

            double d1, d2;
            bool ok1 = double.TryParse(numPartA, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d1);
            bool ok2 = double.TryParse(numPartB, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d2);

            if (ok1 && ok2)
            {
                int comp = d1.CompareTo(d2);
                if (comp != 0) return comp;
            }
            else if (ok1)
            {
                return -1;
            }
            else if (ok2)
            {
                return 1;
            }

            return string.Compare(cleanA, cleanB, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractClassification(string s)
        {
            Match m = Regex.Match(s, @"^[0-9]+(\.[0-9]+)?");
            return m.Success ? m.Value : "";
        }
    }

    public static class StringSimilarity
    {
        public static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1])) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        public static double LevenshteinRatio(string s, string t)
        {
            if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(t)) return 1.0;
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0.0;
            int maxLen = Math.Max(s.Length, t.Length);
            int dist = LevenshteinDistance(s, t);
            return 1.0 - ((double)dist / maxLen);
        }

        public static double JaroWinkler(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            s1 = s1.ToLowerInvariant();
            s2 = s2.ToLowerInvariant();
            if (s1 == s2) return 1.0;

            int l1 = s1.Length;
            int l2 = s2.Length;
            int matchWindow = Math.Max(l1, l2) / 2 - 1;
            if (matchWindow < 0) matchWindow = 0;

            bool[] s1Matches = new bool[l1];
            bool[] s2Matches = new bool[l2];
            int matches = 0;

            for (int i = 0; i < l1; i++)
            {
                int start = Math.Max(0, i - matchWindow);
                int end = Math.Min(i + matchWindow + 1, l2);
                for (int j = start; j < end; j++)
                {
                    if (s2Matches[j]) continue;
                    if (s1[i] != s2[j]) continue;
                    s1Matches[i] = true;
                    s2Matches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            double transpositions = 0;
            int k = 0;
            for (int i = 0; i < l1; i++)
            {
                if (!s1Matches[i]) continue;
                while (!s2Matches[k]) k++;
                if (s1[i] != s2[k]) transpositions++;
                k++;
            }

            double m = matches;
            double jaro = ((m / l1) + (m / l2) + ((m - transpositions / 2.0) / m)) / 3.0;

            int prefix = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(l1, l2)); i++)
            {
                if (s1[i] == s2[i]) prefix++;
                else break;
            }

            return jaro + (prefix * 0.1 * (1.0 - jaro));
        }

        public static double TokenOverlapRatio(string s1, string s2)
        {
            string clean1 = Regex.Replace(s1 ?? "", @"[^\w\s]", " ").ToLowerInvariant();
            string clean2 = Regex.Replace(s2 ?? "", @"[^\w\s]", " ").ToLowerInvariant();

            HashSet<string> set1 = new HashSet<string>(clean1.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            HashSet<string> set2 = new HashSet<string>(clean2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            if (set1.Count == 0 && set2.Count == 0) return 1.0;
            if (set1.Count == 0 || set2.Count == 0) return 0.0;

            int intersect = set1.Count(w => set2.Contains(w));
            int union = set1.Union(set2).Count();
            int minCount = Math.Min(set1.Count, set2.Count);

            double jaccard = (double)intersect / union;
            double subset = (double)intersect / minCount;

            // Blend Jaccard overlap and subset inclusion (detects subtitle/edition extensions)
            return (jaccard * 0.40) + (subset * 0.60);
        }

        public static string SortTokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string clean = Regex.Replace(s, @"[^\w\s]", " ").ToLowerInvariant();
            var tokens = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(w => w);
            return string.Join(" ", tokens.ToArray());
        }

        public static double CombinedSimilarity(string titleA, string authorA, string titleB, string authorB)
        {
            // Title similarity: combination of character edit distance, Jaro-Winkler, and token overlap/subset
            double titleJw = JaroWinkler(titleA, titleB);
            double titleLev = LevenshteinRatio(titleA, titleB);
            double titleTokens = TokenOverlapRatio(titleA, titleB);

            // Also check token-sorted title similarity (handles inverted subtitles/articles)
            string sortedTitleA = SortTokens(titleA);
            string sortedTitleB = SortTokens(titleB);
            double sortedTitleJw = JaroWinkler(sortedTitleA, sortedTitleB);

            double titleScore = Math.Max(
                (titleJw * 0.40) + (titleLev * 0.30) + (titleTokens * 0.30),
                (sortedTitleJw * 0.35) + (titleTokens * 0.65)
            );

            // Author similarity: robust to "Last, First" vs "First Last"
            double authorScore = 1.0;
            if (!string.IsNullOrWhiteSpace(authorA) && !string.IsNullOrWhiteSpace(authorB))
            {
                string sortedAuthA = SortTokens(authorA);
                string sortedAuthB = SortTokens(authorB);
                double authSortedJw = JaroWinkler(sortedAuthA, sortedAuthB);
                double authTokens = TokenOverlapRatio(authorA, authorB);
                authorScore = (authSortedJw * 0.5) + (authTokens * 0.5);
            }

            return (titleScore * 0.70) + (authorScore * 0.30);
        }
    }

    #endregion

    #region Catalog Database & Seed Data

    public class CatalogDatabase
    {
        public List<CatalogItem> Items { get; private set; }
        public string StoragePath { get; private set; }

        public CatalogDatabase(string filePath)
        {
            StoragePath = filePath;
            Items = new List<CatalogItem>();
            Load();
        }

        public void Load()
        {
            Items.Clear();
            if (File.Exists(StoragePath))
            {
                string[] lines = File.ReadAllLines(StoragePath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    CatalogItem item = CatalogItem.FromTsvLine(line);
                    if (item != null && !string.IsNullOrEmpty(item.Barcode))
                    {
                        Items.Add(item);
                    }
                }
            }

            if (Items.Count == 0)
            {
                SeedDefaultCollection();
                Save();
            }
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<string> lines = new List<string>();
            lines.Add("# Barcode\tISBN\tTitle\tAuthor\tClassification\tCutter\tShelfLocation\tCopy\tYear\tStatus\tLastAudited\tNotes");
            foreach (var item in Items)
            {
                lines.Add(item.ToTsvLine());
            }
            File.WriteAllLines(StoragePath, lines.ToArray(), Encoding.UTF8);
        }

        public CatalogItem FindByBarcode(string barcode)
        {
            if (string.IsNullOrEmpty(barcode)) return null;
            return Items.FirstOrDefault(i => string.Equals(i.Barcode.Trim(), barcode.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public List<CatalogItem> FindByShelf(string shelf)
        {
            if (string.IsNullOrEmpty(shelf)) return new List<CatalogItem>();
            return Items.Where(i => string.Equals(i.ShelfLocation.Trim(), shelf.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public List<string> GetDistinctShelves()
        {
            return Items.Select(i => (i.ShelfLocation ?? "").Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s)
                        .ToList();
        }

        public List<CatalogItem> Search(string query, string shelfFilter, ItemStatus? statusFilter)
        {
            var q = Items.AsEnumerable();

            if (!string.IsNullOrEmpty(shelfFilter) && shelfFilter != "ALL")
            {
                q = q.Where(i => string.Equals(i.ShelfLocation, shelfFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (statusFilter.HasValue)
            {
                q = q.Where(i => i.Status == statusFilter.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                string term = query.Trim().ToLowerInvariant();
                q = q.Where(i =>
                    (i.Title ?? "").ToLowerInvariant().Contains(term) ||
                    (i.Author ?? "").ToLowerInvariant().Contains(term) ||
                    (i.Barcode ?? "").ToLowerInvariant().Contains(term) ||
                    (i.Isbn ?? "").ToLowerInvariant().Contains(term) ||
                    (i.Classification ?? "").ToLowerInvariant().Contains(term) ||
                    (i.Notes ?? "").ToLowerInvariant().Contains(term)
                );
            }

            return q.OrderBy(i => i.ShelfLocation).ThenBy(i => i.Classification, new CustomDeweyStringComparer()).ToList();
        }

        public class CustomDeweyStringComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                return DeweyCallComparer.Compare(x, y);
            }
        }

        private void SeedDefaultCollection()
        {
            // Seed 28 rich, realistic library holdings across 5 key stacks
            Items.AddRange(new[]
            {
                // STACK-A1-LIT: Classical & World Literature
                new CatalogItem { Barcode="LIB-001001", Isbn="978-0141439518", Title="Pride and Prejudice", Author="Austen, Jane", Classification="823.7", Cutter="A933p", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1813, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-14), Notes="Penguin Classics Edition" },
                new CatalogItem { Barcode="LIB-001002", Isbn="978-0141439518", Title="Pride and Prejudice", Author="Austen, Jane", Classification="823.7", Cutter="A933p", ShelfLocation="STACK-A1-LIT", CopyNumber=2, PublicationYear=1813, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-14), Notes="Copy 2 reserve" },
                new CatalogItem { Barcode="LIB-001003", Isbn="978-0451524935", Title="1984", Author="Orwell, George", Classification="823.912", Cutter="O79n", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1949, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-5), Notes="Signet Classic" },
                new CatalogItem { Barcode="LIB-001004", Isbn="978-0061120084", Title="To Kill a Mockingbird", Author="Lee, Harper", Classification="813.54", Cutter="L478t", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1960, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-3), Notes="Harper Perennial" },
                new CatalogItem { Barcode="LIB-001005", Isbn="978-0679720201", Title="The Stranger (L'Etranger)", Author="Camus, Albert", Classification="843.914", Cutter="C211e", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1942, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-12), Notes="Vintage International - English trans." },
                new CatalogItem { Barcode="LIB-001006", Isbn="978-0385420174", Title="Palace Walk (Cairo Trilogy)", Author="Mahfouz, Naguib", Classification="892.736", Cutter="M214p", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1956, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-12), Notes="Anchor Books Nobel Laureate Edition" },
                new CatalogItem { Barcode="LIB-001007", Isbn="978-0743273565", Title="The Great Gatsby", Author="Fitzgerald, F. Scott", Classification="813.52", Cutter="F553g", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=1925, Status=ItemStatus.CheckedOut, LastAudited=DateTime.Today.AddDays(-20), Notes="Borrowed by member M-1049" },

                // STACK-A2-LIT: Speculative Fiction, Fantasy & Sci-Fi
                new CatalogItem { Barcode="LIB-002001", Isbn="978-0547928227", Title="The Hobbit", Author="Tolkien, J.R.R.", Classification="823.912", Cutter="T649h", ShelfLocation="STACK-A2-LIT", CopyNumber=1, PublicationYear=1937, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-8), Notes="Mariner Books" },
                new CatalogItem { Barcode="LIB-002002", Isbn="978-0544003415", Title="The Fellowship of the Ring", Author="Tolkien, J.R.R.", Classification="823.912", Cutter="T649f", ShelfLocation="STACK-A2-LIT", CopyNumber=1, PublicationYear=1954, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-8), Notes="Lord of the Rings Vol 1" },
                new CatalogItem { Barcode="LIB-002003", Isbn="978-0441172719", Title="Dune", Author="Herbert, Frank", Classification="813.54", Cutter="H536d", ShelfLocation="STACK-A2-LIT", CopyNumber=1, PublicationYear=1965, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-8), Notes="Ace Books 40th Anniv" },
                new CatalogItem { Barcode="LIB-002004", Isbn="978-0553573404", Title="A Game of Thrones", Author="Martin, George R.R.", Classification="813.54", Cutter="M381g", ShelfLocation="STACK-A2-LIT", CopyNumber=1, PublicationYear=1996, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-8), Notes="Song of Ice and Fire Book 1" },

                // STACK-B1-CS: Computer Science, Algorithms & Software Engineering
                new CatalogItem { Barcode="LIB-003001", Isbn="978-0262033848", Title="Introduction to Algorithms (CLRS)", Author="Cormen, Thomas H.", Classification="005.1", Cutter="C811i", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=2009, Status=ItemStatus.ReferenceOnly, LastAudited=DateTime.Today.AddDays(-2), Notes="Reference desk only. 3rd Edition." },
                new CatalogItem { Barcode="LIB-003002", Isbn="978-0132350884", Title="Clean Code: A Handbook of Agile Software Craftsmanship", Author="Martin, Robert C.", Classification="005.13", Cutter="M382c", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=2008, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-2), Notes="Prentice Hall" },
                new CatalogItem { Barcode="LIB-003003", Isbn="978-0201616224", Title="The Pragmatic Programmer", Author="Hunt, Andrew", Classification="005.1", Cutter="H939p", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=1999, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-2), Notes="Addison-Wesley" },
                new CatalogItem { Barcode="LIB-003004", Isbn="978-0201896831", Title="The Art of Computer Programming, Vol 1", Author="Knuth, Donald E.", Classification="005.1", Cutter="K74a", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=1997, Status=ItemStatus.ReferenceOnly, LastAudited=DateTime.Today.AddDays(-10), Notes="Volume 1: Fundamental Algorithms" },
                new CatalogItem { Barcode="LIB-003005", Isbn="978-0596517748", Title="JavaScript: The Good Parts", Author="Crockford, Douglas", Classification="005.133", Cutter="C938j", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=2008, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-10), Notes="O'Reilly Media" },
                new CatalogItem { Barcode="LIB-003006", Isbn="978-0131103627", Title="The C Programming Language", Author="Kernighan, Brian W.", Classification="005.133", Cutter="K39c", ShelfLocation="STACK-B1-CS", CopyNumber=1, PublicationYear=1988, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-10), Notes="2nd Edition (ANSI C)" },

                // STACK-B2-SCI: Natural Sciences, Physics, Mathematics
                new CatalogItem { Barcode="LIB-004001", Isbn="978-0553380163", Title="A Brief History of Time", Author="Hawking, Stephen", Classification="523.1", Cutter="H393b", ShelfLocation="STACK-B2-SCI", CopyNumber=1, PublicationYear=1988, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-6), Notes="Bantam 10th Anniversary" },
                new CatalogItem { Barcode="LIB-004002", Isbn="978-0465024377", Title="Six Easy Pieces", Author="Feynman, Richard P.", Classification="530", Cutter="F435s", ShelfLocation="STACK-B2-SCI", CopyNumber=1, PublicationYear=1995, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-6), Notes="Basic Books" },
                new CatalogItem { Barcode="LIB-004003", Isbn="978-0199291151", Title="The Selfish Gene", Author="Dawkins, Richard", Classification="576.8", Cutter="D271s", ShelfLocation="STACK-B2-SCI", CopyNumber=1, PublicationYear=1976, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-6), Notes="Oxford Landmark Science" },
                new CatalogItem { Barcode="LIB-004004", Isbn="978-0394716725", Title="Cosmos", Author="Sagan, Carl", Classification="520", Cutter="S129c", ShelfLocation="STACK-B2-SCI", CopyNumber=1, PublicationYear=1980, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-6), Notes="Random House" },

                // STACK-C1-HIST: History, Philosophy, Civilization
                new CatalogItem { Barcode="LIB-005001", Isbn="978-0691166377", Title="The Muqaddimah: An Introduction to History", Author="Ibn Khaldun", Classification="901", Cutter="I13m", ShelfLocation="STACK-C1-HIST", CopyNumber=1, PublicationYear=1377, Status=ItemStatus.ReferenceOnly, LastAudited=DateTime.Today.AddDays(-15), Notes="Princeton Classic translated by Franz Rosenthal" },
                new CatalogItem { Barcode="LIB-005002", Isbn="978-0062316097", Title="Sapiens: A Brief History of Humankind", Author="Harari, Yuval Noah", Classification="909", Cutter="H254s", ShelfLocation="STACK-C1-HIST", CopyNumber=1, PublicationYear=2014, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-15), Notes="Harper" },
                new CatalogItem { Barcode="LIB-005003", Isbn="978-0140449136", Title="The Republic", Author="Plato", Classification="321.07", Cutter="P718r", ShelfLocation="STACK-C1-HIST", CopyNumber=1, PublicationYear=-375, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-15), Notes="Penguin Classics (Desmond Lee translation)" },
                new CatalogItem { Barcode="LIB-005004", Isbn="978-0140449495", Title="Meditations", Author="Marcus Aurelius", Classification="188", Cutter="M322m", ShelfLocation="STACK-C1-HIST", CopyNumber=1, PublicationYear=180, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-15), Notes="Penguin Classics" },

                // Purposeful test items for audit & deduplication testing:
                // Variant edition with different ISBN to demonstrate fuzzy deduplication:
                new CatalogItem { Barcode="LIB-001008", Isbn="978-0062420701", Title="To Kill a Mockingbird (50th Anniversary Edition)", Author="Harper Lee", Classification="813.54", Cutter="L478t", ShelfLocation="STACK-A1-LIT", CopyNumber=2, PublicationYear=2010, Status=ItemStatus.Available, LastAudited=DateTime.Today.AddDays(-3), Notes="Special commemorative edition - different ISBN" },
                // Intentional duplicate title variant of 'Clean Code':
                new CatalogItem { Barcode="LIB-003099", Isbn="978-0132350884", Title="Clean Code: A Handbook of Agile Software Craftsmanship (Copy 2)", Author="Robert C. Martin", Classification="005.13", Cutter="M382c", ShelfLocation="STACK-B1-CS", CopyNumber=2, PublicationYear=2008, Status=ItemStatus.Available, LastAudited=null, Notes="Suspected duplicate record" },
                // Book currently flagged Missing:
                new CatalogItem { Barcode="LIB-001099", Isbn="978-0140449112", Title="The Odyssey", Author="Homer", Classification="883.01", Cutter="H766o", ShelfLocation="STACK-A1-LIT", CopyNumber=1, PublicationYear=-800, Status=ItemStatus.Missing, LastAudited=DateTime.Today.AddDays(-45), Notes="Reported missing during autumn audit" }
            });
        }
    }

    #endregion

    #region Shelf Audit Session Engine

    public class ShelfAuditSession
    {
        public string TargetShelf { get; private set; }
        public DateTime StartTime { get; private set; }
        public List<AuditEvent> Events { get; private set; }
        public HashSet<string> ScannedBarcodes { get; private set; }
        public CatalogDatabase Database { get; private set; }

        public ShelfAuditSession(string targetShelf, CatalogDatabase db)
        {
            TargetShelf = targetShelf;
            Database = db;
            StartTime = DateTime.Now;
            Events = new List<AuditEvent>();
            ScannedBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<CatalogItem> GetExpectedItems()
        {
            if (string.Equals(TargetShelf, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return Database.Items.OrderBy(i => i.ShelfLocation).ThenBy(i => i.Classification, new CatalogDatabase.CustomDeweyStringComparer()).ToList();
            }
            return Database.FindByShelf(TargetShelf).OrderBy(i => i.Classification, new CatalogDatabase.CustomDeweyStringComparer()).ToList();
        }

        public AuditEvent ProcessBarcodeScan(string rawBarcode)
        {
            string barcode = (rawBarcode ?? "").Trim();
            AuditEvent ev = new AuditEvent
            {
                Timestamp = DateTime.Now,
                ScannedBarcode = barcode
            };

            if (string.IsNullOrEmpty(barcode))
            {
                ev.Outcome = AuditOutcome.Uncataloged;
                ev.Message = "Empty barcode submitted.";
                Events.Add(ev);
                return ev;
            }

            if (ScannedBarcodes.Contains(barcode))
            {
                ev.Outcome = AuditOutcome.DuplicateScan;
                ev.MatchedItem = Database.FindByBarcode(barcode);
                ev.Message = string.Format("Item '{0}' was already scanned in this audit session.", barcode);
                Events.Add(ev);
                return ev;
            }

            ScannedBarcodes.Add(barcode);
            CatalogItem item = Database.FindByBarcode(barcode);
            ev.MatchedItem = item;

            if (item == null)
            {
                ev.Outcome = AuditOutcome.Uncataloged;
                ev.Message = string.Format("Barcode '{0}' is not present in the master catalog (Uncataloged Ghost Item).", barcode);
                Events.Add(ev);
                return ev;
            }

            bool shelfMatches = string.Equals(TargetShelf, "ALL", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.ShelfLocation, TargetShelf, StringComparison.OrdinalIgnoreCase);

            if (!shelfMatches)
            {
                ev.Outcome = AuditOutcome.Misplaced;
                ev.Message = string.Format("MISPLACED: '{0}' belongs to shelf '{1}', but was scanned on '{2}'!",
                    item.Title, item.ShelfLocation, TargetShelf);
            }
            else if (item.Status == ItemStatus.CheckedOut || item.Status == ItemStatus.Missing)
            {
                ev.Outcome = AuditOutcome.StatusConflict;
                ev.Message = string.Format("STATUS CONFLICT: '{0}' is marked '{1}' in database, but is physically on shelf!",
                    item.Title, item.Status);
            }
            else
            {
                ev.Outcome = AuditOutcome.VerifiedPresent;
                ev.Message = string.Format("VERIFIED: '{0}' is present on shelf '{1}'.", item.Title, item.ShelfLocation);
            }

            // Check shelf sequence (Dewey sequence order compared to previous verified item)
            var prevVerified = Events.LastOrDefault(e => e.Outcome == AuditOutcome.VerifiedPresent && e.MatchedItem != null);
            if (prevVerified != null && item != null && !string.IsNullOrEmpty(prevVerified.MatchedItem.Classification) && !string.IsNullOrEmpty(item.Classification))
            {
                int order = DeweyCallComparer.Compare(prevVerified.MatchedItem.Classification, item.Classification);
                if (order > 0)
                {
                    ev.OutOfSequence = true;
                    ev.Message += string.Format(" [WARNING: Out of Dewey sequence after '{0}' ({1} > {2})]",
                        prevVerified.MatchedItem.Title, prevVerified.MatchedItem.Classification, item.Classification);
                }
            }

            Events.Add(ev);
            return ev;
        }

        public int ReconcileCatalog(bool moveMisplaced, bool clearMissingAndCheckedOut, bool flagUnscannedAsMissing)
        {
            int updatedCount = 0;
            DateTime now = DateTime.Now;

            // 1. Process scanned events
            foreach (var ev in Events)
            {
                if (ev.MatchedItem == null) continue;
                CatalogItem dbItem = Database.FindByBarcode(ev.MatchedItem.Barcode);
                if (dbItem == null) continue;

                bool modified = false;

                // Update last audited
                dbItem.LastAudited = now;
                modified = true;

                if (ev.Outcome == AuditOutcome.Misplaced && moveMisplaced && !string.Equals(TargetShelf, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    dbItem.Notes = (dbItem.Notes + string.Format(" [Reconciled: moved from {0} to {1} on {2:yyyy-MM-dd}]", dbItem.ShelfLocation, TargetShelf, now)).Trim();
                    dbItem.ShelfLocation = TargetShelf;
                    modified = true;
                }

                if (ev.Outcome == AuditOutcome.StatusConflict && clearMissingAndCheckedOut)
                {
                    dbItem.Notes = (dbItem.Notes + string.Format(" [Reconciled: cleared {0} status on {1:yyyy-MM-dd}]", dbItem.Status, now)).Trim();
                    dbItem.Status = ItemStatus.Available;
                    modified = true;
                }

                if (modified) updatedCount++;
            }

            // 2. Process unscanned items on the target shelf
            if (flagUnscannedAsMissing && !string.Equals(TargetShelf, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                List<CatalogItem> expected = Database.FindByShelf(TargetShelf);
                foreach (var exp in expected)
                {
                    if (!ScannedBarcodes.Contains(exp.Barcode) && exp.Status != ItemStatus.CheckedOut && exp.Status != ItemStatus.Missing)
                    {
                        exp.Status = ItemStatus.Missing;
                        exp.Notes = (exp.Notes + string.Format(" [Flagged Missing during audit of {0} on {1:yyyy-MM-dd}]", TargetShelf, now)).Trim();
                        updatedCount++;
                    }
                }
            }

            if (updatedCount > 0)
            {
                Database.Save();
            }

            return updatedCount;
        }

        public List<CatalogItem> GetMissingItems()
        {
            List<CatalogItem> expected = GetExpectedItems();
            return expected.Where(i => !ScannedBarcodes.Contains(i.Barcode)).ToList();
        }
    }

    #endregion

    #region Duplicate & Variant Detection Engine

    public class DuplicateDetectionEngine
    {
        public static List<DuplicatePair> ScanDuplicates(CatalogDatabase db, double fuzzyThreshold, bool checkIsbn, bool checkExact, bool checkFuzzy, bool checkBarcodeCollision)
        {
            List<DuplicatePair> results = new List<DuplicatePair>();
            var items = db.Items;
            int count = items.Count;

            // 1. Check Barcode collisions (should never happen if database is valid, but critical to catch)
            if (checkBarcodeCollision)
            {
                var barcodeGroups = items.GroupBy(i => (i.Barcode ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                                         .Where(g => g.Count() > 1 && !string.IsNullOrEmpty(g.Key));
                foreach (var g in barcodeGroups)
                {
                    var list = g.ToList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            results.Add(new DuplicatePair
                            {
                                ItemA = list[i],
                                ItemB = list[j],
                                MatchType = "Barcode Collision",
                                SimilarityScore = 1.0,
                                Reason = string.Format("Both items share identical barcode '{0}'!", list[i].Barcode)
                            });
                        }
                    }
                }
            }

            // 2. Check ISBN Match (Normalized ISBN-13 equivalence)
            if (checkIsbn)
            {
                var isbnGroups = items.Where(i => !string.IsNullOrEmpty(i.CanonicalIsbn13))
                                      .GroupBy(i => i.CanonicalIsbn13, StringComparer.OrdinalIgnoreCase)
                                      .Where(g => g.Count() > 1);
                foreach (var g in isbnGroups)
                {
                    var list = g.ToList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            // Don't report if same barcode already reported
                            if (results.Any(r => r.ItemA.Barcode == list[i].Barcode && r.ItemB.Barcode == list[j].Barcode)) continue;

                            results.Add(new DuplicatePair
                            {
                                ItemA = list[i],
                                ItemB = list[j],
                                MatchType = "ISBN Match",
                                SimilarityScore = 1.0,
                                Reason = string.Format("Normalized ISBN-13 matches ({0}). Copies: #{1} on {2} vs #{3} on {4}",
                                    list[i].CanonicalIsbn13, list[i].CopyNumber, list[i].ShelfLocation, list[j].CopyNumber, list[j].ShelfLocation)
                            });
                        }
                    }
                }
            }

            // 3. Exact Title + Author Match
            if (checkExact)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        var a = items[i];
                        var b = items[j];
                        if (results.Any(r => (r.ItemA.Barcode == a.Barcode && r.ItemB.Barcode == b.Barcode) ||
                                             (r.ItemA.Barcode == b.Barcode && r.ItemB.Barcode == a.Barcode)))
                            continue;

                        bool sameTitle = string.Equals((a.Title ?? "").Trim(), (b.Title ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                        bool sameAuthor = string.Equals((a.Author ?? "").Trim(), (b.Author ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

                        if (sameTitle && sameAuthor)
                        {
                            results.Add(new DuplicatePair
                            {
                                ItemA = a,
                                ItemB = b,
                                MatchType = "Exact Title & Author",
                                SimilarityScore = 1.0,
                                Reason = string.Format("Identical title and author. Copies: #{0} on {1} vs #{2} on {3}",
                                    a.CopyNumber, a.ShelfLocation, b.CopyNumber, b.ShelfLocation)
                            });
                        }
                    }
                }
            }

            // 4. Fuzzy Title & Author Match
            if (checkFuzzy)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        var a = items[i];
                        var b = items[j];

                        if (results.Any(r => (r.ItemA.Barcode == a.Barcode && r.ItemB.Barcode == b.Barcode) ||
                                             (r.ItemA.Barcode == b.Barcode && r.ItemB.Barcode == a.Barcode)))
                            continue;

                        double score = StringSimilarity.CombinedSimilarity(a.Title, a.Author, b.Title, b.Author);
                        if (score >= fuzzyThreshold)
                        {
                            results.Add(new DuplicatePair
                            {
                                ItemA = a,
                                ItemB = b,
                                MatchType = "Fuzzy Variant",
                                SimilarityScore = Math.Round(score, 3),
                                Reason = string.Format("High textual similarity ({0:P0}): '{1}' by '{2}' ~ '{3}' by '{4}'",
                                    score, a.Title, a.Author, b.Title, b.Author)
                            });
                        }
                    }
                }
            }

            return results.OrderByDescending(r => r.SimilarityScore).ToList();
        }
    }

    #endregion

    #region Report Generator

    public static class ReportExporter
    {
        public static string GenerateHtmlReport(CatalogDatabase db, ShelfAuditSession audit, string title)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
            sb.AppendLine("  <title>" + HtmlEscape(title) + "</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    :root { --primary: #1e3a8a; --secondary: #0284c7; --bg: #f8fafc; --card: #ffffff; --text: #0f172a; --border: #e2e8f0; --success: #16a34a; --warning: #d97706; --danger: #dc2626; }");
            sb.AppendLine("    * { box-sizing: border-box; }");
            sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: var(--bg); color: var(--text); margin: 0; padding: 24px; }");
            sb.AppendLine("    .container { max-width: 1200px; margin: 0 auto; }");
            sb.AppendLine("    .header { background: linear-gradient(135deg, #1e3a8a, #0369a1); color: white; padding: 24px 32px; border-radius: 12px; margin-bottom: 24px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }");
            sb.AppendLine("    .header h1 { margin: 0 0 8px 0; font-size: 26px; }");
            sb.AppendLine("    .header p { margin: 0; opacity: 0.9; font-size: 14px; }");
            sb.AppendLine("    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 16px; margin-bottom: 24px; }");
            sb.AppendLine("    .kpi-card { background: var(--card); padding: 18px; border-radius: 10px; border: 1px solid var(--border); box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
            sb.AppendLine("    .kpi-title { font-size: 12px; text-transform: uppercase; color: #64748b; font-weight: 600; margin-bottom: 6px; }");
            sb.AppendLine("    .kpi-value { font-size: 28px; font-weight: 700; color: var(--text); }");
            sb.AppendLine("    .card { background: var(--card); border-radius: 10px; border: 1px solid var(--border); margin-bottom: 24px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
            sb.AppendLine("    .card-title { background: #f1f5f9; padding: 14px 20px; font-size: 16px; font-weight: 600; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; text-align: left; font-size: 13px; }");
            sb.AppendLine("    th { background: #f8fafc; padding: 10px 14px; font-weight: 600; border-bottom: 1px solid var(--border); color: #475569; }");
            sb.AppendLine("    td { padding: 10px 14px; border-bottom: 1px solid var(--border); vertical-align: middle; }");
            sb.AppendLine("    tr:last-child td { border-bottom: none; }");
            sb.AppendLine("    tr:hover { background-color: #f8fafc; }");
            sb.AppendLine("    .badge { display: inline-block; padding: 3px 8px; border-radius: 9999px; font-size: 11px; font-weight: 600; text-transform: uppercase; }");
            sb.AppendLine("    .badge-success { background: #dcfce7; color: #15803d; }");
            sb.AppendLine("    .badge-warning { background: #fef3c7; color: #b45309; }");
            sb.AppendLine("    .badge-danger { background: #fee2e2; color: #b91c1c; }");
            sb.AppendLine("    .badge-info { background: #e0f2fe; color: #0369a1; }");
            sb.AppendLine("    .badge-secondary { background: #f1f5f9; color: #475569; }");
            sb.AppendLine("    .btn { display: inline-block; background: #0284c7; color: white; padding: 8px 16px; border-radius: 6px; text-decoration: none; font-weight: 500; font-size: 13px; cursor: pointer; border: none; }");
            sb.AppendLine("    .btn:hover { background: #0369a1; }");
            sb.AppendLine("    @media print { body { background: white; padding: 0; } .header { background: #1e3a8a !important; -webkit-print-color-adjust: exact; } .no-print { display: none !important; } }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");

            // Header
            sb.AppendLine("  <div class=\"header\">");
            sb.AppendLine("    <div style=\"float: right;\" class=\"no-print\"><button class=\"btn\" onclick=\"window.print()\">Print / Save PDF</button></div>");
            sb.AppendLine("    <h1>" + HtmlEscape(title) + "</h1>");
            sb.AppendLine("    <p>Generated by <strong>CatalogAuditDesk</strong> &bull; " + DateTime.Now.ToString("dddd, MMMM dd, yyyy HH:mm") + " &bull; Database: " + HtmlEscape(Path.GetFileName(db.StoragePath)) + "</p>");
            sb.AppendLine("  </div>");

            // KPI Grid
            int totalItems = db.Items.Count;
            int distinctTitles = db.Items.Select(i => (i.Title ?? "").Trim().ToLowerInvariant()).Distinct().Count();
            int shelvesCount = db.GetDistinctShelves().Count;
            int missingCount = db.Items.Count(i => i.Status == ItemStatus.Missing);
            int auditedRecently = db.Items.Count(i => i.LastAudited.HasValue && i.LastAudited.Value >= DateTime.Today.AddDays(-7));

            sb.AppendLine("  <div class=\"kpi-grid\">");
            sb.AppendLine(string.Format("    <div class=\"kpi-card\"><div class=\"kpi-title\">Total Physical Copies</div><div class=\"kpi-value\">{0}</div></div>", totalItems));
            sb.AppendLine(string.Format("    <div class=\"kpi-card\"><div class=\"kpi-title\">Distinct Titles</div><div class=\"kpi-value\">{0}</div></div>", distinctTitles));
            sb.AppendLine(string.Format("    <div class=\"kpi-card\"><div class=\"kpi-title\">Shelves Monitored</div><div class=\"kpi-value\">{0}</div></div>", shelvesCount));
            sb.AppendLine(string.Format("    <div class=\"kpi-card\"><div class=\"kpi-title\">Audited in Last 7d</div><div class=\"kpi-value\" style=\"color: #16a34a;\">{0}</div></div>", auditedRecently));
            sb.AppendLine(string.Format("    <div class=\"kpi-card\"><div class=\"kpi-title\">Missing Items</div><div class=\"kpi-value\" style=\"color: #dc2626;\">{0}</div></div>", missingCount));
            sb.AppendLine("  </div>");

            // If an audit session is attached, show audit discrepancy summary
            if (audit != null)
            {
                int verified = audit.Events.Count(e => e.Outcome == AuditOutcome.VerifiedPresent);
                int misplaced = audit.Events.Count(e => e.Outcome == AuditOutcome.Misplaced);
                int uncataloged = audit.Events.Count(e => e.Outcome == AuditOutcome.Uncataloged);
                int statusConflicts = audit.Events.Count(e => e.Outcome == AuditOutcome.StatusConflict);
                var missingList = audit.GetMissingItems();

                sb.AppendLine("  <div class=\"card\">");
                sb.AppendLine("    <div class=\"card-title\"><span>Shelf Inventory Audit Results: " + HtmlEscape(audit.TargetShelf) + "</span><span class=\"badge badge-info\">" + audit.Events.Count + " Scans Processed</span></div>");
                sb.AppendLine("    <div style=\"padding: 16px; display: flex; gap: 24px; flex-wrap: wrap;\">");
                sb.AppendLine(string.Format("      <div><strong>Verified on Shelf:</strong> <span class=\"badge badge-success\">{0}</span></div>", verified));
                sb.AppendLine(string.Format("      <div><strong>Misplaced from Other Stacks:</strong> <span class=\"badge badge-warning\">{0}</span></div>", misplaced));
                sb.AppendLine(string.Format("      <div><strong>Uncataloged / Ghost:</strong> <span class=\"badge badge-danger\">{0}</span></div>", uncataloged));
                sb.AppendLine(string.Format("      <div><strong>Status Conflicts:</strong> <span class=\"badge badge-warning\">{0}</span></div>", statusConflicts));
                sb.AppendLine(string.Format("      <div><strong>Missing / Unscanned:</strong> <span class=\"badge badge-danger\">{0}</span></div>", missingList.Count));
                sb.AppendLine("    </div>");

                if (misplaced > 0 || uncataloged > 0 || statusConflicts > 0 || missingList.Count > 0)
                {
                    sb.AppendLine("    <table>");
                    sb.AppendLine("      <thead><tr><th>Event</th><th>Barcode</th><th>Title / Expected</th><th>Call Number</th><th>Registered Shelf</th><th>Discrepancy Notes</th></tr></thead>");
                    sb.AppendLine("      <tbody>");

                    // Misplaced / conflicts / uncataloged
                    foreach (var ev in audit.Events.Where(e => e.Outcome != AuditOutcome.VerifiedPresent))
                    {
                        string badgeClass = ev.Outcome == AuditOutcome.Uncataloged ? "badge-danger" : "badge-warning";
                        string titleStr = ev.MatchedItem != null ? ev.MatchedItem.Title : "(Uncataloged Item)";
                        string callStr = ev.MatchedItem != null ? ev.MatchedItem.FullCallNumber : "-";
                        string regShelf = ev.MatchedItem != null ? ev.MatchedItem.ShelfLocation : "-";

                        sb.AppendLine("        <tr>");
                        sb.AppendLine(string.Format("          <td><span class=\"badge {0}\">{1}</span></td>", badgeClass, ev.Outcome));
                        sb.AppendLine(string.Format("          <td><code>{0}</code></td>", HtmlEscape(ev.ScannedBarcode)));
                        sb.AppendLine(string.Format("          <td><strong>{0}</strong></td>", HtmlEscape(titleStr)));
                        sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(callStr)));
                        sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(regShelf)));
                        sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(ev.Message)));
                        sb.AppendLine("        </tr>");
                    }

                    // Missing items
                    foreach (var m in missingList)
                    {
                        sb.AppendLine("        <tr>");
                        sb.AppendLine("          <td><span class=\"badge badge-danger\">MISSING / NOT SCANNED</span></td>");
                        sb.AppendLine(string.Format("          <td><code>{0}</code></td>", HtmlEscape(m.Barcode)));
                        sb.AppendLine(string.Format("          <td><strong>{0}</strong> ({1})</td>", HtmlEscape(m.Title), HtmlEscape(m.Author)));
                        sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(m.FullCallNumber)));
                        sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(m.ShelfLocation)));
                        sb.AppendLine("          <td>Expected on this shelf, but was not detected in physical scan pass.</td>");
                        sb.AppendLine("        </tr>");
                    }

                    sb.AppendLine("      </tbody>");
                    sb.AppendLine("    </table>");
                }
                sb.AppendLine("  </div>");
            }

            // Catalog Table
            sb.AppendLine("  <div class=\"card\">");
            sb.AppendLine(string.Format("    <div class=\"card-title\"><span>Master Catalog Holdings ({0} items)</span></div>", db.Items.Count));
            sb.AppendLine("    <table>");
            sb.AppendLine("      <thead><tr><th>Barcode</th><th>Title</th><th>Author</th><th>Call #</th><th>Shelf</th><th>Copy</th><th>Status</th><th>Last Audited</th></tr></thead>");
            sb.AppendLine("      <tbody>");

            foreach (var item in db.Items.OrderBy(i => i.ShelfLocation).ThenBy(i => i.Classification, new CatalogDatabase.CustomDeweyStringComparer()))
            {
                string statusBadge = "badge-secondary";
                if (item.Status == ItemStatus.Available) statusBadge = "badge-success";
                else if (item.Status == ItemStatus.CheckedOut) statusBadge = "badge-warning";
                else if (item.Status == ItemStatus.Missing) statusBadge = "badge-danger";
                else if (item.Status == ItemStatus.ReferenceOnly) statusBadge = "badge-info";

                string auditStr = item.LastAudited.HasValue ? item.LastAudited.Value.ToString("yyyy-MM-dd") : "<span style=\"color:#94a3b8;\">Never</span>";

                sb.AppendLine("        <tr>");
                sb.AppendLine(string.Format("          <td><code>{0}</code></td>", HtmlEscape(item.Barcode)));
                sb.AppendLine(string.Format("          <td><strong>{0}</strong><br/><small style=\"color:#64748b;\">ISBN: {1}</small></td>", HtmlEscape(item.Title), HtmlEscape(item.Isbn)));
                sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(item.Author)));
                sb.AppendLine(string.Format("          <td>{0}</td>", HtmlEscape(item.FullCallNumber)));
                sb.AppendLine(string.Format("          <td><span class=\"badge badge-secondary\">{0}</span></td>", HtmlEscape(item.ShelfLocation)));
                sb.AppendLine(string.Format("          <td>c.{0}</td>", item.CopyNumber));
                sb.AppendLine(string.Format("          <td><span class=\"badge {0}\">{1}</span></td>", statusBadge, item.Status));
                sb.AppendLine(string.Format("          <td>{0}</td>", auditStr));
                sb.AppendLine("        </tr>");
            }

            sb.AppendLine("      </tbody>");
            sb.AppendLine("    </table>");
            sb.AppendLine("  </div>");

            sb.AppendLine("  <div style=\"text-align: center; color: #94a3b8; font-size: 12px; margin-top: 24px;\">");
            sb.AppendLine("    CatalogAuditDesk &bull; Library & Copy-Shop Tools Suite");
            sb.AppendLine("  </div>");

            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        public static string GenerateCsv(CatalogDatabase db)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Barcode,ISBN,NormalizedISBN13,Title,Author,Classification,Cutter,CallNumber,ShelfLocation,CopyNumber,Year,Status,LastAudited,Notes");
            foreach (var item in db.Items)
            {
                string auditStr = item.LastAudited.HasValue ? item.LastAudited.Value.ToString("yyyy-MM-dd HH:mm") : "";
                sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",{9},{10},\"{11}\",\"{12}\",\"{13}\"",
                    CsvEscape(item.Barcode),
                    CsvEscape(item.Isbn),
                    CsvEscape(item.CanonicalIsbn13),
                    CsvEscape(item.Title),
                    CsvEscape(item.Author),
                    CsvEscape(item.Classification),
                    CsvEscape(item.Cutter),
                    CsvEscape(item.FullCallNumber),
                    CsvEscape(item.ShelfLocation),
                    item.CopyNumber,
                    item.PublicationYear,
                    item.Status,
                    auditStr,
                    CsvEscape(item.Notes)));
            }
            return sb.ToString();
        }

        public static string GenerateMarkdownAudit(ShelfAuditSession audit)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("# Shelf Audit Discrepancy Report: `{0}`", audit.TargetShelf));
            sb.AppendLine();
            sb.AppendLine(string.Format("- **Audit Date**: {0:yyyy-MM-dd HH:mm}", audit.StartTime));
            sb.AppendLine(string.Format("- **Target Shelf**: `{0}`", audit.TargetShelf));
            sb.AppendLine(string.Format("- **Total Expected Books**: {0}", audit.GetExpectedItems().Count));
            sb.AppendLine(string.Format("- **Total Scans Recorded**: {0}", audit.Events.Count));
            sb.AppendLine(string.Format("- **Verified Present**: {0}", audit.Events.Count(e => e.Outcome == AuditOutcome.VerifiedPresent)));
            sb.AppendLine(string.Format("- **Misplaced from Other Stacks**: {0}", audit.Events.Count(e => e.Outcome == AuditOutcome.Misplaced)));
            sb.AppendLine(string.Format("- **Uncataloged / Ghost Barcodes**: {0}", audit.Events.Count(e => e.Outcome == AuditOutcome.Uncataloged)));
            sb.AppendLine(string.Format("- **Status Conflicts (CheckedOut/Missing on shelf)**: {0}", audit.Events.Count(e => e.Outcome == AuditOutcome.StatusConflict)));
            sb.AppendLine(string.Format("- **Unscanned / Missing from Shelf**: {0}", audit.GetMissingItems().Count));
            sb.AppendLine();

            var discrepancies = audit.Events.Where(e => e.Outcome != AuditOutcome.VerifiedPresent).ToList();
            if (discrepancies.Count > 0)
            {
                sb.AppendLine("## Scanned Discrepancies");
                sb.AppendLine();
                sb.AppendLine("| Outcome | Barcode | Title | Registered Shelf | Note |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var ev in discrepancies)
                {
                    string t = ev.MatchedItem != null ? ev.MatchedItem.Title : "(Uncataloged)";
                    string s = ev.MatchedItem != null ? ev.MatchedItem.ShelfLocation : "-";
                    sb.AppendLine(string.Format("| **{0}** | `{1}` | {2} | `{3}` | {4} |",
                        ev.Outcome, ev.ScannedBarcode, t, s, ev.Message));
                }
                sb.AppendLine();
            }

            var missing = audit.GetMissingItems();
            if (missing.Count > 0)
            {
                sb.AppendLine("## Missing Items (Expected on Shelf but NOT Scanned)");
                sb.AppendLine();
                sb.AppendLine("| Barcode | Call Number | Title | Author | Copy |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var m in missing)
                {
                    sb.AppendLine(string.Format("| `{0}` | `{1}` | **{2}** | {3} | c.{4} |",
                        m.Barcode, m.FullCallNumber, m.Title, m.Author, m.CopyNumber));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\"", "\"\"");
        }
    }

    #endregion

    #region GUI Dialogs & Forms

    public class ItemEditDialog : Form
    {
        public CatalogItem ResultItem { get; private set; }

        private TextBox txtBarcode;
        private TextBox txtIsbn;
        private TextBox txtTitle;
        private TextBox txtAuthor;
        private TextBox txtClass;
        private TextBox txtCutter;
        private TextBox txtShelf;
        private NumericUpDown numCopy;
        private NumericUpDown numYear;
        private ComboBox cmbStatus;
        private TextBox txtNotes;

        public ItemEditDialog(CatalogItem existing, List<string> existingShelves)
        {
            this.Text = existing == null ? "Add New Catalog Item" : "Edit Catalog Item - " + existing.Barcode;
            this.Size = new Size(500, 480);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Segoe UI", 9.5f);

            InitializeControls(existing, existingShelves);
        }

        private void InitializeControls(CatalogItem item, List<string> existingShelves)
        {
            TableLayoutPanel tlp = new TableLayoutPanel();
            tlp.Dock = DockStyle.Fill;
            tlp.Padding = new Padding(16);
            tlp.ColumnCount = 2;
            tlp.RowCount = 11;
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int r = 0;
            txtBarcode = AddRow(tlp, "Barcode ID:", r++, item != null ? item.Barcode : "LIB-" + new Random().Next(100000, 999999));
            txtIsbn = AddRow(tlp, "ISBN-10 / 13:", r++, item != null ? item.Isbn : "");
            txtTitle = AddRow(tlp, "Book Title *:", r++, item != null ? item.Title : "");
            txtAuthor = AddRow(tlp, "Author(s) *:", r++, item != null ? item.Author : "");
            txtClass = AddRow(tlp, "Dewey Class:", r++, item != null ? item.Classification : "000");
            txtCutter = AddRow(tlp, "Cutter / Mark:", r++, item != null ? item.Cutter : "");
            txtShelf = AddRow(tlp, "Shelf Location:", r++, item != null ? item.ShelfLocation : (existingShelves.FirstOrDefault() ?? "STACK-A1-LIT"));

            tlp.Controls.Add(new Label { Text = "Copy Number:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
            numCopy = new NumericUpDown { Minimum = 1, Maximum = 99, Value = item != null ? item.CopyNumber : 1, Dock = DockStyle.Fill };
            tlp.Controls.Add(numCopy, 1, r++);

            tlp.Controls.Add(new Label { Text = "Publication Year:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
            numYear = new NumericUpDown { Minimum = -1000, Maximum = 2100, Value = item != null ? item.PublicationYear : 2024, Dock = DockStyle.Fill };
            tlp.Controls.Add(numYear, 1, r++);

            tlp.Controls.Add(new Label { Text = "Circulation Status:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
            cmbStatus = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStatus.Items.AddRange(Enum.GetNames(typeof(ItemStatus)));
            cmbStatus.SelectedItem = item != null ? item.Status.ToString() : ItemStatus.Available.ToString();
            tlp.Controls.Add(cmbStatus, 1, r++);

            txtNotes = AddRow(tlp, "Librarian Notes:", r++, item != null ? item.Notes : "");

            Panel btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
            Button btnSave = new Button { Text = "Save Item", DialogResult = DialogResult.OK, Width = 110, Height = 32, Dock = DockStyle.Right, BackColor = Color.FromArgb(2, 132, 199), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 32, Dock = DockStyle.Right };

            btnSave.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtBarcode.Text) || string.IsNullOrWhiteSpace(txtTitle.Text))
                {
                    MessageBox.Show("Barcode and Title are mandatory fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }

                ResultItem = new CatalogItem
                {
                    Barcode = txtBarcode.Text.Trim(),
                    Isbn = txtIsbn.Text.Trim(),
                    Title = txtTitle.Text.Trim(),
                    Author = txtAuthor.Text.Trim(),
                    Classification = txtClass.Text.Trim(),
                    Cutter = txtCutter.Text.Trim(),
                    ShelfLocation = txtShelf.Text.Trim(),
                    CopyNumber = (int)numCopy.Value,
                    PublicationYear = (int)numYear.Value,
                    Status = (ItemStatus)Enum.Parse(typeof(ItemStatus), cmbStatus.SelectedItem.ToString()),
                    LastAudited = item != null ? item.LastAudited : null,
                    Notes = txtNotes.Text.Trim()
                };
            };

            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(new Label { Width = 10, Dock = DockStyle.Right });
            btnPanel.Controls.Add(btnSave);

            this.Controls.Add(tlp);
            this.Controls.Add(btnPanel);
        }

        private TextBox AddRow(TableLayoutPanel tlp, string label, int row, string val)
        {
            Label lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            TextBox txt = new TextBox { Text = val, Dock = DockStyle.Fill };
            tlp.Controls.Add(lbl, 0, row);
            tlp.Controls.Add(txt, 1, row);
            return txt;
        }
    }

    public class MainForm : Form
    {
        private CatalogDatabase db;
        private ShelfAuditSession auditSession;

        // GUI Controls
        private TabControl tabs;
        private TabPage tabCatalog;
        private TabPage tabAudit;
        private TabPage tabDedup;
        private TabPage tabReports;

        // Tab 1: Catalog
        private TextBox txtSearchQuery;
        private ComboBox cmbShelfFilter;
        private ComboBox cmbStatusFilter;
        private DataGridView gridCatalog;
        private Label lblCatalogStats;

        // Tab 2: Shelf Audit
        private ComboBox cmbAuditShelf;
        private Button btnStartAudit;
        private TextBox txtBarcodeScan;
        private Label lblScanFeedback;
        private Panel pnlFeedbackColor;
        private DataGridView gridAuditEvents;
        private ListBox listMissingExpected;
        private Label lblAuditKpi;
        private Button btnReconcile;
        private Button btnImportScanFile;

        // Tab 3: Deduplication
        private TrackBar tbFuzzyThreshold;
        private Label lblThresholdVal;
        private CheckBox chkIsbnMatch;
        private CheckBox chkExactMatch;
        private CheckBox chkFuzzyMatch;
        private CheckBox chkBarcodeCollision;
        private Button btnRunDedup;
        private DataGridView gridDuplicates;
        private Label lblDedupCount;

        // Tab 4: Reports
        private TextBox txtReportPreview;
        private Button btnExportHtml;
        private Button btnExportCsv;
        private Button btnExportMarkdown;
        private Button btnOpenInBrowser;
        private Label lblKpiTitles;
        private Label lblKpiCopies;
        private Label lblKpiMissing;
        private Label lblKpiAudited;

        public MainForm(string dataFilePath)
        {
            db = new CatalogDatabase(dataFilePath);
            this.Text = "CatalogAuditDesk - Library Catalog, Shelf Inventory Audit & Duplicate Detection Studio";
            this.Size = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.Icon = SystemIcons.Application;

            BuildUi();
            RefreshCatalogGrid();
            RefreshShelfDropdowns();
            UpdateReportKpis();
        }

        private void BuildUi()
        {
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabCatalog = new TabPage("Master Catalog Holdings");
            tabAudit = new TabPage("Physical Shelf Inventory Audit");
            tabDedup = new TabPage("Duplicate & Variant Detection");
            tabReports = new TabPage("Audit Reports & KPI Dashboard");

            tabs.TabPages.Add(tabCatalog);
            tabs.TabPages.Add(tabAudit);
            tabs.TabPages.Add(tabDedup);
            tabs.TabPages.Add(tabReports);

            BuildCatalogTab();
            BuildAuditTab();
            BuildDedupTab();
            BuildReportsTab();

            this.Controls.Add(tabs);
        }

        #region Tab 1: Master Catalog Builder

        private void BuildCatalogTab()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6) };
            txtSearchQuery = new TextBox { Width = 220, Dock = DockStyle.Left };
            txtSearchQuery.TextChanged += (s, e) => RefreshCatalogGrid();

            Label lblSearch = new Label { Text = "Search Catalog:", Width = 100, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };

            cmbShelfFilter = new ComboBox { Width = 150, Dock = DockStyle.Left, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbShelfFilter.SelectedIndexChanged += (s, e) => RefreshCatalogGrid();
            Label lblShelf = new Label { Text = "Shelf:", Width = 50, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };

            cmbStatusFilter = new ComboBox { Width = 130, Dock = DockStyle.Left, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStatusFilter.Items.Add("ALL STATUSES");
            cmbStatusFilter.Items.AddRange(Enum.GetNames(typeof(ItemStatus)));
            cmbStatusFilter.SelectedIndex = 0;
            cmbStatusFilter.SelectedIndexChanged += (s, e) => RefreshCatalogGrid();
            Label lblStatus = new Label { Text = "Status:", Width = 55, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };

            Button btnAdd = new Button { Text = "+ Add Book", Width = 95, Dock = DockStyle.Right, BackColor = Color.FromArgb(2, 132, 199), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnAdd.Click += BtnAdd_Click;

            Button btnEdit = new Button { Text = "Edit Selected", Width = 100, Dock = DockStyle.Right };
            btnEdit.Click += BtnEdit_Click;

            Button btnDelete = new Button { Text = "Delete", Width = 75, Dock = DockStyle.Right };
            btnDelete.Click += BtnDelete_Click;

            topPanel.Controls.Add(btnDelete);
            topPanel.Controls.Add(new Label { Width = 6, Dock = DockStyle.Right });
            topPanel.Controls.Add(btnEdit);
            topPanel.Controls.Add(new Label { Width = 6, Dock = DockStyle.Right });
            topPanel.Controls.Add(btnAdd);

            topPanel.Controls.Add(cmbStatusFilter);
            topPanel.Controls.Add(lblStatus);
            topPanel.Controls.Add(cmbShelfFilter);
            topPanel.Controls.Add(lblShelf);
            topPanel.Controls.Add(txtSearchQuery);
            topPanel.Controls.Add(lblSearch);

            gridCatalog = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            gridCatalog.CellDoubleClick += (s, e) => BtnEdit_Click(s, e);

            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(6) };
            lblCatalogStats = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(71, 85, 105) };
            bottomPanel.Controls.Add(lblCatalogStats);

            tabCatalog.Controls.Add(gridCatalog);
            tabCatalog.Controls.Add(topPanel);
            tabCatalog.Controls.Add(bottomPanel);
        }

        private void RefreshShelfDropdowns()
        {
            var shelves = db.GetDistinctShelves();

            string prevCatalog = cmbShelfFilter.SelectedItem != null ? cmbShelfFilter.SelectedItem.ToString() : null;
            cmbShelfFilter.Items.Clear();
            cmbShelfFilter.Items.Add("ALL SHELVES");
            foreach (var s in shelves) cmbShelfFilter.Items.Add(s);
            if (!string.IsNullOrEmpty(prevCatalog) && cmbShelfFilter.Items.Contains(prevCatalog))
                cmbShelfFilter.SelectedItem = prevCatalog;
            else
                cmbShelfFilter.SelectedIndex = 0;

            string prevAudit = cmbAuditShelf.SelectedItem != null ? cmbAuditShelf.SelectedItem.ToString() : null;
            cmbAuditShelf.Items.Clear();
            foreach (var s in shelves) cmbAuditShelf.Items.Add(s);
            if (!string.IsNullOrEmpty(prevAudit) && cmbAuditShelf.Items.Contains(prevAudit))
                cmbAuditShelf.SelectedItem = prevAudit;
            else if (cmbAuditShelf.Items.Count > 0)
                cmbAuditShelf.SelectedIndex = 0;
        }

        private void RefreshCatalogGrid()
        {
            string shelfFilter = cmbShelfFilter.SelectedItem != null && cmbShelfFilter.SelectedIndex > 0 ? cmbShelfFilter.SelectedItem.ToString() : null;
            ItemStatus? statusFilter = null;
            if (cmbStatusFilter.SelectedIndex > 0)
            {
                statusFilter = (ItemStatus)Enum.Parse(typeof(ItemStatus), cmbStatusFilter.SelectedItem.ToString());
            }

            var items = db.Search(txtSearchQuery.Text, shelfFilter, statusFilter);

            var table = new System.Data.DataTable();
            table.Columns.Add("Barcode");
            table.Columns.Add("Call #");
            table.Columns.Add("Title");
            table.Columns.Add("Author");
            table.Columns.Add("Shelf");
            table.Columns.Add("Copy");
            table.Columns.Add("ISBN");
            table.Columns.Add("Status");
            table.Columns.Add("Last Audited");

            foreach (var i in items)
            {
                table.Rows.Add(
                    i.Barcode,
                    i.FullCallNumber,
                    i.Title,
                    i.Author,
                    i.ShelfLocation,
                    "c." + i.CopyNumber,
                    i.Isbn,
                    i.Status.ToString(),
                    i.LastAudited.HasValue ? i.LastAudited.Value.ToString("yyyy-MM-dd") : "Never"
                );
            }

            gridCatalog.DataSource = table;
            lblCatalogStats.Text = string.Format("Displaying {0} of {1} catalog copies across {2} monitored shelves.",
                items.Count, db.Items.Count, db.GetDistinctShelves().Count);
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new ItemEditDialog(null, db.GetDistinctShelves()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultItem != null)
                {
                    if (db.FindByBarcode(dlg.ResultItem.Barcode) != null)
                    {
                        MessageBox.Show("An item with barcode '" + dlg.ResultItem.Barcode + "' already exists!", "Duplicate Barcode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    db.Items.Add(dlg.ResultItem);
                    db.Save();
                    RefreshShelfDropdowns();
                    RefreshCatalogGrid();
                    UpdateReportKpis();
                }
            }
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (gridCatalog.SelectedRows.Count == 0) return;
            string barcode = gridCatalog.SelectedRows[0].Cells["Barcode"].Value.ToString();
            CatalogItem existing = db.FindByBarcode(barcode);
            if (existing == null) return;

            using (var dlg = new ItemEditDialog(existing, db.GetDistinctShelves()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultItem != null)
                {
                    // Copy fields
                    existing.Isbn = dlg.ResultItem.Isbn;
                    existing.Title = dlg.ResultItem.Title;
                    existing.Author = dlg.ResultItem.Author;
                    existing.Classification = dlg.ResultItem.Classification;
                    existing.Cutter = dlg.ResultItem.Cutter;
                    existing.ShelfLocation = dlg.ResultItem.ShelfLocation;
                    existing.CopyNumber = dlg.ResultItem.CopyNumber;
                    existing.PublicationYear = dlg.ResultItem.PublicationYear;
                    existing.Status = dlg.ResultItem.Status;
                    existing.Notes = dlg.ResultItem.Notes;

                    db.Save();
                    RefreshShelfDropdowns();
                    RefreshCatalogGrid();
                    UpdateReportKpis();
                }
            }
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (gridCatalog.SelectedRows.Count == 0) return;
            string barcode = gridCatalog.SelectedRows[0].Cells["Barcode"].Value.ToString();
            var item = db.FindByBarcode(barcode);
            if (item == null) return;

            if (MessageBox.Show(string.Format("Are you sure you want to delete '{0}' ({1})?", item.Title, item.Barcode),
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                db.Items.Remove(item);
                db.Save();
                RefreshCatalogGrid();
                UpdateReportKpis();
            }
        }

        #endregion

        #region Tab 2: Shelf Inventory Audit Builder

        private void BuildAuditTab()
        {
            Panel topControl = new Panel { Dock = DockStyle.Top, Height = 95, Padding = new Padding(8), BackColor = Color.FromArgb(248, 250, 252) };

            Label lblChoose = new Label { Text = "Audit Shelf:", Left = 10, Top = 12, Width = 80 };
            cmbAuditShelf = new ComboBox { Left = 95, Top = 9, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };

            btnStartAudit = new Button { Text = "Start Audit Session", Left = 265, Top = 8, Width = 140, Height = 28, BackColor = Color.FromArgb(22, 163, 74), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnStartAudit.Click += BtnStartAudit_Click;

            btnImportScanFile = new Button { Text = "Import Scan File (.txt)...", Left = 415, Top = 8, Width = 160, Height = 28 };
            btnImportScanFile.Click += BtnImportScanFile_Click;

            btnReconcile = new Button { Text = "Reconcile Catalog Changes", Left = 585, Top = 8, Width = 180, Height = 28, BackColor = Color.FromArgb(217, 119, 6), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false };
            btnReconcile.Click += BtnReconcile_Click;

            Label lblScan = new Label { Text = "Barcode Scan:", Left = 10, Top = 46, Width = 90, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            txtBarcodeScan = new TextBox { Left = 105, Top = 44, Width = 250, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Enabled = false };
            txtBarcodeScan.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ProcessEnteredScan();
                }
            };

            Button btnSubmitScan = new Button { Text = "Scan Item", Left = 365, Top = 43, Width = 90, Height = 29, Enabled = false };
            btnSubmitScan.Click += (s, e) => ProcessEnteredScan();

            lblAuditKpi = new Label { Left = 470, Top = 48, Width = 590, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 58, 138), Text = "Select a shelf and click 'Start Audit Session'." };

            topControl.Controls.AddRange(new Control[] { lblChoose, cmbAuditShelf, btnStartAudit, btnImportScanFile, btnReconcile, lblScan, txtBarcodeScan, btnSubmitScan, lblAuditKpi });

            // Visual feedback banner
            pnlFeedbackColor = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(8) };
            lblScanFeedback = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85), Text = "Ready for physical shelf scanning." };
            pnlFeedbackColor.Controls.Add(lblScanFeedback);

            // Split Container: Left = Scanned Audit Log, Right = Expected Missing Checklist
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 680, Orientation = Orientation.Vertical };

            // Left side
            Panel pnlLeft = new Panel { Dock = DockStyle.Fill };
            Label lblLogTitle = new Label { Dock = DockStyle.Top, Height = 26, Text = "Real-Time Audit Event Log", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Padding = new Padding(4) };
            gridAuditEvents = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            pnlLeft.Controls.Add(gridAuditEvents);
            pnlLeft.Controls.Add(lblLogTitle);
            split.Panel1.Controls.Add(pnlLeft);

            // Right side
            Panel pnlRight = new Panel { Dock = DockStyle.Fill };
            Label lblMissingTitle = new Label { Dock = DockStyle.Top, Height = 26, Text = "Missing / Unscanned from Shelf", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Padding = new Padding(4) };
            listMissingExpected = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9f) };
            pnlRight.Controls.Add(listMissingExpected);
            pnlRight.Controls.Add(lblMissingTitle);
            split.Panel2.Controls.Add(pnlRight);

            tabAudit.Controls.Add(split);
            tabAudit.Controls.Add(pnlFeedbackColor);
            tabAudit.Controls.Add(topControl);
        }

        private void BtnStartAudit_Click(object sender, EventArgs e)
        {
            if (cmbAuditShelf.SelectedItem == null) return;
            string shelf = cmbAuditShelf.SelectedItem.ToString();

            auditSession = new ShelfAuditSession(shelf, db);
            txtBarcodeScan.Enabled = true;
            btnReconcile.Enabled = true;
            foreach (Control c in tabAudit.Controls)
            {
                if (c is Panel)
                {
                    foreach (Control sc in c.Controls)
                    {
                        if (sc.Text == "Scan Item") sc.Enabled = true;
                    }
                }
            }

            pnlFeedbackColor.BackColor = Color.FromArgb(224, 242, 254);
            lblScanFeedback.ForeColor = Color.FromArgb(3, 105, 161);
            lblScanFeedback.Text = string.Format("Audit session ACTIVE for '{0}'. Ready to scan barcodes.", shelf);

            UpdateAuditDisplay();
            txtBarcodeScan.Focus();
        }

        private void ProcessEnteredScan()
        {
            if (auditSession == null || string.IsNullOrWhiteSpace(txtBarcodeScan.Text)) return;
            string barcode = txtBarcodeScan.Text.Trim();
            txtBarcodeScan.Text = "";

            AuditEvent ev = auditSession.ProcessBarcodeScan(barcode);

            // Visual and audio feedback
            if (ev.Outcome == AuditOutcome.VerifiedPresent)
            {
                pnlFeedbackColor.BackColor = Color.FromArgb(220, 252, 231);
                lblScanFeedback.ForeColor = Color.FromArgb(21, 128, 61);
                lblScanFeedback.Text = string.Format("OK: {0}", ev.Message);
                SystemSounds.Asterisk.Play();
            }
            else if (ev.Outcome == AuditOutcome.Misplaced)
            {
                pnlFeedbackColor.BackColor = Color.FromArgb(254, 243, 199);
                lblScanFeedback.ForeColor = Color.FromArgb(180, 83, 9);
                lblScanFeedback.Text = string.Format("MISPLACED: {0}", ev.Message);
                SystemSounds.Exclamation.Play();
            }
            else if (ev.Outcome == AuditOutcome.Uncataloged)
            {
                pnlFeedbackColor.BackColor = Color.FromArgb(254, 226, 226);
                lblScanFeedback.ForeColor = Color.FromArgb(185, 28, 28);
                lblScanFeedback.Text = string.Format("UNCATALOGED: {0}", ev.Message);
                SystemSounds.Hand.Play();
            }
            else if (ev.Outcome == AuditOutcome.StatusConflict)
            {
                pnlFeedbackColor.BackColor = Color.FromArgb(243, 232, 255);
                lblScanFeedback.ForeColor = Color.FromArgb(126, 34, 206);
                lblScanFeedback.Text = string.Format("STATUS CONFLICT: {0}", ev.Message);
                SystemSounds.Exclamation.Play();
            }
            else
            {
                pnlFeedbackColor.BackColor = Color.FromArgb(241, 245, 249);
                lblScanFeedback.ForeColor = Color.FromArgb(71, 85, 105);
                lblScanFeedback.Text = ev.Message;
            }

            UpdateAuditDisplay();
            txtBarcodeScan.Focus();
        }

        private void UpdateAuditDisplay()
        {
            if (auditSession == null) return;

            int expected = auditSession.GetExpectedItems().Count;
            int verified = auditSession.Events.Count(e => e.Outcome == AuditOutcome.VerifiedPresent);
            int misplaced = auditSession.Events.Count(e => e.Outcome == AuditOutcome.Misplaced);
            int uncataloged = auditSession.Events.Count(e => e.Outcome == AuditOutcome.Uncataloged);
            int conflicts = auditSession.Events.Count(e => e.Outcome == AuditOutcome.StatusConflict);
            var missingList = auditSession.GetMissingItems();

            lblAuditKpi.Text = string.Format("Shelf: {0} | Exp: {1} | Scanned: {2} (OK: {3}, Misplaced: {4}, Uncat: {5}, Conflict: {6}, Missing: {7})",
                auditSession.TargetShelf, expected, auditSession.Events.Count, verified, misplaced, uncataloged, conflicts, missingList.Count);

            // Update log grid
            var table = new System.Data.DataTable();
            table.Columns.Add("Time");
            table.Columns.Add("Barcode");
            table.Columns.Add("Outcome");
            table.Columns.Add("Title / Registered");
            table.Columns.Add("Call #");
            table.Columns.Add("Details");

            for (int i = auditSession.Events.Count - 1; i >= 0; i--)
            {
                var ev = auditSession.Events[i];
                string t = ev.MatchedItem != null ? ev.MatchedItem.Title : "(Uncataloged Item)";
                string call = ev.MatchedItem != null ? ev.MatchedItem.FullCallNumber : "-";
                table.Rows.Add(
                    ev.Timestamp.ToString("HH:mm:ss"),
                    ev.ScannedBarcode,
                    ev.Outcome.ToString() + (ev.OutOfSequence ? " [OutSeq]" : ""),
                    t,
                    call,
                    ev.Message
                );
            }
            gridAuditEvents.DataSource = table;

            // Update missing list
            listMissingExpected.Items.Clear();
            foreach (var m in missingList)
            {
                listMissingExpected.Items.Add(string.Format("[{0}] {1} ({2})", m.Barcode, m.Title, m.FullCallNumber));
            }
        }

        private void BtnImportScanFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Text Files (*.txt;*.tsv;*.csv)|*.txt;*.tsv;*.csv|All Files (*.*)|*.*";
                ofd.Title = "Import Batch Scanned Barcodes";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    if (auditSession == null)
                    {
                        BtnStartAudit_Click(sender, e);
                    }

                    string[] lines = File.ReadAllLines(ofd.FileName, Encoding.UTF8);
                    int count = 0;
                    foreach (var line in lines)
                    {
                        string b = line.Split('\t', ',')[0].Trim();
                        if (!string.IsNullOrEmpty(b) && !b.StartsWith("#"))
                        {
                            auditSession.ProcessBarcodeScan(b);
                            count++;
                        }
                    }

                    UpdateAuditDisplay();
                    MessageBox.Show(string.Format("Imported and processed {0} barcode scans from '{1}'.", count, Path.GetFileName(ofd.FileName)),
                        "Scan Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnReconcile_Click(object sender, EventArgs e)
        {
            if (auditSession == null) return;

            string msg = string.Format("Reconcile audit results for shelf '{0}' into master catalog?\n\n" +
                                       "- Move misplaced books to '{0}'\n" +
                                       "- Clear 'Missing' or 'CheckedOut' flags for physically found items\n" +
                                       "- Update 'LastAudited' timestamps\n\n" +
                                       "Do you also want to mark the {1} unscanned books as 'Missing'?",
                                       auditSession.TargetShelf, auditSession.GetMissingItems().Count);

            var res = MessageBox.Show(msg, "Confirm Catalog Reconciliation", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (res == DialogResult.Cancel) return;

            bool flagMissing = (res == DialogResult.Yes);
            int updated = auditSession.ReconcileCatalog(true, true, flagMissing);

            RefreshCatalogGrid();
            UpdateReportKpis();
            UpdateAuditDisplay();

            MessageBox.Show(string.Format("Reconciliation completed! Updated {0} catalog records in '{1}'.",
                updated, Path.GetFileName(db.StoragePath)), "Reconciled Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion

        #region Tab 3: Duplicate & Variant Detection Builder

        private void BuildDedupTab()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 95, Padding = new Padding(8), BackColor = Color.FromArgb(248, 250, 252) };

            chkIsbnMatch = new CheckBox { Text = "ISBN-10/13 Canonical Match", Left = 12, Top = 10, Width = 200, Checked = true };
            chkExactMatch = new CheckBox { Text = "Exact Title & Author Match", Left = 220, Top = 10, Width = 190, Checked = true };
            chkFuzzyMatch = new CheckBox { Text = "Fuzzy Title & Author (Levenshtein + Jaro)", Left = 420, Top = 10, Width = 280, Checked = true };
            chkBarcodeCollision = new CheckBox { Text = "Barcode Collisions", Left = 710, Top = 10, Width = 160, Checked = true };

            Label lblThresh = new Label { Text = "Fuzzy Similarity Threshold:", Left = 12, Top = 48, Width = 170 };
            tbFuzzyThreshold = new TrackBar { Left = 185, Top = 42, Width = 200, Minimum = 50, Maximum = 100, Value = 80, TickFrequency = 5 };
            lblThresholdVal = new Label { Text = "80%", Left = 390, Top = 48, Width = 50, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            tbFuzzyThreshold.ValueChanged += (s, e) => { lblThresholdVal.Text = tbFuzzyThreshold.Value + "%"; };

            btnRunDedup = new Button { Text = "Scan For Duplicates", Left = 460, Top = 43, Width = 160, Height = 32, BackColor = Color.FromArgb(2, 132, 199), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnRunDedup.Click += BtnRunDedup_Click;

            lblDedupCount = new Label { Left = 635, Top = 49, Width = 380, ForeColor = Color.FromArgb(71, 85, 105) };

            topPanel.Controls.AddRange(new Control[] { chkIsbnMatch, chkExactMatch, chkFuzzyMatch, chkBarcodeCollision, lblThresh, tbFuzzyThreshold, lblThresholdVal, btnRunDedup, lblDedupCount });

            gridDuplicates = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            tabDedup.Controls.Add(gridDuplicates);
            tabDedup.Controls.Add(topPanel);
        }

        private void BtnRunDedup_Click(object sender, EventArgs e)
        {
            double thresh = tbFuzzyThreshold.Value / 100.0;
            var duplicates = DuplicateDetectionEngine.ScanDuplicates(db, thresh,
                chkIsbnMatch.Checked, chkExactMatch.Checked, chkFuzzyMatch.Checked, chkBarcodeCollision.Checked);

            var table = new System.Data.DataTable();
            table.Columns.Add("Match Type");
            table.Columns.Add("Similarity");
            table.Columns.Add("Record A (Barcode / Title / Shelf)");
            table.Columns.Add("Record B (Barcode / Title / Shelf)");
            table.Columns.Add("Diagnostic Reason");

            foreach (var d in duplicates)
            {
                string descA = string.Format("[{0}] {1} ({2})", d.ItemA.Barcode, d.ItemA.Title, d.ItemA.ShelfLocation);
                string descB = string.Format("[{0}] {1} ({2})", d.ItemB.Barcode, d.ItemB.Title, d.ItemB.ShelfLocation);
                table.Rows.Add(
                    d.MatchType,
                    string.Format("{0:P0}", d.SimilarityScore),
                    descA,
                    descB,
                    d.Reason
                );
            }

            gridDuplicates.DataSource = table;
            lblDedupCount.Text = string.Format("Found {0} duplicate clusters / variant pairs.", duplicates.Count);
        }

        #endregion

        #region Tab 4: Reports & KPI Dashboard Builder

        private void BuildReportsTab()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 95, Padding = new Padding(12), BackColor = Color.FromArgb(248, 250, 252) };

            lblKpiTitles = new Label { Left = 15, Top = 12, Width = 160, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Text = "Titles: -" };
            lblKpiCopies = new Label { Left = 190, Top = 12, Width = 160, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Text = "Copies: -" };
            lblKpiMissing = new Label { Left = 360, Top = 12, Width = 160, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Text = "Missing: -" };
            lblKpiAudited = new Label { Left = 530, Top = 12, Width = 180, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(22, 163, 74), Text = "Audited 7d: -" };

            btnExportHtml = new Button { Text = "Export HTML Report...", Left = 15, Top = 48, Width = 170, Height = 32, BackColor = Color.FromArgb(2, 132, 199), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnExportHtml.Click += BtnExportHtml_Click;

            btnExportCsv = new Button { Text = "Export CSV Catalog...", Left = 195, Top = 48, Width = 160, Height = 32 };
            btnExportCsv.Click += BtnExportCsv_Click;

            btnExportMarkdown = new Button { Text = "Export Markdown Audit...", Left = 365, Top = 48, Width = 180, Height = 32 };
            btnExportMarkdown.Click += BtnExportMarkdown_Click;

            btnOpenInBrowser = new Button { Text = "Preview in Web Browser", Left = 555, Top = 48, Width = 180, Height = 32, BackColor = Color.FromArgb(22, 163, 74), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnOpenInBrowser.Click += BtnOpenInBrowser_Click;

            topPanel.Controls.AddRange(new Control[] { lblKpiTitles, lblKpiCopies, lblKpiMissing, lblKpiAudited, btnExportHtml, btnExportCsv, btnExportMarkdown, btnOpenInBrowser });

            txtReportPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9.5f),
                ReadOnly = true,
                BackColor = Color.FromArgb(255, 255, 255)
            };

            tabReports.Controls.Add(txtReportPreview);
            tabReports.Controls.Add(topPanel);
        }

        private void UpdateReportKpis()
        {
            int totalCopies = db.Items.Count;
            int distinctTitles = db.Items.Select(i => (i.Title ?? "").Trim().ToLowerInvariant()).Distinct().Count();
            int missing = db.Items.Count(i => i.Status == ItemStatus.Missing);
            int audited = db.Items.Count(i => i.LastAudited.HasValue && i.LastAudited.Value >= DateTime.Today.AddDays(-7));

            lblKpiTitles.Text = string.Format("Distinct Titles: {0}", distinctTitles);
            lblKpiCopies.Text = string.Format("Total Copies: {0}", totalCopies);
            lblKpiMissing.Text = string.Format("Missing Items: {0}", missing);
            lblKpiAudited.Text = string.Format("Audited in 7d: {0}", audited);

            txtReportPreview.Text = ReportExporter.GenerateMarkdownAudit(auditSession ?? new ShelfAuditSession(cmbAuditShelf.SelectedItem != null ? cmbAuditShelf.SelectedItem.ToString() : "STACK-A1-LIT", db));
        }

        private void BtnExportHtml_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "HTML Files (*.html)|*.html";
                sfd.FileName = "library_catalog_audit_report.html";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    string html = ReportExporter.GenerateHtmlReport(db, auditSession, "Library Catalog & Shelf Inventory Audit Report");
                    File.WriteAllText(sfd.FileName, html, Encoding.UTF8);
                    MessageBox.Show("Saved HTML report to: " + sfd.FileName, "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV Files (*.csv)|*.csv";
                sfd.FileName = "library_catalog_export.csv";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    string csv = ReportExporter.GenerateCsv(db);
                    File.WriteAllText(sfd.FileName, csv, Encoding.UTF8);
                    MessageBox.Show("Saved CSV catalog export to: " + sfd.FileName, "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnExportMarkdown_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Markdown Files (*.md)|*.md";
                sfd.FileName = "shelf_audit_report.md";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    string md = ReportExporter.GenerateMarkdownAudit(auditSession ?? new ShelfAuditSession("STACK-A1-LIT", db));
                    File.WriteAllText(sfd.FileName, md, Encoding.UTF8);
                    MessageBox.Show("Saved Markdown audit report to: " + sfd.FileName, "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnOpenInBrowser_Click(object sender, EventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "CatalogAuditDesk_Report_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html");
            string html = ReportExporter.GenerateHtmlReport(db, auditSession, "Library Catalog & Shelf Inventory Audit Report");
            File.WriteAllText(tempFile, html, Encoding.UTF8);
            System.Diagnostics.Process.Start(tempFile);
        }

        #endregion
    }

    #endregion

    #region CLI Entry Point & Self-Verification Tests

    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string defaultDbPath = Path.Combine(exeDir, "catalog_inventory.tsv");

            if (args.Length == 0)
            {
                // Launch WinForms GUI
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(defaultDbPath));
                return 0;
            }

            // CLI arguments processor
            string cmd = args[0].ToLowerInvariant();

            if (cmd == "--help" || cmd == "-h" || cmd == "/?")
            {
                PrintHelp();
                return 0;
            }

            if (cmd == "--gui")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(defaultDbPath));
                return 0;
            }

            if (cmd == "--status")
            {
                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                Console.WriteLine("================================================================================");
                Console.WriteLine("CatalogAuditDesk - Standalone Library Catalog & Shelf Inventory Utility");
                Console.WriteLine("Database file: " + db.StoragePath);
                Console.WriteLine("================================================================================");
                Console.WriteLine(string.Format("Total Holdings (Copies):  {0}", db.Items.Count));
                Console.WriteLine(string.Format("Distinct Titles:          {0}", db.Items.Select(i => (i.Title ?? "").ToLowerInvariant().Trim()).Distinct().Count()));
                Console.WriteLine(string.Format("Active Stacks/Shelves:    {0}", db.GetDistinctShelves().Count));
                Console.WriteLine(string.Format("Currently Missing:        {0}", db.Items.Count(i => i.Status == ItemStatus.Missing)));
                Console.WriteLine(string.Format("On Loan (Checked Out):    {0}", db.Items.Count(i => i.Status == ItemStatus.CheckedOut)));
                Console.WriteLine(string.Format("Reference Only (No Loan): {0}", db.Items.Count(i => i.Status == ItemStatus.ReferenceOnly)));
                Console.WriteLine();
                Console.WriteLine("Monitored Shelves:");
                foreach (var shelf in db.GetDistinctShelves())
                {
                    var onShelf = db.FindByShelf(shelf);
                    Console.WriteLine(string.Format("  * {0,-18} : {1,2} items (Missing: {2})",
                        shelf, onShelf.Count, onShelf.Count(i => i.Status == ItemStatus.Missing)));
                }
                return 0;
            }

            if (cmd == "--search")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: Please provide a search term. Example: CatalogAuditDesk.exe --search \"Clean Code\"");
                    return 1;
                }
                string term = args[1];
                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                var matches = db.Search(term, null, null);
                Console.WriteLine(string.Format("Found {0} matches for query '{1}':", matches.Count, term));
                Console.WriteLine("--------------------------------------------------------------------------------");
                foreach (var m in matches)
                {
                    Console.WriteLine(string.Format(" - [{0}] {1} by {2} | Call: {3} | Shelf: {4} | Copy: c.{5} | Status: {6}",
                        m.Barcode, m.Title, m.Author, m.FullCallNumber, m.ShelfLocation, m.CopyNumber, m.Status));
                }
                return 0;
            }

            if (cmd == "--dedup")
            {
                double thresh = 0.80;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--threshold" && i + 1 < args.Length)
                    {
                        double parsed;
                        if (double.TryParse(args[i + 1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                        {
                            thresh = parsed;
                        }
                    }
                }

                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                var duplicates = DuplicateDetectionEngine.ScanDuplicates(db, thresh, true, true, true, true);
                Console.WriteLine(string.Format("Duplicate & Variant Detection Scan (Threshold: {0:P0}):", thresh));
                Console.WriteLine(string.Format("Found {0} potential duplicate pairs / clusters:", duplicates.Count));
                Console.WriteLine("--------------------------------------------------------------------------------");
                foreach (var d in duplicates)
                {
                    Console.WriteLine(string.Format(" * [{0} - {1:P0}] {2}", d.MatchType, d.SimilarityScore, d.Reason));
                    Console.WriteLine(string.Format("    Rec A: [{0}] {1} ({2})", d.ItemA.Barcode, d.ItemA.Title, d.ItemA.ShelfLocation));
                    Console.WriteLine(string.Format("    Rec B: [{0}] {1} ({2})", d.ItemB.Barcode, d.ItemB.Title, d.ItemB.ShelfLocation));
                }
                return 0;
            }

            if (cmd == "--audit")
            {
                string targetShelf = "STACK-A1-LIT";
                string scanFile = null;
                bool reconcile = false;

                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--shelf" && i + 1 < args.Length) targetShelf = args[i + 1];
                    if (args[i] == "--scan-file" && i + 1 < args.Length) scanFile = args[i + 1];
                    if (args[i] == "--reconcile") reconcile = true;
                }

                if (string.IsNullOrEmpty(scanFile) || !File.Exists(scanFile))
                {
                    Console.WriteLine("Error: Must specify an existing scan file with --scan-file <path>");
                    return 1;
                }

                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                ShelfAuditSession session = new ShelfAuditSession(targetShelf, db);
                string[] lines = File.ReadAllLines(scanFile, Encoding.UTF8);
                foreach (var l in lines)
                {
                    string b = l.Split('\t', ',')[0].Trim();
                    if (!string.IsNullOrEmpty(b) && !b.StartsWith("#"))
                    {
                        session.ProcessBarcodeScan(b);
                    }
                }

                Console.WriteLine(ReportExporter.GenerateMarkdownAudit(session));

                if (reconcile)
                {
                    int updated = session.ReconcileCatalog(true, true, false);
                    Console.WriteLine(string.Format("Reconciliation committed: {0} records updated.", updated));
                }
                return 0;
            }

            if (cmd == "--export-html")
            {
                string outPath = (args.Length > 1) ? args[1] : Path.Combine(exeDir, "catalog_report.html");
                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                string html = ReportExporter.GenerateHtmlReport(db, null, "Master Library Holdings & Catalog Index");
                File.WriteAllText(outPath, html, Encoding.UTF8);
                Console.WriteLine("Exported interactive HTML report to: " + outPath);
                return 0;
            }

            if (cmd == "--export-csv")
            {
                string outPath = (args.Length > 1) ? args[1] : Path.Combine(exeDir, "catalog_export.csv");
                CatalogDatabase db = new CatalogDatabase(defaultDbPath);
                string csv = ReportExporter.GenerateCsv(db);
                File.WriteAllText(outPath, csv, Encoding.UTF8);
                Console.WriteLine("Exported catalog CSV to: " + outPath);
                return 0;
            }

            if (cmd == "--demo")
            {
                return RunSelfVerificationTests(defaultDbPath);
            }

            Console.WriteLine("Unknown argument: " + cmd);
            PrintHelp();
            return 1;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("CatalogAuditDesk.exe - Standalone Windows Library Utility");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("  CatalogAuditDesk.exe                          Launch interactive Windows Forms GUI");
            Console.WriteLine("  CatalogAuditDesk.exe --gui                    Explicitly launch Windows Forms GUI");
            Console.WriteLine("  CatalogAuditDesk.exe --status                 Print collection overview & shelf counts");
            Console.WriteLine("  CatalogAuditDesk.exe --search <query>         Fast search across titles, authors, barcodes");
            Console.WriteLine("  CatalogAuditDesk.exe --dedup [--threshold 0.8] Scan for duplicate records & editions");
            Console.WriteLine("  CatalogAuditDesk.exe --audit --shelf <ID> --scan-file <file> [--reconcile]");
            Console.WriteLine("                                                Batch shelf inventory audit reconciliation");
            Console.WriteLine("  CatalogAuditDesk.exe --export-html <file>     Generate standalone HTML report with CSS/print rules");
            Console.WriteLine("  CatalogAuditDesk.exe --export-csv <file>      Export master catalog to CSV");
            Console.WriteLine("  CatalogAuditDesk.exe --demo                   Run self-verification test suite");
            Console.WriteLine("  CatalogAuditDesk.exe --help                   Show this help reference");
        }

        private static int RunSelfVerificationTests(string dbPath)
        {
            Console.WriteLine("=== CatalogAuditDesk Self-Verification Test Suite ===");
            int passed = 0;
            int total = 0;

            // Test 1: ISBN normalization & conversion
            total++;
            string isbn10 = "0-14-143951-3";
            string converted13 = IsbnTools.ToCanonicalIsbn13(isbn10);
            if (converted13 == "9780141439518" && IsbnTools.IsValidIsbn13(converted13))
            {
                Console.WriteLine("  [PASS] Test 1: ISBN-10 to Canonical ISBN-13 conversion");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 1: ISBN conversion expected 9780141439518, got " + converted13);
            }

            // Test 2: Levenshtein distance
            total++;
            int dist = StringSimilarity.LevenshteinDistance("Clean Code", "Clean Coder");
            if (dist == 1)
            {
                Console.WriteLine("  [PASS] Test 2: Levenshtein distance computation");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 2: Levenshtein distance expected 1, got " + dist);
            }

            // Test 3: Jaro-Winkler similarity
            total++;
            double jw = StringSimilarity.JaroWinkler("Pride and Prejudice", "Pride & Prejudice");
            if (jw > 0.85)
            {
                Console.WriteLine(string.Format("  [PASS] Test 3: Jaro-Winkler title similarity ({0:P1})", jw));
                passed++;
            }
            else
            {
                Console.WriteLine(string.Format("  [FAIL] Test 3: Jaro-Winkler expected >0.85, got {0}", jw));
            }

            // Test 4: Dewey call comparison
            total++;
            int dComp = DeweyCallComparer.Compare("823.7", "823.912");
            if (dComp < 0)
            {
                Console.WriteLine("  [PASS] Test 4: Dewey decimal sorting (823.7 < 823.912)");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 4: Dewey sort failed");
            }

            // Test 5: Catalog Database Loading & Seeding
            total++;
            CatalogDatabase db = new CatalogDatabase(dbPath);
            if (db.Items.Count >= 20)
            {
                Console.WriteLine(string.Format("  [PASS] Test 5: Catalog database loaded ({0} items)", db.Items.Count));
                passed++;
            }
            else
            {
                Console.WriteLine(string.Format("  [FAIL] Test 5: Expected >= 20 items, got {0}", db.Items.Count));
            }

            // Test 6: Shelf Audit Session Logic (Verified, Misplaced, Uncataloged)
            total++;
            ShelfAuditSession audit = new ShelfAuditSession("STACK-A1-LIT", db);
            var ev1 = audit.ProcessBarcodeScan("LIB-001001"); // Pride and Prejudice (on A1) -> Verified
            var ev2 = audit.ProcessBarcodeScan("LIB-003002"); // Clean Code (on B1) -> Misplaced!
            var ev3 = audit.ProcessBarcodeScan("LIB-UNKNOWN-999"); // Uncataloged!

            if (ev1.Outcome == AuditOutcome.VerifiedPresent &&
                ev2.Outcome == AuditOutcome.Misplaced &&
                ev3.Outcome == AuditOutcome.Uncataloged)
            {
                Console.WriteLine("  [PASS] Test 6: Shelf audit event classification");
                passed++;
            }
            else
            {
                Console.WriteLine(string.Format("  [FAIL] Test 6: Outcomes: ev1={0}, ev2={1}, ev3={2}", ev1.Outcome, ev2.Outcome, ev3.Outcome));
            }

            // Test 7: Duplicate Detection
            total++;
            var dups = DuplicateDetectionEngine.ScanDuplicates(db, 0.75, true, true, true, true);
            if (dups.Count > 0)
            {
                Console.WriteLine(string.Format("  [PASS] Test 7: Duplicate detection engine ({0} candidate pairs found)", dups.Count));
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 7: Expected duplicate pairs, found 0");
            }

            // Test 8: Report Exporters
            total++;
            string html = ReportExporter.GenerateHtmlReport(db, audit, "Unit Test Report");
            string csv = ReportExporter.GenerateCsv(db);
            if (html.Contains("<!DOCTYPE html>") && csv.Contains("Barcode,ISBN"))
            {
                Console.WriteLine("  [PASS] Test 8: HTML & CSV report generators");
                passed++;
            }
            else
            {
                Console.WriteLine("  [FAIL] Test 8: Report generation failed");
            }

            Console.WriteLine(string.Format("\nTest Results: {0}/{1} tests passed.", passed, total));
            return (passed == total) ? 0 : 1;
        }
    }

    #endregion
}
