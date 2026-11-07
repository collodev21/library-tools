use std::env;
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::path::Path;
use std::process::Command;
use std::time::{SystemTime, UNIX_EPOCH};

const DB_FILE: &str = "wassit_clients.tsv";
const HISTORY_FILE: &str = "wassit_history.tsv";
const ATTESTATIONS_DIR: &str = "attestations";

#[derive(Clone, Debug)]
struct ClientRecord {
    id: u64,
    full_name: String,
    nin: String,
    numero_wassit: String,
    phone: String,
    wilaya: String,
    is_minha: bool,
    platform: String, // "V1" or "V2"
    last_renewal: String,     // YYYY-MM-DD
    expiration_date: String,  // YYYY-MM-DD
    demande_numero: String,
    last_status_code: i32,
    last_status_label: String,
    renewal_count: u32,
    total_paid_dzd: u64,
    notes: String,
}

#[derive(Clone, Debug)]
struct RenewalOutcome {
    http_ok: bool,
    code: i32,
    state: String,
    badge: String,
    title_fr: String,
    title_ar: String,
    detail_fr: String,
    detail_ar: String,
    demande_numero: String,
    expiration_date: String,
    raw_json: String,
}

fn main() {
    let args: Vec<String> = env::args().collect();
    ensure_storage_initialized();

    if args.len() <= 1 {
        run_server(7878, true);
        return;
    }

    match args[1].as_str() {
        "serve" | "--serve" => {
            let mut port = 7878u16;
            let mut open_browser = true;
            let mut i = 2;
            while i < args.len() {
                if (args[i] == "--port" || args[i] == "-p") && i + 1 < args.len() {
                    port = args[i + 1].parse().unwrap_or(7878);
                    i += 1;
                } else if args[i] == "--no-browser" {
                    open_browser = false;
                }
                i += 1;
            }
            run_server(port, open_browser);
        }
        "status" | "--status" | "stats" => {
            cli_print_status();
        }
        "list" | "--list" => {
            cli_list_clients(None, false);
        }
        "due" | "--due" | "alerts" | "--alerts" => {
            cli_list_clients(None, true);
        }
        "search" | "--search" => {
            let q = if args.len() > 2 { args[2..].join(" ") } else { String::new() };
            cli_list_clients(Some(&q), false);
        }
        "validate" | "--validate" => {
            if args.len() < 4 {
                eprintln!("Usage: WassitShopDesk --validate <NIN_18_DIGITS> <NUMERO_WASSIT> [PHONE]");
                std::process::exit(1);
            }
            let nin = &args[2];
            let wassit = &args[3];
            let phone = if args.len() > 4 { &args[4] } else { "0550000000" };
            cli_validate(nin, wassit, phone);
        }
        "add" | "--add" => {
            // WassitShopDesk --add "<Full Name>" <NIN> <NumeroWassit> [Phone] [Wilaya] [Minha:true|false] [FeeDZD]
            if args.len() < 5 {
                eprintln!("Usage: WassitShopDesk --add \"<Full Name>\" <NIN> <NumeroWassit> [Phone] [Wilaya] [Minha:true|false] [FeeDZD]");
                std::process::exit(1);
            }
            let name = &args[2];
            let nin = &args[3];
            let wassit = &args[4];
            let phone = if args.len() > 5 { &args[5] } else { "0550000000" };
            let wilaya = if args.len() > 6 { &args[6] } else { "16 - Alger" };
            let is_minha = if args.len() > 7 {
                 matches!(args[7].to_lowercase().as_str(), "true" | "1" | "yes" | "oui" | "minha")
            } else {
                true
            };
            let fee: u64 = if args.len() > 8 { args[8].parse().unwrap_or(100) } else { 100 };
            cli_add_or_manual_renew(name, nin, wassit, phone, wilaya, is_minha, fee, "V1", "");
        }
        "renew" | "--renew" => {
            if args.len() < 4 {
                eprintln!("Usage: WassitShopDesk --renew <NIN> <NumeroWassit> [\"<Full Name>\"] [Phone] [Wilaya] [FeeDZD]");
                std::process::exit(1);
            }
            let nin = &args[2];
            let wassit = &args[3];
            let name = if args.len() > 4 { &args[4] } else { "Client Guichet" };
            let phone = if args.len() > 5 { &args[5] } else { "" };
            let wilaya = if args.len() > 6 { &args[6] } else { "Algérie" };
            let fee: u64 = if args.len() > 7 { args[7].parse().unwrap_or(100) } else { 100 };
            cli_live_renew(name, nin, wassit, phone, wilaya, true, fee);
        }
        "attestation" | "--attestation" => {
            if args.len() < 4 {
                eprintln!("Usage: WassitShopDesk --attestation <NIN> <NumeroWassit>");
                std::process::exit(1);
            }
            cli_download_attestation(&args[2], &args[3]);
        }
        "change-mobile" | "--change-mobile" => {
            if args.len() < 5 {
                eprintln!("Usage: WassitShopDesk --change-mobile <NIN> <NumeroWassit> <NewMobile10Digits>");
                std::process::exit(1);
            }
            cli_change_mobile(&args[2], &args[3], &args[4]);
        }
        "recover-account" | "--recover-account" => {
            if args.len() < 5 {
                eprintln!("Usage: WassitShopDesk --recover-account <NumeroWassit> <NewEmail> <NewPassword>");
                std::process::exit(1);
            }
            cli_recover_existing_account(&args[2], &args[3], &args[4]);
        }
        "scan-site" | "--scan-site" => {
            cli_scan_wassit_site();
        }
        "export-report" | "--export-report" => {
            cli_export_report();
        }
        "help" | "--help" | "-h" => {
            print_help();
        }
        other => {
            eprintln!("Unknown command: {}", other);
            print_help();
            std::process::exit(1);
        }
    }
}

fn print_help() {
    println!("================================================================================");
    println!("  WASSIT SHOP DESK v1.0 (Rust) - Gestionnaire ANEM (wassit.dz) & Base Clients");
    println!("================================================================================");
    println!("Usage:");
    println!("  WassitShopDesk.exe                        Launch Shop Web UI (http://127.0.0.1:7878)");
    println!("  WassitShopDesk.exe serve --port 7878      Run Web Dashboard on custom port");
    println!("  WassitShopDesk.exe --status               Show shop database KPIs & expiry alerts");
    println!("  WassitShopDesk.exe --list                 List all clients in renewal database");
    println!("  WassitShopDesk.exe --due                  List expiring (<=15d) & expired clients");
    println!("  WassitShopDesk.exe --search <query>       Search clients by name, NIN, Wassit, phone");
    println!("  WassitShopDesk.exe --validate <NIN> <W>   Validate NIN (18 digits) & Numero Wassit");
    println!("  WassitShopDesk.exe --add \"<Name>\" <NIN> <Wassit> [Phone] [Wilaya] [Minha] [FeeDZD]");
    println!("                                            Record a 6-month renewal in local DB");
    println!("  WassitShopDesk.exe --renew <NIN> <Wassit> [\"<Name>\"] [Phone] [Wilaya] [FeeDZD]");
    println!("                                            Live renewal on wassitonline.anem.dz + DB save");
    println!("  WassitShopDesk.exe --attestation <NIN> <Wassit>");
    println!("                                            Download official PDF FicheDemandeurOnline");
    println!("  WassitShopDesk.exe --change-mobile <NIN> <Wassit> <05/06/07XXXXXXXX>");
    println!("                                            Live phone update on wassitonline.anem.dz");
    println!("  WassitShopDesk.exe --recover-account <Wassit> <Email> <Password>");
    println!("                                            Create/overwrite online account for existing ANEM client");
    println!("  WassitShopDesk.exe --scan-site            Live audit of ANEM Wassit V1 & V2 APIs");
    println!("  WassitShopDesk.exe --export-report        Export wassit_renewal_alerts.txt & CSV");
}

// ============================================================================
// DATE & VALIDATION HELPERS (Pure Rust)
// ============================================================================

fn current_unix_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn days_to_ymd(days_since_epoch: i64) -> (i32, u32, u32) {
    // Civil date from days since 1970-01-01 (Howard Hinnant algorithm)
    let z = days_since_epoch + 719468;
    let era = if z >= 0 { z } else { z - 146096 } / 146097;
    let doe = (z - era * 146097) as u32;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = if m <= 2 { y + 1 } else { y };
    (year as i32, m, d)
}

