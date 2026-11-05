# Tool #02: Shelf Label Studio (`ShelfLabelStudio.exe`)

A zero-dependency standalone Windows desktop & CLI utility for library shelf labeling, Cutter-Sanborn author number calculation, and Code 39 barcode production:

- **Spine & Pocket Label Engine**: Generates standard library spine labels (1" x 1.5"), circulation pocket cards (2" x 4"), combo pairs with cut lines, and shelf stack placards.
- **LC / Cutter-Sanborn Number Calculator**: Automatically computes standardized Cutter-Sanborn alphanumeric author call numbers and work marks from author surname and title.
- **Built-in Code 39 Barcode Engine**: Zero-dependency pure C# barcode generator that outputs crisp vector SVGs for print sheets, high-DPI Windows bitmaps, and terminal ASCII bars.
- **Batch Export to Printable HTML & PNG**: Exports ready-to-print sheets with CSS print rules (`@page`, `@media print`), interactive toolbar, and direct PNG image export.

## Usage

### Desktop GUI Studio
Double-click `ShelfLabelStudio.exe` (or run with no arguments) to launch the interactive studio featuring live visual label previews, catalog data editor, zoom controls, and print/export tools.

### Command-Line Interface (CLI)

- **View Catalog Inventory**:
  ```powershell
  .\ShelfLabelStudio.exe --status
  ```

- **Calculate Cutter Number**:
  ```powershell
  .\ShelfLabelStudio.exe --cutter "Tolkien, J.R.R." --title "The Hobbit"
  # Output: .T657h
  ```

- **Render Code 39 Barcode (ASCII & PNG)**:
  ```powershell
  .\ShelfLabelStudio.exe --barcode "LIB-823001" --out barcode.png
  ```

- **Generate Batch Printable HTML Label Sheet**:
  ```powershell
  .\ShelfLabelStudio.exe --batch shelf_items.tsv --template combo --out shelf_labels.html
  ```

- **Generate Single Label**:
  ```powershell
  .\ShelfLabelStudio.exe --single --barcode "LIB-005002" --title "Clean Code" --author "Martin, Robert C." --class "005.13" --year "2008" --out clean_code_label.html
  ```

- **Self-Verification Engine Test**:
  ```powershell
  .\ShelfLabelStudio.exe --demo
  ```

## Recompile from Source

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:ShelfLabelStudio.exe ShelfLabelStudio.cs
```
