# Tool #03: ID Card Copy Maker & CamScanner Bad Photo Enhancer (`IdCardCamScannerDesk.exe`)

A zero-dependency standalone Windows desktop & CLI utility built for e-service libraries, copy shops (*maktaba* / cyber-services), and administrative desks to turn crooked, shadowed smartphone/WhatsApp photos of **ID Cards (Recto/Verso)** and **A4 Documents** into clean, toner-saving prints:

- **🪪 ID Card Copy Maker & A4 Sheet Printer (Recto + Verso)**:
  - Load Front (*Recto*) and Back (*Verso*) photos via file picker, **Drag & Drop**, or **Paste (`Ctrl+V`)** directly from WhatsApp Desktop.
  - **4-Corner Perspective Unwarp + Live 2.5x Magnifying Loupe**: Drag the 4 red corner handles on any tilted phone photo of an ID card to flatten it to exact CR80 aspect ratio.
  - **Exact 1:1 Print Layouts on A4 (210 × 297 mm at 300 DPI)**:
    - `1x Top & Bottom` (Exact CR80 size: `85.60 × 53.98 mm`)
    - `1x Side-by-Side` (Top row only — saves bottom half of A4 sheet)
    - `2x Copies on 1 A4 Sheet` (2 Recto + 2 Verso)
    - `4x Copies on 1 A4 Sheet` (4 Recto + 4 Verso grid)
    - `Enlarged 135% Copy` (For administrative dossiers)
  - Optional **CR80 Rounded Frame**, **Scissor Cut Guides**, and **Cross-Card Security Stamp** (`"USAGE ADMINISTRATIF UNIQUEMENT"`).
- **📄 CamScanner Full-Page Document Enhancer (Fix Bad Photos)**:
  - **8-DOF Projective Homography Unwarp**: Flatten skewed smartphone photos of certificates, attestations, forms, or book pages to A4 proportions.
  - **Morphological Background Illumination Division**: Automatically estimates local paper illumination and divides out dark hand/lamp shadows and yellow indoor bulb tint:
    - **Clean Grayscale (Photocopy Mode)**: Pure white background while keeping ID portrait photos smooth and recognizable.
    - **Magic Color**: Pure white paper background while boosting blue/red official stamps and signatures.
    - **Toner-Saver B&W**: Crisp black text on pure white paper (saves ~90% printer ink/toner).

## Usage

### Desktop GUI Studio
Double-click `IdCardCamScannerDesk.exe` (or run with no arguments). It preloads realistic simulated angled & shadowed ID card and document photos so you can test the 4-corner unwarp and shadow-removal sliders immediately.

### Command-Line Interface (CLI)

- **Clean & Unwarp a Bad Smartphone Document Photo**:
  ```powershell
  .\IdCardCamScannerDesk.exe --enhance bad_photo.jpg clean_a4.png --mode magic --autocrop
  ```

- **Generate a 300-DPI A4 ID Card Sheet (Recto + Verso)**:
  ```powershell
  .\IdCardCamScannerDesk.exe --id-sheet --front id_front.jpg --back id_back.jpg --out a4_id_sheet.png --layout 2x --mode gray
  ```

- **Run Self-Verification Demo**:
  ```powershell
  .\IdCardCamScannerDesk.exe --demo
  ```

## Recompile from Source

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:IdCardCamScannerDesk.exe IdCardCamScannerDesk.cs
```
