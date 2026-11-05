using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LibraryQuickDesk
{
    public class BookLoanRecord
    {
        public string Isbn { get; set; }
        public string Title { get; set; }
        public string ShelfCode { get; set; }
        public string Borrower { get; set; }
        public DateTime DueDate { get; set; }
        public bool Returned { get; set; }

        public string StatusText()
        {
            if (Returned) return "Returned";
            int days = (DueDate.Date - DateTime.Today).Days;
            if (days < 0) return string.Format("OVERDUE ({0}d)", Math.Abs(days));
            if (days == 0) return "Due Today";
            return string.Format("Due in {0}d", days);
        }
    }

    public static class IsbnHelper
    {
        public static bool IsValidIsbn(string raw, out string normalized)
        {
            normalized = new string((raw ?? "").Where(c => char.IsDigit(c) || c == 'X' || c == 'x').Select(char.ToUpperInvariant).ToArray());
            if (normalized.Length == 10)
            {
                int sum = 0;
                for (int i = 0; i < 9; i++)
                {
                    if (!char.IsDigit(normalized[i])) return false;
                    sum += (10 - i) * (normalized[i] - '0');
                }
                int last = normalized[9] == 'X' ? 10 : (char.IsDigit(normalized[9]) ? normalized[9] - '0' : -1);
                if (last < 0) return false;
                sum += last;
                return sum % 11 == 0;
            }
            if (normalized.Length == 13)
            {
                int sum = 0;
                for (int i = 0; i < 13; i++)
                {
                    if (!char.IsDigit(normalized[i])) return false;
                    int digit = normalized[i] - '0';
                    sum += (i % 2 == 0) ? digit : digit * 3;
                }
                return sum % 10 == 0;
            }
            return false;
        }
    }

    public class MainForm : Form
    {
        private List<BookLoanRecord> records = new List<BookLoanRecord>();
        private string dataFilePath;
        private TextBox txtSearch;
        private TextBox txtIsbn;
        private TextBox txtTitle;
        private TextBox txtShelf;
        private TextBox txtBorrower;
        private NumericUpDown numLoanDays;
        private ListView listView;
        private Label lblSummary;

        public MainForm(string storagePath)
        {
            dataFilePath = storagePath;
            Text = "Library QuickDesk - Catalog, ISBN Validator & Loan Tracker";
            Size = new Size(920, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            BuildUi();
            LoadRecords();
            RefreshGrid();
        }

        private void BuildUi()
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 135, Padding = new Padding(12) };

            var lblIsbn = new Label { Text = "ISBN-10/13:", Left = 12, Top = 14, Width = 85 };
            txtIsbn = new TextBox { Left = 100, Top = 11, Width = 150 };

            var btnCheckIsbn = new Button { Text = "Validate ISBN", Left = 258, Top = 9, Width = 105, Height = 27 };
            btnCheckIsbn.Click += (s, e) =>
            {
                string norm;
                bool ok = IsbnHelper.IsValidIsbn(txtIsbn.Text, out norm);
                MessageBox.Show(
                    ok ? "Valid ISBN: " + norm : "Invalid ISBN checksum or length (must be 10 or 13 digits).",
                    "ISBN Check",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };

            var lblTitle = new Label { Text = "Title:", Left = 380, Top = 14, Width = 45 };
            txtTitle = new TextBox { Left = 430, Top = 11, Width = 260 };

            var lblShelf = new Label { Text = "Shelf Code:", Left = 705, Top = 14, Width = 75 };
            txtShelf = new TextBox { Left = 785, Top = 11, Width = 100 };

            var lblBorrower = new Label { Text = "Patron:", Left = 12, Top = 52, Width = 85 };
            txtBorrower = new TextBox { Left = 100, Top = 49, Width = 263 };

            var lblDays = new Label { Text = "Loan Days:", Left = 380, Top = 52, Width = 75 };
            numLoanDays = new NumericUpDown { Left = 460, Top = 49, Width = 65, Minimum = 1, Maximum = 365, Value = 14 };

            var btnAddLoan = new Button { Text = "Check Out / Add Item", Left = 540, Top = 47, Width = 165, Height = 28 };
            btnAddLoan.Click += (s, e) => AddLoanRecord();

            var btnMarkReturned = new Button { Text = "Mark Selected Returned", Left = 715, Top = 47, Width = 170, Height = 28 };
            btnMarkReturned.Click += (s, e) => MarkSelectedReturned();

            var lblFilter = new Label { Text = "Quick Search:", Left = 12, Top = 92, Width = 85 };
            txtSearch = new TextBox { Left = 100, Top = 89, Width = 350 };
            txtSearch.TextChanged += (s, e) => RefreshGrid();

            var btnExport = new Button { Text = "Export Overdue Report (.txt)", Left = 465, Top = 87, Width = 200, Height = 28 };
            btnExport.Click += (s, e) => ExportOverdueReport();

            lblSummary = new Label { Left = 680, Top = 92, Width = 215, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

            topPanel.Controls.AddRange(new Control[] {
                lblIsbn, txtIsbn, btnCheckIsbn, lblTitle, txtTitle, lblShelf, txtShelf,
                lblBorrower, txtBorrower, lblDays, numLoanDays, btnAddLoan, btnMarkReturned,
                lblFilter, txtSearch, btnExport, lblSummary
            });

            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            listView.Columns.Add("ISBN", 130);
            listView.Columns.Add("Title", 270);
            listView.Columns.Add("Shelf Code", 100);
            listView.Columns.Add("Patron / Borrower", 160);
            listView.Columns.Add("Due Date", 105);
            listView.Columns.Add("Status", 120);

            Controls.Add(listView);
            Controls.Add(topPanel);
        }

        private void AddLoanRecord()
        {
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("Please enter a book title.", "Missing Title", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string normIsbn;
            if (!string.IsNullOrWhiteSpace(txtIsbn.Text) && IsbnHelper.IsValidIsbn(txtIsbn.Text, out normIsbn))
            {
                txtIsbn.Text = normIsbn;
            }
            records.Add(new BookLoanRecord
            {
                Isbn = string.IsNullOrWhiteSpace(txtIsbn.Text) ? "N/A" : txtIsbn.Text.Trim(),
                Title = txtTitle.Text.Trim(),
                ShelfCode = string.IsNullOrWhiteSpace(txtShelf.Text) ? "GEN-01" : txtShelf.Text.Trim(),
                Borrower = string.IsNullOrWhiteSpace(txtBorrower.Text) ? "In-Library Desk" : txtBorrower.Text.Trim(),
                DueDate = DateTime.Today.AddDays((int)numLoanDays.Value),
                Returned = false
            });
            SaveRecords();
            txtTitle.Clear();
            txtBorrower.Clear();
            RefreshGrid();
        }

        private void MarkSelectedReturned()
        {
            if (listView.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in listView.SelectedItems)
            {
                var rec = item.Tag as BookLoanRecord;
                if (rec != null) rec.Returned = true;
            }
            SaveRecords();
            RefreshGrid();
        }

        private void ExportOverdueReport()
        {
            string reportPath = Path.Combine(Path.GetDirectoryName(dataFilePath), "overdue_report.txt");
            var overdue = records.Where(r => !r.Returned && r.DueDate.Date < DateTime.Today).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("=== LIBRARY OVERDUE & ACTIVE LOANS REPORT ===");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Overdue Count: " + overdue.Count);
            sb.AppendLine("---------------------------------------------");
            foreach (var r in records.Where(x => !x.Returned).OrderBy(x => x.DueDate))
            {
                sb.AppendLine(string.Format("[{0}] {1} | ISBN: {2} | Shelf: {3} | Patron: {4} | Due: {5:yyyy-MM-dd}",
                    r.StatusText(), r.Title, r.Isbn, r.ShelfCode, r.Borrower, r.DueDate));
            }
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("Saved report to:\n" + reportPath, "Report Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoadRecords()
        {
            records.Clear();
            if (!File.Exists(dataFilePath))
            {
                records.Add(new BookLoanRecord { Isbn = "9780262033848", Title = "Introduction to Algorithms", ShelfCode = "CS-101.4", Borrower = "A. Benali", DueDate = DateTime.Today.AddDays(-3), Returned = false });
                records.Add(new BookLoanRecord { Isbn = "9780131103627", Title = "The C Programming Language", ShelfCode = "CS-102.1", Borrower = "S. El Amrani", DueDate = DateTime.Today.AddDays(5), Returned = false });
                records.Add(new BookLoanRecord { Isbn = "9780201633610", Title = "Design Patterns", ShelfCode = "SE-204.2", Borrower = "M. Tazi", DueDate = DateTime.Today, Returned = false });
                SaveRecords();
                return;
            }
            foreach (var line in File.ReadAllLines(dataFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 6) continue;
                DateTime due;
                if (!DateTime.TryParse(parts[4], out due)) due = DateTime.Today;
                records.Add(new BookLoanRecord
                {
                    Isbn = parts[0],
                    Title = parts[1],
                    ShelfCode = parts[2],
                    Borrower = parts[3],
                    DueDate = due,
                    Returned = parts[5] == "1"
                });
            }
        }

        private void SaveRecords()
        {
            var lines = records.Select(r => string.Join("\t", new[] {
                r.Isbn.Replace("\t", " "),
                r.Title.Replace("\t", " "),
                r.ShelfCode.Replace("\t", " "),
                r.Borrower.Replace("\t", " "),
                r.DueDate.ToString("yyyy-MM-dd"),
                r.Returned ? "1" : "0"
            }));
            File.WriteAllLines(dataFilePath, lines, Encoding.UTF8);
        }

        private void RefreshGrid()
        {
            string q = (txtSearch.Text ?? "").Trim().ToLowerInvariant();
            listView.BeginUpdate();
            listView.Items.Clear();
            foreach (var r in records)
            {
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = r.Title.ToLowerInvariant().Contains(q)
                        || r.Isbn.ToLowerInvariant().Contains(q)
                        || r.ShelfCode.ToLowerInvariant().Contains(q)
                        || r.Borrower.ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }
                var item = new ListViewItem(new[] {
                    r.Isbn,
                    r.Title,
                    r.ShelfCode,
                    r.Borrower,
                    r.DueDate.ToString("yyyy-MM-dd"),
                    r.StatusText()
                }) { Tag = r };
                if (!r.Returned && r.DueDate.Date < DateTime.Today)
                    item.ForeColor = Color.DarkRed;
                else if (r.Returned)
                    item.ForeColor = Color.Gray;
                listView.Items.Add(item);
            }
            listView.EndUpdate();

            int active = records.Count(x => !x.Returned);
            int overdue = records.Count(x => !x.Returned && x.DueDate.Date < DateTime.Today);
            lblSummary.Text = string.Format("Active Loans: {0} | Overdue: {1}", active, overdue);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataFile = Path.Combine(exeDir, "library_loans.tsv");

            if (args.Length > 0)
            {
                if (args[0] == "--status" || args[0] == "--cli")
                {
                    if (!File.Exists(dataFile))
                    {
                        var formInit = new MainForm(dataFile);
                        formInit.Dispose();
                    }
                    var lines = File.ReadAllLines(dataFile, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    Console.WriteLine("LibraryQuickDesk.exe - Standalone Windows Library Utility");
                    Console.WriteLine("Data file: " + dataFile);
                    Console.WriteLine("Total tracked loan entries: " + lines.Count);
                    foreach (var line in lines)
                    {
                        var p = line.Split('\t');
                        if (p.Length >= 6)
                        {
                            Console.WriteLine(string.Format(" - [{0}] {1} (ISBN: {2}, Shelf: {3}) -> Borrower: {4}, Due: {5}",
                                p[5] == "1" ? "RETURNED" : "ACTIVE", p[1], p[0], p[2], p[3], p[4]));
                        }
                    }
                    return 0;
                }
                if (args[0] == "--validate-isbn" && args.Length > 1)
                {
                    string norm;
                    bool ok = IsbnHelper.IsValidIsbn(args[1], out norm);
                    Console.WriteLine(ok ? ("VALID ISBN: " + norm) : ("INVALID ISBN: " + args[1]));
                    return ok ? 0 : 1;
                }
                Console.WriteLine("Usage:");
                Console.WriteLine("  LibraryQuickDesk.exe                  Launch Windows desktop GUI");
                Console.WriteLine("  LibraryQuickDesk.exe --status         Print active library loan summary in console");
                Console.WriteLine("  LibraryQuickDesk.exe --validate-isbn <ISBN>");
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(dataFile));
            return 0;
        }
    }
}
