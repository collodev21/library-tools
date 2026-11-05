# Library & E-Service Shop Management Tools (`library-tools`)

Automated collection of standalone Windows utilities (`.exe`) built to simplify library, copy-shop (*maktaba*), and e-service desk workflows (ID card A4 photocopy layout, CamScanner bad-photo shadow removal, ANEM `wassit.dz` client renewals, shelf labeling, ISBN validation, and loan tracking).

## Tools Index

| # | Folder | Executable | Description |
|---|--------|------------|-------------|
| 01 | [`01-library-quick-desk`](./01-library-quick-desk) | `LibraryQuickDesk.exe` | Desktop & CLI utility for ISBN-10/13 validation, book checkouts, due-date calculation, and overdue reporting. |
| 02 | [`02-shelf-label-studio`](./02-shelf-label-studio) | `ShelfLabelStudio.exe` | Desktop & CLI utility for library spine & pocket labels, Cutter-Sanborn author number calculation, and Code 39 barcode generation. |
| 02b | [`02-wassit-renewal-desk`](./02-wassit-renewal-desk) | `WassitShopDesk.exe` | **Rust** Web-UI & CLI manager for `wassit.dz` (`wassitonline.anem.dz`): live 6-month job-seeker renewal (`extendMyDemandePublic`), expired demand reactivation, PDF attestation download (`FicheDemandeurOnline`), mobile number change, and shop client renewal database with 15-day expiry & WhatsApp alerts. |
| 03 | [`03-id-card-camscanner-desk`](./03-id-card-camscanner-desk) | `IdCardCamScannerDesk.exe` | **ID Card Copy Maker & CamScanner Bad Photo Enhancer**: 4-corner perspective homography unwarp, morphological shadow removal (`Magic Color`, `Toner-Saver B&W`, `Clean Grayscale`), and 300-DPI A4 ID card Recto/Verso print sheet composer (`1x`, `2x`, `4x`, `135%`) at exact 1:1 CR80 dimensions (`85.6 × 54 mm`). |