fn ymd_to_days(y: i32, m: u32, d: u32) -> i64 {
    let y = y as i64 - if m <= 2 { 1 } else { 0 };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u32;
    let m_adj = if m > 2 { m - 3 } else { m + 9 };
    let doy = (153 * m_adj + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146097 + doe as i64 - 719468
}

fn today_ymd() -> String {
    let days = (current_unix_secs() / 86400) as i64;
    let (y, m, d) = days_to_ymd(days);
    format!("{:04}-{:02}-{:02}", y, m, d)
}

fn add_months_6_from_today() -> String {
    // ANEM validity is 6 months (~182 days)
    let days = (current_unix_secs() / 86400) as i64 + 180;
    let (y, m, d) = days_to_ymd(days);
    format!("{:04}-{:02}-{:02}", y, m, d)
}

fn parse_ymd_days(date_str: &str) -> Option<i64> {
    let clean = date_str.trim();
    if clean.len() < 10 {
        return None;
    }
    let slice = &clean[..10];
    let parts: Vec<&str> = if slice.contains('-') {
        slice.split('-').collect()
    } else if slice.contains('/') {
        slice.split('/').collect()
    } else {
        return None;
    };
    if parts.len() != 3 {
        return None;
    }
    if parts[0].len() == 4 {
        let y = parts[0].parse::<i32>().ok()?;
        let m = parts[1].parse::<u32>().ok()?;
        let d = parts[2].parse::<u32>().ok()?;
        Some(ymd_to_days(y, m, d))
    } else {
        let d = parts[0].parse::<u32>().ok()?;
        let m = parts[1].parse::<u32>().ok()?;
        let y = parts[2].parse::<i32>().ok()?;
        Some(ymd_to_days(y, m, d))
    }
}

fn days_until(expiration_date: &str) -> i64 {
    let today = (current_unix_secs() / 86400) as i64;
    if let Some(exp) = parse_ymd_days(expiration_date) {
        exp - today
    } else {
        0
    }
}

fn normalize_iso_date(raw: &str) -> String {
    let s = raw.trim().trim_matches('"');
    if s.len() >= 10 && s.as_bytes()[4] == b'-' && s.as_bytes()[7] == b'-' {
        return s[..10].to_string();
    }
    s.to_string()
}

fn validate_nin(nin: &str) -> Result<(), String> {
    let s = nin.trim();
    if s.is_empty() {
        return Err("Le NIN est obligatoire (18 chiffres sur la CNI biométrique).".to_string());
    }
    if s.len() != 18 {
        return Err(format!(
            "NIN non valide: {} caractères saisis (exactement 18 chiffres requis par wassit.dz).",
            s.len()
        ));
    }
    if !s.chars().all(|c| c.is_ascii_digit()) {
        return Err("Le NIN ne doit contenir que des chiffres (0-9).".to_string());
    }
    Ok(())
}

fn validate_numero_wassit(num: &str) -> Result<(), String> {
    let s = num.trim();
    if s.is_empty() {
        return Err("Le numéro d'inscription ANEM (Wassit) est obligatoire.".to_string());
    }
    if s.len() > 18 {
        return Err("Le numéro Wassit ne peut pas dépasser 18 caractères.".to_string());
    }
    // Exact regex from wassit_main.js: /^[0-9dD]+$/
    if !s.chars().all(|c| c.is_ascii_digit() || c == 'd' || c == 'D') {
        return Err("Le numéro Wassit ne doit contenir que des chiffres (et éventuellement 'D').".to_string());
    }
    Ok(())
}

fn validate_mobile(mobile: &str) -> Result<(), String> {
    let s = mobile.trim();
    if s.is_empty() {
        return Ok(());
    }
    // Exact regex from wassit_main.js: /^(06|07|05)\d{8}$/
    if s.len() != 10 || !s.chars().all(|c| c.is_ascii_digit()) {
        return Err("Le numéro mobile doit comporter exactement 10 chiffres (ex: 0550123456).".to_string());
    }
    let prefix = &s[..2];
    if prefix != "05" && prefix != "06" && prefix != "07" {
        return Err("Le numéro mobile algérien doit commencer par 05, 06 ou 07.".to_string());
    }
    Ok(())
}

// ============================================================================
// DATABASE STORAGE (TSV + HISTORY LOG)
// ============================================================================

fn sanitize_tsv(input: &str) -> String {
    input
        .replace('\t', " ")
        .replace('\r', " ")
        .replace('\n', " ")
        .trim()
        .to_string()
}

fn ensure_storage_initialized() {
    let _ = fs::create_dir_all(ATTESTATIONS_DIR);
    if !Path::new(DB_FILE).exists() {
        let today = today_ymd();
        let today_days = (current_unix_secs() / 86400) as i64;
        let fmt_offset = |off: i64| -> String {
            let (y, m, d) = days_to_ymd(today_days + off);
            format!("{:04}-{:02}-{:02}", y, m, d)
        };
        let sample_clients = vec![
            ClientRecord {
                id: 1,
                full_name: "Boudiaf Mohamed Amine".to_string(),
                nin: "109941601012345601".to_string(),
                numero_wassit: "160194001245".to_string(),
                phone: "0551234567".to_string(),
                wilaya: "16 - Alger".to_string(),
                is_minha: true,
                platform: "V1".to_string(),
                last_renewal: fmt_offset(-172),
                expiration_date: fmt_offset(8), // Expiring in 8 days!
                demande_numero: "DEM-16-2026-8821".to_string(),
                last_status_code: 4,
                last_status_label: "Prolongée (Expire bientôt)".to_string(),
                renewal_count: 3,
                total_paid_dzd: 300,
                notes: "Client fidèle Minha - Appeler avant expiration".to_string(),
            },
            ClientRecord {
                id: 2,
                full_name: "Merabet Fatima Zohra".to_string(),
                nin: "119983102054321002".to_string(),
                numero_wassit: "310298004512".to_string(),
                phone: "0662345678".to_string(),
                wilaya: "31 - Oran".to_string(),
                is_minha: true,
                platform: "V1".to_string(),
                last_renewal: fmt_offset(-190),
                expiration_date: fmt_offset(-10), // Expired 10 days ago!
                demande_numero: "DEM-31-2026-1940".to_string(),
                last_status_code: 5,
                last_status_label: "Expirée (<1 an - Réactivable)".to_string(),
                renewal_count: 2,
                total_paid_dzd: 200,
                notes: "Allocation chômage suspendue si non réactivée".to_string(),
            },
            ClientRecord {
                id: 3,
                full_name: "Khelifi Yacine".to_string(),
                nin: "109922501098765403".to_string(),
                numero_wassit: "250192007890".to_string(),
                phone: "0773456789".to_string(),
                wilaya: "25 - Constantine".to_string(),
                is_minha: true,
                platform: "V1".to_string(),
                last_renewal: today.clone(),
                expiration_date: fmt_offset(180),
                demande_numero: "DEM-25-2026-9932".to_string(),
                last_status_code: 4,
                last_status_label: "Active (6 mois)".to_string(),
                renewal_count: 4,
                total_paid_dzd: 400,
                notes: "Attestation PDF imprimée au magasin".to_string(),
            },
            ClientRecord {
                id: 4,
                full_name: "Mansouri Karim".to_string(),
                nin: "109961901011223304".to_string(),
                numero_wassit: "190196003321".to_string(),
                phone: "0559876543".to_string(),
                wilaya: "19 - Sétif".to_string(),
                is_minha: false,
                platform: "V2".to_string(),
                last_renewal: fmt_offset(-40),
                expiration_date: fmt_offset(140),
                demande_numero: "W2-19-2026-4410".to_string(),
                last_status_code: 9,
                last_status_label: "Migré Wassit Online 2".to_string(),
                renewal_count: 1,
                total_paid_dzd: 150,
                notes: "Wilaya pilote V2 (applicant.anem.dz)".to_string(),
            },
        ];
        save_clients(&sample_clients);
    }

    if !Path::new(HISTORY_FILE).exists() {
        let mut f = File::create(HISTORY_FILE).expect("Cannot create history file");
        let _ = writeln!(
            f,
            "TIMESTAMP\tCLIENT_NAME\tNIN\tNUMERO_WASSIT\tACTION\tANEM_CODE\tEXPIRATION\tFEE_DZD\tDETAILS"
        );
    }
}

fn load_clients() -> Vec<ClientRecord> {
    let content = match fs::read_to_string(DB_FILE) {
        Ok(c) => c,
        Err(_) => return Vec::new(),
    };
    let mut list = Vec::new();
    for (idx, line) in content.lines().enumerate() {
        if idx == 0 || line.trim().is_empty() {
            continue;
        }
        let cols: Vec<&str> = line.split('\t').collect();
        if cols.len() < 16 {
            continue;
        }
        list.push(ClientRecord {
            id: cols[0].parse().unwrap_or(idx as u64),
            full_name: cols[1].to_string(),
            nin: cols[2].to_string(),
            numero_wassit: cols[3].to_string(),
            phone: cols[4].to_string(),
            wilaya: cols[5].to_string(),
            is_minha: cols[6] == "true" || cols[6] == "1",
            platform: cols[7].to_string(),
            last_renewal: cols[8].to_string(),
            expiration_date: cols[9].to_string(),
            demande_numero: cols[10].to_string(),
            last_status_code: cols[11].parse().unwrap_or(0),
            last_status_label: cols[12].to_string(),
            renewal_count: cols[13].parse().unwrap_or(1),
            total_paid_dzd: cols[14].parse().unwrap_or(0),
            notes: cols[15].to_string(),
        });
    }
    list
}

fn save_clients(clients: &[ClientRecord]) {
    let mut out = String::from(
        "ID\tFULL_NAME\tNIN\tNUMERO_WASSIT\tPHONE\tWILAYA\tIS_MINHA\tPLATFORM\tLAST_RENEWAL\tEXPIRATION_DATE\tDEMANDE_NUMERO\tLAST_STATUS_CODE\tLAST_STATUS_LABEL\tRENEWAL_COUNT\tTOTAL_PAID_DZD\tNOTES\n",
    );
    for c in clients {
        out.push_str(&format!(
            "{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\n",
            c.id,
            sanitize_tsv(&c.full_name),
            sanitize_tsv(&c.nin),
            sanitize_tsv(&c.numero_wassit),
            sanitize_tsv(&c.phone),
            sanitize_tsv(&c.wilaya),
            c.is_minha,
            sanitize_tsv(&c.platform),
            sanitize_tsv(&c.last_renewal),
            sanitize_tsv(&c.expiration_date),
            sanitize_tsv(&c.demande_numero),
            c.last_status_code,
            sanitize_tsv(&c.last_status_label),
            c.renewal_count,
            c.total_paid_dzd,
            sanitize_tsv(&c.notes)
        ));
    }
    let _ = fs::write(DB_FILE, out);
}

fn append_history(
    client_name: &str,
    nin: &str,
    wassit: &str,
    action: &str,
    code: i32,
    expiration: &str,
    fee_dzd: u64,
    details: &str,
) {
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(HISTORY_FILE) {
        let _ = writeln!(
            f,
            "{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
            today_ymd(),
            sanitize_tsv(client_name),
            sanitize_tsv(nin),
            sanitize_tsv(wassit),
            sanitize_tsv(action),
            code,
            sanitize_tsv(expiration),
            fee_dzd,
            sanitize_tsv(details)
        );
    }
}

fn upsert_client_in_db(
    full_name: &str,
    nin: &str,
    numero_wassit: &str,
    phone: &str,
    wilaya: &str,
    is_minha: bool,
    platform: &str,
    expiration_date: &str,
    demande_numero: &str,
    status_code: i32,
    status_label: &str,
    fee_dzd: u64,
    notes: &str,
) -> ClientRecord {
    let mut clients = load_clients();
    let nin_clean = nin.trim();
    let wassit_clean = numero_wassit.trim();
    let today = today_ymd();
    let exp = if expiration_date.trim().is_empty() {
        add_months_6_from_today()
    } else {
        normalize_iso_date(expiration_date)
    };

    if let Some(existing) = clients
        .iter_mut()
        .find(|c| c.nin == nin_clean || c.numero_wassit.eq_ignore_ascii_case(wassit_clean))
    {
        if !full_name.trim().is_empty() && full_name != "Client Guichet" {
            existing.full_name = full_name.trim().to_string();
        }
        existing.nin = nin_clean.to_string();
        existing.numero_wassit = wassit_clean.to_string();
        if !phone.trim().is_empty() {
            existing.phone = phone.trim().to_string();
        }
        if !wilaya.trim().is_empty() {
            existing.wilaya = wilaya.trim().to_string();
        }
        existing.is_minha = is_minha;
        if !platform.trim().is_empty() {
            existing.platform = platform.trim().to_string();
        }
        existing.last_renewal = today;
        existing.expiration_date = exp;
        if !demande_numero.trim().is_empty() {
            existing.demande_numero = demande_numero.trim().to_string();
        }
        existing.last_status_code = status_code;
        existing.last_status_label = status_label.to_string();
        existing.renewal_count += 1;
        existing.total_paid_dzd += fee_dzd;
        if !notes.trim().is_empty() {
            existing.notes = notes.trim().to_string();
        }
        let updated = existing.clone();
        save_clients(&clients);
        return updated;
    }

    let next_id = clients.iter().map(|c| c.id).max().unwrap_or(0) + 1;
    let new_rec = ClientRecord {
        id: next_id,
        full_name: if full_name.trim().is_empty() {
            format!("Client {}", next_id)
        } else {
            full_name.trim().to_string()
        },
        nin: nin_clean.to_string(),
        numero_wassit: wassit_clean.to_string(),
        phone: phone.trim().to_string(),
        wilaya: if wilaya.trim().is_empty() {
            "Algérie".to_string()
        } else {
            wilaya.trim().to_string()
        },
        is_minha,
        platform: if platform.trim().is_empty() {
            "V1".to_string()
        } else {
            platform.trim().to_string()
        },
        last_renewal: today,
        expiration_date: exp,
        demande_numero: if demande_numero.trim().is_empty() {
            format!("ANEM-{}", wassit_clean)
        } else {
            demande_numero.trim().to_string()
        },
        last_status_code: status_code,
        last_status_label: status_label.to_string(),
        renewal_count: 1,
        total_paid_dzd: fee_dzd,
        notes: notes.trim().to_string(),
    };
    clients.push(new_rec.clone());
    save_clients(&clients);
    new_rec
}

// ============================================================================
// LIVE WASSIT.DZ (ANEM) API CLIENT
// ============================================================================
// Reverse-engineered from https://wassitonline.anem.dz/postulation/static/js/main.12c68fa6.js:
// 1. PUT  https://wassitonline.anem.dz/api/extendMyDemandePublic
//         body: {"nin":"<18 digits>","numeroWassit":"<ANEM ID>"}
// 2. POST https://wassitonline.anem.dz/api/CreateDemandeProlongation
//         body: Result object from Code 5 (expired < 1 year reactivation)
// 3. GET  https://wassitonline.anem.dz/api/FicheDemandeurOnline?nin=<nin>&numeroWassit=<numeroWassit>
//         returns Base64 PDF string for FicheDemandeur-NumWassit-<numeroWassit>.pdf
// 4. POST https://wassitonline.anem.dz/api/applicant/ChangerMobile
//         body: {"nin":"...","numeroWassit":"...","mobile":"05/06/07..."}

fn extract_json_int(json: &str, key: &str) -> Option<i32> {
    let pattern = format!("\"{}\":", key);
    let idx = json.find(&pattern)?;
    let rest = json[idx + pattern.len()..].trim_start();
    let num_str: String = rest
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == '-')
        .collect();
    num_str.parse().ok()
}

fn extract_json_str(json: &str, key: &str) -> Option<String> {
    let pattern = format!("\"{}\":", key);
    let idx = json.find(&pattern)?;
    let rest = json[idx + pattern.len()..].trim_start();
    if !rest.starts_with('"') {
        return None;
    }
    let mut out = String::new();
    let mut escaped = false;
    for ch in rest[1..].chars() {
        if escaped {
            out.push(ch);
            escaped = false;
        } else if ch == '\\' {
            escaped = true;
        } else if ch == '"' {
            return Some(out);
        } else {
            out.push(ch);
        }
    }
    None
}

fn extract_json_raw_field(json: &str, key: &str) -> Option<String> {
    let pattern = format!("\"{}\":", key);
    let idx = json.find(&pattern)?;
    let rest = json[idx + pattern.len()..].trim();
    if rest.starts_with("null") {
        return None;
    }
    if rest.starts_with('{') {
        let mut depth = 0i32;
        let mut end_idx = 0;
        for (i, ch) in rest.char_indices() {
            if ch == '{' {
                depth += 1;
            } else if ch == '}' {
                depth -= 1;
                if depth == 0 {
                    end_idx = i + 1;
                    break;
                }
            }
        }
        if end_idx > 0 {
            return Some(rest[..end_idx].to_string());
        }
    } else if rest.starts_with('"') {
        return extract_json_str(json, key);
    }
    None
}

