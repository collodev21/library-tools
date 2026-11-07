# Library & E-Service Shop Management Tools (`library-tools`)

Automated collection of standalone Windows utilities (`.exe`) built to simplify library, copy-shop (*maktaba*), and e-service desk workflows (ID card A4 photocopy layout, CamScanner bad-photo shadow removal, ANEM `wassit.dz` client renewals, shelf labeling, ISBN validation, and loan tracking).

## Tools Index

| # | Folder | Executable | Description |
|---|--------|------------|-------------|
| 01 | [`01-library-quick-desk`](./01-library-quick-desk) | `LibraryQuickDesk.exe` | Desktop & CLI utility for ISBN-10/13 validation, book checkouts, due-date calculation, and overdue reporting. |
| 02 | [`02-shelf-label-studio`](./02-shelf-label-studio) | `ShelfLabelStudio.exe` | Desktop & CLI utility for library spine & pocket labels, Cutter-Sanborn author number calculation, and Code 39 barcode generation. |
| 02b | [`02-wassit-renewal-desk`](./02-wassit-renewal-desk) | `WassitShopDesk.exe` | **Rust** Web-UI & CLI manager for `wassit.dz` (`wassitonline.anem.dz`): live 6-month job-seeker renewal (`extendMyDemandePublic`), expired demand reactivation, PDF attestation download (`FicheDemandeurOnline`), mobile number change, and shop client renewal database with 15-day expiry & WhatsApp alerts. |
| 03 | [`03-id-card-camscanner-desk`](./03-id-card-camscanner-desk) | `IdCardCamScannerDesk.exe` | **ID Card Copy Maker & CamScanner Bad Photo Enhancer**: 4-corner perspective homography unwarp, morphological shadow removal (`Magic Color`, `Toner-Saver B&W`, `Clean Grayscale`), and 300-DPI A4 ID card Recto/Verso print sheet composer (`1x`, `2x`, `4x`, `135%`) at exact 1:1 CR80 dimensions (`85.6 × 54 mm`). |
| 04 | [`04-catalog-audit-dedup`](./04-catalog-audit-dedup) | `CatalogAuditDesk.exe` | **Library Catalog, Shelf Inventory Audit & Deduplication Desk**: Full-text indexing, USB wedge barcode shelf-reading audit, real-time discrepancy reconciliation (`Verified`, `Misplaced`, `Uncataloged`, `StatusConflict`), Dewey sequence verifier, Levenshtein/Jaro-Winkler fuzzy edition deduplication, and standalone HTML/CSV/Markdown report generation. |
| 05 | [`05-circulation-overdue-desk`](./05-circulation-overdue-desk) | `CirculationOverdueDesk.exe` | **Circulation & Overdue Notice Desk**: Patron directory & category lending policies, fast barcode wedge check-in/checkout, automated hold-shelf alerts upon return, 4-tier overdue escalation (Courtesy, 1st Notice, 2nd Notice with privilege suspension, Final Demand & lost book billing), multi-channel notice dispatcher (printable HTML letters with `@media print`, RFC 822 `.eml` emails, one-click WhatsApp dispatch links), and complete fine/waiver cashier ledger. |
