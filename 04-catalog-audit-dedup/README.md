# Tool #04: Catalog Audit & Deduplication Desk (`CatalogAuditDesk.exe`)

A zero-dependency standalone Windows desktop and CLI utility for library catalog management, physical shelf inventory audit, barcode wedge scanning reconciliation, and intelligent duplicate/variant detection.

## Key Capabilities

- **Master Catalog Holdings & Indexing Engine**:
  - Full-text instant indexing across Book Title, Author, DDC Dewey Decimal Classification, Cutter-Sanborn call number, Barcode, and ISBN.
  - Multi-stack organization with filtering by shelf/stack location and circulation status (`Available`, `CheckedOut`, `InRepair`, `Missing`, `ReferenceOnly`).
  - Add, edit, remove, and manage holdings with automatic persistent TSV storage (`catalog_inventory.tsv`) and seed collection.

- **Physical Shelf Inventory Audit & Shelf-Reading**:
  - Live barcode scanner input box optimized for USB hardware wedge scanners and manual high-speed entry.
  - Automatic discrepancy classification on every scan:
    - `VerifiedPresent`: Item belongs to the target shelf and is confirmed present.
    - `Misplaced`: Item belongs to a different stack (e.g. STACK-B1-CS book found on STACK-A1-LIT).
    - `Uncataloged`: Barcode not found anywhere in the catalog (ghost item / unentered donation).
    - `StatusConflict`: Item is marked `CheckedOut` or `Missing` in system records, but is physically found on the shelf.
    - `DuplicateScan`: Item was already scanned in the active session.
  - **Dewey Decimal Sequence Verifier**: Detects and warns when a book is placed out of Dewey Decimal numerical sequence on the shelf (e.g. `823.912` placed before `005.13`).
  - **Batch Scan File Import**: Load text scan files (`.txt`, `.tsv`, `.csv`) from handheld batch scanners or mobile phone scanning apps.
  - **One-Click Reconciliation**: Automatically re-shelves misplaced books, clears false checkout/missing statuses, and updates `LastAudited` timestamps.

- **Intelligent Duplicate & Edition Variant Detector**:
  - **ISBN-10 & ISBN-13 Canonical Equivalence**: Accurately matches 10-digit and 13-digit ISBNs across different printings.
  - **Token-Sorted & Inverted Author Matching**: Transparently matches inverted names (`"Lee, Harper"` vs `"Harper Lee"`).
  - **Fuzzy Title & Subtitle Matching**: Uses hybrid Levenshtein distance, Jaro-Winkler similarity, and Token Subset inclusion to detect commemorative editions, subtitle additions, and catalog typos (e.g., *"To Kill a Mockingbird"* ~ *"To Kill a Mockingbird (50th Anniversary Edition)"*).
  - **Barcode Collision Warning**: Flags critical cataloging errors where identical barcodes were assigned to different titles.

- **Reporting & Export Studio**:
  - **Self-Contained Interactive HTML Report**: Clean modern dashboard with KPI cards, color-coded discrepancy tables, printable CSS (`@media print`), and one-click print/PDF preview.
  - **CSV Catalog Export**: Standard comma-separated output compatible with Koha, PMB, Excel, and Google Sheets.
  - **Markdown Audit Logs**: Ready-to-commit audit discrepancy reports.

---

## Usage Guide

### 1. Interactive Desktop GUI Studio
Launch `CatalogAuditDesk.exe` without arguments (or with `--gui`) to open the Windows Forms application:
- **Tab 1: Master Catalog Holdings**: Live search bar, shelf filter, item editor dialog, and collection statistics.
- **Tab 2: Physical Shelf Inventory Audit**: Shelf selection, barcode scanner input, real-time audio/visual alert banner, live audit log, and missing item checklist.
- **Tab 3: Duplicate & Variant Detection**: Adjustable fuzzy threshold slider (50%–100%), criteria checkboxes, and duplicate pair diagnostics.
- **Tab 4: Audit Reports & KPI Dashboard**: Real-time inventory KPIs, HTML report generation, CSV export, and instant browser preview.

### 2. Command-Line Interface (CLI)

- **Collection Status & Shelf Breakdown**:
  ```powershell
  .\CatalogAuditDesk.exe --status
  ```

- **Instant Catalog Search**:
  ```powershell
  .\CatalogAuditDesk.exe --search "Pride"
  .\CatalogAuditDesk.exe --search "Clean Code"
  ```

- **Run Duplicate & Variant Scan**:
  ```powershell
  .\CatalogAuditDesk.exe --dedup
  .\CatalogAuditDesk.exe --dedup --threshold 0.75
  ```

- **Batch Shelf Inventory Audit from Scanned Barcode File**:
  ```powershell
  .\CatalogAuditDesk.exe --audit --shelf "STACK-A1-LIT" --scan-file sample_shelf_scan.txt
  ```

- **Batch Audit with Automatic Catalog Reconciliation**:
  ```powershell
  .\CatalogAuditDesk.exe --audit --shelf "STACK-A1-LIT" --scan-file sample_shelf_scan.txt --reconcile
  ```

- **Export Standalone Interactive HTML Report**:
  ```powershell
  .\CatalogAuditDesk.exe --export-html library_catalog_report.html
  ```

- **Export Catalog to CSV**:
  ```powershell
  .\CatalogAuditDesk.exe --export-csv library_catalog_export.csv
  ```

- **Run Self-Verification Engine Tests**:
  ```powershell
  .\CatalogAuditDesk.exe --demo
  ```

---

## Sample Data Files Included

- `catalog_inventory.tsv`: Master database of 28 curated books across 5 stacks (`STACK-A1-LIT`, `STACK-A2-LIT`, `STACK-B1-CS`, `STACK-B2-SCI`, `STACK-C1-HIST`).
- `sample_shelf_scan.txt`: Example batch scan file containing 8 scans demonstrating verified items, misplaced items, status conflicts, and uncataloged barcodes.
- `library_catalog_report.html`: Exported interactive HTML report.
- `library_catalog_export.csv`: Exported full catalog CSV.

---

## Recompile from Source

Using the standard Microsoft .NET Framework 4.0 C# compiler:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:CatalogAuditDesk.exe CatalogAuditDesk.cs
```