fn call_anem_extend_demande(nin: &str, numero_wassit: &str) -> RenewalOutcome {
    let payload = format!(
        "{{\"nin\":\"{}\",\"numeroWassit\":\"{}\"}}",
        nin.trim(),
        numero_wassit.trim()
    );

    let output = Command::new("curl.exe")
        .args([
            "-s",
            "-m",
            "20",
            "-X",
            "PUT",
            "https://wassitonline.anem.dz/api/extendMyDemandePublic",
            "-H",
            "Content-Type: application/json",
            "-H",
            "Accept: application/json",
            "-H",
            "Origin: https://wassitonline.anem.dz",
            "-H",
            "Referer: https://wassitonline.anem.dz/postulation/prolongationDemande",
            "-d",
            &payload,
        ])
        .output();

    let raw = match output {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).trim().to_string(),
        Ok(o) => {
            let err = String::from_utf8_lossy(&o.stderr).to_string();
            return RenewalOutcome {
                http_ok: false,
                code: -1,
                state: "network_error".to_string(),
                badge: "Erreur Réseau".to_string(),
                title_fr: "Serveur ANEM injoignable".to_string(),
                title_ar: "تعذر الاتصال بخادم الوكالة الوطنية للتشغيل".to_string(),
                detail_fr: format!("Erreur HTTP/curl vers wassitonline.anem.dz: {}", err),
                detail_ar: "يرجى التحقق من اتصال الإنترنت أو المحاولة لاحقاً.".to_string(),
                demande_numero: String::new(),
                expiration_date: add_months_6_from_today(),
                raw_json: err,
            };
        }
        Err(e) => {
            return RenewalOutcome {
                http_ok: false,
                code: -1,
                state: "network_error".to_string(),
                badge: "Erreur Système".to_string(),
                title_fr: "Impossible d'exécuter curl.exe".to_string(),
                title_ar: "خطأ في الاتصال".to_string(),
                detail_fr: e.to_string(),
                detail_ar: e.to_string(),
                demande_numero: String::new(),
                expiration_date: add_months_6_from_today(),
                raw_json: e.to_string(),
            };
        }
    };

    let code = extract_json_int(&raw, "Code").unwrap_or(0);
    let state = extract_json_str(&raw, "State").unwrap_or_else(|| "unknown".to_string());

    match code {
        4 => {
            // Code 4: either extended right now (Result has numero & anticipatedExpirationDate)
            // OR expiration date is still far (Result is a date string)
            let numero = extract_json_str(&raw, "numero").unwrap_or_default();
            let exp = extract_json_str(&raw, "anticipatedExpirationDate")
                .or_else(|| extract_json_str(&raw, "Result"))
                .map(|d| normalize_iso_date(&d))
                .unwrap_or_else(add_months_6_from_today);

            if !numero.is_empty() {
                RenewalOutcome {
                    http_ok: true,
                    code: 4,
                    state: "success".to_string(),
                    badge: "Prolongée 6 Mois".to_string(),
                    title_fr: "Demande d'emploi prolongée avec succès !".to_string(),
                    title_ar: "تم تمديد بطاقة طلب العمل بنجاح لمدة 6 أشهر!".to_string(),
                    detail_fr: format!(
                        "La demande N° {} a été prolongée jusqu'au {}. Vous pouvez télécharger l'attestation PDF.",
                        numero, exp
                    ),
                    detail_ar: format!(
                        "تم تمديد طلب العمل رقم {} إلى غاية {}. يمكنك الآن تحميل وطبع شهادة التسجيل.",
                        numero, exp
                    ),
                    demande_numero: numero,
                    expiration_date: exp,
                    raw_json: raw,
                }
            } else {
                RenewalOutcome {
                    http_ok: true,
                    code: 4,
                    state: "warning".to_string(),
                    badge: "Déjà Active (Date Loin)".to_string(),
                    title_fr: "Demande déjà active (Date d'expiration encore loin)".to_string(),
                    title_ar: "طلب العمل لا يزال ساري المفعول (تاريخ انتهاء الصلاحية بعيد)".to_string(),
                    detail_fr: format!(
                        "Votre demande est déjà active jusqu'au {}. Le renouvellement se fait dans les derniers jours avant l'échéance.",
                        exp
                    ),
                    detail_ar: format!(
                        "طلب العمل الخاص بك مفعل إلى غاية {}. تم حفظ تاريخ انتهاء الصلاحية في قاعدة بيانات المحل لتذكير الزبون.",
                        exp
                    ),
                    demande_numero: String::new(),
                    expiration_date: exp,
                    raw_json: raw,
                }
            }
        }
        5 => {
            // Code 5: Expired < 1 year ("aucuneDemandeActive"). We can auto-reactivate via POST /api/CreateDemandeProlongation!
            let date_cloture = extract_json_str(&raw, "DateCloture")
                .map(|d| normalize_iso_date(&d))
                .unwrap_or_else(|| "N/A".to_string());
            let metier_id = extract_json_str(&raw, "MetierPrincipalId").unwrap_or_default();

            if let Some(result_obj) = extract_json_raw_field(&raw, "Result") {
                if result_obj.starts_with('{') {
                    // Attempt automatic reactivation on ANEM!
                    if let Ok(react_out) = Command::new("curl.exe")
                        .args([
                            "-s",
                            "-m",
                            "20",
                            "-X",
                            "POST",
                            "https://wassitonline.anem.dz/api/CreateDemandeProlongation",
                            "-H",
                            "Content-Type: application/json",
                            "-H",
                            "Accept: application/json",
                            "-d",
                            &result_obj,
                        ])
                        .output()
                    {
                        let react_raw = String::from_utf8_lossy(&react_out.stdout).trim().to_string();
                        if let Some(new_exp) = extract_json_str(&react_raw, "DateAnnulationPrevue") {
                            let norm_exp = normalize_iso_date(&new_exp);
                            return RenewalOutcome {
                                http_ok: true,
                                code: 50, // Reactivated!
                                state: "success".to_string(),
                                badge: "Réactivée 6 Mois".to_string(),
                                title_fr: "Demande expirée réactivée avec succès !".to_string(),
                                title_ar: "تم إعادة تفعيل طلب العمل المنتهي بنجاح لمدة 6 أشهر!".to_string(),
                                detail_fr: format!(
                                    "Ancienne clôture: {}. Nouvelle demande réactivée jusqu'au {} (Allocation Minha rétablie automatiquement).",
                                    date_cloture, norm_exp
                                ),
                                detail_ar: format!(
                                    "تمت إعادة تفعيل الطلب بنجاح إلى غاية {} (تستأنف منحة البطالة تلقائياً).",
                                    norm_exp
                                ),
                                demande_numero: metier_id,
                                expiration_date: norm_exp,
                                raw_json: react_raw,
                            };
                        }
                    }
                }
            }

            RenewalOutcome {
                http_ok: true,
                code: 5,
                state: "info".to_string(),
                badge: "Expirée (<1 an) - Réactivable".to_string(),
                title_fr: "Aucune demande active (Clôturée récemment - Réactivation possible)".to_string(),
                title_ar: "لا يوجد طلب عمل مفعل حالياً (قابل لإعادة التفعيل)".to_string(),
                detail_fr: format!(
                    "Demande clôturée le {} (Métier: {}). Cliquez sur Réactiver pour créer une nouvelle prolongation.",
                    date_cloture, metier_id
                ),
                detail_ar: format!(
                    "انتهت صلاحية الطلب بتاريخ {}. يمكن إعادة تفعيله مباشرة لاسترجاع منحة البطالة.",
                    date_cloture
                ),
                demande_numero: metier_id,
                expiration_date: date_cloture,
                raw_json: raw,
            }
        }
        1 => RenewalOutcome {
            http_ok: true,
            code: 1,
            state: "error".to_string(),
            badge: "Données Erronées (Code 1)".to_string(),
            title_fr: "Informations introduites erronées (NIN ou N° Wassit)".to_string(),
            title_ar: "المعلومات المدرجة غير صحيحة (رقم التعريف الوطني أو رقم الوسيط)".to_string(),
            detail_fr: "Le numéro d'inscription ANEM ou le NIN (18 chiffres) est erroné ou non concordant. Vérifiez l'attestation d'inscription du client.".to_string(),
            detail_ar: "رقم التسجيل في الوكالة أو رقم التعريف الوطني غير صحيح. يرجى التحقق من شهادة التسجيل الخاصة بالزبون.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        2 => RenewalOutcome {
            http_ok: true,
            code: 2,
            state: "error".to_string(),
            badge: "Aucune Demande (Code 2)".to_string(),
            title_fr: "Aucune demande d'emploi trouvée pour ce dossier".to_string(),
            title_ar: "لا يوجد أي طلب عمل مسجل بهذه المعلومات".to_string(),
            detail_fr: "Aucune demande n'est enregistrée sous ce numéro. Le client doit créer une demande sur Wassit Online ou se rapprocher de son annexe ANEM.".to_string(),
            detail_ar: "لا يوجد طلب عمل مسجل. يجب على الزبون التسجيل عبر المنصة أو التقرب من الملحقة المحلية للتشغيل.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        3 => RenewalOutcome {
            http_ok: true,
            code: 3,
            state: "warning".to_string(),
            badge: "Erreur Serveur ANEM (Code 3)".to_string(),
            title_fr: "Erreur technique temporaire sur Wassit Online".to_string(),
            title_ar: "خطأ تقني مؤقت في منصة وسيط".to_string(),
            detail_fr: "Une erreur est survenue côté ANEM lors du traitement de la demande. Veuillez réessayer dans quelques instants.".to_string(),
            detail_ar: "حدث خطأ مؤقت في خادم الوكالة. يرجى إعادة المحاولة بعد قليل.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        6 => RenewalOutcome {
            http_ok: true,
            code: 6,
            state: "warning".to_string(),
            badge: "Mise à jour NIN Requise (Code 6)".to_string(),
            title_fr: "Ancienne CNI détectée — Mise à jour du NIN nécessaire en agence".to_string(),
            title_ar: "تحديث رقم التعريف الوطني ضروري في الملحقة المحلية".to_string(),
            detail_fr: "Ce dossier est encore lié à l'ancienne carte d'identité nationale. Le client doit se présenter à son annexe ANEM avec sa CNI biométrique.".to_string(),
            detail_ar: "الملف مرتبط ببطاقة التعريف القديمة. يجب التقرب من الملحقة المحلية للتشغيل لتحديث رقم التعريف البيومتري (NIN).".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        7 => RenewalOutcome {
            http_ok: true,
            code: 7,
            state: "warning".to_string(),
            badge: "Expirée > 1 An (Code 7)".to_string(),
            title_fr: "Demande expirée depuis plus d'une année !".to_string(),
            title_ar: "طلب العمل منتهي الصلاحية منذ أكثر من سنة!".to_string(),
            detail_fr: "La date de clôture dépasse les délais autorisés pour la prolongation/réactivation publique. Il faut créer une nouvelle demande via l'espace personnel Wassit Online ou en agence ANEM.".to_string(),
            detail_ar: "تجاوز تاريخ الإغلاق الآجال المسموح بها للتمديد المباشر. يجب إنشاء طلب عمل جديد عبر الحساب الشخصي أو في الوكالة.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        8 => RenewalOutcome {
            http_ok: true,
            code: 8,
            state: "warning".to_string(),
            badge: "Métier Désactivé (Code 8)".to_string(),
            title_fr: "Demande expirée et métier principal désactivé dans la nomenclature NAME".to_string(),
            title_ar: "الطلب منتهي والمهنة المسجلة لم تعد متاحة".to_string(),
            detail_fr: "Le métier souhaité dans l'ancienne demande n'est plus disponible. Le client doit créer une nouvelle demande avec un nouveau métier via son compte Wassit ou en agence.".to_string(),
            detail_ar: "المهنة المختارة سابقاً لم تعد متوفرة. يرجى إنشاء طلب عمل جديد عبر الحساب الشخصي أو الملحقة.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        9 => RenewalOutcome {
            http_ok: true,
            code: 9,
            state: "warning".to_string(),
            badge: "Wilaya Pilote Wassit V2 (Code 9)".to_string(),
            title_fr: "Dossier rattaché à la nouvelle plateforme Wassit Online 2 (applicant.anem.dz)".to_string(),
            title_ar: "هذا الملف تابع للمنصة الجديدة وسيط 2 (wassitonline2.anem.dz)".to_string(),
            detail_fr: "Cette wilaya utilise désormais la plateforme Wassit Online 2 (https://applicant.anem.dz -> /Profil/ExtendProfilValidityPeriod). Utilisez le bouton Wassit V2.".to_string(),
            detail_ar: "هذه الولاية تعمل بالمنصة الجديدة https://wassitonline2.anem.dz. يرجى التمديد عبر فضاء طالب العمل الجديد.".to_string(),
            demande_numero: String::new(),
            expiration_date: add_months_6_from_today(),
            raw_json: raw,
        },
        _ => RenewalOutcome {
            http_ok: false,
            code,
            state,
            badge: format!("Réponse ANEM ({})", code),
            title_fr: "Réponse reçue du serveur ANEM".to_string(),
            title_ar: "رد من خادم الوكالة الوطنية للتشغيل".to_string(),
            detail_fr: raw.clone(),
            detail_ar: raw.clone(),
            demande_numero: String::new(),
            expiration_date: add_months_6_from_today(),
            raw_json: raw,
        },
    }
}

fn decode_base64(input: &str) -> Vec<u8> {
    let mut table = [255u8; 256];
    for (i, &b) in b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
        .iter()
        .enumerate()
    {
        table[b as usize] = i as u8;
    }
    let mut out = Vec::with_capacity(input.len() * 3 / 4);
    let mut buf = 0u32;
    let mut bits = 0u32;
    for &b in input.as_bytes() {
        if b == b'=' {
            break;
        }
        let val = table[b as usize];
        if val == 255 {
            continue;
        }
        buf = (buf << 6) | (val as u32);
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push(((buf >> bits) & 0xFF) as u8);
        }
    }
    out
}

fn download_anem_attestation_pdf(nin: &str, numero_wassit: &str) -> Result<String, String> {
    validate_nin(nin)?;
    validate_numero_wassit(numero_wassit)?;

    let url = format!(
        "https://wassitonline.anem.dz/api/FicheDemandeurOnline?nin={}&numeroWassit={}",
        nin.trim(),
        numero_wassit.trim()
    );
    let output = Command::new("curl.exe")
        .args([
            "-s",
            "-m",
            "25",
            "-X",
            "GET",
            &url,
            "-H",
            "Accept: application/json, text/plain, */*",
            "-H",
            "Referer: https://wassitonline.anem.dz/postulation/prolongationDemande",
        ])
        .output()
        .map_err(|e| format!("Failed to execute curl.exe: {}", e))?;

    if !output.status.success() {
        return Err(format!(
            "Erreur HTTP lors du téléchargement de l'attestation: {}",
            String::from_utf8_lossy(&output.stderr)
        ));
    }

    let body = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if body.is_empty() || body == "null" || body.starts_with("{\"State\":\"error\"") || body.starts_with("{\"Message\"") {
        return Err(format!(
            "Aucune attestation PDF retournée par ANEM pour NIN={} / Wassit={}. Réponse: {}",
            nin, numero_wassit, body
        ));
    }

    // Extract base64 content (either a quoted JSON string or {"demande":"..."} or raw base64)
    let b64_str = if let Some(d) = extract_json_str(&body, "demande")
        .or_else(|| extract_json_str(&body, "base64"))
        .or_else(|| extract_json_str(&body, "Result"))
    {
        d
    } else {
        body.trim_matches('"').to_string()
    };

    let bytes = if output.stdout.starts_with(b"%PDF") {
        output.stdout
    } else {
        decode_base64(&b64_str)
    };

    if bytes.len() < 64 || !bytes.starts_with(b"%PDF") {
        return Err(format!(
            "Le serveur ANEM n'a pas renvoyé un PDF valide (taille reçue: {} octets). Vérifiez que la demande est bien active.",
            bytes.len()
        ));
    }

    let _ = fs::create_dir_all(ATTESTATIONS_DIR);
    let file_path = format!(
        "{}/FicheDemandeur-NumWassit-{}.pdf",
        ATTESTATIONS_DIR,
        numero_wassit.trim()
    );
    fs::write(&file_path, &bytes).map_err(|e| format!("Erreur d'écriture PDF: {}", e))?;
    Ok(file_path)
}

