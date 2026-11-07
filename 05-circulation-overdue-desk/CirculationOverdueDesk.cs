using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CirculationOverdueDesk
{
    #region Enums & Data Models

    public enum PatronCategory
    {
        Undergraduate,
        Postgraduate,
        Faculty,
        Staff,
        Community,
        Alumni
    }

    public enum PatronStatus
    {
        Active,
        Suspended,
        Expired,
        Barred
    }

    public enum LoanStatus
    {
        Active,
        Returned,
        ClaimedReturned,
        Lost
    }

    public enum OverdueTier
    {
        None,
        DueSoon,      // Courtesy Reminder (-2 to 0 days before due, or within grace)
        FirstNotice,  // Tier 1: 1 - 7 days overdue
        SecondNotice, // Tier 2: 8 - 21 days overdue (Privilege suspension warning)
        FinalDemand   // Tier 3: 22+ days overdue (Item assumed lost, billing assessed)
    }

    public enum PaymentMethod
    {
        Cash,
        CreditCard,
        StudentAccount,
        BankTransfer,
        Waived
    }

    public enum TransactionType
    {
        Charge,
        Payment,
        Waiver
    }

    public enum HoldStatus
    {
        Queued,
        AwaitingPickup,
        Fulfilled,
        Cancelled,
        Expired
    }

    public class CirculationPolicy
    {
        public PatronCategory Category { get; set; }
        public int LoanDays { get; set; }
        public int MaxItems { get; set; }
        public decimal DailyFine { get; set; }
        public int GraceDays { get; set; }
        public decimal MaxFineCap { get; set; }
        public int MaxRenewals { get; set; }
        public decimal ReplacementFee { get; set; }

        public CirculationPolicy()
        {
            LoanDays = 14;
            MaxItems = 5;
            DailyFine = 0.50m;
            GraceDays = 2;
            MaxFineCap = 15.00m;
            MaxRenewals = 2;
            ReplacementFee = 50.00m;
        }

        public static Dictionary<PatronCategory, CirculationPolicy> GetDefaultPolicies()
        {
            Dictionary<PatronCategory, CirculationPolicy> dict = new Dictionary<PatronCategory, CirculationPolicy>();

            dict[PatronCategory.Undergraduate] = new CirculationPolicy()
            {
                Category = PatronCategory.Undergraduate,
                LoanDays = 14,
                MaxItems = 5,
                DailyFine = 0.50m,
                GraceDays = 2,
                MaxFineCap = 15.00m,
                MaxRenewals = 2,
                ReplacementFee = 50.00m
            };

            dict[PatronCategory.Postgraduate] = new CirculationPolicy()
            {
                Category = PatronCategory.Postgraduate,
                LoanDays = 28,
                MaxItems = 10,
                DailyFine = 0.25m,
                GraceDays = 3,
                MaxFineCap = 20.00m,
                MaxRenewals = 4,
                ReplacementFee = 60.00m
            };

            dict[PatronCategory.Faculty] = new CirculationPolicy()
            {
                Category = PatronCategory.Faculty,
                LoanDays = 90,
                MaxItems = 25,
                DailyFine = 0.10m,
                GraceDays = 7,
                MaxFineCap = 10.00m,
                MaxRenewals = 10,
                ReplacementFee = 75.00m
            };

            dict[PatronCategory.Staff] = new CirculationPolicy()
            {
                Category = PatronCategory.Staff,
                LoanDays = 30,
                MaxItems = 8,
                DailyFine = 0.20m,
                GraceDays = 3,
                MaxFineCap = 15.00m,
                MaxRenewals = 3,
                ReplacementFee = 50.00m
            };

            dict[PatronCategory.Community] = new CirculationPolicy()
            {
                Category = PatronCategory.Community,
                LoanDays = 14,
                MaxItems = 3,
                DailyFine = 1.00m,
                GraceDays = 1,
                MaxFineCap = 25.00m,
                MaxRenewals = 1,
                ReplacementFee = 70.00m
            };

            dict[PatronCategory.Alumni] = new CirculationPolicy()
            {
                Category = PatronCategory.Alumni,
                LoanDays = 21,
                MaxItems = 4,
                DailyFine = 0.50m,
                GraceDays = 2,
                MaxFineCap = 20.00m,
                MaxRenewals = 2,
                ReplacementFee = 55.00m
            };

            return dict;
        }
    }

    public class Patron
    {
        public string PatronId { get; set; }
        public string FullName { get; set; }
        public PatronCategory Category { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Department { get; set; }
        public DateTime RegisteredDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public PatronStatus Status { get; set; }
        public string Notes { get; set; }

        public Patron()
        {
            PatronId = "";
            FullName = "";
            Category = PatronCategory.Undergraduate;
            Email = "";
            Phone = "";
            Department = "";
            RegisteredDate = DateTime.Now;
            ExpiryDate = DateTime.Now.AddYears(1);
            Status = PatronStatus.Active;
            Notes = "";
        }
    }

    public class LoanRecord
    {
        public string LoanId { get; set; }
        public string ItemBarcode { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string CallNumber { get; set; }
        public string PatronId { get; set; }
        public DateTime CheckoutDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public int RenewalCount { get; set; }
        public LoanStatus Status { get; set; }
        public decimal AssessedFine { get; set; }
        public decimal PaidFine { get; set; }
        public DateTime? LastNoticeSentDate { get; set; }
        public OverdueTier LastNoticeTier { get; set; }
        public string Notes { get; set; }

        public LoanRecord()
        {
            LoanId = "";
            ItemBarcode = "";
            Title = "";
            Author = "";
            CallNumber = "";
            PatronId = "";
            CheckoutDate = DateTime.Now;
            DueDate = DateTime.Now.AddDays(14);
            ReturnDate = null;
            RenewalCount = 0;
            Status = LoanStatus.Active;
            AssessedFine = 0m;
            PaidFine = 0m;
            LastNoticeSentDate = null;
            LastNoticeTier = OverdueTier.None;
            Notes = "";
        }
    }

    public class HoldRequest
    {
        public string HoldId { get; set; }
        public string ItemBarcode { get; set; }
        public string Title { get; set; }
        public string PatronId { get; set; }
        public DateTime RequestDate { get; set; }
        public HoldStatus Status { get; set; }
        public DateTime? PickupDeadline { get; set; }
        public bool NotificationSent { get; set; }

        public HoldRequest()
        {
            HoldId = "";
            ItemBarcode = "";
            Title = "";
            PatronId = "";
            RequestDate = DateTime.Now;
            Status = HoldStatus.Queued;
            PickupDeadline = null;
            NotificationSent = false;
        }
    }

    public class FineTransaction
    {
        public string TransactionId { get; set; }
        public DateTime Date { get; set; }
        public string LoanId { get; set; }
        public string PatronId { get; set; }
        public decimal Amount { get; set; }
        public TransactionType Type { get; set; }
        public PaymentMethod Method { get; set; }
        public string Reason { get; set; }
        public string StaffOperator { get; set; }
        public string ReceiptNumber { get; set; }

        public FineTransaction()
        {
            TransactionId = "";
            Date = DateTime.Now;
            LoanId = "";
            PatronId = "";
            Amount = 0m;
            Type = TransactionType.Charge;
            Method = PaymentMethod.Cash;
            Reason = "";
            StaffOperator = "DeskStaff";
            ReceiptNumber = "";
        }
    }

    public class OverdueAssessment
    {
        public LoanRecord Loan { get; set; }
        public Patron Patron { get; set; }
        public CirculationPolicy Policy { get; set; }
        public int DaysOverdue { get; set; }
        public OverdueTier Tier { get; set; }
        public decimal DailyRate { get; set; }
        public decimal GrossFine { get; set; }
        public decimal NetFineRemaining { get; set; }
        public bool GraceApplied { get; set; }
        public bool CappedAtMax { get; set; }
        public decimal ReplacementFeeAssessed { get; set; }

        public OverdueAssessment()
        {
            DaysOverdue = 0;
            Tier = OverdueTier.None;
            DailyRate = 0m;
            GrossFine = 0m;
            NetFineRemaining = 0m;
            GraceApplied = false;
            CappedAtMax = false;
            ReplacementFeeAssessed = 0m;
        }
    }

    #endregion

    #region Circulation Database Engine

    public class CirculationDatabase
    {
        public List<Patron> Patrons { get; private set; }
        public List<LoanRecord> Loans { get; private set; }
        public List<HoldRequest> Holds { get; private set; }
        public List<FineTransaction> Transactions { get; private set; }
        public Dictionary<PatronCategory, CirculationPolicy> Policies { get; private set; }

        public string BaseDirectory { get; private set; }

        public string PatronsFilePath { get { return Path.Combine(BaseDirectory, "patrons.tsv"); } }
        public string LoansFilePath { get { return Path.Combine(BaseDirectory, "loans.tsv"); } }
        public string HoldsFilePath { get { return Path.Combine(BaseDirectory, "holds.tsv"); } }
        public string FinesFilePath { get { return Path.Combine(BaseDirectory, "fines.tsv"); } }

        public CirculationDatabase(string baseDir = null)
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            BaseDirectory = baseDir;

            Patrons = new List<Patron>();
            Loans = new List<LoanRecord>();
            Holds = new List<HoldRequest>();
            Transactions = new List<FineTransaction>();
            Policies = CirculationPolicy.GetDefaultPolicies();
        }

        public void LoadOrSeed()
        {
            if (File.Exists(PatronsFilePath) && File.Exists(LoansFilePath))
            {
                LoadData();
            }
            else
            {
                SeedDefaultData();
                SaveAll();
            }
        }

        public void SeedDefaultData()
        {
            DateTime now = new DateTime(2026, 11, 7, 11, 30, 0);

            Patrons.Clear();
            Loans.Clear();
            Holds.Clear();
            Transactions.Clear();

            // Seed 10 realistic patrons across categories
            Patrons.Add(new Patron
            {
                PatronId = "PAT-1001",
                FullName = "Alice Martin",
                Category = PatronCategory.Undergraduate,
                Email = "alice.martin@campus.univ.dz",
                Phone = "+213 551 234 567",
                Department = "Computer Science",
                RegisteredDate = now.AddMonths(-8),
                ExpiryDate = now.AddMonths(4),
                Status = PatronStatus.Active,
                Notes = "Senior undergraduate project lead"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1002",
                FullName = "Dr. Robert Langdon",
                Category = PatronCategory.Faculty,
                Email = "r.langdon@campus.univ.dz",
                Phone = "+213 552 345 678",
                Department = "History & Iconology",
                RegisteredDate = now.AddYears(-3),
                ExpiryDate = now.AddYears(2),
                Status = PatronStatus.Active,
                Notes = "Professor of Cultural Heritage"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1003",
                FullName = "Kevin Zhang",
                Category = PatronCategory.Undergraduate,
                Email = "k.zhang@campus.univ.dz",
                Phone = "+213 553 456 789",
                Department = "Electrical Engineering",
                RegisteredDate = now.AddMonths(-6),
                ExpiryDate = now.AddMonths(6),
                Status = PatronStatus.Active,
                Notes = "Robotics Lab assistant"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1004",
                FullName = "Prof. Amina Mansouri",
                Category = PatronCategory.Faculty,
                Email = "a.mansouri@campus.univ.dz",
                Phone = "+213 554 567 890",
                Department = "Applied Mathematics",
                RegisteredDate = now.AddYears(-5),
                ExpiryDate = now.AddYears(3),
                Status = PatronStatus.Active,
                Notes = "Department Chair"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1005",
                FullName = "Sarah Connor",
                Category = PatronCategory.Community,
                Email = "s.connor@cybernet-algeria.org",
                Phone = "+213 555 678 901",
                Department = "Public Member",
                RegisteredDate = now.AddMonths(-4),
                ExpiryDate = now.AddMonths(8),
                Status = PatronStatus.Active,
                Notes = "Independent AI researcher"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1006",
                FullName = "Driss Bensalem",
                Category = PatronCategory.Postgraduate,
                Email = "d.bensalem@campus.univ.dz",
                Phone = "+213 556 789 012",
                Department = "Data Science & NLP",
                RegisteredDate = now.AddMonths(-14),
                ExpiryDate = now.AddMonths(10),
                Status = PatronStatus.Active,
                Notes = "PhD Candidate"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1007",
                FullName = "Elena Rostova",
                Category = PatronCategory.Postgraduate,
                Email = "e.rostova@campus.univ.dz",
                Phone = "+213 557 890 123",
                Department = "Theoretical Physics",
                RegisteredDate = now.AddMonths(-10),
                ExpiryDate = now.AddMonths(14),
                Status = PatronStatus.Active,
                Notes = "Exchange Fellow"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1008",
                FullName = "Tariq Haddad",
                Category = PatronCategory.Community,
                Email = "tariq.haddad@freemail.dz",
                Phone = "+213 558 901 234",
                Department = "Public Member",
                RegisteredDate = now.AddMonths(-12),
                ExpiryDate = now.AddMonths(2),
                Status = PatronStatus.Active,
                Notes = "Archival book club member"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1009",
                FullName = "Yasmine Belkacem",
                Category = PatronCategory.Undergraduate,
                Email = "y.belkacem@campus.univ.dz",
                Phone = "+213 559 012 345",
                Department = "Molecular Biology",
                RegisteredDate = now.AddMonths(-2),
                ExpiryDate = now.AddMonths(10),
                Status = PatronStatus.Active,
                Notes = "First year student"
            });

            Patrons.Add(new Patron
            {
                PatronId = "PAT-1010",
                FullName = "Marcus Vance",
                Category = PatronCategory.Alumni,
                Email = "m.vance@alumni.univ.dz",
                Phone = "+213 560 123 456",
                Department = "Literature & Philosophy",
                RegisteredDate = now.AddYears(-2),
                ExpiryDate = now.AddMonths(-1),
                Status = PatronStatus.Suspended,
                Notes = "Card expired; past unpaid replacement fee"
            });

            // Seed 12 realistic loans:
            // 1. On time active loans
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0101",
                ItemBarcode = "LIB-100101",
                Title = "Clean Code: A Handbook of Agile Software Craftsmanship",
                Author = "Robert C. Martin",
                CallNumber = "005.13 MAR",
                PatronId = "PAT-1001",
                CheckoutDate = now.AddDays(-6),
                DueDate = now.AddDays(8),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0102",
                ItemBarcode = "LIB-100102",
                Title = "Introduction to Algorithms (4th Edition)",
                Author = "Thomas H. Cormen et al.",
                CallNumber = "005.1 COR",
                PatronId = "PAT-1004",
                CheckoutDate = now.AddDays(-20),
                DueDate = now.AddDays(70),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 2. Due soon (Courtesy Reminder - due in 2 days)
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0103",
                ItemBarcode = "LIB-100106",
                Title = "The Pragmatic Programmer: Your Journey to Mastery",
                Author = "David Thomas & Andrew Hunt",
                CallNumber = "005.1 THO",
                PatronId = "PAT-1006",
                CheckoutDate = now.AddDays(-26),
                DueDate = now.AddDays(2),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 3. Overdue Tier 1: First Notice (5 days overdue, outside 2-day grace)
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0104",
                ItemBarcode = "LIB-100107",
                Title = "Operating System Concepts (10th Edition)",
                Author = "Abraham Silberschatz",
                CallNumber = "005.43 SIL",
                PatronId = "PAT-1003",
                CheckoutDate = now.AddDays(-19),
                DueDate = now.AddDays(-5),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 4. Overdue Tier 2: Second Notice (15 days overdue, card privileges warning)
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0105",
                ItemBarcode = "LIB-100109",
                Title = "Computer Networks: A Systems Approach",
                Author = "Larry L. Peterson & Bruce S. Davie",
                CallNumber = "004.6 PET",
                PatronId = "PAT-1005",
                CheckoutDate = now.AddDays(-29),
                DueDate = now.AddDays(-15),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 5. Overdue Tier 3: Final Demand / Item Assumed Lost (38 days overdue)
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0106",
                ItemBarcode = "LIB-100110",
                Title = "Compilers: Principles, Techniques, and Tools (Dragon Book)",
                Author = "Alfred V. Aho, Monica S. Lam, Ravi Sethi",
                CallNumber = "005.45 AHO",
                PatronId = "PAT-1008",
                CheckoutDate = now.AddDays(-52),
                DueDate = now.AddDays(-38),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 6. Active loan for Faculty (Langdon)
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0107",
                ItemBarcode = "LIB-100111",
                Title = "Medieval Manuscripts & Sacred Geometry",
                Author = "Umberto Eco & Robert Langdon",
                CallNumber = "091.2 ECO",
                PatronId = "PAT-1002",
                CheckoutDate = now.AddDays(-30),
                DueDate = now.AddDays(60),
                Status = LoanStatus.Active,
                RenewalCount = 1
            });

            // 7. Active loan for Elena Rostova
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0108",
                ItemBarcode = "LIB-100112",
                Title = "Modern Operating Systems (4th Edition)",
                Author = "Andrew S. Tanenbaum",
                CallNumber = "005.43 TAN",
                PatronId = "PAT-1007",
                CheckoutDate = now.AddDays(-10),
                DueDate = now.AddDays(18),
                Status = LoanStatus.Active,
                RenewalCount = 0
            });

            // 8. Returned loans with fine history
            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0095",
                ItemBarcode = "LIB-100104",
                Title = "Structure and Interpretation of Computer Programs",
                Author = "Harold Abelson & Gerald Jay Sussman",
                CallNumber = "005.13 ABE",
                PatronId = "PAT-1001",
                CheckoutDate = now.AddDays(-40),
                DueDate = now.AddDays(-26),
                ReturnDate = now.AddDays(-22),
                Status = LoanStatus.Returned,
                RenewalCount = 0,
                AssessedFine = 2.00m,
                PaidFine = 2.00m
            });

            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0096",
                ItemBarcode = "LIB-100105",
                Title = "Artificial Intelligence: A Modern Approach (4th Edition)",
                Author = "Stuart Russell & Peter Norvig",
                CallNumber = "006.3 RUS",
                PatronId = "PAT-1003",
                CheckoutDate = now.AddDays(-35),
                DueDate = now.AddDays(-21),
                ReturnDate = now.AddDays(-15),
                Status = LoanStatus.Returned,
                RenewalCount = 0,
                AssessedFine = 3.00m,
                PaidFine = 3.00m
            });

            Loans.Add(new LoanRecord
            {
                LoanId = "LN-2026-0097",
                ItemBarcode = "LIB-100108",
                Title = "Database System Concepts (7th Edition)",
                Author = "Abraham Silberschatz & Henry F. Korth",
                CallNumber = "005.74 SIL",
                PatronId = "PAT-1006",
                CheckoutDate = now.AddDays(-50),
                DueDate = now.AddDays(-22),
                ReturnDate = now.AddDays(-20),
                Status = LoanStatus.Returned,
                RenewalCount = 0,
                AssessedFine = 0.50m,
                PaidFine = 0m,
                Notes = "Grace applied; $0.50 waived for courier weather delay"
            });

            // Seed 3 hold requests (one awaiting pickup on hold shelf!)
            Holds.Add(new HoldRequest
            {
                HoldId = "HLD-2026-001",
                ItemBarcode = "LIB-100103",
                Title = "Design Patterns: Elements of Reusable Object-Oriented Software",
                PatronId = "PAT-1002",
                RequestDate = now.AddDays(-4),
                Status = HoldStatus.AwaitingPickup,
                PickupDeadline = now.AddDays(3),
                NotificationSent = true
            });

            Holds.Add(new HoldRequest
            {
                HoldId = "HLD-2026-002",
                ItemBarcode = "LIB-100105",
                Title = "Artificial Intelligence: A Modern Approach",
                PatronId = "PAT-1001",
                RequestDate = now.AddDays(-2),
                Status = HoldStatus.Queued,
                PickupDeadline = null,
                NotificationSent = false
            });

            Holds.Add(new HoldRequest
            {
                HoldId = "HLD-2026-003",
                ItemBarcode = "LIB-100107",
                Title = "Operating System Concepts",
                PatronId = "PAT-1007",
                RequestDate = now.AddDays(-1),
                Status = HoldStatus.Queued,
                PickupDeadline = null,
                NotificationSent = false
            });

            // Seed transactions
            Transactions.Add(new FineTransaction
            {
                TransactionId = "TX-2026-001",
                Date = now.AddDays(-22),
                LoanId = "LN-2026-0095",
                PatronId = "PAT-1001",
                Amount = 2.00m,
                Type = TransactionType.Payment,
                Method = PaymentMethod.Cash,
                Reason = "Late return 4 days",
                StaffOperator = "Desk-Samir",
                ReceiptNumber = "REC-20261016-001"
            });

            Transactions.Add(new FineTransaction
            {
                TransactionId = "TX-2026-002",
                Date = now.AddDays(-15),
                LoanId = "LN-2026-0096",
                PatronId = "PAT-1003",
                Amount = 3.00m,
                Type = TransactionType.Payment,
                Method = PaymentMethod.StudentAccount,
                Reason = "Late return 6 days",
                StaffOperator = "Desk-Leila",
                ReceiptNumber = "REC-20261023-002"
            });

            Transactions.Add(new FineTransaction
            {
                TransactionId = "TX-2026-003",
                Date = now.AddDays(-20),
                LoanId = "LN-2026-0097",
                PatronId = "PAT-1006",
                Amount = 0.50m,
                Type = TransactionType.Waiver,
                Method = PaymentMethod.Waived,
                Reason = "System grace / snow courier closure waiver",
                StaffOperator = "ChiefLibrarian",
                ReceiptNumber = "WAV-20261018-001"
            });
        }

        #region TSV Persistence

        public void SaveAll()
        {
            SavePatrons();
            SaveLoans();
            SaveHolds();
            SaveTransactions();
        }

        public void SavePatrons()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PatronId\tFullName\tCategory\tEmail\tPhone\tDepartment\tRegisteredDate\tExpiryDate\tStatus\tNotes");
            foreach (Patron p in Patrons)
            {
                sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}",
                    SanitizeTsv(p.PatronId),
                    SanitizeTsv(p.FullName),
                    p.Category.ToString(),
                    SanitizeTsv(p.Email),
                    SanitizeTsv(p.Phone),
                    SanitizeTsv(p.Department),
                    p.RegisteredDate.ToString("yyyy-MM-dd"),
                    p.ExpiryDate.ToString("yyyy-MM-dd"),
                    p.Status.ToString(),
                    SanitizeTsv(p.Notes)));
            }
            File.WriteAllText(PatronsFilePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveLoans()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LoanId\tItemBarcode\tTitle\tAuthor\tCallNumber\tPatronId\tCheckoutDate\tDueDate\tReturnDate\tRenewalCount\tStatus\tAssessedFine\tPaidFine\tLastNoticeSentDate\tLastNoticeTier\tNotes");
            foreach (LoanRecord l in Loans)
            {
                sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12}\t{13}\t{14}\t{15}",
                    SanitizeTsv(l.LoanId),
                    SanitizeTsv(l.ItemBarcode),
                    SanitizeTsv(l.Title),
                    SanitizeTsv(l.Author),
                    SanitizeTsv(l.CallNumber),
                    SanitizeTsv(l.PatronId),
                    l.CheckoutDate.ToString("yyyy-MM-dd"),
                    l.DueDate.ToString("yyyy-MM-dd"),
                    l.ReturnDate.HasValue ? l.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                    l.RenewalCount.ToString(),
                    l.Status.ToString(),
                    l.AssessedFine.ToString("F2"),
                    l.PaidFine.ToString("F2"),
                    l.LastNoticeSentDate.HasValue ? l.LastNoticeSentDate.Value.ToString("yyyy-MM-dd") : "",
                    l.LastNoticeTier.ToString(),
                    SanitizeTsv(l.Notes)));
            }
            File.WriteAllText(LoansFilePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveHolds()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("HoldId\tItemBarcode\tTitle\tPatronId\tRequestDate\tStatus\tPickupDeadline\tNotificationSent");
            foreach (HoldRequest h in Holds)
            {
                sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}",
                    SanitizeTsv(h.HoldId),
                    SanitizeTsv(h.ItemBarcode),
                    SanitizeTsv(h.Title),
                    SanitizeTsv(h.PatronId),
                    h.RequestDate.ToString("yyyy-MM-dd"),
                    h.Status.ToString(),
                    h.PickupDeadline.HasValue ? h.PickupDeadline.Value.ToString("yyyy-MM-dd") : "",
                    h.NotificationSent ? "1" : "0"));
            }
            File.WriteAllText(HoldsFilePath, sb.ToString(), Encoding.UTF8);
        }

        public void SaveTransactions()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TransactionId\tDate\tLoanId\tPatronId\tAmount\tType\tMethod\tReason\tStaffOperator\tReceiptNumber");
            foreach (FineTransaction t in Transactions)
            {
                sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}",
                    SanitizeTsv(t.TransactionId),
                    t.Date.ToString("yyyy-MM-dd HH:mm"),
                    SanitizeTsv(t.LoanId),
                    SanitizeTsv(t.PatronId),
                    t.Amount.ToString("F2"),
                    t.Type.ToString(),
                    t.Method.ToString(),
                    SanitizeTsv(t.Reason),
                    SanitizeTsv(t.StaffOperator),
                    SanitizeTsv(t.ReceiptNumber)));
            }
            File.WriteAllText(FinesFilePath, sb.ToString(), Encoding.UTF8);
        }

        public void LoadData()
        {
            // Load Patrons
            if (File.Exists(PatronsFilePath))
            {
                Patrons.Clear();
                string[] lines = File.ReadAllLines(PatronsFilePath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 9) continue;

                    Patron p = new Patron();
                    p.PatronId = parts[0].Trim();
                    p.FullName = parts[1].Trim();
                    try { p.Category = (PatronCategory)Enum.Parse(typeof(PatronCategory), parts[2].Trim(), true); }
                    catch { p.Category = PatronCategory.Undergraduate; }
                    p.Email = parts[3].Trim();
                    p.Phone = parts[4].Trim();
                    p.Department = parts[5].Trim();

                    DateTime regDate, expDate;
                    if (DateTime.TryParse(parts[6].Trim(), out regDate)) p.RegisteredDate = regDate;
                    if (DateTime.TryParse(parts[7].Trim(), out expDate)) p.ExpiryDate = expDate;

                    try { p.Status = (PatronStatus)Enum.Parse(typeof(PatronStatus), parts[8].Trim(), true); }
                    catch { p.Status = PatronStatus.Active; }

                    if (parts.Length > 9) p.Notes = parts[9].Trim();
                    Patrons.Add(p);
                }
            }

            // Load Loans
            if (File.Exists(LoansFilePath))
            {
                Loans.Clear();
                string[] lines = File.ReadAllLines(LoansFilePath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 11) continue;

                    LoanRecord l = new LoanRecord();
                    l.LoanId = parts[0].Trim();
                    l.ItemBarcode = parts[1].Trim();
                    l.Title = parts[2].Trim();
                    l.Author = parts[3].Trim();
                    l.CallNumber = parts[4].Trim();
                    l.PatronId = parts[5].Trim();

                    DateTime chkDate, dueDate;
                    if (DateTime.TryParse(parts[6].Trim(), out chkDate)) l.CheckoutDate = chkDate;
                    if (DateTime.TryParse(parts[7].Trim(), out dueDate)) l.DueDate = dueDate;

                    if (!string.IsNullOrEmpty(parts[8].Trim()))
                    {
                        DateTime retDate;
                        if (DateTime.TryParse(parts[8].Trim(), out retDate)) l.ReturnDate = retDate;
                    }

                    int renewals;
                    if (int.TryParse(parts[9].Trim(), out renewals)) l.RenewalCount = renewals;

                    try { l.Status = (LoanStatus)Enum.Parse(typeof(LoanStatus), parts[10].Trim(), true); }
                    catch { l.Status = LoanStatus.Active; }

                    decimal assessed, paid;
                    if (parts.Length > 11 && decimal.TryParse(parts[11].Trim(), out assessed)) l.AssessedFine = assessed;
                    if (parts.Length > 12 && decimal.TryParse(parts[12].Trim(), out paid)) l.PaidFine = paid;

                    if (parts.Length > 13 && !string.IsNullOrEmpty(parts[13].Trim()))
                    {
                        DateTime noticeDate;
                        if (DateTime.TryParse(parts[13].Trim(), out noticeDate)) l.LastNoticeSentDate = noticeDate;
                    }

                    if (parts.Length > 14 && !string.IsNullOrEmpty(parts[14].Trim()))
                    {
                        try { l.LastNoticeTier = (OverdueTier)Enum.Parse(typeof(OverdueTier), parts[14].Trim(), true); }
                        catch { l.LastNoticeTier = OverdueTier.None; }
                    }

                    if (parts.Length > 15) l.Notes = parts[15].Trim();
                    Loans.Add(l);
                }
            }

            // Load Holds
            if (File.Exists(HoldsFilePath))
            {
                Holds.Clear();
                string[] lines = File.ReadAllLines(HoldsFilePath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 6) continue;

                    HoldRequest h = new HoldRequest();
                    h.HoldId = parts[0].Trim();
                    h.ItemBarcode = parts[1].Trim();
                    h.Title = parts[2].Trim();
                    h.PatronId = parts[3].Trim();

                    DateTime reqDate;
                    if (DateTime.TryParse(parts[4].Trim(), out reqDate)) h.RequestDate = reqDate;

                    try { h.Status = (HoldStatus)Enum.Parse(typeof(HoldStatus), parts[5].Trim(), true); }
                    catch { h.Status = HoldStatus.Queued; }

                    if (parts.Length > 6 && !string.IsNullOrEmpty(parts[6].Trim()))
                    {
                        DateTime deadline;
                        if (DateTime.TryParse(parts[6].Trim(), out deadline)) h.PickupDeadline = deadline;
                    }

                    if (parts.Length > 7)
                    {
                        h.NotificationSent = (parts[7].Trim() == "1" || parts[7].Trim().ToLower() == "true");
                    }
                    Holds.Add(h);
                }
            }

            // Load Fines
            if (File.Exists(FinesFilePath))
            {
                Transactions.Clear();
                string[] lines = File.ReadAllLines(FinesFilePath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 6) continue;

                    FineTransaction t = new FineTransaction();
                    t.TransactionId = parts[0].Trim();

                    DateTime txDate;
                    if (DateTime.TryParse(parts[1].Trim(), out txDate)) t.Date = txDate;

                    t.LoanId = parts[2].Trim();
                    t.PatronId = parts[3].Trim();

                    decimal amt;
                    if (decimal.TryParse(parts[4].Trim(), out amt)) t.Amount = amt;

                    try { t.Type = (TransactionType)Enum.Parse(typeof(TransactionType), parts[5].Trim(), true); }
                    catch { t.Type = TransactionType.Charge; }

                    if (parts.Length > 6)
                    {
                        try { t.Method = (PaymentMethod)Enum.Parse(typeof(PaymentMethod), parts[6].Trim(), true); }
                        catch { t.Method = PaymentMethod.Cash; }
                    }

                    if (parts.Length > 7) t.Reason = parts[7].Trim();
                    if (parts.Length > 8) t.StaffOperator = parts[8].Trim();
                    if (parts.Length > 9) t.ReceiptNumber = parts[9].Trim();

                    Transactions.Add(t);
                }
            }
        }

        private string SanitizeTsv(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        #endregion

        #region Core Circulation Operations

        public Patron FindPatron(string patronIdOrQuery)
        {
            if (string.IsNullOrEmpty(patronIdOrQuery)) return null;
            string q = patronIdOrQuery.Trim().ToLowerInvariant();

            // Direct ID match
            Patron p = Patrons.FirstOrDefault(x => x.PatronId.ToLowerInvariant() == q);
            if (p != null) return p;

            // Name or email match
            p = Patrons.FirstOrDefault(x => x.FullName.ToLowerInvariant().Contains(q) || x.Email.ToLowerInvariant().Contains(q));
            return p;
        }

        public List<LoanRecord> GetActiveLoansForPatron(string patronId)
        {
            return Loans.Where(l => l.PatronId.Equals(patronId, StringComparison.OrdinalIgnoreCase) && l.Status == LoanStatus.Active).ToList();
        }

        public decimal GetOutstandingBalanceForPatron(string patronId, DateTime asOfDate)
        {
            decimal totalDue = 0m;

            // Outstanding unpaid fines on active loans
            List<LoanRecord> activeLoans = GetActiveLoansForPatron(patronId);
            foreach (LoanRecord l in activeLoans)
            {
                OverdueAssessment assess = AssessOverdue(l, asOfDate);
                totalDue += assess.NetFineRemaining;
            }

            // Past unpaid assessed fines on returned/lost loans
            List<LoanRecord> historicLoans = Loans.Where(l => l.PatronId.Equals(patronId, StringComparison.OrdinalIgnoreCase) && l.Status != LoanStatus.Active).ToList();
            foreach (LoanRecord l in historicLoans)
            {
                decimal unpd = l.AssessedFine - l.PaidFine;
                if (unpd > 0) totalDue += unpd;
            }

            return totalDue;
        }

        public CirculationPolicy GetPolicy(PatronCategory cat)
        {
            if (Policies.ContainsKey(cat))
                return Policies[cat];
            return Policies[PatronCategory.Undergraduate];
        }

        public bool CheckoutItem(string barcode, string title, string author, string callNumber, string patronId, out LoanRecord createdLoan, out string errorMsg)
        {
            createdLoan = null;
            errorMsg = "";

            if (string.IsNullOrEmpty(barcode))
            {
                errorMsg = "Item Barcode cannot be empty.";
                return false;
            }
            if (string.IsNullOrEmpty(patronId))
            {
                errorMsg = "Patron ID cannot be empty.";
                return false;
            }

            Patron patron = FindPatron(patronId);
            if (patron == null)
            {
                errorMsg = string.Format("Patron ID '{0}' not found in the roster.", patronId);
                return false;
            }

            if (patron.Status == PatronStatus.Suspended)
            {
                errorMsg = string.Format("Checkout blocked: Patron '{0}' ({1}) is SUSPENDED.", patron.FullName, patron.PatronId);
                return false;
            }
            if (patron.Status == PatronStatus.Barred)
            {
                errorMsg = string.Format("Checkout blocked: Patron '{0}' is BARRED from library services.", patron.FullName);
                return false;
            }
            if (patron.ExpiryDate < DateTime.Now.Date)
            {
                errorMsg = string.Format("Checkout blocked: Patron card EXPIRED on {0}.", patron.ExpiryDate.ToString("yyyy-MM-dd"));
                return false;
            }

            decimal balance = GetOutstandingBalanceForPatron(patron.PatronId, DateTime.Now);
            if (balance >= 10.00m)
            {
                errorMsg = string.Format("Checkout blocked: Patron has outstanding unpaid fines of ${0:F2} (Block threshold is $10.00).", balance);
                return false;
            }

            CirculationPolicy policy = GetPolicy(patron.Category);
            List<LoanRecord> activeLoans = GetActiveLoansForPatron(patron.PatronId);
            if (activeLoans.Count >= policy.MaxItems)
            {
                errorMsg = string.Format("Checkout blocked: Patron has reached max allowed items ({0}/{1} active loans).", activeLoans.Count, policy.MaxItems);
                return false;
            }

            // Check if item is already checked out
            LoanRecord existingLoan = Loans.FirstOrDefault(l => l.ItemBarcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) && l.Status == LoanStatus.Active);
            if (existingLoan != null)
            {
                errorMsg = string.Format("Item '{0}' is already checked out to {1} (Due: {2}).", barcode, existingLoan.PatronId, existingLoan.DueDate.ToString("yyyy-MM-dd"));
                return false;
            }

            // Check if item is currently on the Hold shelf awaiting pickup by someone ELSE
            HoldRequest pendingHold = Holds.FirstOrDefault(h => h.ItemBarcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) && h.Status == HoldStatus.AwaitingPickup);
            if (pendingHold != null && !pendingHold.PatronId.Equals(patron.PatronId, StringComparison.OrdinalIgnoreCase))
            {
                errorMsg = string.Format("Item '{0}' is currently on the HOLD SHELF reserved for {1} until {2}.",
                    barcode, pendingHold.PatronId, pendingHold.PickupDeadline.HasValue ? pendingHold.PickupDeadline.Value.ToString("yyyy-MM-dd") : "soon");
                return false;
            }

            // If it was on hold for THIS patron, fulfill the hold
            if (pendingHold != null && pendingHold.PatronId.Equals(patron.PatronId, StringComparison.OrdinalIgnoreCase))
            {
                pendingHold.Status = HoldStatus.Fulfilled;
            }

            DateTime checkoutDate = DateTime.Now;
            DateTime dueDate = checkoutDate.AddDays(policy.LoanDays);

            string loanId = string.Format("LN-{0}-{1:D4}", checkoutDate.Year, Loans.Count + 101);
            LoanRecord newLoan = new LoanRecord
            {
                LoanId = loanId,
                ItemBarcode = barcode.Trim().ToUpperInvariant(),
                Title = string.IsNullOrEmpty(title) ? ("Book Item " + barcode) : title.Trim(),
                Author = string.IsNullOrEmpty(author) ? "Cataloged Author" : author.Trim(),
                CallNumber = string.IsNullOrEmpty(callNumber) ? "000 GEN" : callNumber.Trim(),
                PatronId = patron.PatronId,
                CheckoutDate = checkoutDate,
                DueDate = dueDate,
                Status = LoanStatus.Active,
                RenewalCount = 0
            };

            Loans.Add(newLoan);
            SaveLoans();
            if (pendingHold != null) SaveHolds();

            createdLoan = newLoan;
            return true;
        }

        public bool ReturnItem(string barcode, DateTime returnDate, out string summaryMsg, out HoldRequest holdTriggered, out decimal assessedFine)
        {
            summaryMsg = "";
            holdTriggered = null;
            assessedFine = 0m;

            if (string.IsNullOrEmpty(barcode))
            {
                summaryMsg = "Item Barcode cannot be empty.";
                return false;
            }

            LoanRecord loan = Loans.FirstOrDefault(l => l.ItemBarcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) && l.Status == LoanStatus.Active);
            if (loan == null)
            {
                summaryMsg = string.Format("No active loan found for barcode '{0}'. Item may already be returned or not in circulation.", barcode);
                return false;
            }

            loan.ReturnDate = returnDate;
            loan.Status = LoanStatus.Returned;

            OverdueAssessment assess = AssessOverdue(loan, returnDate);
            if (assess.NetFineRemaining > 0)
            {
                loan.AssessedFine = assess.GrossFine;
                assessedFine = assess.GrossFine;

                // Record fine charge transaction
                FineTransaction charge = new FineTransaction
                {
                    TransactionId = string.Format("TX-{0}-{1:D4}", DateTime.Now.Year, Transactions.Count + 1),
                    Date = returnDate,
                    LoanId = loan.LoanId,
                    PatronId = loan.PatronId,
                    Amount = assess.GrossFine,
                    Type = TransactionType.Charge,
                    Method = PaymentMethod.Cash,
                    Reason = string.Format("Overdue fine: {0} days late ({1})", assess.DaysOverdue, assess.Tier.ToString()),
                    StaffOperator = "CirculationDesk",
                    ReceiptNumber = string.Format("CHG-{0}-{1:D4}", DateTime.Now.ToString("yyyyMMdd"), Transactions.Count + 1)
                };
                Transactions.Add(charge);
                SaveTransactions();
            }

            // Check if there is a queued hold for this item
            HoldRequest nextHold = Holds.FirstOrDefault(h => h.ItemBarcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) && h.Status == HoldStatus.Queued);
            if (nextHold != null)
            {
                nextHold.Status = HoldStatus.AwaitingPickup;
                nextHold.PickupDeadline = returnDate.AddDays(7);
                holdTriggered = nextHold;
                SaveHolds();
            }

            SaveLoans();

            if (assessedFine > 0)
            {
                summaryMsg = string.Format("Returned: '{0}' by {1}. OVERDUE: {2} days late. Fine of ${3:F2} charged to {4}.",
                    loan.Title, loan.PatronId, assess.DaysOverdue, assessedFine, loan.PatronId);
            }
            else
            {
                summaryMsg = string.Format("Returned on time: '{0}' by {1}. No fine assessed.", loan.Title, loan.PatronId);
            }

            if (holdTriggered != null)
            {
                summaryMsg += string.Format(" [HOLD ALERT]: Reserved for {0}! Hold on shelf until {1}.",
                    holdTriggered.PatronId, holdTriggered.PickupDeadline.Value.ToString("yyyy-MM-dd"));
            }

            return true;
        }

        public bool RenewLoan(string barcodeOrLoanId, out string errorMsg, out DateTime newDueDate)
        {
            errorMsg = "";
            newDueDate = DateTime.MinValue;

            if (string.IsNullOrEmpty(barcodeOrLoanId))
            {
                errorMsg = "Barcode or Loan ID cannot be empty.";
                return false;
            }

            string q = barcodeOrLoanId.Trim().ToUpperInvariant();
            LoanRecord loan = Loans.FirstOrDefault(l => (l.ItemBarcode.ToUpperInvariant() == q || l.LoanId.ToUpperInvariant() == q) && l.Status == LoanStatus.Active);
            if (loan == null)
            {
                errorMsg = string.Format("Active loan '{0}' not found.", barcodeOrLoanId);
                return false;
            }

            Patron patron = FindPatron(loan.PatronId);
            CirculationPolicy policy = GetPolicy(patron != null ? patron.Category : PatronCategory.Undergraduate);

            if (loan.RenewalCount >= policy.MaxRenewals)
            {
                errorMsg = string.Format("Renewal limit reached: Loan has already been renewed {0} times (Max: {1}).", loan.RenewalCount, policy.MaxRenewals);
                return false;
            }

            // Check if there is a pending hold for this title from another user
            HoldRequest hold = Holds.FirstOrDefault(h => h.ItemBarcode.Equals(loan.ItemBarcode, StringComparison.OrdinalIgnoreCase) && (h.Status == HoldStatus.Queued || h.Status == HoldStatus.AwaitingPickup));
            if (hold != null && !hold.PatronId.Equals(loan.PatronId, StringComparison.OrdinalIgnoreCase))
            {
                errorMsg = string.Format("Cannot renew: Item '{0}' has a pending reservation hold for patron {1}.", loan.ItemBarcode, hold.PatronId);
                return false;
            }

            // Check if account has severe overdue
            DateTime now = DateTime.Now;
            if ((now.Date - loan.DueDate.Date).Days > 7)
            {
                errorMsg = string.Format("Cannot renew: Book is more than 7 days overdue ({0} days). Must be presented at desk for return or replacement.", (now.Date - loan.DueDate.Date).Days);
                return false;
            }

            loan.DueDate = (loan.DueDate > now ? loan.DueDate : now).AddDays(policy.LoanDays);
            loan.RenewalCount++;
            SaveLoans();

            newDueDate = loan.DueDate;
            return true;
        }

        public bool PlaceHold(string barcode, string title, string patronId, out HoldRequest hold, out string errorMsg)
        {
            hold = null;
            errorMsg = "";

            if (string.IsNullOrEmpty(barcode) || string.IsNullOrEmpty(patronId))
            {
                errorMsg = "Barcode and Patron ID cannot be empty.";
                return false;
            }

            Patron p = FindPatron(patronId);
            if (p == null)
            {
                errorMsg = string.Format("Patron ID '{0}' not found.", patronId);
                return false;
            }

            // Check if already on hold by this patron
            HoldRequest existing = Holds.FirstOrDefault(h => h.ItemBarcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) && h.PatronId.Equals(p.PatronId, StringComparison.OrdinalIgnoreCase) && (h.Status == HoldStatus.Queued || h.Status == HoldStatus.AwaitingPickup));
            if (existing != null)
            {
                errorMsg = string.Format("Patron {0} already has an active hold on this item.", p.PatronId);
                return false;
            }

            hold = new HoldRequest
            {
                HoldId = string.Format("HLD-{0}-{1:D3}", DateTime.Now.Year, Holds.Count + 1),
                ItemBarcode = barcode.Trim().ToUpperInvariant(),
                Title = string.IsNullOrEmpty(title) ? ("Book " + barcode) : title.Trim(),
                PatronId = p.PatronId,
                RequestDate = DateTime.Now,
                Status = HoldStatus.Queued
            };

            Holds.Add(hold);
            SaveHolds();
            return true;
        }

        public bool RecordPayment(string patronId, string loanId, decimal amount, PaymentMethod method, string staff, string reason, out string receiptNum, out string errorMsg)
        {
            receiptNum = "";
            errorMsg = "";

            if (amount <= 0)
            {
                errorMsg = "Payment amount must be greater than zero.";
                return false;
            }

            receiptNum = string.Format("REC-{0}-{1:D4}", DateTime.Now.ToString("yyyyMMdd"), Transactions.Count + 1);

            FineTransaction tx = new FineTransaction
            {
                TransactionId = string.Format("TX-{0}-{1:D4}", DateTime.Now.Year, Transactions.Count + 1),
                Date = DateTime.Now,
                LoanId = string.IsNullOrEmpty(loanId) ? "ACC-GENERAL" : loanId,
                PatronId = patronId,
                Amount = amount,
                Type = TransactionType.Payment,
                Method = method,
                Reason = string.IsNullOrEmpty(reason) ? "Fine payment" : reason,
                StaffOperator = string.IsNullOrEmpty(staff) ? "CashierDesk" : staff,
                ReceiptNumber = receiptNum
            };

            Transactions.Add(tx);

            // If linked to loan, credit the loan's PaidFine
            if (!string.IsNullOrEmpty(loanId))
            {
                LoanRecord loan = Loans.FirstOrDefault(l => l.LoanId.Equals(loanId, StringComparison.OrdinalIgnoreCase));
                if (loan != null)
                {
                    loan.PaidFine += amount;
                    SaveLoans();
                }
            }

            SaveTransactions();
            return true;
        }

        public bool WaiveFine(string patronId, string loanId, decimal amount, string reason, string staff, out string receiptNum, out string errorMsg)
        {
            receiptNum = "";
            errorMsg = "";

            if (amount <= 0)
            {
                errorMsg = "Waive amount must be greater than zero.";
                return false;
            }
            if (string.IsNullOrEmpty(reason))
            {
                errorMsg = "A waiver justification reason is mandatory (e.g., Medical excuse, System grace).";
                return false;
            }

            receiptNum = string.Format("WAV-{0}-{1:D4}", DateTime.Now.ToString("yyyyMMdd"), Transactions.Count + 1);

            FineTransaction tx = new FineTransaction
            {
                TransactionId = string.Format("TX-{0}-{1:D4}", DateTime.Now.Year, Transactions.Count + 1),
                Date = DateTime.Now,
                LoanId = string.IsNullOrEmpty(loanId) ? "ACC-GENERAL" : loanId,
                PatronId = patronId,
                Amount = amount,
                Type = TransactionType.Waiver,
                Method = PaymentMethod.Waived,
                Reason = reason,
                StaffOperator = string.IsNullOrEmpty(staff) ? "Supervisor" : staff,
                ReceiptNumber = receiptNum
            };

            Transactions.Add(tx);

            if (!string.IsNullOrEmpty(loanId))
            {
                LoanRecord loan = Loans.FirstOrDefault(l => l.LoanId.Equals(loanId, StringComparison.OrdinalIgnoreCase));
                if (loan != null)
                {
                    loan.PaidFine += amount;
                    SaveLoans();
                }
            }

            SaveTransactions();
            return true;
        }

        #endregion

        #region Overdue & Policy Assessment Engine

        public OverdueAssessment AssessOverdue(LoanRecord loan, DateTime asOfDate)
        {
            OverdueAssessment assessment = new OverdueAssessment();
            assessment.Loan = loan;

            Patron patron = FindPatron(loan.PatronId);
            assessment.Patron = patron;

            CirculationPolicy policy = GetPolicy(patron != null ? patron.Category : PatronCategory.Undergraduate);
            assessment.Policy = policy;
            assessment.DailyRate = policy.DailyFine;

            // Compute days difference between asOfDate (or ReturnDate) and DueDate
            DateTime referenceDate = loan.ReturnDate.HasValue ? loan.ReturnDate.Value : asOfDate;
            int daysDiff = (referenceDate.Date - loan.DueDate.Date).Days;
            assessment.DaysOverdue = daysDiff;

            if (daysDiff <= 0)
            {
                // Not overdue yet
                if (daysDiff >= -2 && loan.Status == LoanStatus.Active)
                {
                    assessment.Tier = OverdueTier.DueSoon;
                }
                else
                {
                    assessment.Tier = OverdueTier.None;
                }
                assessment.GrossFine = 0m;
                assessment.NetFineRemaining = 0m;
                return assessment;
            }

            // Days overdue > 0
            if (daysDiff <= policy.GraceDays)
            {
                assessment.GraceApplied = true;
                assessment.GrossFine = 0m;
                assessment.Tier = OverdueTier.DueSoon;
            }
            else
            {
                // Grace exceeded: all days are billed
                decimal rawFine = daysDiff * policy.DailyFine;
                if (rawFine > policy.MaxFineCap)
                {
                    assessment.GrossFine = policy.MaxFineCap;
                    assessment.CappedAtMax = true;
                }
                else
                {
                    assessment.GrossFine = rawFine;
                }

                // Determine escalation tier
                if (daysDiff <= 7)
                {
                    assessment.Tier = OverdueTier.FirstNotice;
                }
                else if (daysDiff <= 21)
                {
                    assessment.Tier = OverdueTier.SecondNotice;
                }
                else
                {
                    assessment.Tier = OverdueTier.FinalDemand;
                    // In Final Demand, item is assessed replacement fee
                    assessment.ReplacementFeeAssessed = policy.ReplacementFee;
                    assessment.GrossFine += policy.ReplacementFee;
                }
            }

            decimal net = assessment.GrossFine - loan.PaidFine;
            if (net < 0) net = 0m;
            assessment.NetFineRemaining = net;

            return assessment;
        }

        public List<OverdueAssessment> AssessAllOverdues(DateTime asOfDate)
        {
            List<OverdueAssessment> list = new List<OverdueAssessment>();
            foreach (LoanRecord loan in Loans)
            {
                if (loan.Status == LoanStatus.Active)
                {
                    OverdueAssessment assess = AssessOverdue(loan, asOfDate);
                    if (assess.Tier != OverdueTier.None)
                    {
                        list.Add(assess);
                    }
                }
            }
            return list.OrderByDescending(a => a.DaysOverdue).ToList();
        }

        #endregion
    }

    #endregion

    #region Multi-Channel Notice Generation Engine

    public static class NoticeGenerator
    {
        public static string GeneratePrintableLetterHtml(OverdueAssessment a, DateTime asOfDate)
        {
            Patron p = a.Patron ?? new Patron { FullName = "Valued Patron", PatronId = a.Loan.PatronId };
            LoanRecord l = a.Loan;

            string tierBadge = "";
            string tierTitle = "";
            string tierDesc = "";
            string headerBg = "#1e3a5f";

            switch (a.Tier)
            {
                case OverdueTier.DueSoon:
                    tierBadge = "COURTESY REMINDER";
                    tierTitle = "Library Courtesy Notice: Book Due Soon";
                    tierDesc = "This is a courtesy reminder that your borrowed library item is due in 1-2 days. If you require more time, please renew online or visit the circulation desk.";
                    headerBg = "#2563eb";
                    break;
                case OverdueTier.FirstNotice:
                    tierBadge = "1ST OVERDUE NOTICE";
                    tierTitle = "First Notice: Overdue Library Item";
                    tierDesc = "According to our circulation records, the following item is now past due. Please return or renew this item promptly to prevent further late fee accumulation.";
                    headerBg = "#d97706";
                    break;
                case OverdueTier.SecondNotice:
                    tierBadge = "SECOND NOTICE: SUSPENSION WARNING";
                    tierTitle = "Second Notice: Privileges Suspended";
                    tierDesc = "Your borrowed item is significantly overdue. In accordance with university library regulations, your borrowing privileges are currently SUSPENDED until this item is returned and fine balance resolved.";
                    headerBg = "#ea580c";
                    break;
                case OverdueTier.FinalDemand:
                    tierBadge = "FINAL DEMAND & BILLING NOTICE";
                    tierTitle = "Final Demand: Item Assumed Lost";
                    tierDesc = "This item is now more than 21 days past due and has been declared ASSUMED LOST. A replacement assessment plus cataloging processing fee has been added to your student/patron account.";
                    headerBg = "#b91c1c";
                    break;
                default:
                    tierBadge = "CIRCULATION NOTICE";
                    tierTitle = "Library Notice";
                    tierDesc = "Notice regarding your borrowed items.";
                    break;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\">");
            sb.AppendLine(string.Format("  <title>{0} - {1} ({2})</title>", tierBadge, p.FullName, l.ItemBarcode));
            sb.AppendLine("  <style>");
            sb.AppendLine("    * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif; }");
            sb.AppendLine("    body { background: #f1f5f9; padding: 30px; color: #1e293b; }");
            sb.AppendLine("    .page { max-width: 800px; margin: 0 auto; background: #ffffff; border-radius: 8px; box-shadow: 0 4px 14px rgba(0,0,0,0.08); overflow: hidden; border: 1px solid #e2e8f0; }");
            sb.AppendLine("    .header { background: " + headerBg + "; color: #ffffff; padding: 28px 36px; display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("    .header h1 { font-size: 20px; font-weight: 700; letter-spacing: 0.5px; }");
            sb.AppendLine("    .header p { font-size: 13px; opacity: 0.85; margin-top: 4px; }");
            sb.AppendLine("    .badge { background: rgba(255,255,255,0.22); color: #fff; padding: 6px 14px; border-radius: 20px; font-weight: 700; font-size: 12px; letter-spacing: 1px; text-transform: uppercase; border: 1px solid rgba(255,255,255,0.4); }");
            sb.AppendLine("    .content { padding: 36px; }");
            sb.AppendLine("    .meta-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 28px; background: #f8fafc; padding: 18px; border-radius: 6px; border-left: 4px solid " + headerBg + "; }");
            sb.AppendLine("    .meta-box h4 { font-size: 11px; text-transform: uppercase; color: #64748b; margin-bottom: 4px; letter-spacing: 0.5px; }");
            sb.AppendLine("    .meta-box p { font-size: 14px; font-weight: 600; color: #0f172a; }");
            sb.AppendLine("    .desc-box { font-size: 14px; line-height: 1.6; color: #334155; margin-bottom: 28px; padding-bottom: 20px; border-bottom: 1px dashed #cbd5e1; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; margin-bottom: 28px; font-size: 13px; }");
            sb.AppendLine("    th { background: #f1f5f9; color: #475569; text-align: left; padding: 12px; font-weight: 600; border-bottom: 2px solid #cbd5e1; }");
            sb.AppendLine("    td { padding: 12px; border-bottom: 1px solid #e2e8f0; color: #1e293b; }");
            sb.AppendLine("    .right { text-align: right; }");
            sb.AppendLine("    .total-box { margin-left: auto; width: 300px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; margin-bottom: 28px; }");
            sb.AppendLine("    .total-row { display: flex; justify-content: space-between; font-size: 13px; margin-bottom: 6px; color: #475569; }");
            sb.AppendLine("    .total-row.grand { font-size: 16px; font-weight: 700; color: #b91c1c; border-top: 2px solid #cbd5e1; padding-top: 8px; margin-top: 8px; }");
            sb.AppendLine("    .instructions { background: #f0fdf4; border: 1px solid #bbf7d0; padding: 18px; border-radius: 6px; font-size: 13px; color: #166534; line-height: 1.5; margin-bottom: 28px; }");
            sb.AppendLine("    .footer { background: #f8fafc; padding: 20px 36px; border-top: 1px solid #e2e8f0; display: flex; justify-content: space-between; align-items: center; font-size: 12px; color: #64748b; }");
            sb.AppendLine("    .barcode { font-family: 'Courier New', monospace; font-size: 18px; letter-spacing: 4px; font-weight: 700; color: #0f172a; }");
            sb.AppendLine("    .no-print { display: flex; gap: 10px; margin-bottom: 20px; justify-content: center; }");
            sb.AppendLine("    .btn { background: #0f172a; color: #fff; border: none; padding: 10px 20px; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 600; text-decoration: none; display: inline-flex; align-items: center; gap: 6px; }");
            sb.AppendLine("    .btn:hover { background: #334155; }");
            sb.AppendLine("    @media print { body { background: #fff; padding: 0; } .page { box-shadow: none; border: none; max-width: 100%; } .no-print { display: none; } }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("  <div class=\"no-print\">");
            sb.AppendLine("    <button class=\"btn\" onclick=\"window.print()\">🖨 Print Official Letter / Save as PDF</button>");
            sb.AppendLine(string.Format("    <a class=\"btn\" style=\"background:#25D366;\" href=\"{0}\" target=\"_blank\">💬 Open WhatsApp Web Dispatch</a>", GenerateWhatsAppUrl(a)));
            sb.AppendLine("  </div>");

            sb.AppendLine("  <div class=\"page\">");
            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("      <div>");
            sb.AppendLine("        <h1>UNIVERSITY & CENTRAL LIBRARY</h1>");
            sb.AppendLine("        <p>Circulation & Patron Lending Services Desk</p>");
            sb.AppendLine("      </div>");
            sb.AppendLine(string.Format("      <div class=\"badge\">{0}</div>", tierBadge));
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"content\">");
            sb.AppendLine("      <div class=\"meta-grid\">");
            sb.AppendLine("        <div class=\"meta-box\">");
            sb.AppendLine("          <h4>Patron Details</h4>");
            sb.AppendLine(string.Format("          <p>{0} ({1})</p>", WebUtility.HtmlEncode(p.FullName), WebUtility.HtmlEncode(p.PatronId)));
            sb.AppendLine(string.Format("          <div style=\"font-size:12px; color:#64748b; margin-top:3px;\">{0} &bull; {1}</div>", WebUtility.HtmlEncode(p.Department), p.Category.ToString()));
            sb.AppendLine(string.Format("          <div style=\"font-size:12px; color:#64748b;\">{0} &bull; {1}</div>", WebUtility.HtmlEncode(p.Email), WebUtility.HtmlEncode(p.Phone)));
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"meta-box\">");
            sb.AppendLine("          <h4>Notice Information</h4>");
            sb.AppendLine(string.Format("          <p>Date: {0}</p>", asOfDate.ToString("MMMM dd, yyyy")));
            sb.AppendLine(string.Format("          <div style=\"font-size:12px; color:#64748b; margin-top:3px;\">Reference: {0}</div>", l.LoanId));
            sb.AppendLine(string.Format("          <div style=\"font-size:12px; color:#64748b;\">Status: <strong>{0}</strong></div>", p.Status.ToString()));
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");

            sb.AppendLine(string.Format("      <h3 style=\"font-size:16px; margin-bottom:12px; color:#0f172a;\">{0}</h3>", tierTitle));
            sb.AppendLine(string.Format("      <div class=\"desc-box\">{0}</div>", tierDesc));

            sb.AppendLine("      <table>");
            sb.AppendLine("        <thead>");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <th>Item Barcode</th>");
            sb.AppendLine("            <th>Call Number</th>");
            sb.AppendLine("            <th>Title & Author</th>");
            sb.AppendLine("            <th>Due Date</th>");
            sb.AppendLine("            <th class=\"right\">Days Late</th>");
            sb.AppendLine("            <th class=\"right\">Accrued Fine</th>");
            sb.AppendLine("          </tr>");
            sb.AppendLine("        </thead>");
            sb.AppendLine("        <tbody>");
            sb.AppendLine("          <tr>");
            sb.AppendLine(string.Format("            <td><strong>{0}</strong></td>", l.ItemBarcode));
            sb.AppendLine(string.Format("            <td><code>{0}</code></td>", WebUtility.HtmlEncode(l.CallNumber)));
            sb.AppendLine(string.Format("            <td><strong>{0}</strong><br><span style=\"color:#64748b; font-size:12px;\">{1}</span></td>", WebUtility.HtmlEncode(l.Title), WebUtility.HtmlEncode(l.Author)));
            sb.AppendLine(string.Format("            <td>{0}</td>", l.DueDate.ToString("yyyy-MM-dd")));
            sb.AppendLine(string.Format("            <td class=\"right\">{0} d</td>", a.DaysOverdue > 0 ? a.DaysOverdue.ToString() : "0"));
            sb.AppendLine(string.Format("            <td class=\"right\">${0:F2}</td>", a.GrossFine));
            sb.AppendLine("          </tr>");
            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");

            sb.AppendLine("      <div class=\"total-box\">");
            sb.AppendLine(string.Format("        <div class=\"total-row\"><span>Daily Late Rate:</span><span>${0:F2} / day</span></div>", a.DailyRate));
            if (a.GraceApplied)
            {
                sb.AppendLine("        <div class=\"total-row\" style=\"color:#16a34a;\"><span>Grace Period Applied:</span><span>2 days waived</span></div>");
            }
            if (a.ReplacementFeeAssessed > 0)
            {
                sb.AppendLine(string.Format("        <div class=\"total-row\" style=\"color:#b91c1c;\"><span>Assumed Lost Replacement:</span><span>${0:F2}</span></div>", a.ReplacementFeeAssessed));
            }
            if (l.PaidFine > 0)
            {
                sb.AppendLine(string.Format("        <div class=\"total-row\"><span>Amount Previously Paid:</span><span>-${0:F2}</span></div>", l.PaidFine));
            }
            sb.AppendLine(string.Format("        <div class=\"total-row grand\"><span>Total Due Now:</span><span>${0:F2}</span></div>", a.NetFineRemaining));
            sb.AppendLine("      </div>");

            sb.AppendLine("      <div class=\"instructions\">");
            sb.AppendLine("        <strong>How to resolve this notice:</strong>");
            sb.AppendLine("        <ul style=\"margin-left: 20px; margin-top: 6px;\">");
            sb.AppendLine("          <li>Return the physical item to the Central Circulation Desk or the 24/7 Book Drop slot.</li>");
            sb.AppendLine("          <li>Fines may be settled at the library cashier desk (Cash / Card) or via the online student portal.</li>");
            sb.AppendLine("          <li>If you believe this notice was issued in error or the book was already returned, please bring your copy of the receipt or contact the desk immediately.</li>");
            sb.AppendLine("        </ul>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"footer\">");
            sb.AppendLine("      <div>");
            sb.AppendLine("        <strong>Central Circulation Desk</strong> &bull; Building A, Floor 1<br>");
            sb.AppendLine("        Email: circulation@univ-library.dz &bull; Phone: +213 21 00 11 22");
            sb.AppendLine("      </div>");
            sb.AppendLine(string.Format("      <div class=\"barcode\">*{0}*</div>", l.ItemBarcode));
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        public static string GenerateEmailEml(OverdueAssessment a, DateTime asOfDate)
        {
            Patron p = a.Patron ?? new Patron { FullName = "Library Patron", Email = "patron@univ.dz" };
            LoanRecord l = a.Loan;

            string subject = "";
            switch (a.Tier)
            {
                case OverdueTier.DueSoon:
                    subject = string.Format("Library Courtesy Reminder: '{0}' is due in 2 days", l.Title);
                    break;
                case OverdueTier.FirstNotice:
                    subject = string.Format("1st Overdue Notice: '{0}' is past due (Fine: ${1:F2})", l.Title, a.NetFineRemaining);
                    break;
                case OverdueTier.SecondNotice:
                    subject = string.Format("2nd Notice & Suspension Alert: '{0}' ({1} days late)", l.Title, a.DaysOverdue);
                    break;
                case OverdueTier.FinalDemand:
                    subject = string.Format("FINAL DEMAND - Item Assumed Lost: '{0}' (Total: ${1:F2})", l.Title, a.NetFineRemaining);
                    break;
                default:
                    subject = string.Format("Library Circulation Notice: '{0}'", l.Title);
                    break;
            }

            string bodyHtml = GeneratePrintableLetterHtml(a, asOfDate);

            StringBuilder eml = new StringBuilder();
            eml.AppendLine(string.Format("To: \"{0}\" <{1}>", p.FullName, p.Email));
            eml.AppendLine("From: \"Central Library Circulation Desk\" <circulation@univ-library.dz>");
            eml.AppendLine(string.Format("Subject: {0}", subject));
            eml.AppendLine(string.Format("Date: {0}", asOfDate.ToString("r")));
            eml.AppendLine("MIME-Version: 1.0");
            eml.AppendLine("Content-Type: text/html; charset=utf-8");
            eml.AppendLine("X-Priority: " + (a.Tier >= OverdueTier.SecondNotice ? "1" : "3"));
            eml.AppendLine();
            eml.Append(bodyHtml);

            return eml.ToString();
        }

        public static string GenerateWhatsAppMessage(OverdueAssessment a)
        {
            Patron p = a.Patron ?? new Patron { FullName = "Library Patron" };
            LoanRecord l = a.Loan;

            StringBuilder sb = new StringBuilder();
            switch (a.Tier)
            {
                case OverdueTier.DueSoon:
                    sb.AppendLine("📚 *University Central Library - Courtesy Reminder*");
                    sb.AppendLine(string.Format("Hello {0},", p.FullName));
                    sb.AppendLine(string.Format("Your borrowed book *'{0}'* (Call: {1}) is due on *{2}*.", l.Title, l.CallNumber, l.DueDate.ToString("yyyy-MM-dd")));
                    sb.AppendLine("Please renew online or return it before the due date to avoid late fees.");
                    break;
                case OverdueTier.FirstNotice:
                    sb.AppendLine("⚠️ *Central Library - 1st Overdue Notice*");
                    sb.AppendLine(string.Format("Hello {0},", p.FullName));
                    sb.AppendLine(string.Format("The book *'{0}'* is now *{1} days overdue* (Due was {2}).", l.Title, a.DaysOverdue, l.DueDate.ToString("yyyy-MM-dd")));
                    sb.AppendLine(string.Format("Accrued fine to date: *${0:F2}* (${1:F2}/day).", a.NetFineRemaining, a.DailyRate));
                    sb.AppendLine("Please return the book to the circulation desk as soon as possible.");
                    break;
                case OverdueTier.SecondNotice:
                    sb.AppendLine("🚫 *Central Library - 2nd Overdue Notice & Privilege Warning*");
                    sb.AppendLine(string.Format("Dear {0},", p.FullName));
                    sb.AppendLine(string.Format("The book *'{0}'* is *{1} days overdue*.", l.Title, a.DaysOverdue));
                    sb.AppendLine(string.Format("Current fine balance: *${0:F2}*.", a.NetFineRemaining));
                    sb.AppendLine("Notice: Borrowing privileges are temporarily suspended until returned. Please visit the desk today.");
                    break;
                case OverdueTier.FinalDemand:
                    sb.AppendLine("🚨 *Central Library - FINAL DEMAND NOTICE*");
                    sb.AppendLine(string.Format("URGENT: Attention {0} ({1}),", p.FullName, p.PatronId));
                    sb.AppendLine(string.Format("Book *'{0}'* is *{1} days overdue* and marked *ASSUMED LOST*.", l.Title, a.DaysOverdue));
                    sb.AppendLine(string.Format("Total balance due (Late fee + Replacement cost): *${0:F2}*.", a.NetFineRemaining));
                    sb.AppendLine("Please resolve this immediately to avoid formal academic hold / administrative block.");
                    break;
                default:
                    sb.AppendLine(string.Format("Hello {0}, notice regarding your library loan: '{1}'.", p.FullName, l.Title));
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("Circulation Desk: +213 21 00 11 22 | Email: circulation@univ-library.dz");
            return sb.ToString();
        }

        public static string GenerateWhatsAppUrl(OverdueAssessment a)
        {
            Patron p = a.Patron;
            string phone = (p != null && !string.IsNullOrEmpty(p.Phone)) ? p.Phone : "";
            string cleanPhone = Regex.Replace(phone, @"[^\d]", "");

            string message = GenerateWhatsAppMessage(a);
            string encoded = Uri.EscapeDataString(message);

            if (string.IsNullOrEmpty(cleanPhone))
            {
                return "https://api.whatsapp.com/send?text=" + encoded;
            }
            return string.Format("https://api.whatsapp.com/send?phone={0}&text={1}", cleanPhone, encoded);
        }

        public static string GenerateMasterHtmlDashboard(CirculationDatabase db, DateTime asOfDate)
        {
            List<OverdueAssessment> overdues = db.AssessAllOverdues(asOfDate);
            int totalActive = db.Loans.Count(l => l.Status == LoanStatus.Active);
            int totalReturned = db.Loans.Count(l => l.Status == LoanStatus.Returned);
            int totalDueSoon = overdues.Count(o => o.Tier == OverdueTier.DueSoon);
            int totalTier1 = overdues.Count(o => o.Tier == OverdueTier.FirstNotice);
            int totalTier2 = overdues.Count(o => o.Tier == OverdueTier.SecondNotice);
            int totalTier3 = overdues.Count(o => o.Tier == OverdueTier.FinalDemand);
            int totalHolds = db.Holds.Count(h => h.Status == HoldStatus.Queued || h.Status == HoldStatus.AwaitingPickup);

            decimal totalFinesAssessed = db.Transactions.Where(t => t.Type == TransactionType.Charge).Sum(t => t.Amount);
            decimal totalFinesCollected = db.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            decimal totalFinesWaived = db.Transactions.Where(t => t.Type == TransactionType.Waiver).Sum(t => t.Amount);
            decimal currentOutstanding = overdues.Sum(o => o.NetFineRemaining);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\">");
            sb.AppendLine("  <title>Library Circulation & Overdue Executive Dashboard</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif; }");
            sb.AppendLine("    body { background: #f8fafc; color: #1e293b; padding: 24px; }");
            sb.AppendLine("    .container { max-width: 1200px; margin: 0 auto; }");
            sb.AppendLine("    .header { background: #1e3a5f; color: #fff; padding: 24px 30px; border-radius: 8px; margin-bottom: 24px; display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("    .header h1 { font-size: 22px; font-weight: 700; }");
            sb.AppendLine("    .header p { font-size: 13px; opacity: 0.85; margin-top: 4px; }");
            sb.AppendLine("    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; margin-bottom: 24px; }");
            sb.AppendLine("    .kpi-card { background: #fff; padding: 20px; border-radius: 8px; border: 1px solid #e2e8f0; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
            sb.AppendLine("    .kpi-card h4 { font-size: 11px; text-transform: uppercase; color: #64748b; margin-bottom: 8px; font-weight: 600; letter-spacing: 0.5px; }");
            sb.AppendLine("    .kpi-card .val { font-size: 24px; font-weight: 700; color: #0f172a; }");
            sb.AppendLine("    .kpi-card.alert .val { color: #dc2626; }");
            sb.AppendLine("    .kpi-card.warn .val { color: #d97706; }");
            sb.AppendLine("    .kpi-card.success .val { color: #16a34a; }");
            sb.AppendLine("    .section { background: #fff; border-radius: 8px; border: 1px solid #e2e8f0; padding: 24px; margin-bottom: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
            sb.AppendLine("    .section h2 { font-size: 16px; font-weight: 700; color: #0f172a; margin-bottom: 16px; display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; font-size: 13px; }");
            sb.AppendLine("    th { background: #f1f5f9; text-align: left; padding: 10px 12px; font-weight: 600; color: #475569; border-bottom: 2px solid #cbd5e1; }");
            sb.AppendLine("    td { padding: 10px 12px; border-bottom: 1px solid #e2e8f0; }");
            sb.AppendLine("    .pill { display: inline-block; padding: 3px 10px; border-radius: 12px; font-size: 11px; font-weight: 700; }");
            sb.AppendLine("    .pill-duesoon { background: #dbeafe; color: #1d4ed8; }");
            sb.AppendLine("    .pill-tier1 { background: #fef3c7; color: #b45309; }");
            sb.AppendLine("    .pill-tier2 { background: #ffedd5; color: #c2410c; }");
            sb.AppendLine("    .pill-tier3 { background: #fee2e2; color: #b91c1c; }");
            sb.AppendLine("    .right { text-align: right; }");
            sb.AppendLine("    @media print { body { padding: 0; background: #fff; } .header { border-radius: 0; } }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <div class=\"container\">");
            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("      <div>");
            sb.AppendLine("        <h1>Circulation & Overdue Management Dashboard</h1>");
            sb.AppendLine(string.Format("        <p>As of {0} &bull; University Central Library</p>", asOfDate.ToString("MMMM dd, yyyy HH:mm")));
            sb.AppendLine("      </div>");
            sb.AppendLine("      <button onclick=\"window.print()\" style=\"background:#fff; color:#1e3a5f; font-weight:700; padding:8px 16px; border:none; border-radius:6px; cursor:pointer;\">🖨 Print Report</button>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"kpi-grid\">");
            sb.AppendLine(string.Format("      <div class=\"kpi-card\"><h4>Active Loans</h4><div class=\"val\">{0}</div></div>", totalActive));
            sb.AppendLine(string.Format("      <div class=\"kpi-card warn\"><h4>Due Soon (Courtesy)</h4><div class=\"val\">{0}</div></div>", totalDueSoon));
            sb.AppendLine(string.Format("      <div class=\"kpi-card alert\"><h4>Overdue Total</h4><div class=\"val\">{0}</div></div>", totalTier1 + totalTier2 + totalTier3));
            sb.AppendLine(string.Format("      <div class=\"kpi-card alert\"><h4>Outstanding Fines</h4><div class=\"val\">${0:F2}</div></div>", currentOutstanding));
            sb.AppendLine(string.Format("      <div class=\"kpi-card success\"><h4>Fines Collected</h4><div class=\"val\">${0:F2}</div></div>", totalFinesCollected));
            sb.AppendLine(string.Format("      <div class=\"kpi-card\"><h4>Active Hold Queue</h4><div class=\"val\">{0}</div></div>", totalHolds));
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"section\">");
            sb.AppendLine(string.Format("      <h2>Active Overdue Items & Escalation Tiers ({0} items)</h2>", overdues.Count));
            sb.AppendLine("      <table>");
            sb.AppendLine("        <thead>");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <th>Loan ID</th>");
            sb.AppendLine("            <th>Patron</th>");
            sb.AppendLine("            <th>Category</th>");
            sb.AppendLine("            <th>Item Barcode</th>");
            sb.AppendLine("            <th>Book Title</th>");
            sb.AppendLine("            <th>Due Date</th>");
            sb.AppendLine("            <th class=\"right\">Days Late</th>");
            sb.AppendLine("            <th>Escalation Tier</th>");
            sb.AppendLine("            <th class=\"right\">Fine Balance</th>");
            sb.AppendLine("          </tr>");
            sb.AppendLine("        </thead>");
            sb.AppendLine("        <tbody>");

            foreach (OverdueAssessment o in overdues)
            {
                string pillClass = "pill-tier1";
                if (o.Tier == OverdueTier.DueSoon) pillClass = "pill-duesoon";
                else if (o.Tier == OverdueTier.SecondNotice) pillClass = "pill-tier2";
                else if (o.Tier == OverdueTier.FinalDemand) pillClass = "pill-tier3";

                Patron p = o.Patron ?? new Patron { FullName = "Unknown", PatronId = o.Loan.PatronId };

                sb.AppendLine("          <tr>");
                sb.AppendLine(string.Format("            <td><code>{0}</code></td>", o.Loan.LoanId));
                sb.AppendLine(string.Format("            <td><strong>{0}</strong><br><small style=\"color:#64748b;\">{1}</small></td>", WebUtility.HtmlEncode(p.FullName), p.PatronId));
                sb.AppendLine(string.Format("            <td>{0}</td>", p.Category.ToString()));
                sb.AppendLine(string.Format("            <td><code>{0}</code></td>", o.Loan.ItemBarcode));
                sb.AppendLine(string.Format("            <td>{0}</td>", WebUtility.HtmlEncode(o.Loan.Title)));
                sb.AppendLine(string.Format("            <td>{0}</td>", o.Loan.DueDate.ToString("yyyy-MM-dd")));
                sb.AppendLine(string.Format("            <td class=\"right\"><strong>{0}</strong></td>", o.DaysOverdue > 0 ? o.DaysOverdue.ToString() : "0"));
                sb.AppendLine(string.Format("            <td><span class=\"pill {0}\">{1}</span></td>", pillClass, o.Tier.ToString()));
                sb.AppendLine(string.Format("            <td class=\"right\" style=\"font-weight:700; color:{0};\">${1:F2}</td>", o.NetFineRemaining > 0 ? "#b91c1c" : "#1e293b", o.NetFineRemaining));
                sb.AppendLine("          </tr>");
            }

            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"section\">");
            sb.AppendLine("      <h2>Active Holds & Hold Shelf Pickups</h2>");
            sb.AppendLine("      <table>");
            sb.AppendLine("        <thead>");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <th>Hold ID</th>");
            sb.AppendLine("            <th>Item Barcode</th>");
            sb.AppendLine("            <th>Title</th>");
            sb.AppendLine("            <th>Reserving Patron</th>");
            sb.AppendLine("            <th>Request Date</th>");
            sb.AppendLine("            <th>Hold Status</th>");
            sb.AppendLine("            <th>Pickup Deadline</th>");
            sb.AppendLine("          </tr>");
            sb.AppendLine("        </thead>");
            sb.AppendLine("        <tbody>");

            foreach (HoldRequest h in db.Holds)
            {
                Patron p = db.FindPatron(h.PatronId);
                string pName = p != null ? p.FullName : h.PatronId;
                sb.AppendLine("          <tr>");
                sb.AppendLine(string.Format("            <td><code>{0}</code></td>", h.HoldId));
                sb.AppendLine(string.Format("            <td><code>{0}</code></td>", h.ItemBarcode));
                sb.AppendLine(string.Format("            <td>{0}</td>", WebUtility.HtmlEncode(h.Title)));
                sb.AppendLine(string.Format("            <td><strong>{0}</strong> ({1})</td>", WebUtility.HtmlEncode(pName), h.PatronId));
                sb.AppendLine(string.Format("            <td>{0}</td>", h.RequestDate.ToString("yyyy-MM-dd")));
                sb.AppendLine(string.Format("            <td><strong>{0}</strong></td>", h.Status.ToString()));
                sb.AppendLine(string.Format("            <td>{0}</td>", h.PickupDeadline.HasValue ? h.PickupDeadline.Value.ToString("yyyy-MM-dd") : "-"));
                sb.AppendLine("          </tr>");
            }

            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");
            sb.AppendLine("    </div>");

            sb.AppendLine("  </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        public static string ExportLoansCsv(CirculationDatabase db)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LoanId,ItemBarcode,Title,Author,CallNumber,PatronId,PatronName,Category,CheckoutDate,DueDate,ReturnDate,DaysOverdue,Status,RenewalCount,AssessedFine,PaidFine");
            DateTime now = DateTime.Now;

            foreach (LoanRecord l in db.Loans)
            {
                Patron p = db.FindPatron(l.PatronId);
                string name = p != null ? p.FullName : "";
                string cat = p != null ? p.Category.ToString() : "";
                int daysOverdue = 0;
                if (l.Status == LoanStatus.Active && now > l.DueDate)
                {
                    daysOverdue = (now.Date - l.DueDate.Date).Days;
                }

                sb.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",{11},\"{12}\",{13},{14:F2},{15:F2}",
                    EscapeCsv(l.LoanId),
                    EscapeCsv(l.ItemBarcode),
                    EscapeCsv(l.Title),
                    EscapeCsv(l.Author),
                    EscapeCsv(l.CallNumber),
                    EscapeCsv(l.PatronId),
                    EscapeCsv(name),
                    EscapeCsv(cat),
                    l.CheckoutDate.ToString("yyyy-MM-dd"),
                    l.DueDate.ToString("yyyy-MM-dd"),
                    l.ReturnDate.HasValue ? l.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                    daysOverdue,
                    l.Status.ToString(),
                    l.RenewalCount,
                    l.AssessedFine,
                    l.PaidFine));
            }
            return sb.ToString();
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\"", "\"\"");
        }
    }

    #endregion

    #region Self-Verification Test Suite (Demo Engine)

    public static class DemoEngine
    {
        public static bool RunAllTests()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("     CIRCULATION & OVERDUE DESK - AUTOMATED VERIFICATION TEST SUITE");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            int passed = 0;
            int failed = 0;

            string tempDir = Path.Combine(Path.GetTempPath(), "CircDeskTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                CirculationDatabase db = new CirculationDatabase(tempDir);
                db.SeedDefaultData();

                // Test 1: Seed database loading
                bool t1 = db.Patrons.Count == 10 && db.Loans.Count == 11 && db.Holds.Count == 3;
                ReportTest("1. Seed Database Initialization", t1, string.Format("Loaded {0} patrons, {1} loans, {2} holds", db.Patrons.Count, db.Loans.Count, db.Holds.Count), ref passed, ref failed);

                // Test 2: Policy matrix retrieval
                CirculationPolicy ugPolicy = db.GetPolicy(PatronCategory.Undergraduate);
                CirculationPolicy facPolicy = db.GetPolicy(PatronCategory.Faculty);
                bool t2 = (ugPolicy.LoanDays == 14 && ugPolicy.GraceDays == 2 && ugPolicy.DailyFine == 0.50m && facPolicy.LoanDays == 90 && facPolicy.MaxItems == 25);
                ReportTest("2. Tiered Circulation Policy Matrix", t2, "Undergraduate (14d/$0.50/2d grace) vs Faculty (90d/$0.10/7d grace)", ref passed, ref failed);

                // Test 3: Grace period fine calculation
                LoanRecord loanGrace = new LoanRecord
                {
                    LoanId = "TEST-LN-01",
                    ItemBarcode = "TEST-BC-01",
                    PatronId = "PAT-1001", // Undergrad: 2 days grace
                    DueDate = new DateTime(2026, 11, 5)
                };
                OverdueAssessment aGrace = db.AssessOverdue(loanGrace, new DateTime(2026, 11, 7)); // Exactly 2 days late
                bool t3 = (aGrace.DaysOverdue == 2 && aGrace.GraceApplied && aGrace.GrossFine == 0m);
                ReportTest("3. Overdue Grace Period Waiver", t3, string.Format("Days late: {0}, Grace applied: {1}, Fine: ${2:F2}", aGrace.DaysOverdue, aGrace.GraceApplied, aGrace.GrossFine), ref passed, ref failed);

                // Test 4: Fine calculation after grace period expiration
                OverdueAssessment aBilled = db.AssessOverdue(loanGrace, new DateTime(2026, 11, 9)); // 4 days late
                bool t4 = (aBilled.DaysOverdue == 4 && !aBilled.GraceApplied && aBilled.GrossFine == 2.00m); // 4 days * $0.50 = $2.00
                ReportTest("4. Daily Fine Accrual Post-Grace", t4, string.Format("Days late: {0}, Fine: ${1:F2} (Expected: $2.00)", aBilled.DaysOverdue, aBilled.GrossFine), ref passed, ref failed);

                // Test 5: Maximum fine cap enforcement
                OverdueAssessment aCapped = db.AssessOverdue(loanGrace, new DateTime(2026, 12, 15)); // 40 days late, undergrad cap = $15.00
                bool t5 = (aCapped.GrossFine >= 15.00m && aCapped.CappedAtMax);
                ReportTest("5. Fine Maximum Cap Guard", t5, string.Format("40 days late, Gross: ${0:F2}, Capped: {1}", aCapped.GrossFine, aCapped.CappedAtMax), ref passed, ref failed);

                // Test 6: Escalation Tier classification
                OverdueAssessment aTier1 = db.AssessOverdue(loanGrace, new DateTime(2026, 11, 10)); // 5 days -> FirstNotice
                OverdueAssessment aTier2 = db.AssessOverdue(loanGrace, new DateTime(2026, 11, 20)); // 15 days -> SecondNotice
                OverdueAssessment aTier3 = db.AssessOverdue(loanGrace, new DateTime(2026, 12, 10)); // 35 days -> FinalDemand
                bool t6 = (aTier1.Tier == OverdueTier.FirstNotice && aTier2.Tier == OverdueTier.SecondNotice && aTier3.Tier == OverdueTier.FinalDemand);
                ReportTest("6. Overdue Notice Escalation Workflow", t6, string.Format("Tiers: 5d={0}, 15d={1}, 35d={2}", aTier1.Tier, aTier2.Tier, aTier3.Tier), ref passed, ref failed);

                // Test 7: Block checkout for suspended/expired card
                LoanRecord testOut;
                string errMsg;
                bool t7 = !db.CheckoutItem("LIB-999001", "Test Book", "Author", "000 GEN", "PAT-1010", out testOut, out errMsg); // PAT-1010 is suspended/expired
                ReportTest("7. Checkout Restriction on Suspended Patron", t7, string.Format("Blocked properly: '{0}'", errMsg), ref passed, ref failed);

                // Test 8: Check-in with automatic Hold shelf notification
                string returnMsg;
                HoldRequest holdFired;
                decimal returnedFine;
                // Seed loan for LIB-100107 which has a queued hold for PAT-1007
                bool t8 = db.ReturnItem("LIB-100107", new DateTime(2026, 11, 7), out returnMsg, out holdFired, out returnedFine);
                bool t8_valid = t8 && holdFired != null && holdFired.Status == HoldStatus.AwaitingPickup && holdFired.PickupDeadline.HasValue;
                ReportTest("8. Check-in & Hold Shelf Trigger", t8_valid, string.Format("Hold transitioned to {0} for {1} (Deadline: {2})",
                    holdFired != null ? holdFired.Status.ToString() : "None",
                    holdFired != null ? holdFired.PatronId : "-",
                    holdFired != null && holdFired.PickupDeadline.HasValue ? holdFired.PickupDeadline.Value.ToString("yyyy-MM-dd") : "-"), ref passed, ref failed);

                // Test 9: Notice Generation (HTML letter & WhatsApp URL)
                string letterHtml = NoticeGenerator.GeneratePrintableLetterHtml(aTier2, new DateTime(2026, 11, 7));
                string waUrl = NoticeGenerator.GenerateWhatsAppUrl(aTier2);
                bool t9 = (letterHtml.Contains("SECOND NOTICE") || letterHtml.Contains("Second Notice")) && waUrl.Contains("api.whatsapp.com");
                ReportTest("9. Multi-Channel Notice Generation", t9, string.Format("HTML letter ({0} bytes) & WhatsApp URL generated", letterHtml.Length), ref passed, ref failed);

                // Test 10: Fine Waiver and Payment Ledger
                string rcptNum, err;
                bool t10_pay = db.RecordPayment("PAT-1001", "TEST-LN-01", 5.00m, PaymentMethod.Cash, "Desk-Samir", "Partial test payment", out rcptNum, out err);
                bool t10_wav = db.WaiveFine("PAT-1001", "TEST-LN-01", 2.00m, "Medical exemption note", "Supervisor", out rcptNum, out err);
                bool t10 = t10_pay && t10_wav && db.Transactions.Count >= 5;
                ReportTest("10. Payment & Waiver Audit Ledger", t10, string.Format("Recorded payment & waiver receipts (Ledger count: {0})", db.Transactions.Count), ref passed, ref failed);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            Console.WriteLine("--------------------------------------------------------------------------------");
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("RESULT: ALL {0} ENGINE TESTS PASSED SUCCESSFULLY! (100% OK)", passed));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("RESULT: {0} PASSED, {1} FAILED.", passed, failed));
            }
            Console.ResetColor();
            Console.WriteLine("================================================================================");
            return failed == 0;
        }

        private static void ReportTest(string name, bool success, string details, ref int passed, ref int failed)
        {
            if (success)
            {
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.Write(name.PadRight(45));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" - " + details);
                Console.ResetColor();
            }
            else
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.Write(name.PadRight(45));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" - " + details);
                Console.ResetColor();
            }
        }
    }

    #endregion

    #region Windows Forms GUI Studio

    public class MainForm : Form
    {
        private CirculationDatabase db;
        private DateTime simulatedDate;

        // Controls
        private TabControl tabControl;
        private TabPage tabCirculation;
        private TabPage tabPatrons;
        private TabPage tabOverdue;
        private TabPage tabFines;
        private TabPage tabHolds;

        // Tab 1: Circulation Controls
        private TextBox txtCheckinBarcode;
        private TextBox txtCheckoutBarcode;
        private TextBox txtCheckoutPatronId;
        private TextBox txtCheckoutTitle;
        private Button btnProcessCheckout;
        private Button btnProcessCheckin;
        private Panel pnlAlertBanner;
        private Label lblAlertText;
        private DataGridView dgvActiveLoans;
        private TextBox txtSearchLoans;

        // Tab 2: Patron Controls
        private DataGridView dgvPatrons;
        private TextBox txtSearchPatrons;
        private ComboBox cmbPatronCategoryFilter;
        private Button btnAddPatron;
        private Button btnTogglePatronStatus;

        // Tab 3: Overdue Controls
        private DataGridView dgvOverdues;
        private ComboBox cmbOverdueTierFilter;
        private Button btnPreviewLetter;
        private Button btnGenerateEmail;
        private Button btnCopyWhatsApp;
        private Button btnOpenWhatsApp;
        private Button btnBatchExportNotices;

        // Tab 4: Fines Controls
        private DataGridView dgvFines;
        private Label lblKpiTotalAssessed;
        private Label lblKpiTotalCollected;
        private Label lblKpiTotalWaived;
        private Label lblKpiOutstanding;
        private TextBox txtPayPatronId;
        private TextBox txtPayAmount;
        private ComboBox cmbPayMethod;
        private Button btnCollectPayment;
        private Button btnWaiveFine;

        // Tab 5: Holds & Reports Controls
        private DataGridView dgvHolds;
        private Button btnPlaceHold;
        private Button btnExportMasterHtml;
        private Button btnExportCsv;

        // Status Strip
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatusInfo;
        private ToolStripStatusLabel lblStatsLoans;
        private ToolStripStatusLabel lblStatsOverdues;
        private ToolStripStatusLabel lblStatsFines;

        public MainForm()
        {
            db = new CirculationDatabase();
            db.LoadOrSeed();
            simulatedDate = DateTime.Now;

            InitializeComponents();
            RefreshAllViews();
        }

        private void InitializeComponents()
        {
            this.Text = "Circulation & Overdue Management Desk - Central Library";
            this.Size = new Size(1180, 750);
            this.MinimumSize = new Size(1000, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.BackColor = Color.FromArgb(248, 250, 252);

            // Header Banner
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(30, 58, 95)
            };

            Label lblTitle = new Label
            {
                Text = "📚 CENTRAL LIBRARY CIRCULATION & OVERDUE DESK",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(20, 12),
                AutoSize = true
            };

            Label lblSubtitle = new Label
            {
                Text = "Automated Patron Circulation, Multi-Tier Overdue Escalation, Notice Dispatcher & Fine Accounting",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(22, 38),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);

            // Tab Control
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Padding = new Point(16, 8)
            };

            tabCirculation = new TabPage("⚡ Circulation Desk (Check-in/Out)");
            tabPatrons = new TabPage("👥 Patron Directory");
            tabOverdue = new TabPage("🔔 Overdue Notice Dispatcher");
            tabFines = new TabPage("💰 Fines & Cashier Ledger");
            tabHolds = new TabPage("📌 Holds & Executive Reports");

            SetupTabCirculation();
            SetupTabPatrons();
            SetupTabOverdue();
            SetupTabFines();
            SetupTabHolds();

            tabControl.TabPages.Add(tabCirculation);
            tabControl.TabPages.Add(tabPatrons);
            tabControl.TabPages.Add(tabOverdue);
            tabControl.TabPages.Add(tabFines);
            tabControl.TabPages.Add(tabHolds);

            // Status Strip
            statusStrip = new StatusStrip();
            lblStatusInfo = new ToolStripStatusLabel { Spring = true, Text = "Ready" };
            lblStatsLoans = new ToolStripStatusLabel { Text = "Loans: 0", BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            lblStatsOverdues = new ToolStripStatusLabel { Text = "Overdues: 0", BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            lblStatsFines = new ToolStripStatusLabel { Text = "Fines: $0.00", BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };

            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatusInfo, lblStatsLoans, lblStatsOverdues, lblStatsFines });

            this.Controls.Add(tabControl);
            this.Controls.Add(pnlHeader);
            this.Controls.Add(statusStrip);
        }

        #region Setup Tabs

        private void SetupTabCirculation()
        {
            tabCirculation.BackColor = Color.White;

            // Top action bar
            Panel pnlActions = new Panel { Dock = DockStyle.Top, Height = 135, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15) };

            // Check In Box (Left)
            GroupBox grpCheckin = new GroupBox
            {
                Text = "⚡ Fast Check-in (Book Return)",
                Location = new Point(15, 10),
                Size = new Size(380, 110),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            Label lblCinBc = new Label { Text = "Scan Item Barcode:", Location = new Point(15, 26), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
            txtCheckinBarcode = new TextBox { Location = new Point(15, 46), Size = new Size(230, 25), Font = new Font("Segoe UI", 10F) };
            txtCheckinBarcode.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoCheckin(); e.SuppressKeyPress = true; } };

            btnProcessCheckin = new Button
            {
                Text = "Return Book",
                Location = new Point(255, 45),
                Size = new Size(110, 28),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnProcessCheckin.Click += (s, e) => DoCheckin();

            grpCheckin.Controls.AddRange(new Control[] { lblCinBc, txtCheckinBarcode, btnProcessCheckin });

            // Check Out Box (Right)
            GroupBox grpCheckout = new GroupBox
            {
                Text = "🚀 Fast Checkout (New Loan)",
                Location = new Point(410, 10),
                Size = new Size(720, 110),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            Label lblCoutPatron = new Label { Text = "Patron ID / Name:", Location = new Point(15, 26), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
            txtCheckoutPatronId = new TextBox { Location = new Point(15, 46), Size = new Size(160, 25), Font = new Font("Segoe UI", 9.5F) };

            Label lblCoutBc = new Label { Text = "Item Barcode:", Location = new Point(185, 26), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
            txtCheckoutBarcode = new TextBox { Location = new Point(185, 46), Size = new Size(160, 25), Font = new Font("Segoe UI", 9.5F) };

            Label lblCoutTitle = new Label { Text = "Book Title (Optional):", Location = new Point(355, 26), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
            txtCheckoutTitle = new TextBox { Location = new Point(355, 46), Size = new Size(220, 25), Font = new Font("Segoe UI", 9.5F) };

            btnProcessCheckout = new Button
            {
                Text = "Check Out",
                Location = new Point(590, 44),
                Size = new Size(115, 29),
                BackColor = Color.FromArgb(16, 149, 193),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnProcessCheckout.Click += (s, e) => DoCheckout();

            grpCheckout.Controls.AddRange(new Control[] { lblCoutPatron, txtCheckoutPatronId, lblCoutBc, txtCheckoutBarcode, lblCoutTitle, txtCheckoutTitle, btnProcessCheckout });

            pnlActions.Controls.Add(grpCheckin);
            pnlActions.Controls.Add(grpCheckout);

            // Alert Banner
            pnlAlertBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(15, 8, 15, 8)
            };

            lblAlertText = new Label
            {
                Dock = DockStyle.Fill,
                Text = "System ready. Ready to scan patron barcodes or return items.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlAlertBanner.Controls.Add(lblAlertText);

            // Loan List Filter Bar
            Panel pnlListFilter = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.White, Padding = new Padding(15, 6, 15, 6) };
            Label lblSearch = new Label { Text = "Search Active Loans:", Location = new Point(15, 10), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtSearchLoans = new TextBox { Location = new Point(160, 7), Size = new Size(260, 24) };
            txtSearchLoans.TextChanged += (s, e) => FilterActiveLoansGrid();

            Button btnRenewSelected = new Button
            {
                Text = "🔄 Renew Selected Loan",
                Location = new Point(440, 6),
                Size = new Size(180, 26),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRenewSelected.Click += (s, e) => DoRenewSelectedLoan();

            pnlListFilter.Controls.AddRange(new Control[] { lblSearch, txtSearchLoans, btnRenewSelected });

            // DataGridView
            dgvActiveLoans = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvActiveLoans.Columns.Add("LoanId", "Loan ID");
            dgvActiveLoans.Columns.Add("Barcode", "Barcode");
            dgvActiveLoans.Columns.Add("Title", "Title");
            dgvActiveLoans.Columns.Add("Patron", "Patron");
            dgvActiveLoans.Columns.Add("DueDate", "Due Date");
            dgvActiveLoans.Columns.Add("DaysOverdue", "Days Late");
            dgvActiveLoans.Columns.Add("Status", "Status");
            dgvActiveLoans.Columns.Add("Renewals", "Renewals");

            dgvActiveLoans.Columns["LoanId"].FillWeight = 50;
            dgvActiveLoans.Columns["Barcode"].FillWeight = 55;
            dgvActiveLoans.Columns["Title"].FillWeight = 140;
            dgvActiveLoans.Columns["Patron"].FillWeight = 80;
            dgvActiveLoans.Columns["DueDate"].FillWeight = 50;
            dgvActiveLoans.Columns["DaysOverdue"].FillWeight = 40;
            dgvActiveLoans.Columns["Status"].FillWeight = 55;
            dgvActiveLoans.Columns["Renewals"].FillWeight = 35;

            tabCirculation.Controls.Add(dgvActiveLoans);
            tabCirculation.Controls.Add(pnlListFilter);
            tabCirculation.Controls.Add(pnlAlertBanner);
            tabCirculation.Controls.Add(pnlActions);
        }

        private void SetupTabPatrons()
        {
            tabPatrons.BackColor = Color.White;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15, 10, 15, 10) };
            Label lblSearch = new Label { Text = "Search Patron:", Location = new Point(15, 14), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtSearchPatrons = new TextBox { Location = new Point(120, 12), Size = new Size(220, 24) };
            txtSearchPatrons.TextChanged += (s, e) => RefreshPatronsGrid();

            Label lblCat = new Label { Text = "Category:", Location = new Point(360, 14), AutoSize = true };
            cmbPatronCategoryFilter = new ComboBox { Location = new Point(430, 11), Size = new Size(160, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPatronCategoryFilter.Items.Add("All Categories");
            foreach (PatronCategory c in Enum.GetValues(typeof(PatronCategory))) cmbPatronCategoryFilter.Items.Add(c.ToString());
            cmbPatronCategoryFilter.SelectedIndex = 0;
            cmbPatronCategoryFilter.SelectedIndexChanged += (s, e) => RefreshPatronsGrid();

            btnAddPatron = new Button
            {
                Text = "+ Add Patron",
                Location = new Point(610, 10),
                Size = new Size(120, 27),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAddPatron.Click += (s, e) => ShowAddPatronDialog();

            btnTogglePatronStatus = new Button
            {
                Text = "Toggle Status (Active/Suspend)",
                Location = new Point(740, 10),
                Size = new Size(200, 27),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F),
                Cursor = Cursors.Hand
            };
            btnTogglePatronStatus.Click += (s, e) => ToggleSelectedPatronStatus();

            pnlTop.Controls.AddRange(new Control[] { lblSearch, txtSearchPatrons, lblCat, cmbPatronCategoryFilter, btnAddPatron, btnTogglePatronStatus });

            dgvPatrons = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvPatrons.Columns.Add("Id", "Patron ID");
            dgvPatrons.Columns.Add("Name", "Full Name");
            dgvPatrons.Columns.Add("Category", "Category");
            dgvPatrons.Columns.Add("Department", "Department");
            dgvPatrons.Columns.Add("Email", "Email");
            dgvPatrons.Columns.Add("Phone", "Phone");
            dgvPatrons.Columns.Add("ActiveLoans", "Active Loans");
            dgvPatrons.Columns.Add("Balance", "Balance Due");
            dgvPatrons.Columns.Add("Status", "Status");

            dgvPatrons.Columns["Id"].FillWeight = 45;
            dgvPatrons.Columns["Name"].FillWeight = 90;
            dgvPatrons.Columns["Category"].FillWeight = 60;
            dgvPatrons.Columns["Department"].FillWeight = 75;
            dgvPatrons.Columns["Email"].FillWeight = 90;
            dgvPatrons.Columns["Phone"].FillWeight = 65;
            dgvPatrons.Columns["ActiveLoans"].FillWeight = 40;
            dgvPatrons.Columns["Balance"].FillWeight = 45;
            dgvPatrons.Columns["Status"].FillWeight = 45;

            tabPatrons.Controls.Add(dgvPatrons);
            tabPatrons.Controls.Add(pnlTop);
        }

        private void SetupTabOverdue()
        {
            tabOverdue.BackColor = Color.White;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15, 12, 15, 12) };

            Label lblFilter = new Label { Text = "Escalation Tier:", Location = new Point(15, 19), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            cmbOverdueTierFilter = new ComboBox { Location = new Point(125, 16), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbOverdueTierFilter.Items.Add("All Tiers");
            cmbOverdueTierFilter.Items.Add("Due Soon (Courtesy)");
            cmbOverdueTierFilter.Items.Add("1st Notice (1-7 days)");
            cmbOverdueTierFilter.Items.Add("2nd Notice (8-21 days)");
            cmbOverdueTierFilter.Items.Add("Final Demand (22+ days)");
            cmbOverdueTierFilter.SelectedIndex = 0;
            cmbOverdueTierFilter.SelectedIndexChanged += (s, e) => RefreshOverduesGrid();

            btnPreviewLetter = new Button
            {
                Text = "📄 Preview Letter (HTML)",
                Location = new Point(320, 15),
                Size = new Size(160, 28),
                BackColor = Color.FromArgb(30, 58, 95),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPreviewLetter.Click += (s, e) => PreviewSelectedOverdueLetter();

            btnGenerateEmail = new Button
            {
                Text = "✉ Export Email (.eml)",
                Location = new Point(490, 15),
                Size = new Size(150, 28),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGenerateEmail.Click += (s, e) => ExportSelectedOverdueEmail();

            btnOpenWhatsApp = new Button
            {
                Text = "💬 Open WhatsApp",
                Location = new Point(650, 15),
                Size = new Size(140, 28),
                BackColor = Color.FromArgb(37, 211, 102),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOpenWhatsApp.Click += (s, e) => OpenSelectedWhatsApp();

            btnCopyWhatsApp = new Button
            {
                Text = "📋 Copy WA Text",
                Location = new Point(800, 15),
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F),
                Cursor = Cursors.Hand
            };
            btnCopyWhatsApp.Click += (s, e) => CopySelectedWhatsAppText();

            btnBatchExportNotices = new Button
            {
                Text = "📦 Batch Export Notices",
                Location = new Point(930, 15),
                Size = new Size(170, 28),
                BackColor = Color.FromArgb(16, 149, 193),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBatchExportNotices.Click += (s, e) => BatchExportAllNotices();

            pnlTop.Controls.AddRange(new Control[] { lblFilter, cmbOverdueTierFilter, btnPreviewLetter, btnGenerateEmail, btnOpenWhatsApp, btnCopyWhatsApp, btnBatchExportNotices });

            dgvOverdues = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvOverdues.Columns.Add("LoanId", "Loan ID");
            dgvOverdues.Columns.Add("Patron", "Patron Name");
            dgvOverdues.Columns.Add("Phone", "Phone / WA");
            dgvOverdues.Columns.Add("Barcode", "Barcode");
            dgvOverdues.Columns.Add("Title", "Book Title");
            dgvOverdues.Columns.Add("DueDate", "Due Date");
            dgvOverdues.Columns.Add("DaysLate", "Days Late");
            dgvOverdues.Columns.Add("Tier", "Escalation Tier");
            dgvOverdues.Columns.Add("FineDue", "Fine Due");

            dgvOverdues.Columns["LoanId"].FillWeight = 45;
            dgvOverdues.Columns["Patron"].FillWeight = 75;
            dgvOverdues.Columns["Phone"].FillWeight = 65;
            dgvOverdues.Columns["Barcode"].FillWeight = 50;
            dgvOverdues.Columns["Title"].FillWeight = 110;
            dgvOverdues.Columns["DueDate"].FillWeight = 45;
            dgvOverdues.Columns["DaysLate"].FillWeight = 35;
            dgvOverdues.Columns["Tier"].FillWeight = 60;
            dgvOverdues.Columns["FineDue"].FillWeight = 45;

            tabOverdue.Controls.Add(dgvOverdues);
            tabOverdue.Controls.Add(pnlTop);
        }

        private void SetupTabFines()
        {
            tabFines.BackColor = Color.White;

            // KPI Header Panel
            Panel pnlKpis = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15, 10, 15, 10) };

            lblKpiTotalAssessed = CreateKpiBox(pnlKpis, "Total Assessed", "$0.00", 15, Color.FromArgb(15, 23, 42));
            lblKpiTotalCollected = CreateKpiBox(pnlKpis, "Total Collected", "$0.00", 220, Color.FromArgb(22, 163, 74));
            lblKpiTotalWaived = CreateKpiBox(pnlKpis, "Total Waived", "$0.00", 425, Color.FromArgb(217, 119, 6));
            lblKpiOutstanding = CreateKpiBox(pnlKpis, "Current Outstanding", "$0.00", 630, Color.FromArgb(220, 38, 38));

            // Bottom payment entry bar
            Panel pnlPaymentBar = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15, 12, 15, 12) };

            Label lblPayP = new Label { Text = "Patron ID:", Location = new Point(15, 18), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtPayPatronId = new TextBox { Location = new Point(85, 15), Size = new Size(130, 24) };

            Label lblPayAmt = new Label { Text = "Amount ($):", Location = new Point(230, 18), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtPayAmount = new TextBox { Location = new Point(315, 15), Size = new Size(80, 24), Text = "5.00" };

            Label lblPayMth = new Label { Text = "Method:", Location = new Point(410, 18), AutoSize = true };
            cmbPayMethod = new ComboBox { Location = new Point(470, 15), Size = new Size(130, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (PaymentMethod m in Enum.GetValues(typeof(PaymentMethod))) cmbPayMethod.Items.Add(m.ToString());
            cmbPayMethod.SelectedIndex = 0;

            btnCollectPayment = new Button
            {
                Text = "💵 Collect Payment",
                Location = new Point(620, 13),
                Size = new Size(140, 28),
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCollectPayment.Click += (s, e) => DoCollectPayment();

            btnWaiveFine = new Button
            {
                Text = "🤝 Waive Fine (Grace/Excuse)",
                Location = new Point(770, 13),
                Size = new Size(200, 28),
                BackColor = Color.FromArgb(217, 119, 6),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnWaiveFine.Click += (s, e) => DoWaiveFine();

            pnlPaymentBar.Controls.AddRange(new Control[] { lblPayP, txtPayPatronId, lblPayAmt, txtPayAmount, lblPayMth, cmbPayMethod, btnCollectPayment, btnWaiveFine });

            // Ledger grid
            dgvFines = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvFines.Columns.Add("Date", "Date");
            dgvFines.Columns.Add("TxId", "Receipt #");
            dgvFines.Columns.Add("PatronId", "Patron ID");
            dgvFines.Columns.Add("Type", "Type");
            dgvFines.Columns.Add("Method", "Method");
            dgvFines.Columns.Add("Amount", "Amount");
            dgvFines.Columns.Add("Reason", "Reason / Notes");
            dgvFines.Columns.Add("Staff", "Staff Operator");

            dgvFines.Columns["Date"].FillWeight = 50;
            dgvFines.Columns["TxId"].FillWeight = 55;
            dgvFines.Columns["PatronId"].FillWeight = 45;
            dgvFines.Columns["Type"].FillWeight = 35;
            dgvFines.Columns["Method"].FillWeight = 45;
            dgvFines.Columns["Amount"].FillWeight = 35;
            dgvFines.Columns["Reason"].FillWeight = 110;
            dgvFines.Columns["Staff"].FillWeight = 45;

            tabFines.Controls.Add(dgvFines);
            tabFines.Controls.Add(pnlPaymentBar);
            tabFines.Controls.Add(pnlKpis);
        }

        private Label CreateKpiBox(Panel parent, string title, string initialVal, int left, Color valColor)
        {
            Panel p = new Panel
            {
                Location = new Point(left, 10),
                Size = new Size(190, 60),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Label lblT = new Label
            {
                Text = title.ToUpperInvariant(),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(10, 6),
                AutoSize = true
            };
            Label lblV = new Label
            {
                Text = initialVal,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = valColor,
                Location = new Point(10, 24),
                AutoSize = true
            };
            p.Controls.Add(lblT);
            p.Controls.Add(lblV);
            parent.Controls.Add(p);
            return lblV;
        }

        private void SetupTabHolds()
        {
            tabHolds.BackColor = Color.White;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(15, 12, 15, 12) };

            btnPlaceHold = new Button
            {
                Text = "📌 Place New Item Hold",
                Location = new Point(15, 12),
                Size = new Size(180, 28),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPlaceHold.Click += (s, e) => ShowPlaceHoldDialog();

            btnExportMasterHtml = new Button
            {
                Text = "📊 Export Master HTML Dashboard",
                Location = new Point(210, 12),
                Size = new Size(230, 28),
                BackColor = Color.FromArgb(30, 58, 95),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExportMasterHtml.Click += (s, e) => ExportMasterHtmlDashboard();

            btnExportCsv = new Button
            {
                Text = "📑 Export Loans to CSV",
                Location = new Point(450, 12),
                Size = new Size(180, 28),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExportCsv.Click += (s, e) => ExportLoansCsvFile();

            pnlTop.Controls.AddRange(new Control[] { btnPlaceHold, btnExportMasterHtml, btnExportCsv });

            dgvHolds = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvHolds.Columns.Add("HoldId", "Hold ID");
            dgvHolds.Columns.Add("Barcode", "Barcode");
            dgvHolds.Columns.Add("Title", "Book Title");
            dgvHolds.Columns.Add("Patron", "Reserving Patron");
            dgvHolds.Columns.Add("RequestDate", "Request Date");
            dgvHolds.Columns.Add("Status", "Status");
            dgvHolds.Columns.Add("Deadline", "Pickup Deadline");

            dgvHolds.Columns["HoldId"].FillWeight = 45;
            dgvHolds.Columns["Barcode"].FillWeight = 50;
            dgvHolds.Columns["Title"].FillWeight = 110;
            dgvHolds.Columns["Patron"].FillWeight = 70;
            dgvHolds.Columns["RequestDate"].FillWeight = 45;
            dgvHolds.Columns["Status"].FillWeight = 50;
            dgvHolds.Columns["Deadline"].FillWeight = 45;

            tabHolds.Controls.Add(dgvHolds);
            tabHolds.Controls.Add(pnlTop);
        }

        #endregion

        #region Refresh Data & View Logic

        private void RefreshAllViews()
        {
            RefreshActiveLoansGrid();
            RefreshPatronsGrid();
            RefreshOverduesGrid();
            RefreshFinesGrid();
            RefreshHoldsGrid();
            UpdateStatusStrip();
        }

        private void RefreshActiveLoansGrid()
        {
            dgvActiveLoans.Rows.Clear();
            List<LoanRecord> active = db.Loans.Where(l => l.Status == LoanStatus.Active).OrderBy(l => l.DueDate).ToList();
            string q = txtSearchLoans.Text.Trim().ToLowerInvariant();

            foreach (LoanRecord l in active)
            {
                Patron p = db.FindPatron(l.PatronId);
                string pName = p != null ? p.FullName : l.PatronId;

                if (!string.IsNullOrEmpty(q))
                {
                    bool match = l.ItemBarcode.ToLowerInvariant().Contains(q) ||
                                 l.Title.ToLowerInvariant().Contains(q) ||
                                 l.LoanId.ToLowerInvariant().Contains(q) ||
                                 pName.ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }

                int daysLate = (simulatedDate.Date - l.DueDate.Date).Days;
                string daysLateStr = daysLate > 0 ? string.Format("+{0} days", daysLate) : "On time";
                string statusStr = daysLate > 0 ? "OVERDUE" : "Active";

                int rowIdx = dgvActiveLoans.Rows.Add(l.LoanId, l.ItemBarcode, l.Title, pName, l.DueDate.ToString("yyyy-MM-dd"), daysLateStr, statusStr, l.RenewalCount.ToString());

                if (daysLate > 0)
                {
                    dgvActiveLoans.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(185, 28, 28);
                    dgvActiveLoans.Rows[rowIdx].DefaultCellStyle.Font = new Font(dgvActiveLoans.Font, FontStyle.Bold);
                }
            }
        }

        private void FilterActiveLoansGrid()
        {
            RefreshActiveLoansGrid();
        }

        private void RefreshPatronsGrid()
        {
            dgvPatrons.Rows.Clear();
            string q = txtSearchPatrons != null ? txtSearchPatrons.Text.Trim().ToLowerInvariant() : "";
            string catFilter = cmbPatronCategoryFilter != null ? cmbPatronCategoryFilter.SelectedItem.ToString() : "All Categories";

            foreach (Patron p in db.Patrons)
            {
                if (catFilter != "All Categories" && p.Category.ToString() != catFilter) continue;
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = p.PatronId.ToLowerInvariant().Contains(q) ||
                                 p.FullName.ToLowerInvariant().Contains(q) ||
                                 p.Department.ToLowerInvariant().Contains(q) ||
                                 p.Email.ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }

                int activeLoans = db.GetActiveLoansForPatron(p.PatronId).Count;
                decimal balance = db.GetOutstandingBalanceForPatron(p.PatronId, simulatedDate);

                int rowIdx = dgvPatrons.Rows.Add(p.PatronId, p.FullName, p.Category.ToString(), p.Department, p.Email, p.Phone, activeLoans, string.Format("${0:F2}", balance), p.Status.ToString());

                if (p.Status == PatronStatus.Suspended)
                {
                    dgvPatrons.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(194, 65, 12);
                }
                else if (balance >= 10.00m)
                {
                    dgvPatrons.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(185, 28, 28);
                }
            }
        }

        private void RefreshOverduesGrid()
        {
            dgvOverdues.Rows.Clear();
            List<OverdueAssessment> overdues = db.AssessAllOverdues(simulatedDate);
            string tierFilter = cmbOverdueTierFilter != null && cmbOverdueTierFilter.SelectedItem != null ? cmbOverdueTierFilter.SelectedItem.ToString() : "All Tiers";

            foreach (OverdueAssessment o in overdues)
            {
                if (tierFilter == "Due Soon (Courtesy)" && o.Tier != OverdueTier.DueSoon) continue;
                if (tierFilter == "1st Notice (1-7 days)" && o.Tier != OverdueTier.FirstNotice) continue;
                if (tierFilter == "2nd Notice (8-21 days)" && o.Tier != OverdueTier.SecondNotice) continue;
                if (tierFilter == "Final Demand (22+ days)" && o.Tier != OverdueTier.FinalDemand) continue;

                Patron p = o.Patron ?? new Patron { FullName = "Unknown", Phone = "-" };
                int rowIdx = dgvOverdues.Rows.Add(
                    o.Loan.LoanId,
                    p.FullName,
                    p.Phone,
                    o.Loan.ItemBarcode,
                    o.Loan.Title,
                    o.Loan.DueDate.ToString("yyyy-MM-dd"),
                    o.DaysOverdue > 0 ? o.DaysOverdue.ToString() : "0",
                    o.Tier.ToString(),
                    string.Format("${0:F2}", o.NetFineRemaining));

                switch (o.Tier)
                {
                    case OverdueTier.DueSoon:
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(29, 78, 216);
                        break;
                    case OverdueTier.FirstNotice:
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(180, 83, 9);
                        break;
                    case OverdueTier.SecondNotice:
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(194, 65, 12);
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.Font = new Font(dgvOverdues.Font, FontStyle.Bold);
                        break;
                    case OverdueTier.FinalDemand:
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(185, 28, 28);
                        dgvOverdues.Rows[rowIdx].DefaultCellStyle.Font = new Font(dgvOverdues.Font, FontStyle.Bold);
                        break;
                }
            }
        }

        private void RefreshFinesGrid()
        {
            dgvFines.Rows.Clear();
            List<FineTransaction> txs = db.Transactions.OrderByDescending(t => t.Date).ToList();

            foreach (FineTransaction t in txs)
            {
                dgvFines.Rows.Add(t.Date.ToString("yyyy-MM-dd HH:mm"), t.ReceiptNumber, t.PatronId, t.Type.ToString(), t.Method.ToString(), string.Format("${0:F2}", t.Amount), t.Reason, t.StaffOperator);
            }

            decimal assessed = db.Transactions.Where(t => t.Type == TransactionType.Charge).Sum(t => t.Amount);
            decimal collected = db.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            decimal waived = db.Transactions.Where(t => t.Type == TransactionType.Waiver).Sum(t => t.Amount);
            decimal outstanding = db.AssessAllOverdues(simulatedDate).Sum(o => o.NetFineRemaining);

            lblKpiTotalAssessed.Text = string.Format("${0:F2}", assessed);
            lblKpiTotalCollected.Text = string.Format("${0:F2}", collected);
            lblKpiTotalWaived.Text = string.Format("${0:F2}", waived);
            lblKpiOutstanding.Text = string.Format("${0:F2}", outstanding);
        }

        private void RefreshHoldsGrid()
        {
            dgvHolds.Rows.Clear();
            foreach (HoldRequest h in db.Holds)
            {
                Patron p = db.FindPatron(h.PatronId);
                string pName = p != null ? p.FullName : h.PatronId;
                int rowIdx = dgvHolds.Rows.Add(
                    h.HoldId,
                    h.ItemBarcode,
                    h.Title,
                    string.Format("{0} ({1})", pName, h.PatronId),
                    h.RequestDate.ToString("yyyy-MM-dd"),
                    h.Status.ToString(),
                    h.PickupDeadline.HasValue ? h.PickupDeadline.Value.ToString("yyyy-MM-dd") : "-");

                if (h.Status == HoldStatus.AwaitingPickup)
                {
                    dgvHolds.Rows[rowIdx].DefaultCellStyle.BackColor = Color.FromArgb(239, 246, 255);
                    dgvHolds.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.FromArgb(29, 78, 216);
                    dgvHolds.Rows[rowIdx].DefaultCellStyle.Font = new Font(dgvHolds.Font, FontStyle.Bold);
                }
            }
        }

        private void UpdateStatusStrip()
        {
            int totalActive = db.Loans.Count(l => l.Status == LoanStatus.Active);
            int totalOverdue = db.AssessAllOverdues(simulatedDate).Count(o => o.Tier != OverdueTier.DueSoon && o.Tier != OverdueTier.None);
            decimal outstanding = db.AssessAllOverdues(simulatedDate).Sum(o => o.NetFineRemaining);

            lblStatsLoans.Text = string.Format("Active Loans: {0}", totalActive);
            lblStatsOverdues.Text = string.Format("Overdue Items: {0}", totalOverdue);
            lblStatsFines.Text = string.Format("Unpaid Fines: ${0:F2}", outstanding);
        }

        private void ShowAlert(string message, Color bg, Color text, bool isBeep)
        {
            pnlAlertBanner.BackColor = bg;
            lblAlertText.ForeColor = text;
            lblAlertText.Text = message;
            lblStatusInfo.Text = message;

            if (isBeep)
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
            else
            {
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
        }

        #endregion

        #region Actions

        private void DoCheckin()
        {
            string barcode = txtCheckinBarcode.Text.Trim();
            if (string.IsNullOrEmpty(barcode))
            {
                ShowAlert("Please scan or type an Item Barcode to return.", Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28), true);
                return;
            }

            string summary;
            HoldRequest holdTriggered;
            decimal fine;
            bool ok = db.ReturnItem(barcode, simulatedDate, out summary, out holdTriggered, out fine);

            if (!ok)
            {
                ShowAlert(summary, Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28), true);
            }
            else
            {
                if (holdTriggered != null)
                {
                    ShowAlert("★ " + summary, Color.FromArgb(239, 246, 255), Color.FromArgb(29, 78, 216), true);
                }
                else if (fine > 0)
                {
                    ShowAlert("⚠ " + summary, Color.FromArgb(255, 247, 237), Color.FromArgb(194, 65, 12), true);
                }
                else
                {
                    ShowAlert("✓ " + summary, Color.FromArgb(240, 253, 244), Color.FromArgb(22, 163, 74), false);
                }
                txtCheckinBarcode.Clear();
                txtCheckinBarcode.Focus();
                RefreshAllViews();
            }
        }

        private void DoCheckout()
        {
            string barcode = txtCheckoutBarcode.Text.Trim();
            string patronId = txtCheckoutPatronId.Text.Trim();
            string title = txtCheckoutTitle.Text.Trim();

            LoanRecord created;
            string err;
            bool ok = db.CheckoutItem(barcode, title, "", "", patronId, out created, out err);

            if (!ok)
            {
                ShowAlert("⛔ " + err, Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28), true);
            }
            else
            {
                Patron p = db.FindPatron(patronId);
                string pName = p != null ? p.FullName : patronId;
                string msg = string.Format("✓ Successfully checked out '{0}' to {1} ({2}). Due: {3}",
                    created.Title, pName, created.PatronId, created.DueDate.ToString("yyyy-MM-dd"));

                ShowAlert(msg, Color.FromArgb(240, 253, 244), Color.FromArgb(22, 163, 74), false);
                txtCheckoutBarcode.Clear();
                txtCheckoutTitle.Clear();
                txtCheckoutBarcode.Focus();
                RefreshAllViews();
            }
        }

        private void DoRenewSelectedLoan()
        {
            if (dgvActiveLoans.SelectedRows.Count == 0)
            {
                MessageBox.Show("Please select an active loan row from the list to renew.", "Renew Loan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string loanId = dgvActiveLoans.SelectedRows[0].Cells["LoanId"].Value.ToString();
            string err;
            DateTime newDue;
            bool ok = db.RenewLoan(loanId, out err, out newDue);

            if (!ok)
            {
                MessageBox.Show(err, "Renewal Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(string.Format("Loan {0} successfully renewed!\nNew Due Date: {1}", loanId, newDue.ToString("yyyy-MM-dd")), "Loan Renewed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshAllViews();
            }
        }

        private void ShowAddPatronDialog()
        {
            Form dlg = new Form
            {
                Text = "Add New Library Patron",
                Size = new Size(420, 360),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label l1 = new Label { Text = "Patron ID (e.g. PAT-1011):", Location = new Point(20, 15), AutoSize = true };
            TextBox tId = new TextBox { Location = new Point(20, 35), Size = new Size(360, 24), Text = string.Format("PAT-{0}", db.Patrons.Count + 1001) };

            Label l2 = new Label { Text = "Full Name:", Location = new Point(20, 70), AutoSize = true };
            TextBox tName = new TextBox { Location = new Point(20, 90), Size = new Size(360, 24) };

            Label l3 = new Label { Text = "Category:", Location = new Point(20, 125), AutoSize = true };
            ComboBox cmb = new ComboBox { Location = new Point(20, 145), Size = new Size(170, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (PatronCategory c in Enum.GetValues(typeof(PatronCategory))) cmb.Items.Add(c.ToString());
            cmb.SelectedIndex = 0;

            Label l4 = new Label { Text = "Department:", Location = new Point(205, 125), AutoSize = true };
            TextBox tDept = new TextBox { Location = new Point(205, 145), Size = new Size(175, 24), Text = "Computer Science" };

            Label l5 = new Label { Text = "Email:", Location = new Point(20, 180), AutoSize = true };
            TextBox tEmail = new TextBox { Location = new Point(20, 200), Size = new Size(170, 24) };

            Label l6 = new Label { Text = "Phone (WhatsApp):", Location = new Point(205, 180), AutoSize = true };
            TextBox tPhone = new TextBox { Location = new Point(205, 200), Size = new Size(175, 24), Text = "+213 " };

            Button btnSave = new Button { Text = "Save Patron", Location = new Point(240, 260), Size = new Size(140, 30), DialogResult = DialogResult.OK, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

            dlg.Controls.AddRange(new Control[] { l1, tId, l2, tName, l3, cmb, l4, tDept, l5, tEmail, l6, tPhone, btnSave });

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(tId.Text) || string.IsNullOrEmpty(tName.Text))
                {
                    MessageBox.Show("Patron ID and Name are required.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Patron p = new Patron
                {
                    PatronId = tId.Text.Trim().ToUpperInvariant(),
                    FullName = tName.Text.Trim(),
                    Category = (PatronCategory)Enum.Parse(typeof(PatronCategory), cmb.SelectedItem.ToString()),
                    Department = tDept.Text.Trim(),
                    Email = tEmail.Text.Trim(),
                    Phone = tPhone.Text.Trim(),
                    RegisteredDate = DateTime.Now,
                    ExpiryDate = DateTime.Now.AddYears(1),
                    Status = PatronStatus.Active
                };

                db.Patrons.Add(p);
                db.SavePatrons();
                RefreshPatronsGrid();
                MessageBox.Show("Patron registered successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ToggleSelectedPatronStatus()
        {
            if (dgvPatrons.SelectedRows.Count == 0) return;
            string patronId = dgvPatrons.SelectedRows[0].Cells["Id"].Value.ToString();
            Patron p = db.FindPatron(patronId);
            if (p != null)
            {
                p.Status = (p.Status == PatronStatus.Active) ? PatronStatus.Suspended : PatronStatus.Active;
                db.SavePatrons();
                RefreshPatronsGrid();
                MessageBox.Show(string.Format("Patron {0} status changed to: {1}", p.FullName, p.Status), "Status Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private OverdueAssessment GetSelectedOverdueAssessment()
        {
            if (dgvOverdues.SelectedRows.Count == 0) return null;
            string loanId = dgvOverdues.SelectedRows[0].Cells["LoanId"].Value.ToString();
            LoanRecord l = db.Loans.FirstOrDefault(x => x.LoanId == loanId);
            if (l == null) return null;
            return db.AssessOverdue(l, simulatedDate);
        }

        private void PreviewSelectedOverdueLetter()
        {
            OverdueAssessment a = GetSelectedOverdueAssessment();
            if (a == null)
            {
                MessageBox.Show("Please select an overdue row first.", "Notice Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string html = NoticeGenerator.GeneratePrintableLetterHtml(a, simulatedDate);
            string tmpFile = Path.Combine(Path.GetTempPath(), string.Format("Notice_{0}_{1}.html", a.Loan.LoanId, DateTime.Now.Ticks));
            File.WriteAllText(tmpFile, html, Encoding.UTF8);

            try { System.Diagnostics.Process.Start(tmpFile); }
            catch (Exception ex) { MessageBox.Show("Failed to open browser: " + ex.Message); }
        }

        private void ExportSelectedOverdueEmail()
        {
            OverdueAssessment a = GetSelectedOverdueAssessment();
            if (a == null)
            {
                MessageBox.Show("Please select an overdue row first.", "Email Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string eml = NoticeGenerator.GenerateEmailEml(a, simulatedDate);
            string fileName = string.Format("Notice_Email_{0}.eml", a.Loan.LoanId);
            string fullPath = Path.Combine(db.BaseDirectory, fileName);
            File.WriteAllText(fullPath, eml, Encoding.UTF8);

            DialogResult dr = MessageBox.Show(string.Format("Email draft exported to:\n{0}\n\nOpen this .eml file in your default email client?", fullPath), "Email Notice Exported", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (dr == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(fullPath); } catch { }
            }
        }

        private void OpenSelectedWhatsApp()
        {
            OverdueAssessment a = GetSelectedOverdueAssessment();
            if (a == null)
            {
                MessageBox.Show("Please select an overdue row first.", "WhatsApp Dispatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string url = NoticeGenerator.GenerateWhatsAppUrl(a);
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { MessageBox.Show("Failed to launch WhatsApp URL: " + ex.Message); }
        }

        private void CopySelectedWhatsAppText()
        {
            OverdueAssessment a = GetSelectedOverdueAssessment();
            if (a == null) return;
            string text = NoticeGenerator.GenerateWhatsAppMessage(a);
            Clipboard.SetText(text);
            MessageBox.Show("WhatsApp message text copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BatchExportAllNotices()
        {
            List<OverdueAssessment> overdues = db.AssessAllOverdues(simulatedDate);
            if (overdues.Count == 0)
            {
                MessageBox.Show("No active overdue notices to export.", "Batch Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string outDir = Path.Combine(db.BaseDirectory, "notices_export");
            Directory.CreateDirectory(outDir);

            int htmlCount = 0;
            int emlCount = 0;

            foreach (OverdueAssessment a in overdues)
            {
                string html = NoticeGenerator.GeneratePrintableLetterHtml(a, simulatedDate);
                File.WriteAllText(Path.Combine(outDir, string.Format("Notice_{0}_{1}.html", a.Tier.ToString(), a.Loan.LoanId)), html, Encoding.UTF8);
                htmlCount++;

                string eml = NoticeGenerator.GenerateEmailEml(a, simulatedDate);
                File.WriteAllText(Path.Combine(outDir, string.Format("Email_{0}_{1}.eml", a.Tier.ToString(), a.Loan.LoanId)), eml, Encoding.UTF8);
                emlCount++;
            }

            MessageBox.Show(string.Format("Batch export completed successfully!\nExported {0} HTML letters and {1} RFC 822 .eml drafts to:\n{2}", htmlCount, emlCount, outDir), "Batch Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

            try { System.Diagnostics.Process.Start(outDir); } catch { }
        }

        private void DoCollectPayment()
        {
            string patronId = txtPayPatronId.Text.Trim();
            decimal amt;
            if (!decimal.TryParse(txtPayAmount.Text.Trim(), out amt) || amt <= 0)
            {
                MessageBox.Show("Please enter a valid positive payment amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PaymentMethod m = (PaymentMethod)Enum.Parse(typeof(PaymentMethod), cmbPayMethod.SelectedItem.ToString());
            string rcpt, err;
            bool ok = db.RecordPayment(patronId, "", amt, m, "DeskCashier", "Cashier payment entry", out rcpt, out err);

            if (!ok)
            {
                MessageBox.Show(err, "Payment Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(string.Format("Payment of ${0:F2} processed!\nReceipt Number: {1}", amt, rcpt), "Payment Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshAllViews();
            }
        }

        private string PromptInput(string prompt, string title, string defaultValue)
        {
            Form promptForm = new Form
            {
                Width = 420,
                Height = 180,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };
            Label textLabel = new Label { Left = 20, Top = 18, Text = prompt, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            TextBox textBox = new TextBox { Left = 20, Top = 45, Width = 360, Text = defaultValue, Font = new Font("Segoe UI", 9.5F) };
            Button confirmation = new Button { Text = "OK", Left = 200, Width = 85, Top = 88, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button cancel = new Button { Text = "Cancel", Left = 295, Width = 85, Top = 88, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            promptForm.Controls.Add(textLabel);
            promptForm.Controls.Add(textBox);
            promptForm.Controls.Add(confirmation);
            promptForm.Controls.Add(cancel);
            promptForm.AcceptButton = confirmation;
            promptForm.CancelButton = cancel;

            return promptForm.ShowDialog(this) == DialogResult.OK ? textBox.Text : "";
        }

        private void DoWaiveFine()
        {
            string patronId = txtPayPatronId.Text.Trim();
            decimal amt;
            if (!decimal.TryParse(txtPayAmount.Text.Trim(), out amt) || amt <= 0)
            {
                MessageBox.Show("Please enter a valid positive waiver amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string reason = PromptInput("Enter justification reason for fine waiver (Required):", "Waive Fine Justification", "Medical certificate submitted");
            if (string.IsNullOrEmpty(reason))
            {
                MessageBox.Show("Fine waiver was cancelled. Justification reason is required.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string rcpt, err;
            bool ok = db.WaiveFine(patronId, "", amt, reason, "Supervisor", out rcpt, out err);

            if (!ok)
            {
                MessageBox.Show(err, "Waiver Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(string.Format("Waiver of ${0:F2} authorized!\nWaiver Reference: {1}", amt, rcpt), "Fine Waived", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshAllViews();
            }
        }

        private void ShowPlaceHoldDialog()
        {
            Form dlg = new Form
            {
                Text = "Place Reservation Hold on Book",
                Size = new Size(380, 240),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label l1 = new Label { Text = "Item Barcode to Reserve:", Location = new Point(20, 15), AutoSize = true };
            TextBox tBc = new TextBox { Location = new Point(20, 35), Size = new Size(320, 24) };

            Label l2 = new Label { Text = "Book Title (Optional):", Location = new Point(20, 70), AutoSize = true };
            TextBox tTitle = new TextBox { Location = new Point(20, 90), Size = new Size(320, 24) };

            Label l3 = new Label { Text = "Reserving Patron ID:", Location = new Point(20, 125), AutoSize = true };
            TextBox tPatron = new TextBox { Location = new Point(20, 145), Size = new Size(200, 24) };

            Button btnOk = new Button { Text = "Confirm Hold", Location = new Point(230, 143), Size = new Size(110, 27), DialogResult = DialogResult.OK, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

            dlg.Controls.AddRange(new Control[] { l1, tBc, l2, tTitle, l3, tPatron, btnOk });

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                HoldRequest h;
                string err;
                bool ok = db.PlaceHold(tBc.Text, tTitle.Text, tPatron.Text, out h, out err);
                if (!ok)
                {
                    MessageBox.Show(err, "Hold Placement Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Hold placed successfully! Queued in system.", "Hold Placed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshHoldsGrid();
                }
            }
        }

        private void ExportMasterHtmlDashboard()
        {
            string html = NoticeGenerator.GenerateMasterHtmlDashboard(db, simulatedDate);
            string file = Path.Combine(db.BaseDirectory, "circulation_executive_report.html");
            File.WriteAllText(file, html, Encoding.UTF8);

            DialogResult dr = MessageBox.Show(string.Format("Executive HTML report exported to:\n{0}\n\nOpen report in your web browser?", file), "Report Exported", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (dr == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(file); } catch { }
            }
        }

        private void ExportLoansCsvFile()
        {
            string csv = NoticeGenerator.ExportLoansCsv(db);
            string file = Path.Combine(db.BaseDirectory, "active_loans_export.csv");
            File.WriteAllText(file, csv, Encoding.UTF8);

            MessageBox.Show(string.Format("Active loans exported to CSV:\n{0}", file), "CSV Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion
    }

    #endregion

    #region CLI Dispatcher & Program Entrypoint

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }

            string command = args[0].Trim().ToLowerInvariant();

            if (command == "--gui")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }

            if (command == "--demo" || command == "--test")
            {
                bool success = DemoEngine.RunAllTests();
                return success ? 0 : 1;
            }

            if (command == "--help" || command == "-h" || command == "/?")
            {
                PrintHelp();
                return 0;
            }

            CirculationDatabase db = new CirculationDatabase();
            db.LoadOrSeed();
            DateTime now = DateTime.Now;

            if (command == "--status" || command == "--summary")
            {
                PrintStatus(db, now);
                return 0;
            }

            if (command == "--overdue")
            {
                PrintOverdues(db, now);
                return 0;
            }

            if (command == "--patrons")
            {
                PrintPatrons(db, now);
                return 0;
            }

            if (command == "--checkout")
            {
                if (args.Length < 3)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Missing arguments for --checkout. Usage: --checkout <item_barcode> <patron_id> [title]");
                    Console.ResetColor();
                    return 1;
                }
                string barcode = args[1];
                string patronId = args[2];
                string title = args.Length > 3 ? args[3] : "";

                LoanRecord loan;
                string err;
                bool ok = db.CheckoutItem(barcode, title, "", "", patronId, out loan, out err);
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Checkout FAILED: " + err);
                    Console.ResetColor();
                    return 1;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(string.Format("Checkout SUCCESS: '{0}' checked out to {1} until {2} (LoanId: {3})",
                        loan.Title, loan.PatronId, loan.DueDate.ToString("yyyy-MM-dd"), loan.LoanId));
                    Console.ResetColor();
                    return 0;
                }
            }

            if (command == "--checkin")
            {
                if (args.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Missing barcode for --checkin. Usage: --checkin <item_barcode>");
                    Console.ResetColor();
                    return 1;
                }
                string barcode = args[1];
                string summary;
                HoldRequest hold;
                decimal fine;
                bool ok = db.ReturnItem(barcode, now, out summary, out hold, out fine);
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Check-in FAILED: " + summary);
                    Console.ResetColor();
                    return 1;
                }
                else
                {
                    Console.ForegroundColor = (fine > 0) ? ConsoleColor.Yellow : ConsoleColor.Green;
                    Console.WriteLine("Check-in SUCCESS: " + summary);
                    Console.ResetColor();
                    return 0;
                }
            }

            if (command == "--renew")
            {
                if (args.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Missing argument for --renew. Usage: --renew <barcode_or_loan_id>");
                    Console.ResetColor();
                    return 1;
                }
                string q = args[1];
                string err;
                DateTime newDue;
                bool ok = db.RenewLoan(q, out err, out newDue);
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Renewal FAILED: " + err);
                    Console.ResetColor();
                    return 1;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(string.Format("Renewal SUCCESS: '{0}' renewed. New Due Date: {1}", q, newDue.ToString("yyyy-MM-dd")));
                    Console.ResetColor();
                    return 0;
                }
            }

            if (command == "--generate-notices")
            {
                string outDir = Path.Combine(db.BaseDirectory, "notices_export");
                if (args.Length > 2 && (args[1] == "--out-dir" || args[1] == "-o"))
                {
                    outDir = args[2];
                }
                Directory.CreateDirectory(outDir);

                List<OverdueAssessment> overdues = db.AssessAllOverdues(now);
                Console.WriteLine(string.Format("Generating notices for {0} overdue loans into '{1}'...", overdues.Count, outDir));

                int count = 0;
                foreach (OverdueAssessment a in overdues)
                {
                    string html = NoticeGenerator.GeneratePrintableLetterHtml(a, now);
                    File.WriteAllText(Path.Combine(outDir, string.Format("Notice_{0}_{1}.html", a.Tier.ToString(), a.Loan.LoanId)), html, Encoding.UTF8);

                    string eml = NoticeGenerator.GenerateEmailEml(a, now);
                    File.WriteAllText(Path.Combine(outDir, string.Format("Email_{0}_{1}.eml", a.Tier.ToString(), a.Loan.LoanId)), eml, Encoding.UTF8);
                    count++;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("Exported {0} HTML letters and {0} .eml files successfully.", count));
                Console.ResetColor();
                return 0;
            }

            if (command == "--export-html")
            {
                string targetFile = (args.Length > 1) ? args[1] : "circulation_executive_report.html";
                string html = NoticeGenerator.GenerateMasterHtmlDashboard(db, now);
                File.WriteAllText(targetFile, html, Encoding.UTF8);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("Executive HTML report exported to: {0}", Path.GetFullPath(targetFile)));
                Console.ResetColor();
                return 0;
            }

            if (command == "--export-csv")
            {
                string targetFile = (args.Length > 1) ? args[1] : "active_loans_export.csv";
                string csv = NoticeGenerator.ExportLoansCsv(db);
                File.WriteAllText(targetFile, csv, Encoding.UTF8);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("Loans exported to CSV: {0}", Path.GetFullPath(targetFile)));
                Console.ResetColor();
                return 0;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(string.Format("Unknown command: '{0}'. Use --help for available options.", command));
            Console.ResetColor();
            return 1;
        }

        private static void PrintStatus(CirculationDatabase db, DateTime asOfDate)
        {
            List<OverdueAssessment> overdues = db.AssessAllOverdues(asOfDate);
            int active = db.Loans.Count(l => l.Status == LoanStatus.Active);
            int returned = db.Loans.Count(l => l.Status == LoanStatus.Returned);
            decimal totalFinesAssessed = db.Transactions.Where(t => t.Type == TransactionType.Charge).Sum(t => t.Amount);
            decimal totalFinesCollected = db.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            decimal outstanding = overdues.Sum(o => o.NetFineRemaining);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("             CENTRAL LIBRARY CIRCULATION & OVERDUE STATUS");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(string.Format("Date As Of          : {0}", asOfDate.ToString("yyyy-MM-dd HH:mm")));
            Console.WriteLine(string.Format("Registered Patrons  : {0}", db.Patrons.Count));
            Console.WriteLine(string.Format("Active Loans        : {0}", active));
            Console.WriteLine(string.Format("Returned History    : {0}", returned));
            Console.WriteLine(string.Format("Overdue Items       : {0}", overdues.Count));
            Console.WriteLine(string.Format("Pending Holds       : {0}", db.Holds.Count(h => h.Status == HoldStatus.Queued || h.Status == HoldStatus.AwaitingPickup)));
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine(string.Format("Total Fines Charged : ${0:F2}", totalFinesAssessed));
            Console.WriteLine(string.Format("Total Collected     : ${0:F2}", totalFinesCollected));
            Console.ForegroundColor = (outstanding > 0) ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine(string.Format("Current Outstanding : ${0:F2}", outstanding));
            Console.ResetColor();
            Console.WriteLine("================================================================================");
        }

        private static void PrintOverdues(CirculationDatabase db, DateTime asOfDate)
        {
            List<OverdueAssessment> overdues = db.AssessAllOverdues(asOfDate);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("                                ACTIVE OVERDUE ITEMS & ESCALATION TIERS");
            Console.WriteLine("========================================================================================================");
            Console.ResetColor();
            Console.WriteLine(string.Format("{0,-12} {1,-14} {2,-11} {3,-12} {4,-32} {5,-6} {6,-14} {7,8}",
                "LOAN ID", "PATRON", "CATEGORY", "DUE DATE", "BOOK TITLE", "DAYS", "TIER", "FINE DUE"));
            Console.WriteLine(new string('-', 104));

            foreach (OverdueAssessment o in overdues)
            {
                Patron p = o.Patron ?? new Patron { FullName = "Unknown", PatronId = o.Loan.PatronId };
                string title = o.Loan.Title.Length > 30 ? (o.Loan.Title.Substring(0, 27) + "...") : o.Loan.Title;

                if (o.Tier == OverdueTier.FinalDemand) Console.ForegroundColor = ConsoleColor.Red;
                else if (o.Tier == OverdueTier.SecondNotice) Console.ForegroundColor = ConsoleColor.DarkYellow;
                else if (o.Tier == OverdueTier.FirstNotice) Console.ForegroundColor = ConsoleColor.Yellow;
                else Console.ForegroundColor = ConsoleColor.Cyan;

                Console.WriteLine(string.Format("{0,-12} {1,-14} {2,-11} {3,-12} {4,-32} {5,-6} {6,-14} ${7,7:F2}",
                    o.Loan.LoanId,
                    p.FullName.Length > 13 ? (p.FullName.Substring(0, 11) + "..") : p.FullName,
                    p.Category.ToString(),
                    o.Loan.DueDate.ToString("yyyy-MM-dd"),
                    title,
                    o.DaysOverdue > 0 ? o.DaysOverdue.ToString() : "0",
                    o.Tier.ToString(),
                    o.NetFineRemaining));
            }
            Console.ResetColor();
            Console.WriteLine("========================================================================================================");
        }

        private static void PrintPatrons(CirculationDatabase db, DateTime asOfDate)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("                                        PATRON ROSTER DIRECTORY");
            Console.WriteLine("========================================================================================================");
            Console.ResetColor();
            Console.WriteLine(string.Format("{0,-10} {1,-22} {2,-14} {3,-24} {4,-8} {5,-10} {6,-10}",
                "PATRON ID", "FULL NAME", "CATEGORY", "DEPARTMENT", "LOANS", "BALANCE", "STATUS"));
            Console.WriteLine(new string('-', 104));

            foreach (Patron p in db.Patrons)
            {
                int activeLoans = db.GetActiveLoansForPatron(p.PatronId).Count;
                decimal balance = db.GetOutstandingBalanceForPatron(p.PatronId, asOfDate);

                if (p.Status == PatronStatus.Suspended) Console.ForegroundColor = ConsoleColor.Red;
                else if (balance >= 10.00m) Console.ForegroundColor = ConsoleColor.Yellow;
                else Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine(string.Format("{0,-10} {1,-22} {2,-14} {3,-24} {4,-8} ${5,-9:F2} {6,-10}",
                    p.PatronId,
                    p.FullName.Length > 20 ? (p.FullName.Substring(0, 18) + "..") : p.FullName,
                    p.Category.ToString(),
                    p.Department.Length > 22 ? (p.Department.Substring(0, 20) + "..") : p.Department,
                    activeLoans,
                    balance,
                    p.Status.ToString()));
            }
            Console.ResetColor();
            Console.WriteLine("========================================================================================================");
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"Circulation & Overdue Management Desk (CirculationOverdueDesk.exe)
A standalone zero-dependency Windows desktop & CLI utility for library patron circulation,
loan tracking, tiered overdue escalation, notice generation, and fine ledger management.

USAGE:
  CirculationOverdueDesk.exe [command] [options]

COMMANDS:
  (no args) or --gui                   Launch interactive Windows Forms Desktop Studio.
  --status | --summary                 Display high-level circulation & overdue KPI metrics.
  --overdue                            Display itemized overdue loans and escalation tiers.
  --patrons                            List registered patron directory and balances.
  --checkout <barcode> <patron_id>     Process fast book checkout.
  --checkin <barcode>                  Process book return, compute fines & check holds.
  --renew <barcode_or_loan_id>         Renew active loan if permitted by policy & holds.
  --generate-notices [-o <dir>]        Batch-export HTML letters & RFC 822 .eml email drafts.
  --export-html [filename.html]        Export self-contained executive HTML dashboard report.
  --export-csv [filename.csv]          Export active loans and overdue roster to CSV.
  --demo | --test                      Run automated self-verification engine test suite.
  --help | -h                          Display this help message.

EXAMPLES:
  .\CirculationOverdueDesk.exe --status
  .\CirculationOverdueDesk.exe --overdue
  .\CirculationOverdueDesk.exe --checkout LIB-100101 PAT-1001
  .\CirculationOverdueDesk.exe --checkin LIB-100107
  .\CirculationOverdueDesk.exe --generate-notices -o notices_export
  .\CirculationOverdueDesk.exe --export-html report.html
  .\CirculationOverdueDesk.exe --demo
");
        }
    }

    #endregion
}
