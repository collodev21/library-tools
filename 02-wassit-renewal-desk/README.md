# Tool #02: Wassit Shop Desk (`WassitShopDesk.exe`) — Rust Manager for `wassit.dz` (ANEM)

A standalone **Rust** desktop Web-UI & CLI utility built for cybercafés, library shops, and public service desks in Algeria to manage **ANEM (`wassit.dz` / `wassitonline.anem.dz`)** job-seeker renewals (*Prolongation / Renouvellement de la demande d'emploi — 6 mois*), **Minha (منحة البطالة)** client tracking, PDF attestation downloads, and phone number updates.

---

## Full Scan of `wassit.dz` / `wassitonline.anem.dz` Ecosystem

Our full technical scan of the live ANEM bundles (`/postulation/static/js/main.12c68fa6.js` on `wassitonline.anem.dz`, `wassitonline2.anem.dz`, and `applicant.anem.dz`) uncovered the exact public & authenticated endpoints used by ANEM:

### 1. Classic Wassit Online Public Services (`https://wassitonline.anem.dz`) — No Password Required
- **Prolongation de la demande d'emploi (6 mois)** (`/postulation/prolongationDemande`):
  - **Endpoint**: `PUT https://wassitonline.anem.dz/api/extendMyDemandePublic`
  - **Payload**: `{"nin": "<18-digit NIN>", "numeroWassit": "<Numéro d'inscription ANEM>"}`
  - **Official Response Codes (`Code 1..9`)**:
    - `Code 4` (with `Result.numero`): **Prolongée avec succès (6 mois)** — returns `Result.numero` and `Result.anticipatedExpirationDate`.
    - `Code 4` (with `Result` as ISO date only): **Demande déjà active (Date d'expiration loin)** — returns the current active expiration date.
    - `Code 5`: **Demande expirée < 1 an (Réactivable)** — returns `DateCloture` and `MetierPrincipalId`, and unlocks immediate reactivation via:
      - `POST https://wassitonline.anem.dz/api/CreateDemandeProlongation` (automatically restores the 6-month registration and resumes suspended *Allocation Chômage / Minha*).
    - `Code 1`: **Données erronées** (NIN 18 chiffres or N° Wassit invalid/mismatched).
    - `Code 2`: **Aucune demande trouvée**.
    - `Code 3`: **Erreur technique serveur ANEM**.
    - `Code 6`: **Mise à jour NIN requise en agence** (dossier encore lié à l'ancienne CNI classique).
    - `Code 7`: **Demande expirée > 1 an** (nécessite une nouvelle demande via compte Wassit ou en agence).
    - `Code 8`: **Métier principal désactivé** dans la nomenclature NAME.
    - `Code 9`: **Wilaya Pilote migrée vers Wassit Online 2** (`https://wassitonline2.anem.dz` / `https://applicant.anem.dz`).
- **Téléchargement Attestation d'Inscription PDF**:
  - **Endpoint**: `GET https://wassitonline.anem.dz/api/FicheDemandeurOnline?nin=<NIN>&numeroWassit=<NumeroWassit>`
  - Returns a Base64-encoded PDF decoded and saved as `attestations/FicheDemandeur-NumWassit-<NumeroWassit>.pdf`.
- **Changement du Numéro Mobile** (`/postulation/ChangementMobile`):
  - **Endpoint**: `POST https://wassitonline.anem.dz/api/applicant/ChangerMobile`
  - **Payload**: `{"nin": "<18-digit NIN>", "numeroWassit": "<Numéro Wassit>", "mobile": "<05/06/07XXXXXXXX>"}`

### 2. Main AngularJS / ASP.NET MVC Portal (`https://wassitonline.anem.dz/`)
- **Online Account Recovery / Creation for Existing Agency Clients** (`/Account/RegisterApplicant` -> `applicantRegisterCtrlV4.js`):
  - **Endpoint**: `POST https://wassitonline.anem.dz/Account/RegisterExisingApplicant`
  - **Payload**: `{"Numero": "<NumeroWassit>", "Email": "<NewEmail>", "Password": "<Pass>", "PasswordConfirmation": "<Pass>", "ForceReplacement": true}`
  - Setting `"ForceReplacement": true` overwrites an old/forgotten email (`Code 2`) and attaches a new email + password to an existing ANEM registration number!
- **Civil Status Registration Check**:
  - **Endpoint**: `POST https://wassitonline.anem.dz/api/v1/applicant/existance/` (params: `firstName`, `lastName`, `birthDate`, `numActeNaissance` 6 digits, `paysNaissanceId`, `communeNaissance`).
- **Authenticated Demand Renewal** (`/app/services/demande-services.js`):
  - **Endpoints**: `GET /api/getapplicantDemandes`, `PUT /api/extendMyDemande`, `POST /api/createDemande`.
- **The 6 Pilot Wilayas Migrated to Wassit V2 (`AppSettings.restrictedWilayas` in `/app/app.js`)**:
  - `06 - Béjaïa` (`bf5865ec-931d-40ff-beef-1557b011164d`)
  - `20 - Saïda` (`a8db39d7-1031-4173-a583-d5523e9a843d`)
  - `22 - Sidi Bel Abbès` (`100381fb-a615-4309-93c3-da5f0be55103`)
  - `24 - Guelma` (`f72bf7ed-84b8-4414-beaf-28e582cc22ce`)
  - `29 - Mascara` (`4796c276-fa4f-4212-a568-3b95c998eb26`)
  - `43 - Mila` (`ba33d45b-73e9-4b73-8bb2-f041b9febc45`)

### 3. New Wassit Online 2 Platform (`https://wassitonline2.anem.dz` & `https://applicant.anem.dz`)
- **Applicant Portal**: `https://applicant.anem.dz` (Keycloak SSO at `https://authpub.anem.dz/`, realm `External-Resources`, `clientId: DemandeurAPI`).
- **Authenticated Profile Extension**: `POST https://ExtDemApi.anem.dz/api/Profil/ExtendProfilValidityPeriod`
- **Authenticated Attestation Report**: `GET https://ExtDemApi.anem.dz/api/Reports/AttestationInscriptionDemReport/AttestationInscriptionDemandeurReport`
- **National Job Market KPIs**: `GET https://OffersSearchApi.anem.dz/api/v1/DashboardKPIs/GetDashboardKpis`
- **Wilayas & ANEM Structures Directory**: `GET https://ExtRefApi.anem.dz/api/Wilaya/GetWilayaOfAlgerie`

---

## Key Features for Your Shop

1. **Direct 1-Click Prolongation & Auto-Reactivation**:
   - Calls `PUT /api/extendMyDemandePublic` directly from Rust, decodes all 9 ANEM status codes in **French & Arabic**, and automatically calls `POST /api/CreateDemandeProlongation` if the client's demand recently expired (`Code 5`).
2. **Shop Client Renewal Database (`wassit_clients.tsv` & `wassit_history.tsv`)**:
   - Every client who renews in your shop is saved with their **Full Name**, **18-digit NIN**, **N° Wassit**, **Mobile Phone**, **Wilaya**, **Minha status (منحة البطالة)**, **Last Renewal Date**, **6-Month Expiration Date**, **Renewal Count**, and **Shop Fee Paid (DA)**.
3. **15-Day Expiry Alert System + 1-Click WhatsApp Reminder**:
   - Automatically flags clients whose 6-month registration is expiring within **15 days** (`⚠️ J-15`) or already expired (`Expiré`), with a 1-click **WhatsApp button** pre-filled with a polite Arabic message inviting them back to your shop to renew before their *Minha* is suspended.
4. **Official PDF Attestation Downloader**:
   - Downloads and decodes `FicheDemandeur-NumWassit-<N>.pdf` into the `attestations/` folder for immediate printing.

---

## Usage

### 1. Launch Interactive Shop Web Dashboard (`http://127.0.0.1:7878`)
```powershell
.\WassitShopDesk.exe
# or custom port without auto-opening browser:
.\WassitShopDesk.exe serve --port 7878 --no-browser
```

### 2. CLI Commands
```powershell
# Shop KPI summary & expiry alerts
.\WassitShopDesk.exe --status

# List all clients in the shop database
.\WassitShopDesk.exe --list

# List only clients due for renewal (<= 15 days or expired)
.\WassitShopDesk.exe --due

# Search client by name, NIN, N° Wassit, phone, or Wilaya
.\WassitShopDesk.exe --search "Boudiaf"

# Validate NIN (18 digits), N° Wassit, and Algerian mobile number
.\WassitShopDesk.exe --validate 109941601012345601 160194001245 0551234567

# Live renewal against wassitonline.anem.dz + save client in database
.\WassitShopDesk.exe --renew 109941601012345601 160194001245 "Boudiaf Mohamed Amine" 0551234567 "16 - Alger" 100

# Record a manual +6 month renewal in the shop database
.\WassitShopDesk.exe --add "Amrani Sofiane" 109951601099887766 160195008877 0661122334 "16 - Alger" true 100

# Download official ANEM PDF attestation
.\WassitShopDesk.exe --attestation 109941601012345601 160194001245

# Change mobile phone on ANEM Wassit
.\WassitShopDesk.exe --change-mobile 109941601012345601 160194001245 0551234567

# Create or overwrite online account (Email + Password) for an existing ANEM client
.\WassitShopDesk.exe --recover-account 160194001245 client@gmail.com Anem2026

# Live diagnostic & KPI scan of wassit.dz V1 & V2
.\WassitShopDesk.exe --scan-site

# Export printable alerts report (wassit_renewal_alerts.txt) & CSV (wassit_clients_export.csv)
.\WassitShopDesk.exe --export-report
```

## Build from Source (Rust)
```powershell
cargo build --release
Copy-Item target\release\WassitShopDesk.exe .\WassitShopDesk.exe -Force
```