fn call_anem_changer_mobile(nin: &str, numero_wassit: &str, mobile: &str) -> RenewalOutcome {
    let payload = format!(
        "{{\"nin\":\"{}\",\"numeroWassit\":\"{}\",\"mobile\":\"{}\"}}",
        nin.trim(),
        numero_wassit.trim(),
        mobile.trim()
    );

    let output = Command::new("curl.exe")
        .args([
            "-s",
            "-m",
            "20",
            "-X",
            "POST",
            "https://wassitonline.anem.dz/api/applicant/ChangerMobile",
            "-H",
            "Content-Type: application/json",
            "-H",
            "Accept: application/json",
            "-d",
            &payload,
        ])
        .output();

    let raw = match output {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).trim().to_string(),
        Ok(o) => String::from_utf8_lossy(&o.stderr).to_string(),
        Err(e) => e.to_string(),
    };

    let code = extract_json_int(&raw, "Code").unwrap_or(0);
    match code {
        1 => RenewalOutcome {
            http_ok: true,
            code: 1,
            state: "success".to_string(),
            badge: "Mobile Modifié (Code 1)".to_string(),
            title_fr: "Numéro de téléphone mobile modifié avec succès !".to_string(),
            title_ar: "تم تغيير رقم الهاتف المحمول بنجاح!".to_string(),
            detail_fr: format!("Nouveau numéro enregistré sur Wassit Online: {}", mobile.trim()),
            detail_ar: format!("تم تعديل رقم هاتفك المحمول بنجاح، رقمك الجديد هو: {}", mobile.trim()),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        2 => RenewalOutcome {
            http_ok: true,
            code: 2,
            state: "error".to_string(),
            badge: "Données Erronées (Code 2)".to_string(),
            title_fr: "Informations introduites erronées (NIN ou N° Wassit)".to_string(),
            title_ar: "المعلومات المدرجة غير صحيحة!".to_string(),
            detail_fr: "Le numéro d'inscription ANEM ou le NIN introduit est erroné.".to_string(),
            detail_ar: "رقم التسجيل في الوكالة أو رقم التعريف الوطني غير صحيح.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        3 => RenewalOutcome {
            http_ok: true,
            code: 3,
            state: "warning".to_string(),
            badge: "Mise à jour NIN Requise (Code 3)".to_string(),
            title_fr: "Mise à jour du NIN nécessaire en agence ANEM".to_string(),
            title_ar: "تحديث رقم التعريف الوطني ضروري في الملحقة".to_string(),
            detail_fr: "Le dossier correspond à l'ancienne CNI. Présentez-vous à l'annexe locale ANEM.".to_string(),
            detail_ar: "يرجى التقرب من الملحقة المحلية للتشغيل لتحيين رقم التعريف الوطني.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        4 => RenewalOutcome {
            http_ok: true,
            code: 4,
            state: "error".to_string(),
            badge: "Mobile Déjà Utilisé (Code 4)".to_string(),
            title_fr: "Numéro de téléphone déjà utilisé par un autre demandeur".to_string(),
            title_ar: "رقم الهاتف مستخدم من مسجل آخر من قبل".to_string(),
            detail_fr: "Ce numéro mobile est déjà rattaché à un autre dossier ANEM.".to_string(),
            detail_ar: "رقم الهاتف الذي أدخلته مستخدم من قبل مستخدم آخر.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        5 => RenewalOutcome {
            http_ok: true,
            code: 5,
            state: "error".to_string(),
            badge: "Mobile Identique (Code 5)".to_string(),
            title_fr: "Le numéro saisi est identique à l'ancien numéro".to_string(),
            title_ar: "الهاتف المحمول مطابق للرقم القديم".to_string(),
            detail_fr: "Ce numéro est déjà enregistré sur ce dossier.".to_string(),
            detail_ar: "رقم الهاتف المحمول الجديد المدخل هو نفسه رقمك القديم.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        9 => RenewalOutcome {
            http_ok: true,
            code: 9,
            state: "warning".to_string(),
            badge: "Wilaya Pilote V2 (Code 9)".to_string(),
            title_fr: "Dossier migré vers Wassit Online 2 (applicant.anem.dz)".to_string(),
            title_ar: "الملف تابع للمنصة الجديدة wassitonline2.anem.dz".to_string(),
            detail_fr: "Le changement de téléphone pour cette wilaya s'effectue sur https://applicant.anem.dz.".to_string(),
            detail_ar: "يرجى تغيير رقم الهاتف عبر المنصة الجديدة https://applicant.anem.dz.".to_string(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
        _ => RenewalOutcome {
            http_ok: false,
            code,
            state: "error".to_string(),
            badge: format!("Code {}", code),
            title_fr: "Réponse Changement Mobile".to_string(),
            title_ar: "رد تغيير رقم الهاتف".to_string(),
            detail_fr: raw.clone(),
            detail_ar: raw.clone(),
            demande_numero: String::new(),
            expiration_date: String::new(),
            raw_json: raw,
        },
    }
}

// Discovered in https://wassitonline.anem.dz/app/controllers/applicant/applicantRegisterCtrlV4.js:
// Creates or overwrites an online account (Email + Password) for a client already registered at an ANEM branch
// using POST https://wassitonline.anem.dz/Account/RegisterExisingApplicant
fn call_anem_recover_existing_account(
    numero_wassit: &str,
    email: &str,
    password: &str,
    force_replacement: bool,
) -> RenewalOutcome {
    let payload = format!(
        "{{\"Numero\":\"{}\",\"Email\":\"{}\",\"Password\":\"{}\",\"PasswordConfirmation\":\"{}\",\"ForceReplacement\":{}}}",
        json_escape(numero_wassit.trim()),
        json_escape(email.trim()),
        json_escape(password),
        json_escape(password),
        if force_replacement { "true" } else { "false" }
    );

    let output = Command::new("curl.exe")
        .args([
            "-s",
            "-m",
            "20",
            "-X",
            "POST",
            "https://wassitonline.anem.dz/Account/RegisterExisingApplicant",
            "-H",
            "Content-Type: application/json",
            "-H",
            "Accept: application/json",
            "-H",
            "Origin: https://wassitonline.anem.dz",
            "-H",
            "Referer: https://wassitonline.anem.dz/Account/RegisterApplicant",
            "-d",
            &payload,
        ])
        .output();

    let raw = match output {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).trim().to_string(),
        Ok(o) => String::from_utf8_lossy(&o.stderr).to_string(),
        Err(e) => e.to_string(),
    };

    let code = extract_json_int(&raw, "Code").unwrap_or(-1);
    let state = extract_json_str(&raw, "State").unwrap_or_else(|| "info".to_string());
    let msg = extract_json_str(&raw, "Message").unwrap_or_default();

    if state == "success" || code == 0 {
        RenewalOutcome {
            http_ok: true,
            code: 0,
            state: "success".to_string(),
            badge: "Compte En Ligne Activé".to_string(),
            title_fr: "Compte en ligne créé / récupéré avec succès sur Wassit Online !".to_string(),
            title_ar: "تم إنشاء / استرجاع الحساب الشخصي على منصة وسيط بنجاح!".to_string(),
            detail_fr: format!(
                "Le dossier N° {} est maintenant lié à l'email {}. Un lien d'activation a été envoyé.",
                numero_wassit.trim(),
                email.trim()
            ),
            detail_ar: format!(
                "تم ربط رقم التسجيل {} بالبريد الإلكتروني {} بنجاح.",
                numero_wassit.trim(),
                email.trim()
            ),
            demande_numero: numero_wassit.trim().to_string(),
            expiration_date: String::new(),
            raw_json: raw,
        }
    } else if code == 2 {
        RenewalOutcome {
            http_ok: true,
            code: 2,
            state: "warning".to_string(),
            badge: "Ancien Email Détecté (Code 2)".to_string(),
            title_fr: "Ce dossier possède déjà un ancien compte email sur Wassit Online".to_string(),
            title_ar: "هذا الملف مرتبط مسبقاً ببريد إلكتروني قديم (يمكن استبداله عبر ForceReplacement)".to_string(),
            detail_fr: format!(
                "Message ANEM: {}. Activez 'Remplacer l'ancien email (ForceReplacement=true)' pour écraser l'ancien email oublié par le client.",
                msg
            ),
            detail_ar: "يرجى تفعيل خيار استبدال البريد الإلكتروني القديم لتعيين البريد الجديد.".to_string(),
            demande_numero: numero_wassit.trim().to_string(),
            expiration_date: String::new(),
            raw_json: raw,
        }
    } else {
        RenewalOutcome {
            http_ok: true,
            code,
            state: "error".to_string(),
            badge: format!("Réponse Compte ({})", code),
            title_fr: if msg.is_empty() {
                "Impossible de rattacher ce numéro Wassit".to_string()
            } else {
                msg.clone()
            },
            title_ar: "تعذر ربط الحساب الإلكتروني برقم الوسيط المدخل".to_string(),
            detail_fr: if msg.is_empty() { raw.clone() } else { msg },
            detail_ar: "يرجى التحقق من رقم التسجيل في الوكالة (N° Wassit) أو من عدم استخدام البريد الإلكتروني في حساب آخر.".to_string(),
            demande_numero: numero_wassit.trim().to_string(),
            expiration_date: String::new(),
            raw_json: raw,
        }
    }
}

// ============================================================================
// CLI COMMAND HANDLERS
// ============================================================================

fn cli_print_status() {
    let clients = load_clients();
    let total = clients.len();
    let mut active = 0usize;
    let mut expiring_soon = 0usize;
    let mut expired = 0usize;
    let mut minha_count = 0usize;
    let mut total_revenue = 0u64;
    let mut total_renewals = 0u32;

    for c in &clients {
        let rem = days_until(&c.expiration_date);
        if rem < 0 {
            expired += 1;
        } else if rem <= 15 {
            expiring_soon += 1;
        } else {
            active += 1;
        }
        if c.is_minha {
            minha_count += 1;
        }
        total_revenue += c.total_paid_dzd;
        total_renewals += c.renewal_count;
    }

    println!("================================================================================");
    println!("  WASSIT SHOP DESK - BILAN DU MAGASIN ({})", today_ymd());
    println!("================================================================================");
    println!("  Clients enregistrés        : {}", total);
    println!("  Bénéficiaires Minha        : {} ({}%)", minha_count, if total > 0 { minha_count * 100 / total } else { 0 });
    println!("  Demandes Actives (>15j)    : {}", active);
    println!("  À renouveler bientôt (<=15j): {}  <-- À APPELER / WHATSAPP", expiring_soon);
    println!("  Demandes Expirées (<0j)    : {}  <-- URGENT (Suspension Minha)", expired);
    println!("  Total renouvellements      : {} opérations", total_renewals);
    println!("  Recette totale boutique    : {} DA (DZD)", total_revenue);
    println!("================================================================================");
}

fn cli_list_clients(query: Option<&str>, only_due: bool) {
    let clients = load_clients();
    let q = query.unwrap_or("").trim().to_lowercase();

    println!(
        "{:<4} | {:<24} | {:<18} | {:<13} | {:<10} | {:<10} | {:<9} | {:<6} | {}",
        "ID", "NOM & PRENOM", "NIN (18 CHIFFRES)", "N° WASSIT", "TELEPHONE", "EXPIRATION", "JOURS", "MINHA", "STATUT"
    );
    println!("{}", "-".repeat(122));

    for c in &clients {
        let rem = days_until(&c.expiration_date);
        if only_due && rem > 15 {
            continue;
        }
        if !q.is_empty() {
            let hay = format!(
                "{} {} {} {} {}",
                c.full_name, c.nin, c.numero_wassit, c.phone, c.wilaya
            )
            .to_lowercase();
            if !hay.contains(&q) {
                continue;
            }
        }
        let rem_str = if rem < 0 {
            format!("EXP ({}j)", rem)
        } else if rem <= 15 {
            format!("J-{} !!", rem)
        } else {
            format!("J-{}", rem)
        };
        println!(
            "{:<4} | {:<24} | {:<18} | {:<13} | {:<10} | {:<10} | {:<9} | {:<6} | {}",
            c.id,
            truncate_str(&c.full_name, 24),
            c.nin,
            c.numero_wassit,
            c.phone,
            c.expiration_date,
            rem_str,
            if c.is_minha { "OUI" } else { "NON" },
            c.last_status_label
        );
    }
}

fn truncate_str(s: &str, max_chars: usize) -> String {
    let count = s.chars().count();
    if count <= max_chars {
        s.to_string()
    } else {
        let mut out: String = s.chars().take(max_chars.saturating_sub(1)).collect();
        out.push('…');
        out
    }
}

fn cli_validate(nin: &str, wassit: &str, phone: &str) {
    println!("Validation des identifiants ANEM Wassit:");
    match validate_nin(nin) {
        Ok(()) => println!("  [OK] NIN ({}) : 18 chiffres valides.", nin.trim()),
        Err(e) => println!("  [ERREUR] NIN : {}", e),
    }
    match validate_numero_wassit(wassit) {
        Ok(()) => println!("  [OK] N° Wassit ({}) : Format ANEM valide.", wassit.trim()),
        Err(e) => println!("  [ERREUR] N° Wassit : {}", e),
    }
    match validate_mobile(phone) {
        Ok(()) => println!("  [OK] Mobile ({}) : Format opérateur DZ (05/06/07) valide.", phone.trim()),
        Err(e) => println!("  [ERREUR] Mobile : {}", e),
    }
}

fn cli_add_or_manual_renew(
    name: &str,
    nin: &str,
    wassit: &str,
    phone: &str,
    wilaya: &str,
    is_minha: bool,
    fee: u64,
    platform: &str,
    notes: &str,
) {
    if let Err(e) = validate_nin(nin) {
        eprintln!("[ERREUR NIN] {}", e);
        std::process::exit(1);
    }
    if let Err(e) = validate_numero_wassit(wassit) {
        eprintln!("[ERREUR WASSIT] {}", e);
        std::process::exit(1);
    }
    let exp = add_months_6_from_today();
    let rec = upsert_client_in_db(
        name,
        nin,
        wassit,
        phone,
        wilaya,
        is_minha,
        platform,
        &exp,
        "",
        4,
        "Active (Enregistrée +6 mois)",
        fee,
        notes,
    );
    append_history(
        &rec.full_name,
        &rec.nin,
        &rec.numero_wassit,
        "MANUAL_RENEWAL_6M",
        4,
        &rec.expiration_date,
        fee,
        "Enregistrement / Prolongation 6 mois au guichet",
    );
    println!(
        "[OK] Client #{} '{}' enregistré ! Nouvelle date d'expiration (+6 mois) : {} (Tarif: {} DA)",
        rec.id, rec.full_name, rec.expiration_date, fee
    );
}

fn cli_live_renew(
    name: &str,
    nin: &str,
    wassit: &str,
    phone: &str,
    wilaya: &str,
    is_minha: bool,
    fee: u64,
) {
    if let Err(e) = validate_nin(nin) {
        eprintln!("[ERREUR NIN] {}", e);
        std::process::exit(1);
    }
    if let Err(e) = validate_numero_wassit(wassit) {
        eprintln!("[ERREUR WASSIT] {}", e);
        std::process::exit(1);
    }

    println!(
        "Connexion directe à https://wassitonline.anem.dz/api/extendMyDemandePublic (NIN={}, Wassit={})...",
        nin.trim(),
        wassit.trim()
    );
    let outcome = call_anem_extend_demande(nin, wassit);
    println!("--------------------------------------------------------------------------------");
    println!("  Statut ANEM : [{}] (Code {})", outcome.badge, outcome.code);
    println!("  FR          : {} — {}", outcome.title_fr, outcome.detail_fr);
    println!("  AR          : {} — {}", outcome.title_ar, outcome.detail_ar);
    println!("--------------------------------------------------------------------------------");

    let platform = if outcome.code == 9 { "V2" } else { "V1" };
    let exp = if !outcome.expiration_date.is_empty() {
        outcome.expiration_date.clone()
    } else {
        add_months_6_from_today()
    };

    let charged_fee = if outcome.code == 4 || outcome.code == 50 { fee } else { 0 };
    let rec = upsert_client_in_db(
        name,
        nin,
        wassit,
        phone,
        wilaya,
        is_minha,
        platform,
        &exp,
        &outcome.demande_numero,
        outcome.code,
        &outcome.badge,
        charged_fee,
        &outcome.title_fr,
    );
    append_history(
        &rec.full_name,
        &rec.nin,
        &rec.numero_wassit,
        "LIVE_ANEM_RENEW",
        outcome.code,
        &rec.expiration_date,
        charged_fee,
        &outcome.detail_fr,
    );
    println!(
        "[BASE DE DONNEES] Client #{} '{}' synchronisé (Expiration suivie: {}).",
        rec.id, rec.full_name, rec.expiration_date
    );
}

fn cli_download_attestation(nin: &str, wassit: &str) {
    println!(
        "Téléchargement de l'attestation depuis https://wassitonline.anem.dz/api/FicheDemandeurOnline..."
    );
    match download_anem_attestation_pdf(nin, wassit) {
        Ok(path) => println!("[OK] Attestation PDF enregistrée : {}", path),
        Err(e) => eprintln!("[INFO/ERREUR] {}", e),
    }
}

fn cli_change_mobile(nin: &str, wassit: &str, mobile: &str) {
    if let Err(e) = validate_nin(nin) {
        eprintln!("[ERREUR NIN] {}", e);
        std::process::exit(1);
    }
    if let Err(e) = validate_numero_wassit(wassit) {
        eprintln!("[ERREUR WASSIT] {}", e);
        std::process::exit(1);
    }
    if let Err(e) = validate_mobile(mobile) {
        eprintln!("[ERREUR MOBILE] {}", e);
        std::process::exit(1);
    }
    let outcome = call_anem_changer_mobile(nin, wassit, mobile);
    println!("Statut Changement Mobile : [{}] {}", outcome.badge, outcome.detail_fr);
}

fn cli_recover_existing_account(wassit: &str, email: &str, password: &str) {
    if let Err(e) = validate_numero_wassit(wassit) {
        eprintln!("[ERREUR WASSIT] {}", e);
        std::process::exit(1);
    }
    println!(
        "Rattachement du dossier N° {} à l'email '{}' via POST https://wassitonline.anem.dz/Account/RegisterExisingApplicant...",
        wassit.trim(),
        email.trim()
    );
    let outcome = call_anem_recover_existing_account(wassit, email, password, true);
    println!("Statut Récupération Compte : [{}] {} — {}", outcome.badge, outcome.title_fr, outcome.detail_fr);
}

fn cli_scan_wassit_site() {
    println!("================================================================================");
    println!("  SCAN COMPLET DE L'ECOSYSTEME https://wassitonline.anem.dz/ (V1 & V2)");
    println!("================================================================================");
    println!("1. Guichet Public Sans Mot de Passe (/postulation React SPA):");
    println!("   - URL Guichet Prolongation : https://wassitonline.anem.dz/postulation/prolongationDemande");
    println!("   - API Prolongation (6 mois): PUT  https://wassitonline.anem.dz/api/extendMyDemandePublic");
    println!("   - API Réactivation (<1 an) : POST https://wassitonline.anem.dz/api/CreateDemandeProlongation");
    println!("   - API Attestation PDF      : GET  https://wassitonline.anem.dz/api/FicheDemandeurOnline");
    println!("   - API Changement Téléphone : POST https://wassitonline.anem.dz/api/applicant/ChangerMobile");
    println!();
    println!("2. Portail Principal AngularJS / ASP.NET MVC (https://wassitonline.anem.dz/):");
    println!("   - Vérif. Existence État Civ: POST https://wassitonline.anem.dz/api/v1/applicant/existance/");
    println!("   - Récup. Compte Existant   : POST https://wassitonline.anem.dz/Account/RegisterExisingApplicant");
    println!("                                (ForceReplacement: true écrase l'ancien email oublié)");
    println!("   - Prolongation Authentifiée: PUT  https://wassitonline.anem.dz/api/extendMyDemande");
    println!("   - 6 Wilayas Pilotes V2     : 06-Béjaïa, 20-Saïda, 22-Sidi Bel Abbès, 24-Guelma, 29-Mascara, 43-Mila");
    println!();
    println!("3. Nouvelle Plateforme Nationale (Wassit Online 2 - Wilayas Pilotes & Compte Demandeur):");
    println!("   - Portail Public V2        : https://wassitonline2.anem.dz");
    println!("   - Espace Demandeur V2      : https://applicant.anem.dz");
    println!("   - SSO Keycloak Auth        : https://authpub.anem.dz (Realm: External-Resources)");
    println!("   - API Prolongation V2      : POST https://ExtDemApi.anem.dz/api/Profil/ExtendProfilValidityPeriod");
    println!("   - API Attestation PDF V2   : GET  https://ExtDemApi.anem.dz/api/Reports/AttestationInscriptionDemReport/AttestationInscriptionDemandeurReport");
    println!("   - API Offres d'Emploi V2   : GET  https://OffersSearchApi.anem.dz/api/v1/DashboardKPIs/GetDashboardKpis");
    println!("   - API Référentiel & Wilayas: GET  https://ExtRefApi.anem.dz/api/Wilaya/GetWilayaOfAlgerie");
    println!("--------------------------------------------------------------------------------");

    if let Ok(out) = Command::new("curl.exe")
        .args([
            "-s",
            "-m",
            "10",
            "https://OffersSearchApi.anem.dz/api/v1/DashboardKPIs/GetDashboardKpis",
        ])
        .output()
    {
        let kpi = String::from_utf8_lossy(&out.stdout);
        if !kpi.trim().is_empty() {
            println!("  [LIVE KPI ANEM] {}", kpi.trim());
        }
    }
    println!("================================================================================");
}

fn cli_export_report() {
    let clients = load_clients();
    let mut report = String::new();
    report.push_str("================================================================================\n");
    report.push_str(&format!(
        "  RAPPORT D'ALERTES RENOUVELLEMENT ANEM WASSIT.DZ - DATE: {}\n",
        today_ymd()
    ));
    report.push_str("================================================================================\n\n");

    let mut csv = String::from("ID,Nom_Prenom,NIN,Numero_Wassit,Telephone,Wilaya,Minha,Plateforme,Dernier_Renouvellement,Date_Expiration,Jours_Restants,Statut,Total_Paye_DA\n");

    for c in &clients {
        let rem = days_until(&c.expiration_date);
        csv.push_str(&format!(
            "{},\"{}\",\"{}\",\"{}\",\"{}\",\"{}\",{},{},{},{},{},\"{}\",{}\n",
            c.id,
            c.full_name.replace('"', "'"),
            c.nin,
            c.numero_wassit,
            c.phone,
            c.wilaya.replace('"', "'"),
            if c.is_minha { "OUI" } else { "NON" },
            c.platform,
            c.last_renewal,
            c.expiration_date,
            rem,
            c.last_status_label.replace('"', "'"),
            c.total_paid_dzd
        ));

        if rem <= 15 {
            report.push_str(&format!(
                "[{}] {} | Tél: {} | NIN: {} | Wassit: {} | Expire: {} ({} jours) | Minha: {}\n",
                if rem < 0 { "EXPIRE" } else { "URGENT <=15J" },
                c.full_name,
                c.phone,
                c.nin,
                c.numero_wassit,
                c.expiration_date,
                rem,
                if c.is_minha { "OUI" } else { "NON" }
            ));
        }
    }

    let _ = fs::write("wassit_renewal_alerts.txt", &report);
    let _ = fs::write("wassit_clients_export.csv", &csv);
    println!("[OK] Rapports générés : wassit_renewal_alerts.txt et wassit_clients_export.csv");
}

// ============================================================================
// EMBEDDED SHOP DESK WEB SERVER (http://127.0.0.1:7878)
// ============================================================================

fn run_server(port: u16, open_browser: bool) {
    let addr = format!("127.0.0.1:{}", port);
    let listener = match TcpListener::bind(&addr) {
        Ok(l) => l,
        Err(e) => {
            eprintln!("Impossible de démarrer le serveur sur {}: {}", addr, e);
            return;
        }
    };

    let url = format!("http://{}", addr);
    println!("================================================================================");
    println!("  WASSIT SHOP DESK (Rust) - Guichet Renouvellement ANEM & Base Clients");
    println!("  Interface Web active sur : {}", url);
    println!("  Base de données locale   : {}", DB_FILE);
    println!("  Dossier Attestations PDF : {}/", ATTESTATIONS_DIR);
    println!("================================================================================");

    if open_browser {
        let _ = Command::new("cmd").args(["/C", "start", "", &url]).spawn();
    }

    for stream in listener.incoming().flatten() {
        handle_http_client(stream);
    }
}

fn url_decode(s: &str) -> String {
    let mut bytes = Vec::with_capacity(s.len());
    let mut chars = s.as_bytes().iter().copied();
    while let Some(b) = chars.next() {
        if b == b'+' {
            bytes.push(b' ');
        } else if b == b'%' {
            let h1 = chars.next().unwrap_or(b'0');
            let h2 = chars.next().unwrap_or(b'0');
            let hex = [h1, h2];
            if let Ok(hex_str) = std::str::from_utf8(&hex) {
                if let Ok(v) = u8::from_str_radix(hex_str, 16) {
                    bytes.push(v);
                    continue;
                }
            }
            bytes.push(b'%');
            bytes.push(h1);
            bytes.push(h2);
        } else {
            bytes.push(b);
        }
    }
    String::from_utf8_lossy(&bytes).to_string()
}

fn get_form_param(body: &str, key: &str) -> String {
    for pair in body.split('&') {
        let mut kv = pair.splitn(2, '=');
        let k = kv.next().unwrap_or("");
        let v = kv.next().unwrap_or("");
        if k == key {
            return url_decode(v);
        }
    }
    String::new()
}

fn json_escape(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 8);
    for ch in s.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push(' '),
            c => out.push(c),
        }
    }
    out
}

