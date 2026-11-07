# Tool #05: Circulation & Overdue Notice Desk (`CirculationOverdueDesk.exe`)

A zero-dependency standalone Windows desktop and CLI utility for library patron circulation, loan tracking, multi-tier overdue escalation, notice generation, hold shelf reservations, and fine ledger management.

## Key Capabilities

- **Patron Directory & Tiered Circulation Policy Engine**:
  - Full patron lifecycle management across membership categories: `Undergraduate`, `Postgraduate`, `Faculty`, `Staff`, `Community`, and `Alumni`.
  - Category-specific lending parameters:
    - *Undergraduate*: 14 days loan, 5 items max, 2 days grace, $0.50/day fine, $15.00 cap, 2 renewals max.
    - *Postgraduate*: 28 days loan, 10 items max, 3 days grace, $0.25/day fine, $20.00 cap, 4 renewals max.
    - *Faculty*: 90 days loan, 25 items max, 7 days grace, $0.10/day fine, $10.00 cap, 10 renewals max.
    - *Community*: 14 days loan, 3 items max, 1 day grace, $1.00/day fine, $25.00 cap, 1 renewal max.
  - Automatic borrowing restrictions: blocks checkout on expired cards, suspended accounts, or unpaid balance exceeding the block threshold ($10.00).

- **Fast Circulation Desk (Check-in, Check-out, Renewal & Hold Triggers)**:
  - Hardware wedge scanner friendly inputs for barcode entry with Enter key triggers.
  - Audio and visual alert banners for check-in confirmation, overdue warnings, and hold shelf alerts.
  - Real-time **Hold Shelf Alert**: Automatically detects when a returned book has a waiting reservation, transitioning the hold to `AwaitingPickup` with a 7-day pickup deadline and sounding an alert.
  - Renewal guard: checks maximum renewals permitted per category and prevents renewals if another patron has a pending reservation hold.

- **Multi-Tier Overdue Notice & Escalation Dispatcher**:
  - 4-Tier Automated Escalation:
    - **Tier 1: Courtesy Reminder / Due Soon** (-2 to 0 days before due date, or within grace period). Friendly tone with renewal instructions.
    - **Tier 2: 1st Overdue Notice** (1 to 7 days overdue). Notice of accrued daily fines.
    - **Tier 3: 2nd Notice & Suspension Alert** (8 to 21 days overdue). Warning that borrowing privileges are temporarily suspended.
    - **Tier 4: Final Demand & Assumed Lost Billing** (22+ days overdue). Item declared assumed lost with replacement fee assessment ($50.00–$75.00) plus administrative warning.
  - **Multi-Channel Notice Formats**:
    - **Printable HTML Notice Letter**: Official library letterhead, itemized table of overdue books, barcode slip, and `@media print` styling for 1-click A4/Letter printing or PDF export.
    - **RFC 822 Email Drafts (`.eml`)**: Pre-formatted MIME emails ready to open directly in Outlook, Thunderbird, or mail clients.
    - **WhatsApp & SMS Click-to-Chat**: Generates one-click `https://api.whatsapp.com/send?phone=...&text=...` URLs for instant dispatch from the librarian's desk.

- **Fines, Payments & Waiver Accounting Ledger**:
  - Complete financial audit trail tracking charges, payments, and supervisor-authorized waivers.
  - Payment methods: Cash, Credit Card, Student Account, Bank Transfer.
  - Mandatory justification notes for fee waivers (medical excuses, weather closures, system grace).
  - Automated receipt generation (`REC-YYYYMMDD-XXXX` and `WAV-YYYYMMDD-XXXX`).

- **Executive Reporting & Data Export**:
  - **Master HTML Dashboard**: Responsive executive overview with KPI summary cards, color-coded overdue item tables, and active hold shelf queue.
  - **CSV Export**: Standard CSV export of active loans and patron balances.
  - **TSV Persistence**: Self-contained plain-text storage (`patrons.tsv`, `loans.tsv`, `holds.tsv`, `fines.tsv`) with automatic seed data on first run.

