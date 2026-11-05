# Tool #01: Library QuickDesk (`LibraryQuickDesk.exe`)

A zero-dependency standalone Windows desktop & CLI utility for daily library desk operations:
- **ISBN-10 & ISBN-13 Checksum Validator**: Validates and normalizes 10-digit and 13-digit ISBN codes.
- **Loan & Due-Date Tracker**: Check out books by shelf code and borrower, automatically compute due dates, and highlight overdue loans.
- **Instant Search & Overdue Report Export**: Filter by title, ISBN, shelf code, or borrower, and export a formatted `overdue_report.txt`.

## Usage

- **Desktop GUI**: Double-click `LibraryQuickDesk.exe` (or run with no arguments).
- **CLI Status Report**:
  ```powershell
  .\LibraryQuickDesk.exe --status
  ```
- **CLI ISBN Validation**:
  ```powershell
  .\LibraryQuickDesk.exe --validate-isbn 978-0-262-03384-8
  ```

## Recompile from Source

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:LibraryQuickDesk.exe LibraryQuickDesk.cs
```