fn clients_to_json(clients: &[ClientRecord]) -> String {
    let mut out = String::from("[");
    for (i, c) in clients.iter().enumerate() {
        if i > 0 {
            out.push(',');
        }
        let rem = days_until(&c.expiration_date);
        let pdf_path = format!(
            "{}/FicheDemandeur-NumWassit-{}.pdf",
            ATTESTATIONS_DIR, c.numero_wassit
        );
        let has_pdf = Path::new(&pdf_path).exists();
        out.push_str(&format!(
            "{{\"id\":{},\"full_name\":\"{}\",\"nin\":\"{}\",\"numero_wassit\":\"{}\",\"phone\":\"{}\",\"wilaya\":\"{}\",\"is_minha\":{},\"platform\":\"{}\",\"last_renewal\":\"{}\",\"expiration_date\":\"{}\",\"days_remaining\":{},\"demande_numero\":\"{}\",\"last_status_code\":{},\"last_status_label\":\"{}\",\"renewal_count\":{},\"total_paid_dzd\":{},\"notes\":\"{}\",\"has_pdf\":{}}}",
            c.id,
            json_escape(&c.full_name),
            json_escape(&c.nin),
            json_escape(&c.numero_wassit),
            json_escape(&c.phone),
            json_escape(&c.wilaya),
            c.is_minha,
            json_escape(&c.platform),
            json_escape(&c.last_renewal),
            json_escape(&c.expiration_date),
            rem,
            json_escape(&c.demande_numero),
            c.last_status_code,
            json_escape(&c.last_status_label),
            c.renewal_count,
            c.total_paid_dzd,
            json_escape(&c.notes),
            has_pdf
        ));
    }
    out.push(']');
    out
}

fn outcome_to_json(o: &RenewalOutcome, client: Option<&ClientRecord>) -> String {
    let client_json = match client {
        Some(c) => clients_to_json(&[c.clone()]),
        None => "[]".to_string(),
    };
    format!(
        "{{\"http_ok\":{},\"code\":{},\"state\":\"{}\",\"badge\":\"{}\",\"title_fr\":\"{}\",\"title_ar\":\"{}\",\"detail_fr\":\"{}\",\"detail_ar\":\"{}\",\"demande_numero\":\"{}\",\"expiration_date\":\"{}\",\"raw_json\":\"{}\",\"client\":{}}}",
        o.http_ok,
        o.code,
        json_escape(&o.state),
        json_escape(&o.badge),
        json_escape(&o.title_fr),
        json_escape(&o.title_ar),
        json_escape(&o.detail_fr),
        json_escape(&o.detail_ar),
        json_escape(&o.demande_numero),
        json_escape(&o.expiration_date),
        json_escape(&o.raw_json),
        client_json
    )
}

fn send_http_response(stream: &mut TcpStream, status: &str, content_type: &str, body: &[u8]) {
    let header = format!(
        "HTTP/1.1 {}\r\nContent-Type: {}\r\nContent-Length: {}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
        status,
        content_type,
        body.len()
    );
    let _ = stream.write_all(header.as_bytes());
    let _ = stream.write_all(body);
    let _ = stream.flush();
}