---

## Usage Guide

### 1. Interactive Desktop GUI Studio

Launch `CirculationOverdueDesk.exe` without arguments (or with `--gui`) to open the Windows Forms desktop application:

- **Tab 1: ⚡ Circulation Desk**:
  - Scan or enter barcode in *Fast Check-in* to return books; hear immediate audio confirmation and see hold shelf alerts.
  - Scan patron ID and barcode in *Fast Checkout* to process new loans with automatic due-date calculation.
  - View live active loans list, search by title or patron, and renew loans with one click.
- **Tab 2: 👥 Patron Directory**:
  - Search patrons by name, ID, or department; filter by membership category.
  - Add new patrons or toggle suspension status.
- **Tab 3: 🔔 Overdue Notice Dispatcher**:
  - Filter overdue items by escalation tier (Courtesy, 1st Notice, 2nd Notice, Final Demand).
  - Click **Preview Letter (HTML)** to preview the formatted printable letter in your browser.
  - Click **Export Email (.eml)** to generate an RFC 822 email file.
  - Click **Open WhatsApp** to launch instant WhatsApp web chat with the patron.
  - Click **Batch Export Notices** to generate all notices into `notices_export/`.
- **Tab 4: 💰 Fines & Cashier Ledger**:
  - View real-time financial KPIs (Total Assessed, Collected, Waived, Outstanding).
  - Collect payments or authorize fine waivers with prompt for justification.
- **Tab 5: 📌 Holds & Executive Reports**:
  - Manage reservation queues and pending hold shelf pickups.
  - Export the self-contained interactive executive HTML report or CSV file.

### 2. Command-Line Interface (CLI)

- **Circulation & Overdue KPI Summary**:
  ```powershell
  .\CirculationOverdueDesk.exe --status
  ```

- **Itemized Overdue Loans & Escalation Tiers**:
  ```powershell
  .\CirculationOverdueDesk.exe --overdue
  ```

- **Patron Roster & Balance Ledger**:
  ```powershell
  .\CirculationOverdueDesk.exe --patrons
  ```

- **Process Fast Book Checkout**:
  ```powershell
  .\CirculationOverdueDesk.exe --checkout LIB-100101 PAT-1001 "Clean Code"
  ```

- **Process Book Return (Check-in)**:
  ```powershell
  .\CirculationOverdueDesk.exe --checkin LIB-100107
  ```

- **Renew Active Loan**:
  ```powershell
  .\CirculationOverdueDesk.exe --renew LN-2026-0101
  ```

- **Batch Generate All Overdue Notices**:
  ```powershell
  .\CirculationOverdueDesk.exe --generate-notices -o notices_export
  ```

- **Export Executive HTML Report**:
  ```powershell
  .\CirculationOverdueDesk.exe --export-html circulation_executive_report.html
  ```

- **Export Active Loans to CSV**:
  ```powershell
  .\CirculationOverdueDesk.exe --export-csv active_loans_export.csv
  ```

- **Run Self-Verification Engine Tests**:
  ```powershell
  .\CirculationOverdueDesk.exe --demo
  ```

---

## Sample Data Files Included

- `patrons.tsv`: Master directory of 10 curated patrons across all 6 membership categories.
- `loans.tsv`: 11 loan records covering active on-time loans, courtesy due-soon loans, 1st/2nd notices, final demands, and returned history.
- `holds.tsv`: Active reservation queue including hold shelf pickups awaiting patron collection.
- `fines.tsv`: Financial transaction ledger of charges, payments, and waivers.
- `notices_export/`: Sample generated HTML notice letters and RFC 822 `.eml` email drafts.
- `circulation_executive_report.html`: Exported interactive executive HTML report.
- `active_loans_export.csv`: Exported active loans CSV.

---

## Recompile from Source

Using the standard Microsoft .NET Framework 4.0 C# compiler:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:CirculationOverdueDesk.exe CirculationOverdueDesk.cs
```