fn handle_http_client(mut stream: TcpStream) {
    let mut buf = [0u8; 65536];
    let n = match stream.read(&mut buf) {
        Ok(n) if n > 0 => n,
        _ => return,
    };
    let req_str = String::from_utf8_lossy(&buf[..n]);
    let mut lines = req_str.lines();
    let first_line = lines.next().unwrap_or("");
    let parts: Vec<&str> = first_line.split_whitespace().collect();
    if parts.len() < 2 {
        return;
    }
    let method = parts[0];
    let path = parts[1];

    let body_start = req_str.find("\r\n\r\n").map(|i| i + 4).unwrap_or(req_str.len());
    let body = &req_str[body_start..];

    if method == "GET" && (path == "/" || path.starts_with("/?")) {
        let html = build_dashboard_html();
        send_http_response(&mut stream, "200 OK", "text/html; charset=utf-8", html.as_bytes());
    } else if method == "GET" && path == "/api/clients" {
        let clients = load_clients();
        let json = clients_to_json(&clients);
        send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", json.as_bytes());
    } else if method == "POST" && path == "/api/renew" {
        let full_name = get_form_param(body, "full_name");
        let nin = get_form_param(body, "nin");
        let wassit = get_form_param(body, "numero_wassit");
        let phone = get_form_param(body, "phone");
        let wilaya = get_form_param(body, "wilaya");
        let is_minha = get_form_param(body, "is_minha") == "true";
        let fee: u64 = get_form_param(body, "fee_dzd").parse().unwrap_or(100);
        let notes = get_form_param(body, "notes");
        let mode = get_form_param(body, "mode"); // "live" or "manual"

        if let Err(e) = validate_nin(&nin) {
            let err_out = RenewalOutcome {
                http_ok: false,
                code: -10,
                state: "error".to_string(),
                badge: "NIN Invalide".to_string(),
                title_fr: e.clone(),
                title_ar: "رقم التعريف الوطني غير صحيح (يجب أن يتكون من 18 رقماً)".to_string(),
                detail_fr: e,
                detail_ar: "يرجى التأكد من إدخال 18 رقماً من بطاقة التعريف البيومترية.".to_string(),
                demande_numero: String::new(),
                expiration_date: String::new(),
                raw_json: String::new(),
            };
            let j = outcome_to_json(&err_out, None);
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
            return;
        }
        if let Err(e) = validate_numero_wassit(&wassit) {
            let err_out = RenewalOutcome {
                http_ok: false,
                code: -11,
                state: "error".to_string(),
                badge: "N° Wassit Invalide".to_string(),
                title_fr: e.clone(),
                title_ar: "رقم التسجيل في الوكالة غير صحيح".to_string(),
                detail_fr: e,
                detail_ar: "يرجى التحقق من رقم الوسيط الموجود في شهادة التسجيل.".to_string(),
                demande_numero: String::new(),
                expiration_date: String::new(),
                raw_json: String::new(),
            };
            let j = outcome_to_json(&err_out, None);
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
            return;
        }

        if mode == "manual" {
            let custom_exp = get_form_param(body, "expiration_date");
            let exp = if custom_exp.trim().is_empty() {
                add_months_6_from_today()
            } else {
                custom_exp
            };
            let platform = get_form_param(body, "platform");
            let rec = upsert_client_in_db(
                &full_name,
                &nin,
                &wassit,
                &phone,
                &wilaya,
                is_minha,
                if platform.is_empty() { "V1" } else { &platform },
                &exp,
                "",
                4,
                "Enregistrée (+6 mois)",
                fee,
                &notes,
            );
            append_history(
                &rec.full_name,
                &rec.nin,
                &rec.numero_wassit,
                "SHOP_DB_SAVE",
                4,
                &rec.expiration_date,
                fee,
                "Enregistrement manuel +6 mois dans la base du magasin",
            );
            let manual_out = RenewalOutcome {
                http_ok: true,
                code: 4,
                state: "success".to_string(),
                badge: "Enregistré (+6 Mois)".to_string(),
                title_fr: format!(
                    "Client '{}' enregistré avec succès dans la base du magasin !",
                    rec.full_name
                ),
                title_ar: format!("تم حفظ الزبون '{}' في قاعدة بيانات المحل بنجاح!", rec.full_name),
                detail_fr: format!(
                    "Prochain renouvellement prévu le {} (dans 6 mois). Tarif comptabilisé: {} DA.",
                    rec.expiration_date, fee
                ),
                detail_ar: format!(
                    "تاريخ انتهاء الصلاحية القادم: {} (بعد 6 أشهر).",
                    rec.expiration_date
                ),
                demande_numero: rec.demande_numero.clone(),
                expiration_date: rec.expiration_date.clone(),
                raw_json: String::new(),
            };
            let j = outcome_to_json(&manual_out, Some(&rec));
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
        } else {
            let outcome = call_anem_extend_demande(&nin, &wassit);
            let platform = if outcome.code == 9 { "V2" } else { "V1" };
            let exp = if !outcome.expiration_date.is_empty() {
                outcome.expiration_date.clone()
            } else {
                add_months_6_from_today()
            };
            let charged_fee = if outcome.code == 4 || outcome.code == 50 { fee } else { 0 };
            let note_to_save = if !notes.trim().is_empty() {
                notes
            } else {
                outcome.badge.clone()
            };
            let rec = upsert_client_in_db(
                &full_name,
                &nin,
                &wassit,
                &phone,
                &wilaya,
                is_minha,
                platform,
                &exp,
                &outcome.demande_numero,
                outcome.code,
                &outcome.badge,
                charged_fee,
                &note_to_save,
            );
            append_history(
                &rec.full_name,
                &rec.nin,
                &rec.numero_wassit,
                "LIVE_ANEM_EXTEND",
                outcome.code,
                &rec.expiration_date,
                charged_fee,
                &outcome.detail_fr,
            );
            let j = outcome_to_json(&outcome, Some(&rec));
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
        }
    } else if method == "POST" && path == "/api/change-mobile" {
        let nin = get_form_param(body, "nin");
        let wassit = get_form_param(body, "numero_wassit");
        let mobile = get_form_param(body, "mobile");

        if let Err(e) = validate_mobile(&mobile) {
            let err_out = RenewalOutcome {
                http_ok: false,
                code: -12,
                state: "error".to_string(),
                badge: "Mobile Invalide".to_string(),
                title_fr: e.clone(),
                title_ar: "رقم الهاتف غير صحيح (10 أرقام تبدأ بـ 05 أو 06 أو 07)".to_string(),
                detail_fr: e,
                detail_ar: String::new(),
                demande_numero: String::new(),
                expiration_date: String::new(),
                raw_json: String::new(),
            };
            let j = outcome_to_json(&err_out, None);
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
            return;
        }

        let outcome = call_anem_changer_mobile(&nin, &wassit, &mobile);
        if outcome.code == 1 {
            let mut clients = load_clients();
            if let Some(c) = clients
                .iter_mut()
                .find(|c| c.nin == nin.trim() || c.numero_wassit == wassit.trim())
            {
                c.phone = mobile.trim().to_string();
                save_clients(&clients);
            }
        }
        let j = outcome_to_json(&outcome, None);
        send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
    } else if method == "POST" && path == "/api/recover-account" {
        let wassit = get_form_param(body, "numero_wassit");
        let email = get_form_param(body, "email");
        let password = get_form_param(body, "password");
        let force = get_form_param(body, "force_replacement") == "true";

        if let Err(e) = validate_numero_wassit(&wassit) {
            let err_out = RenewalOutcome {
                http_ok: false,
                code: -13,
                state: "error".to_string(),
                badge: "N° Wassit Invalide".to_string(),
                title_fr: e.clone(),
                title_ar: "رقم التسجيل في الوكالة غير صحيح".to_string(),
                detail_fr: e,
                detail_ar: String::new(),
                demande_numero: String::new(),
                expiration_date: String::new(),
                raw_json: String::new(),
            };
            let j = outcome_to_json(&err_out, None);
            send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
            return;
        }
        let outcome = call_anem_recover_existing_account(&wassit, &email, &password, force);
        let j = outcome_to_json(&outcome, None);
        send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", j.as_bytes());
    } else if method == "GET" && path.starts_with("/api/receipt?") {
        let query = &path["/api/receipt?".len()..];
        let id: u64 = get_form_param(query, "id").parse().unwrap_or(0);
        let clients = load_clients();
        if let Some(c) = clients.iter().find(|x| x.id == id) {
            let html = build_receipt_html(c);
            send_http_response(&mut stream, "200 OK", "text/html; charset=utf-8", html.as_bytes());
        } else {
            send_http_response(&mut stream, "404 Not Found", "text/plain; charset=utf-8", b"Client introuvable");
        }
    } else if method == "POST" && path == "/api/delete" {
        let id: u64 = get_form_param(body, "id").parse().unwrap_or(0);
        let mut clients = load_clients();
        clients.retain(|c| c.id != id);
        save_clients(&clients);
        let json = clients_to_json(&clients);
        send_http_response(&mut stream, "200 OK", "application/json; charset=utf-8", json.as_bytes());
    } else if method == "GET" && path.starts_with("/api/attestation?") {
        let query = &path["/api/attestation?".len()..];
        let nin = get_form_param(query, "nin");
        let wassit = get_form_param(query, "numero_wassit");
        match download_anem_attestation_pdf(&nin, &wassit) {
            Ok(file_path) => {
                if let Ok(bytes) = fs::read(&file_path) {
                    let header = format!(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/pdf\r\nContent-Disposition: inline; filename=\"FicheDemandeur-NumWassit-{}.pdf\"\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
                        wassit.trim(),
                        bytes.len()
                    );
                    let _ = stream.write_all(header.as_bytes());
                    let _ = stream.write_all(&bytes);
                    let _ = stream.flush();
                    return;
                }
                send_http_response(
                    &mut stream,
                    "500 Internal Server Error",
                    "text/plain; charset=utf-8",
                    b"Impossible de lire le PDF telecharge.",
                );
            }
            Err(e) => {
                let html = format!(
                    "<!doctype html><html><head><meta charset='utf-8'><title>Attestation ANEM</title><style>body{{font-family:system-ui;background:#0f172a;color:#f8fafc;padding:40px;text-align:center}}.box{{max-width:620px;margin:40px auto;background:#1e293b;border:1px solid #334155;border-radius:12px;padding:28px}}a{{color:#38bdf8}}</style></head><body><div class='box'><h2>⚠️ Attestation PDF non disponible</h2><p>{}</p><p>Note: Le téléchargement direct de <code>FicheDemandeurOnline</code> nécessite un NIN (18 chiffres) et un N° Wassit correspondant à une demande d'emploi <b>active</b> sur <code>wassitonline.anem.dz</code>.</p><p><a href='/'>← Retour au Guichet Wassit Shop Desk</a></p></div></body></html>",
                    e
                );
                send_http_response(&mut stream, "200 OK", "text/html; charset=utf-8", html.as_bytes());
            }
        }
    } else if method == "GET" && path == "/api/export-csv" {
        cli_export_report();
        if let Ok(bytes) = fs::read("wassit_clients_export.csv") {
            let header = format!(
                "HTTP/1.1 200 OK\r\nContent-Type: text/csv; charset=utf-8\r\nContent-Disposition: attachment; filename=\"wassit_clients_export.csv\"\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
                bytes.len()
            );
            let _ = stream.write_all(header.as_bytes());
            let _ = stream.write_all(&bytes);
            let _ = stream.flush();
        } else {
            send_http_response(&mut stream, "404 Not Found", "text/plain", b"CSV non disponible");
        }
    } else {
        send_http_response(&mut stream, "404 Not Found", "text/plain", b"Not Found");
    }
}

fn build_dashboard_html() -> String {
    r#"<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Wassit Shop Desk (Rust) — Gestionnaire Renouvellement ANEM & Base Clients</title>
<style>
  :root {
    --bg: #0b1120;
    --panel: #111827;
    --panel-2: #1e293b;
    --border: #334155;
    --text: #f8fafc;
    --muted: #94a3b8;
    --primary: #0ea5e9;
    --primary-hover: #0284c7;
    --success: #10b981;
    --warning: #f59e0b;
    --danger: #ef4444;
    --purple: #8b5cf6;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    background: var(--bg);
    color: var(--text);
    line-height: 1.5;
    padding-bottom: 50px;
  }
  header {
    background: linear-gradient(90deg, #0f172a 0%, #1e293b 50%, #0f172a 100%);
    border-bottom: 1px solid var(--border);
    padding: 16px 28px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    flex-wrap: wrap;
    gap: 14px;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .brand-badge {
    background: linear-gradient(135deg, #0ea5e9, #10b981);
    color: #fff;
    font-weight: 800;
    font-size: 18px;
    padding: 10px 14px;
    border-radius: 10px;
    letter-spacing: 0.5px;
    box-shadow: 0 4px 14px rgba(14, 165, 233, 0.35);
  }
  .brand h1 { font-size: 20px; font-weight: 700; }
  .brand p { font-size: 13px; color: var(--muted); }
  .header-links { display: flex; gap: 10px; flex-wrap: wrap; }
  .btn {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    padding: 9px 14px;
    border-radius: 8px;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    border: 1px solid transparent;
    text-decoration: none;
    transition: all 0.15s;
    color: #fff;
  }
  .btn-primary { background: var(--primary); }
  .btn-primary:hover { background: var(--primary-hover); }
  .btn-success { background: var(--success); }
  .btn-success:hover { background: #059669; }
  .btn-warning { background: var(--warning); color: #000; }
  .btn-purple { background: var(--purple); }
  .btn-outline {
    background: rgba(255,255,255,0.04);
    border-color: var(--border);
    color: var(--text);
  }
  .btn-outline:hover { background: rgba(255,255,255,0.1); }
  .btn-danger { background: rgba(239,68,68,0.18); border-color: #ef4444; color: #fca5a5; }
  .btn-danger:hover { background: #ef4444; color: #fff; }
  .container { max-width: 1440px; margin: 22px auto; padding: 0 22px; }
  .kpi-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
    gap: 14px;
    margin-bottom: 22px;
  }
  .kpi-card {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 16px 18px;
    position: relative;
    overflow: hidden;
  }
  .kpi-card .label { font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.6px; }
  .kpi-card .value { font-size: 28px; font-weight: 800; margin: 6px 0 2px; }
  .kpi-card .sub { font-size: 12px; color: var(--muted); }
  .main-grid {
    display: grid;
    grid-template-columns: 430px 1fr;
    gap: 20px;
    align-items: start;
  }
  @media (max-width: 1100px) {
    .main-grid { grid-template-columns: 1fr; }
  }
  .card {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 20px;
    margin-bottom: 20px;
  }
  .card-title {
    font-size: 16px;
    font-weight: 700;
    margin-bottom: 14px;
    padding-bottom: 10px;
    border-bottom: 1px solid var(--border);
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .form-group { margin-bottom: 12px; }
  .form-group label {
    display: flex;
    justify-content: space-between;
    font-size: 12.5px;
    font-weight: 600;
    margin-bottom: 5px;
    color: #cbd5e1;
  }
  .form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
  input[type="text"], input[type="number"], input[type="date"], select {
    width: 100%;
    padding: 9px 12px;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: #0f172a;
    color: var(--text);
    font-size: 13.5px;
    font-family: inherit;
  }
  input:focus, select:focus {
    outline: none;
    border-color: var(--primary);
    box-shadow: 0 0 0 2px rgba(14, 165, 233, 0.25);
  }
  .mono { font-family: 'Consolas', 'Courier New', monospace; letter-spacing: 0.5px; }
  .checkbox-row {
    display: flex;
    align-items: center;
    gap: 8px;
    background: rgba(14, 165, 233, 0.08);
    border: 1px solid rgba(14, 165, 233, 0.25);
    padding: 9px 12px;
    border-radius: 8px;
    margin: 12px 0;
    font-size: 13px;
  }
  .action-stack { display: flex; flex-direction: column; gap: 9px; margin-top: 14px; }
  .action-row { display: grid; grid-template-columns: 1fr 1fr; gap: 9px; }
  .result-banner {
    display: none;
    border-radius: 10px;
    padding: 14px 16px;
    margin-top: 14px;
    border: 1px solid var(--border);
    background: var(--panel-2);
  }
  .result-banner.success { border-color: var(--success); background: rgba(16, 185, 129, 0.12); }
  .result-banner.warning { border-color: var(--warning); background: rgba(245, 158, 11, 0.12); }
  .result-banner.error { border-color: var(--danger); background: rgba(239, 68, 68, 0.12); }
  .result-banner h4 { font-size: 14px; margin-bottom: 4px; }
  .result-banner p { font-size: 12.5px; color: #e2e8f0; margin-top: 3px; }
  .result-banner .ar { direction: rtl; font-family: 'Tahoma', 'Segoe UI', sans-serif; color: #bae6fd; margin-top: 6px; font-weight: 600; }
  .toolbar {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
    margin-bottom: 14px;
  }
  .filter-tabs { display: flex; gap: 6px; flex-wrap: wrap; }
  .tab {
    padding: 6px 12px;
    border-radius: 20px;
    font-size: 12.5px;
    font-weight: 600;
    cursor: pointer;
    background: var(--panel-2);
    border: 1px solid var(--border);
    color: var(--muted);
  }
  .tab.active {
    background: var(--primary);
    color: #fff;
    border-color: var(--primary);
  }
  .table-wrap { overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th, td {
    padding: 11px 10px;
    text-align: left;
    border-bottom: 1px solid rgba(51, 65, 85, 0.65);
  }
  th {
    background: rgba(30, 41, 59, 0.7);
    color: var(--muted);
    font-size: 11.5px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }
  tr:hover { background: rgba(255, 255, 255, 0.025); }
  .badge {
    display: inline-block;
    padding: 3px 8px;
    border-radius: 6px;
    font-size: 11.5px;
    font-weight: 700;
  }
  .badge-ok { background: rgba(16, 185, 129, 0.16); color: #34d399; border: 1px solid rgba(16, 185, 129, 0.35); }
  .badge-warn { background: rgba(245, 158, 11, 0.18); color: #fbbf24; border: 1px solid rgba(245, 158, 11, 0.4); }
  .badge-exp { background: rgba(239, 68, 68, 0.18); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.4); }
  .badge-minha { background: rgba(14, 165, 233, 0.18); color: #38bdf8; border: 1px solid rgba(14, 165, 233, 0.35); }
  .badge-v2 { background: rgba(139, 92, 246, 0.18); color: #c4b5fd; border: 1px solid rgba(139, 92, 246, 0.35); }
  .row-actions { display: flex; gap: 5px; flex-wrap: wrap; }
  .btn-xs {
    padding: 4px 8px;
    font-size: 11.5px;
    border-radius: 6px;
    cursor: pointer;
    border: 1px solid var(--border);
    background: var(--panel-2);
    color: var(--text);
    text-decoration: none;
    font-weight: 600;
  }
  .btn-xs:hover { border-color: var(--primary); color: #38bdf8; }
  .site-scan-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
    gap: 14px;
    margin-top: 10px;
  }
  .scan-box {
    background: var(--panel-2);
    border: 1px solid var(--border);
    border-radius: 10px;
    padding: 14px;
    font-size: 12.5px;
  }
  .scan-box h4 { color: #38bdf8; margin-bottom: 6px; font-size: 13.5px; }
  .scan-box code { color: #a7f3d0; font-size: 11.5px; word-break: break-all; }
</style>
</head>
<body>

<header>
  <div class="brand">
    <div class="brand-badge">WASSIT.DZ</div>
    <div>
      <h1>Wassit Shop Desk — Guichet Renouvellement ANEM & Base Clients (Rust)</h1>
      <p>Prolongation 6 mois • Réactivation • Téléchargement Attestation PDF • Suivi Minha (منحة البطالة) & Alertes</p>
    </div>
  </div>
  <div class="header-links">
    <a class="btn btn-outline" href="https://wassitonline.anem.dz/postulation/prolongationDemande" target="_blank">🌐 Page Prolongation V1</a>
    <a class="btn btn-outline" href="https://wassitonline.anem.dz/postulation/ChangementMobile" target="_blank">📱 Page Mobile V1</a>
    <a class="btn btn-purple" href="https://applicant.anem.dz" target="_blank">🚀 Espace Demandeur V2</a>
    <a class="btn btn-outline" href="https://minha.anem.dz" target="_blank">💳 Minha ANEM</a>
    <a class="btn btn-success" href="/api/export-csv">📥 Exporter Base CSV</a>
  </div>
</header>

<div class="container">
  <!-- KPI BAR -->
  <div class="kpi-grid">
    <div class="kpi-card">
      <div class="label">Total Clients Magasin</div>
      <div class="value" id="kpi-total">0</div>
      <div class="sub">Fiches NIN + N° Wassit enregistrées</div>
    </div>
    <div class="kpi-card">
      <div class="label">Demandes Actives (&gt;15j)</div>
      <div class="value" style="color: var(--success)" id="kpi-active">0</div>
      <div class="sub">Validité 6 mois en cours</div>
    </div>
    <div class="kpi-card">
      <div class="label">⚠️ À Renouveler (≤15j / Expirés)</div>
      <div class="value" style="color: var(--warning)" id="kpi-due">0</div>
      <div class="sub">Clients à contacter par Tél / WhatsApp</div>
    </div>
    <div class="kpi-card">
      <div class="label">Bénéficiaires Minha (منحة البطالة)</div>
      <div class="value" style="color: var(--primary)" id="kpi-minha">0</div>
      <div class="sub">Priorité absolue (Suspension si expiré)</div>
    </div>
    <div class="kpi-card">
      <div class="label">Recette Guichet (DZD)</div>
      <div class="value" style="color: #a7f3d0" id="kpi-revenue">0 DA</div>
      <div class="sub" id="kpi-ops">0 renouvellements effectués</div>
    </div>
  </div>

  <div class="main-grid">
    <!-- LEFT COLUMN: RENEWAL & CLIENT FORM -->
    <div>
      <div class="card">
        <div class="card-title">
          <span>⚡ Guichet Prolongation / تجديد طلب العمل</span>
          <button class="btn-xs" onclick="clearForm()">+ Nouveau Client</button>
        </div>

        <div class="form-group">
          <label><span>Nom & Prénom du Client</span><span>الاسم واللقب</span></label>
          <input type="text" id="f-name" placeholder="Ex: Boudiaf Mohamed Amine">
        </div>

        <div class="form-group">
          <label>
            <span>NIN (18 chiffres CNI Biométrique) *</span>
            <span id="nin-counter" style="color: var(--muted)">0 / 18</span>
          </label>
          <input type="text" id="f-nin" class="mono" maxlength="18" placeholder="Ex: 109941601012345601" oninput="updateNinCount()">
        </div>

        <div class="form-group">
          <label><span>Numéro d'inscription ANEM (Wassit) *</span><span>رقم التسجيل في الوكالة</span></label>
          <input type="text" id="f-wassit" class="mono" maxlength="18" placeholder="Ex: 160194001245">
        </div>

        <div class="form-row">
          <div class="form-group">
            <label><span>Téléphone Mobile</span><span>رقم الهاتف</span></label>
            <input type="text" id="f-phone" class="mono" maxlength="10" placeholder="05/06/07XXXXXXXX">
          </div>
          <div class="form-group">
            <label><span>Wilaya</span><span>الولاية</span></label>
            <input type="text" id="f-wilaya" placeholder="Ex: 16 - Alger" value="16 - Alger">
          </div>
        </div>

        <div class="form-row">
          <div class="form-group">
            <label><span>Tarif Service (DA)</span><span>التسعيرة (دج)</span></label>
            <input type="number" id="f-fee" value="100" step="50">
          </div>
          <div class="form-group">
            <label><span>Plateforme</span><span>المنصة</span></label>
            <select id="f-platform">
              <option value="V1">Wassit V1 (Classique)</option>
              <option value="V2">Wassit V2 (applicant.anem.dz)</option>
            </select>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group">
            <label><span>Échéance (+6 mois auto si vide)</span></label>
            <input type="date" id="f-exp">
          </div>
          <div class="form-group">
            <label><span>Notes Magasin</span></label>
            <input type="text" id="f-notes" placeholder="Ex: Attestation imprimée">
          </div>
        </div>

        <label class="checkbox-row">
          <input type="checkbox" id="f-minha" checked>
          <span><strong>Bénéficiaire Allocation Chômage (Minha / منحة البطالة)</strong> — Prioritaire</span>
        </label>

        <div class="action-stack">
          <button class="btn btn-primary" style="justify-content:center; padding:12px" onclick="submitRenewal('live')">
            ⚡ Prolonger sur Wassit.dz (API Directe) + Sauvegarder Client
          </button>
          <div class="action-row">
            <button class="btn btn-success" style="justify-content:center" onclick="submitRenewal('manual')">
              💾 Sauvegarder (+6 Mois)
            </button>
            <button class="btn btn-outline" style="justify-content:center" onclick="downloadPdfFromForm()">
              📄 Attestation PDF
            </button>
          </div>
          <button class="btn btn-outline" style="justify-content:center" onclick="submitChangeMobile()">
            📱 Changer le Numéro Mobile sur Wassit.dz (API ChangerMobile)
          </button>
        </div>

        <div id="result-banner" class="result-banner">
          <h4 id="res-title"></h4>
          <p id="res-detail"></p>
          <div class="ar" id="res-ar"></div>
        </div>
      </div>

      <!-- ACCOUNT RECOVERY FOR EXISTING AGENCY CLIENTS (Discovered on https://wassitonline.anem.dz/) -->
      <div class="card">
        <div class="card-title">
          <span>🔑 Récupération / Création Compte (Inscrit en Agence)</span>
          <span class="badge badge-minha">Portail Principal</span>
        </div>
        <p style="font-size:12px; color:var(--muted); margin-bottom:10px">
          Pour un client déjà inscrit à l'ANEM qui n'a pas de compte en ligne ou qui a oublié son ancien email (<code>POST /Account/RegisterExisingApplicant</code> avec <code>ForceReplacement: true</code>).
        </p>
        <div class="form-row">
          <div class="form-group">
            <label><span>N° Wassit *</span></label>
            <input type="text" id="rec-wassit" class="mono" placeholder="Ex: 160194001245">
          </div>
          <div class="form-group">
            <label><span>Nouvel Email *</span></label>
            <input type="text" id="rec-email" placeholder="client@gmail.com">
          </div>
        </div>
        <div class="form-row">
          <div class="form-group">
            <label><span>Nouveau Mot de Passe *</span></label>
            <input type="text" id="rec-pass" class="mono" value="Anem2026">
          </div>
          <div class="form-group" style="display:flex;align-items:end">
            <button class="btn btn-purple" style="width:100%;justify-content:center" onclick="submitRecoverAccount()">
              🔑 Lier / Écraser Email
            </button>
          </div>
        </div>
      </div>

      <!-- SITE SCAN SUMMARY CARD -->
      <div class="card">
        <div class="card-title">
          <span>🔍 Architecture Complète de wassitonline.anem.dz</span>
          <span class="badge badge-ok">V1 + Portail + V2</span>
        </div>
        <div class="scan-box" style="margin-bottom:10px">
          <h4>1. Guichet Public Sans Mot de Passe (/postulation)</h4>
          <p>• Prolongation (6 mois) : <code>PUT /api/extendMyDemandePublic</code></p>
          <p>• Réactivation (&lt;1 an) : <code>POST /api/CreateDemandeProlongation</code></p>
          <p>• Attestation PDF : <code>GET /api/FicheDemandeurOnline</code></p>
          <p>• Mobile : <code>POST /api/applicant/ChangerMobile</code></p>
        </div>
        <div class="scan-box" style="margin-bottom:10px">
          <h4>2. Portail Principal AngularJS (https://wassitonline.anem.dz/)</h4>
          <p>• Récup. Compte Existant : <code>POST /Account/RegisterExisingApplicant</code></p>
          <p>• Vérif. État Civil : <code>POST /api/v1/applicant/existance/</code></p>
          <p>• 6 Wilayas Pilotes V2 : <code>06 Béjaïa, 20 Saïda, 22 Sidi Bel Abbès, 24 Guelma, 29 Mascara, 43 Mila</code></p>
        </div>
        <div class="scan-box">
          <h4>3. Nouvelle Plateforme Wassit Online 2 (Wilayas V2)</h4>
          <p>• Portail Demandeur : <code>https://applicant.anem.dz</code></p>
          <p>• API Prolongation V2 : <code>POST /Profil/ExtendProfilValidityPeriod</code></p>
          <p>• Attestation V2 : <code>GET /Reports/AttestationInscriptionDemReport/...</code></p>
        </div>
      </div>
    </div>

    <!-- RIGHT COLUMN: CLIENT DATABASE & EXPIRY ALERTS -->
    <div>
      <div class="card">
        <div class="card-title">
          <span>🗂️ Base de Données des Clients du Magasin (Suivi des 6 Mois)</span>
          <span id="table-count" style="font-size:12.5px; color:var(--muted)">0 clients</span>
        </div>

        <div class="toolbar">
          <div class="filter-tabs">
            <button class="tab active" onclick="setFilter('all', this)">Tous</button>
            <button class="tab" onclick="setFilter('due', this)">⚠️ À Renouveler (≤15j / Expirés)</button>
            <button class="tab" onclick="setFilter('active', this)">🟢 Actifs (&gt;15j)</button>
            <button class="tab" onclick="setFilter('minha', this)">💳 Minha (منحة البطالة)</button>
            <button class="tab" onclick="setFilter('v2', this)">🟣 Wassit V2</button>
          </div>
          <div style="min-width:260px; flex:1; max-width:360px">
            <input type="text" id="search-box" placeholder="🔍 Rechercher Nom, NIN, N° Wassit, Tél..." oninput="renderTable()">
          </div>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Client / Wilaya</th>
                <th>NIN (18 Chiffres) & N° Wassit</th>
                <th>Téléphone</th>
                <th>Dernier Renouv.</th>
                <th>Expiration (6 Mois)</th>
                <th>Statut ANEM</th>
                <th>Actions Guichet</th>
              </tr>
            </thead>
            <tbody id="clients-tbody"></tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</div>

<script>
let allClients = [];
let currentFilter = 'all';

function updateNinCount() {
  const v = document.getElementById('f-nin').value.trim();
  const el = document.getElementById('nin-counter');
  el.textContent = v.length + ' / 18';
  el.style.color = (v.length === 18 && /^\d+$/.test(v)) ? '#10b981' : '#f59e0b';
}

function clearForm() {
  document.getElementById('f-name').value = '';
  document.getElementById('f-nin').value = '';
  document.getElementById('f-wassit').value = '';
  document.getElementById('f-phone').value = '';
  document.getElementById('f-exp').value = '';
  document.getElementById('f-notes').value = '';
  document.getElementById('f-minha').checked = true;
  updateNinCount();
}

function fillFormClient(id) {
  const c = allClients.find(x => x.id === id);
  if (!c) return;
  document.getElementById('f-name').value = c.full_name;
  document.getElementById('f-nin').value = c.nin;
  document.getElementById('f-wassit').value = c.numero_wassit;
  document.getElementById('f-phone').value = c.phone;
  document.getElementById('f-wilaya').value = c.wilaya;
  document.getElementById('f-platform').value = c.platform || 'V1';
  document.getElementById('f-minha').checked = !!c.is_minha;
  document.getElementById('f-notes').value = c.notes || '';
  updateNinCount();
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

async function loadClients() {
  const res = await fetch('/api/clients');
  allClients = await res.json();
  updateKpis();
  renderTable();
}

function updateKpis() {
  let active = 0, due = 0, minha = 0, rev = 0, ops = 0;
  for (const c of allClients) {
    if (c.days_remaining <= 15) due++;
    else active++;
    if (c.is_minha) minha++;
    rev += (c.total_paid_dzd || 0);
    ops += (c.renewal_count || 0);
  }
  document.getElementById('kpi-total').textContent = allClients.length;
  document.getElementById('kpi-active').textContent = active;
  document.getElementById('kpi-due').textContent = due;
  document.getElementById('kpi-minha').textContent = minha;
  document.getElementById('kpi-revenue').textContent = rev + ' DA';
  document.getElementById('kpi-ops').textContent = ops + ' renouvellements effectués';
}

function setFilter(f, btn) {
  currentFilter = f;
  document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
  btn.classList.add('active');
  renderTable();
}

function renderTable() {
  const q = document.getElementById('search-box').value.trim().toLowerCase();
  const tbody = document.getElementById('clients-tbody');
  tbody.innerHTML = '';

  const filtered = allClients.filter(c => {
    if (currentFilter === 'due' && c.days_remaining > 15) return false;
    if (currentFilter === 'active' && c.days_remaining <= 15) return false;
    if (currentFilter === 'minha' && !c.is_minha) return false;
    if (currentFilter === 'v2' && c.platform !== 'V2') return false;
    if (q) {
      const hay = (c.full_name + ' ' + c.nin + ' ' + c.numero_wassit + ' ' + c.phone + ' ' + c.wilaya).toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });

  // Sort by days_remaining ascending so urgent renewals appear first
  filtered.sort((a, b) => a.days_remaining - b.days_remaining);
  document.getElementById('table-count').textContent = filtered.length + ' client(s) affiché(s)';

  for (const c of filtered) {
    const tr = document.createElement('tr');
    let expBadge = '';
    if (c.days_remaining < 0) {
      expBadge = `<span class="badge badge-exp">Expiré (${c.days_remaining}j)</span>`;
    } else if (c.days_remaining <= 15) {
      expBadge = `<span class="badge badge-warn">⚠️ J-${c.days_remaining}</span>`;
    } else {
      expBadge = `<span class="badge badge-ok">J-${c.days_remaining}</span>`;
    }

    const minhaTag = c.is_minha ? `<span class="badge badge-minha">Minha</span>` : '';
    const platTag = c.platform === 'V2' ? `<span class="badge badge-v2">V2</span>` : `<span class="badge" style="background:#1e293b;color:#94a3b8">V1</span>`;

    // WhatsApp message in Arabic & French for shop reminder
    const waPhone = c.phone && c.phone.startsWith('0') ? '213' + c.phone.slice(1) : c.phone;
    const waText = encodeURIComponent(
      `السلام عليكم ${c.full_name}، نذكركم بأن صلاحية بطاقة طلب العمل (ANEM / منحة البطالة) رقم ${c.numero_wassit} تنتهي بتاريخ ${c.expiration_date}. يمكنكم المرور إلى المحل لتجديدها لمدة 6 أشهر.`
    );
    const waUrl = waPhone ? `https://wa.me/${waPhone}?text=${waText}` : '#';

    tr.innerHTML = `
      <td>
        <div style="font-weight:700; color:#f8fafc">${escapeHtml(c.full_name)}</div>
        <div style="font-size:11.5px; color:#94a3b8">${escapeHtml(c.wilaya)} ${minhaTag} ${platTag}</div>
      </td>
      <td class="mono">
        <div style="color:#38bdf8; font-weight:600">NIN: ${escapeHtml(c.nin)}</div>
        <div style="color:#cbd5e1; font-size:12px">Wassit: ${escapeHtml(c.numero_wassit)} <button class="btn-xs" style="padding:1px 5px" onclick="copyText('${escapeHtml(c.nin)}','${escapeHtml(c.numero_wassit)}')">📋 Copier</button></div>
      </td>
      <td class="mono">${escapeHtml(c.phone || '—')}</td>
      <td>
        <div>${escapeHtml(c.last_renewal)}</div>
        <div style="font-size:11px; color:#94a3b8">${c.renewal_count}x (${c.total_paid_dzd} DA)</div>
      </td>
      <td>
        <div style="font-weight:700">${escapeHtml(c.expiration_date)}</div>
        <div style="margin-top:3px">${expBadge}</div>
      </td>
      <td>
        <div style="font-weight:600; font-size:12px">${escapeHtml(c.last_status_label)}</div>
        <div style="font-size:11px; color:#94a3b8">${escapeHtml(c.notes || '')}</div>
      </td>
      <td>
        <div class="row-actions">
          <button class="btn-xs" title="Charger au guichet" onclick="fillFormClient(${c.id})">✏️ Guichet</button>
          <button class="btn-xs" style="border-color:#0ea5e9;color:#38bdf8" onclick="quickRenewClient(${c.id})">⚡ Prolonger</button>
          <a class="btn-xs" href="/api/attestation?nin=${encodeURIComponent(c.nin)}&numero_wassit=${encodeURIComponent(c.numero_wassit)}" target="_blank">📄 PDF</a>
          <a class="btn-xs" style="border-color:#8b5cf6;color:#c4b5fd" href="/api/receipt?id=${c.id}" target="_blank">🖨️ Reçu</a>
          ${c.phone ? `<a class="btn-xs" style="border-color:#10b981;color:#34d399" href="${waUrl}" target="_blank">💬 WhatsApp</a>` : ''}
          <button class="btn-xs" style="color:#fca5a5" onclick="deleteClient(${c.id})">🗑️</button>
        </div>
      </td>
    `;
    tbody.appendChild(tr);
  }
}

function escapeHtml(s) {
  return String(s || '').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
}

function copyText(nin, wassit) {
  navigator.clipboard.writeText(nin + '\t' + wassit);
  showBanner('success', 'Copié dans le presse-papiers !', `NIN: ${nin} | N° Wassit: ${wassit}`, 'تم نسخ رقم التعريف الوطني ورقم الوسيط!');
}

function showBanner(state, titleFr, detailFr, textAr) {
  const b = document.getElementById('result-banner');
  b.style.display = 'block';
  b.className = 'result-banner ' + (state === 'success' ? 'success' : state === 'error' ? 'error' : 'warning');
  document.getElementById('res-title').textContent = titleFr;
  document.getElementById('res-detail').textContent = detailFr;
  document.getElementById('res-ar').textContent = textAr || '';
}

async function submitRenewal(mode) {
  const params = new URLSearchParams();
  params.set('mode', mode);
  params.set('full_name', document.getElementById('f-name').value);
  params.set('nin', document.getElementById('f-nin').value);
  params.set('numero_wassit', document.getElementById('f-wassit').value);
  params.set('phone', document.getElementById('f-phone').value);
  params.set('wilaya', document.getElementById('f-wilaya').value);
  params.set('fee_dzd', document.getElementById('f-fee').value);
  params.set('platform', document.getElementById('f-platform').value);
  params.set('expiration_date', document.getElementById('f-exp').value);
  params.set('notes', document.getElementById('f-notes').value);
  params.set('is_minha', document.getElementById('f-minha').checked ? 'true' : 'false');

  showBanner('warning', '⏳ Traitement en cours...', 'Communication avec wassitonline.anem.dz et mise à jour de la base locale...', 'جاري الاتصال بمنصة وسيط وتحديث قاعدة البيانات...');

  const res = await fetch('/api/renew', {
    method: 'POST',
    headers: {'Content-Type': 'application/x-www-form-urlencoded'},
    body: params.toString()
  });
  const data = await res.json();
  showBanner(data.state, `[${data.badge}] ${data.title_fr}`, data.detail_fr, `${data.title_ar} — ${data.detail_ar}`);
  await loadClients();
}

async function quickRenewClient(id) {
  fillFormClient(id);
  await submitRenewal('live');
}

function downloadPdfFromForm() {
  const nin = document.getElementById('f-nin').value.trim();
  const wassit = document.getElementById('f-wassit').value.trim();
  if (!nin || !wassit) {
    showBanner('error', 'Champs manquants', 'Veuillez saisir le NIN (18 chiffres) et le N° Wassit pour télécharger l\'attestation PDF.', 'يرجى إدخال رقم التعريف الوطني ورقم الوسيط أولاً.');
    return;
  }
  window.open(`/api/attestation?nin=${encodeURIComponent(nin)}&numero_wassit=${encodeURIComponent(wassit)}`, '_blank');
}

async function submitChangeMobile() {
  const nin = document.getElementById('f-nin').value.trim();
  const wassit = document.getElementById('f-wassit').value.trim();
  const mobile = document.getElementById('f-phone').value.trim();
  const params = new URLSearchParams({ nin, numero_wassit: wassit, mobile });
  showBanner('warning', '⏳ Changement du numéro mobile...', 'Envoi de la requête vers /api/applicant/ChangerMobile...', 'جاري تغيير رقم الهاتف...');
  const res = await fetch('/api/change-mobile', {
    method: 'POST',
    headers: {'Content-Type': 'application/x-www-form-urlencoded'},
    body: params.toString()
  });
  const data = await res.json();
  showBanner(data.state, `[${data.badge}] ${data.title_fr}`, data.detail_fr, `${data.title_ar} — ${data.detail_ar}`);
  await loadClients();
}

async function submitRecoverAccount() {
  const wassit = (document.getElementById('rec-wassit').value || document.getElementById('f-wassit').value).trim();
  const email = document.getElementById('rec-email').value.trim();
  const password = document.getElementById('rec-pass').value;
  if (!wassit || !email || !password) {
    showBanner('error', 'Champs manquants', 'Veuillez saisir le N° Wassit, le nouvel Email et le Mot de passe.', 'يرجى إدخال رقم الوسيط والبريد الإلكتروني وكلمة المرور.');
    return;
  }
  const params = new URLSearchParams({
    numero_wassit: wassit,
    email,
    password,
    force_replacement: 'true'
  });
  showBanner('warning', '⏳ Rattachement du compte en ligne...', 'Envoi vers POST /Account/RegisterExisingApplicant (ForceReplacement=true)...', 'جاري ربط رقم الوسيط بالبريد الإلكتروني الجديد...');
  const res = await fetch('/api/recover-account', {
    method: 'POST',
    headers: {'Content-Type': 'application/x-www-form-urlencoded'},
    body: params.toString()
  });
  const data = await res.json();
  showBanner(data.state, `[${data.badge}] ${data.title_fr}`, data.detail_fr, `${data.title_ar} — ${data.detail_ar}`);
}

async function deleteClient(id) {
  if (!confirm('Supprimer ce client de la base de données du magasin ?')) return;
  const params = new URLSearchParams({ id: String(id) });
  await fetch('/api/delete', {
    method: 'POST',
    headers: {'Content-Type': 'application/x-www-form-urlencoded'},
    body: params.toString()
  });
  await loadClients();
}

loadClients();
</script>
</body>
</html>
"#.to_string()
}

fn build_receipt_html(c: &ClientRecord) -> String {
    format!(
        r#"<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="UTF-8">
<title>Reçu de Prolongation ANEM — {}</title>
<style>
  body {{ font-family: 'Segoe UI', Tahoma, sans-serif; background: #f1f5f9; color: #0f172a; padding: 24px; }}
  .ticket {{
    max-width: 420px; margin: 0 auto; background: #fff; border: 2px dashed #334155;
    border-radius: 12px; padding: 22px; box-shadow: 0 8px 24px rgba(0,0,0,0.08);
  }}
  .head {{ text-align: center; border-bottom: 2px solid #0f172a; padding-bottom: 12px; margin-bottom: 14px; }}
  .head h2 {{ margin: 0; font-size: 18px; }}
  .head p {{ margin: 4px 0 0; font-size: 13px; color: #475569; font-weight: 600; }}
  .row {{ display: flex; justify-content: space-between; padding: 7px 0; border-bottom: 1px solid #e2e8f0; font-size: 13.5px; }}
  .row strong {{ color: #0f172a; }}
  .exp-box {{
    margin-top: 14px; padding: 12px; background: #ecfdf5; border: 2px solid #10b981;
    border-radius: 8px; text-align: center;
  }}
  .exp-box .lbl {{ font-size: 12px; color: #047857; font-weight: 700; text-transform: uppercase; }}
  .exp-box .dt {{ font-size: 22px; font-weight: 900; color: #065f46; margin: 4px 0; }}
  .foot {{ text-align: center; font-size: 11.5px; color: #64748b; margin-top: 14px; }}
  .print-btn {{
    display: block; width: 100%; margin-top: 16px; padding: 10px; background: #0ea5e9;
    color: #fff; font-weight: 700; border: none; border-radius: 8px; cursor: pointer; font-size: 14px;
  }}
  @media print {{ .print-btn {{ display: none; }} body {{ background: #fff; padding: 0; }} .ticket {{ box-shadow: none; border: 1px solid #000; }} }}
</style>
</head>
<body>
<div class="ticket">
  <div class="head">
    <h2>وصل تجديد بطاقة طلب العمل (ANEM)</h2>
    <p>GUICHET SERVICES NUMÉRIQUES — WASSIT.DZ</p>
  </div>
  <div class="row"><span>Client / الاسم واللقب :</span><strong>{}</strong></div>
  <div class="row"><span>NIN (18 chiffres) :</span><strong>{}</strong></div>
  <div class="row"><span>N° Wassit (ANEM) :</span><strong>{}</strong></div>
  <div class="row"><span>Téléphone / الهاتف :</span><strong>{}</strong></div>
  <div class="row"><span>Wilaya / الولاية :</span><strong>{}</strong></div>
  <div class="row"><span>Allocation Minha :</span><strong>{}</strong></div>
  <div class="row"><span>Date de prolongation :</span><strong>{}</strong></div>
  <div class="exp-box">
    <div class="lbl">Prochaine Date d'Expiration (6 Mois) / تاريخ انتهاء الصلاحية</div>
    <div class="dt">{}</div>
    <div style="font-size:12px;color:#047857">يرجى تجديد الطلب قبل انتهاء هذا التاريخ لتفادي توقيف المنحة</div>
  </div>
  <div class="foot">
    Fiche Client #{} • Statut: {}
  </div>
  <button class="print-btn" onclick="window.print()">🖨️ Imprimer le Reçu Client</button>
</div>
</body>
</html>"#,
        c.full_name,
        c.full_name,
        c.nin,
        c.numero_wassit,
        c.phone,
        c.wilaya,
        if c.is_minha { "OUI (منحة البطالة)" } else { "NON" },
        c.last_renewal,
        c.expiration_date,
        c.id,
        c.last_status_label
    )
}
