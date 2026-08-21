# PRD — Synapse IIoT

**Industrial IoT Gateway & Mini-SCADA** · pengganti SCADA untuk pemantauan dan penarikan data
berbasis web.

| | |
|---|---|
| Versi dokumen | 1.0 |
| Status | disetujui untuk implementasi |
| Backend | ASP.NET Core 10 · EF Core · SignalR |
| Frontend | React 19 · Vite · Tailwind v4 · Zustand |
| Database | **PostgreSQL 16 + TimescaleDB** (keputusan §11 blueprint) |
| Lingkup kendali | **Read-only** untuk Fase 0–3 |
| Skala target | 200–2.000 tag, scan 500 ms – 1 s |
| Dokumen pendamping | Blueprint rancangan sistem (artifact), `Synapse_IIoT_FE/SKILL.md`, `Synapse_IIoT_FE/references/architecture.md` |

---

## 1. Ringkasan

Synapse menarik data dari perangkat lapangan (PLC, RTU, meter, sensor) lewat protokol industri,
menormalkannya menjadi **tag** bersatuan teknis, menyimpannya ke **historian** runtun waktu tanpa
kehilangan sampel, dan menyajikannya lewat antarmuka web: dasbor realtime, tren historis, alarm,
dan ekspor ke sistem lain.

### 1.1 Masalah yang diselesaikan

| Masalah hari ini | Akibat |
|---|---|
| SCADA konvensional terikat lisensi, workstation Windows, dan vendor tertentu | Biaya per titik data mahal; akses hanya dari ruang kontrol |
| Penarikan data untuk laporan dilakukan manual atau lewat ekspor SCADA | Laporan terlambat, rentan salah salin, tidak bisa diotomasi |
| Data historis terkunci di format proprietary | Sulit dipakai analitik, MES, atau ERP |
| Penambahan satu tag baru butuh vendor / lisensi tambahan | Perubahan proses kalah cepat dari kebutuhan data |

### 1.2 Tujuan terukur

| # | Tujuan | Ukuran keberhasilan |
|---|---|---|
| G1 | Akuisisi data tanpa kehilangan sampel | Setiap sampel yang berhasil diakuisisi sampai ke historian **tepat satu kali**, atau tercatat di `acquisition_gap` dengan alasan |
| G2 | Penambahan tag tanpa henti layanan | Tag/perangkat baru aktif < 2 detik setelah disimpan, **tanpa restart** proses |
| G3 | Pemetaan sumber data tanpa mengetik JSONPath | Operator memilih key dari daftar hasil deteksi otomatis; ≥ 90% tag dibuat lewat pemilih, bukan diketik |
| G4 | Akses dari mana saja di jaringan pabrik | Browser modern, tanpa instalasi klien |
| G5 | Tren historis dapat dikueri cepat | Grafik 1 tahun (1 tag) tampil < 2 detik lewat rollup |

---

## 2. Lingkup

### 2.1 Termasuk

- Akuisisi multi-protokol: Modbus TCP/RTU, OPC UA, MQTT, HTTP (bertahap sesuai §9)
- Deteksi otomatis struktur data (**key discovery**) untuk HTTP dan MQTT
- Tag engine: penskalaan raw→EU, quality code, dua timestamp
- Worker akuisisi yang menyesuaikan diri saat konfigurasi berubah
- Buffer tahan-mati (WAL) + penulisan batch idempoten
- Historian runtun waktu + rollup + retensi
- Dasbor realtime, tren historis multi-tag
- Alarm & event dengan acknowledgement
- Ekspor/proyeksi data ke tabel tujuan (Storage Flow) + laporan terjadwal
- Manajemen pengguna, peran, audit
- Kesehatan sistem: status perangkat, gap, kedalaman buffer, denyut gateway

### 2.2 Tidak termasuk

| Tidak termasuk | Alasan |
|---|---|
| **Kendali / tulis-balik ke perangkat** | Diputuskan read-only untuk Fase 0–3. Skema tag menyimpan `AccessMode` tapi API menolak operasi tulis |
| Loop kendali (PID), interlock, fungsi trip | Tinggal di PLC dan SIS. Synapse tidak boleh berada di loop keselamatan |
| **Editor mimic/HMI penuh** (gambar P&ID beranimasi) | Lihat §2.3 — dijawab terpisah karena ini pertanyaan yang sering muncul |
| Konfigurasi/pemrograman PLC | Di luar lingkup; Synapse hanya membaca |
| Analitik prediktif / ML | Data historian terbuka, jadi bisa dikerjakan sistem lain |

### 2.3 Soal "desain semacam SCADA" — apakah sudah termasuk?

**Belum, dan ini disengaja.** Yang biasanya orang bayangkan saat menyebut "tampilan SCADA" adalah
**mimic diagram**: gambar P&ID atau layout pabrik, dengan nilai dan status yang menempel pada
gambar itu (tangki terisi sesuai level, motor berubah warna saat jalan, pipa berkedip saat
mengalir). Itu adalah modul tersendiri yang lingkupnya besar: kanvas editor, pustaka simbol ISA,
pengikatan elemen ke tag, aturan animasi, layer, dan versi gambar.

Apa yang **sudah** ada di lingkup Fase 0–3:

| Sudah termasuk | Belum termasuk |
|---|---|
| Dasbor angka realtime per perangkat/tag | Kanvas mimic yang bisa digambar sendiri |
| Tren historis multi-tag dengan resolusi otomatis | Simbol ISA (valve, pump, tank) beranimasi |
| Daftar status perangkat & alarm aktif | Denah pabrik interaktif berlapis |
| Diagram alur data OT→IT (statis) | Faceplate per peralatan |

Rekomendasi saya: **jangan langsung membangun editor mimic.** Ada jalan tengah yang memenuhi
~80% kebutuhan pemantauan dengan biaya jauh lebih kecil, dan itu saya usulkan sebagai
**Fase 5A — Dashboard Builder** (§9): pengguna menyusun halaman pantau dari widget yang di-*drag*
ke grid — angka besar, gauge, lampu status, bar level, sparkline, tabel tag — lalu mengikat tiap
widget ke tag. Tidak ada gambar bebas, tapi seluruh informasi operasional terbaca sekilas dan bisa
ditampilkan di layar besar ruang kontrol.

Editor mimic penuh (**Fase 5B**) baru masuk kalau setelah Fase 5A masih terasa kurang — dan pada
titik itu keputusannya sudah berdasarkan pemakaian nyata, bukan bayangan.

> **Keputusan yang dibutuhkan:** Fase 5A cukup, atau Fase 5B tetap diperlukan? Ini tidak
> memblokir Fase 0–3.

---

## 3. Persona & peran

| Peran | Siapa | Kebutuhan utama | Hak |
|---|---|---|---|
| **VIEWER** | Manajemen, supervisor produksi | Lihat dasbor, tren, laporan | Baca semua data; tanpa ubah konfigurasi |
| **OPERATOR** | Operator ruang kontrol | Pantau realtime, ack alarm, ekspor data shift | Baca + ack alarm + jalankan ekspor |
| **ENGINEER** | Teknisi instrumentasi / IT OT | Tambah perangkat & tag, atur skala, atur alarm | Semua di atas + ubah konfigurasi |
| **ADMIN** | Penanggung jawab sistem | Kelola pengguna, retensi, integrasi | Semua + hapus + kelola pengguna |

Aturan yang sudah berjalan di frontend: `useCanWrite()` = ADMIN/ENGINEER, `useCanDelete()` = ADMIN.

---

## 4. Model data

Perombakan skema dilakukan (disetujui). Yang berubah secara mendasar: **nilai runtun waktu pindah
ke model tag-value**, dan tabel dinamis buatan pengguna turun peran menjadi tujuan **proyeksi**.

### 4.1 Tabel konfigurasi

| Tabel | Isi | Perubahan dari sekarang |
|---|---|---|
| `users` | Akun, peran, hash password | — |
| `devices` | Perangkat + `connection_config` (JSONB) + `protocol` + status koneksi | `polling_interval` → `scan_class_id`; tambah `last_connect_at`, `last_error`, `consecutive_failures` |
| `tags` | Definisi titik ukur | **Banyak tambahan** — lihat §4.2 |
| `scan_classes` | Kelas laju: nama + interval ms | **Baru** |
| `master_tables`, `master_table_fields` | Skema tabel tujuan proyeksi | Kuoting identifier Postgres |
| `storage_flows`, `storage_flow_devices`, `storage_flow_mappings` | Definisi proyeksi/ekspor | Sumbernya berubah: dari historian, bukan dari perangkat langsung |
| `alarm_rules` | Aturan alarm per tag | **Baru** |
| `audit_logs` | Jejak perubahan & akses | Tambah audit perubahan konfigurasi tag/device |
| `dashboards`, `dashboard_widgets` | Halaman pantau susunan pengguna (Fase 5A) | **Baru** |

### 4.2 `tags` — kolom lengkap

```
id                uuid PK
device_id         uuid FK
name              varchar(100)         -- Suhu_Oven_A
description       varchar(255) null

-- alamat sumber: satu bentuk per protokol
address           varchar(500)         -- Modbus: "40001" · OPC UA: NodeId · HTTP/MQTT: JSONPath "$.data.temp"
source_topic      varchar(500) null    -- MQTT saja: topik asal (satu broker = banyak topik)
register_type     smallint null        -- Modbus: holding/input/coil/discrete
word_order        smallint null        -- Modbus: urutan word untuk 32-bit (BE/LE/word-swap)

data_type         smallint             -- BOOLEAN/INT16/UINT16/INT32/UINT32/FLOAT/STRING
access_mode       smallint             -- READONLY (READWRITE ditolak API sampai Fase 4)
unit              varchar(20)

-- penskalaan linier
is_scaled         boolean
raw_min           double null
raw_max           double null
eu_min            double null
eu_max            double null

-- akuisisi & penyimpanan
scan_class_id     uuid FK              -- 500ms / 1s / 5s / 1m
store_mode        smallint             -- FULL (default) | DEADBAND | ON_CHANGE
deadband_abs      double null          -- salah satu dipakai
deadband_pct      double null
max_store_gap_ms  int null             -- simpan paksa tiap N ms walau tak berubah

is_enabled        boolean
created_at, updated_at, deleted_at
```

### 4.3 Tabel runtun waktu (TimescaleDB)

| Tabel | Bentuk | Kebijakan |
|---|---|---|
| `tag_current` | 1 baris/tag: `value_num`, `value_bool`, `value_text`, `quality`, `source_ts`, `gateway_ts`, `seq` | Cermin RTDB di memori; ditulis berkala, bukan tiap sampel |
| `tag_history` | **hypertable**: `tag_id`, `source_ts`, `value_num`, `value_bool`, `value_text`, `quality`; `UNIQUE(tag_id, source_ts)` | chunk 6 jam · compress > 7 hari · retensi mentah 90 hari |
| `tag_history_1m`, `tag_history_1h` | Continuous aggregate: min/max/avg/count + `bad_quality_ms` | Retensi 5 tahun |
| `acquisition_gap` | `device_id`, `from_ts`, `to_ts`, `reason`, `estimated_samples` | Append-only, tidak pernah dihapus otomatis |
| `alarm_events` | `alarm_rule_id`, `tag_id`, `state`, `priority`, `trigger_value`, `raised_at`, `cleared_at`, `acked_at`, `acked_by` | Append-only kecuali kolom ack |

### 4.4 Quality code

```
0 GOOD        nilai sah dan baru
1 UNCERTAIN   nilai sah tapi diragukan (di luar rentang skala, konversi meragukan)
2 BAD         gagal baca / timeout / perangkat mati
3 STALE       nilai lama, belum diperbarui melewati batas timeout
```

Aturan wajib: **nilai basi tidak pernah dikirim sebagai GOOD**, dan UI menggambar rentang non-GOOD
sebagai garis terputus, bukan garis lurus.

---

## 5. Modul & fitur

Penomoran `FR-<modul>-<n>` dipakai sebagai rujukan di issue tracker.

### M1 — Autentikasi & pengguna

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-AUTH-1 | Login cookie HTTP-only | Sudah berjalan; cookie `JWT-TOKEN`, umur 1 jam | ✅ |
| FR-AUTH-2 | Verifikasi sesi | `GET /auth/info` dipanggil setiap masuk halaman terproteksi | ✅ |
| FR-AUTH-3 | Registrasi | Default peran VIEWER; tidak otomatis login | ✅ |
| FR-AUTH-4 | Refresh token / perpanjang sesi | Sesi 1 jam terlalu pendek untuk shift 8 jam — perlu sliding expiration atau refresh token | 0 |
| FR-AUTH-5 | Ganti password + paksa ganti pada login pertama | | 3 |
| FR-AUTH-6 | Manajemen pengguna (CRUD, aktif/nonaktif, set peran) | Halaman khusus ADMIN | 3 |

### M2 — Perangkat

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-DEV-1 | CRUD perangkat per protokol | Form konfigurasi berganti mengikuti protokol | ✅ |
| FR-DEV-2 | Uji koneksi sebelum simpan | HTTP sudah ada; ditambah Modbus, OPC UA, MQTT | 1 |
| FR-DEV-3 | Aktif/nonaktif perangkat | Nonaktif = worker berhenti menghubunginya | ✅ |
| FR-DEV-4 | Status koneksi realtime | Online/offline/gagal + `consecutive_failures` + pesan error terakhir | 0 |
| FR-DEV-5 | Riwayat koneksi | Kapan tersambung/terputus, durasi outage — dari `acquisition_gap` | 1 |
| FR-DEV-6 | Duplikat perangkat | Salin konfigurasi + seluruh tag; menghemat waktu untuk 10 mesin identik | 2 |
| FR-DEV-7 | Impor/ekspor konfigurasi perangkat+tag (JSON/CSV) | Untuk memindahkan konfigurasi antar gateway | 2 |

### M3 — Peta Sumber & Key Discovery ⭐

**Modul yang paling menentukan pengalaman pengguna.** Tujuannya satu: operator tidak pernah perlu
mengetik JSONPath. Detail alur dan wireframe di §6.

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-MAP-1 | **Probe HTTP** | Panggil endpoint sekali, kembalikan status, latensi, payload mentah, dan daftar key hasil pendataran | 0 |
| FR-MAP-2 | **Sniff MQTT** | Sambung broker, langganan filter topik, kumpulkan pesan selama 5–30 detik, kembalikan daftar topik + key per topik | 0 |
| FR-MAP-3 | Pendataran JSON → daftar key | `$.data.temp` + tipe tersimpulkan + contoh nilai; array ditangani eksplisit | 0 |
| FR-MAP-4 | Payload non-JSON | Angka/teks polos pada satu topik → satu key `$` (nilai mentah) | 0 |
| FR-MAP-5 | **Pemilih key** dua panel | Pohon key dengan checkbox → daftar tag terpilih yang bisa diedit massal | 0 |
| FR-MAP-6 | Saran nama tag otomatis | `$.data.oven_temp` → `Oven_Temp`; bentrok nama diberi akhiran | 0 |
| FR-MAP-7 | Simpulkan tipe & satuan | Angka desimal → FLOAT; boolean → BOOLEAN; teks → STRING. Satuan ditebak dari nama key (`_c`, `_celsius`, `_bar`, `_kwh`) sebagai **saran**, bukan keputusan | 0 |
| FR-MAP-8 | Buat tag massal | Satu klik membuat N tag dari key terpilih | 0 |
| FR-MAP-9 | Deteksi perubahan struktur | Bandingkan key hasil probe dengan tag yang sudah ada: tandai key baru, key hilang, tipe berubah | 2 |
| FR-MAP-10 | Browse OPC UA address space | Pohon node dari server → pilih node → jadi tag (padanan pemilih key untuk OPC UA) | 1 |
| FR-MAP-11 | Impor peta register Modbus (CSV) | Format vendor umum: nama, register, tipe, skala, satuan | 1 |

### M4 — Tag

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-TAG-1 | CRUD tag | Sudah ada; ditambah kolom §4.2 | ✅ |
| FR-TAG-2 | Penskalaan raw→EU | Berlaku seragam untuk semua protokol dan tipe, dievaluasi di tag engine | 0 |
| FR-TAG-3 | Pemilihan scan class | Bukan interval bebas per tag — cegah 500 timer berbeda | 0 |
| FR-TAG-4 | Mode simpan per tag | FULL (default) / DEADBAND / ON_CHANGE + `max_store_gap_ms` | 0 |
| FR-TAG-5 | Edit massal | Pilih N tag → ubah scan class / mode simpan / satuan sekaligus | 1 |
| FR-TAG-6 | Uji baca satu tag | Baca sekarang, tampilkan raw + hasil skala + quality — untuk memverifikasi konfigurasi | 1 |
| FR-TAG-7 | Nonaktifkan tag tanpa hapus | Berhenti diakuisisi, sejarahnya tetap ada | 0 |
| FR-TAG-8 | Tag turunan (kalkulasi) | Ekspresi antar tag: `(A+B)/2`, delta, laju. Dievaluasi di tag engine | 4 |

### M5 — Worker akuisisi ⭐

Permintaan eksplisit: worker harus **ikut menyesuaikan saat ada penambahan tag atau perangkat**.
Detail teknis di §7.

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-WRK-1 | Scan scheduler berbasis scan class | Satu `PeriodicTimer` per kelas, bukan per perangkat | 0 |
| FR-WRK-2 | Kompensasi drift | Periode diukur dari jadwal, bukan dari selesainya pekerjaan | 0 |
| FR-WRK-3 | **Rencana ulang otomatis (hot reload)** | Tag/perangkat/scan class berubah → rencana akuisisi disusun ulang < 2 detik, tanpa restart | 0 |
| FR-WRK-4 | **Debounce perubahan** | Membuat 50 tag sekaligus memicu **satu** kali rencana ulang, bukan 50 | 0 |
| FR-WRK-5 | Pengelompokan blok register Modbus | Tag berdampingan digabung jadi satu permintaan (maks 125 register) | 1 |
| FR-WRK-6 | Kontrak `IDeviceDriver` | Satu antarmuka untuk semua protokol; driver push memenuhi kontrak yang sama | 0 |
| FR-WRK-7 | Isolasi kegagalan | Satu perangkat mati tidak menghentikan scan perangkat lain di kelas yang sama | 0 |
| FR-WRK-8 | Backoff koneksi | Gagal berulang → jeda bertambah (1s, 2s, 5s, 15s, 30s) agar tidak membanjiri perangkat mati | 0 |
| FR-WRK-9 | Batas konkurensi per perangkat | Satu percakapan pada satu waktu per koneksi; RTU: per port serial | 1 |
| FR-WRK-10 | Metrik worker | Sampel/detik, durasi scan aktual vs target, kedalaman antrean, jumlah gagal | 0 |

### M6 — Durabilitas

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-DUR-1 | Antrean berbatas + tekanan eksplisit | Produsen melambat; **tidak pernah membuang diam-diam** | 0 |
| FR-DUR-2 | WAL lokal | Sampel di-ack setelah tertulis durabel ke disk, bukan setelah dikirim ke DB | 0 |
| FR-DUR-3 | Penulisan batch idempoten | 1.000 baris / 500 ms; `ON CONFLICT (tag_id, source_ts) DO NOTHING` | 0 |
| FR-DUR-4 | Replay setelah crash | WAL dibaca ulang saat start; duplikat aman karena idempoten | 0 |
| FR-DUR-5 | Gap ledger | 3 scan gagal berurutan → buka gap; sambung kembali → tutup gap | 0 |
| FR-DUR-6 | Pemantauan kapasitas WAL | Sisa ruang disk jadi tag terpantau; ambang peringatan 70%, kritis 90% | 1 |
| FR-DUR-7 | Backfill dari buffer sumber | OPC UA queue / MQTT QoS 1 tertahan diterima dan ditulis dengan `source_ts` aslinya | 1 |

### M7 — Historian & tren

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-HIS-1 | Hypertable + partisi otomatis | chunk 6 jam | 2 |
| FR-HIS-2 | Kompresi | Setelah 7 hari, 10–20× | 2 |
| FR-HIS-3 | Rollup 1 menit & 1 jam | Continuous aggregate, mutakhir sendiri | 2 |
| FR-HIS-4 | Kebijakan retensi | Mentah 90 hari, rollup 5 tahun; dapat diatur per tag | 2 |
| FR-HIS-5 | API kueri rentang | `?tagIds=&from=&to=&resolution=auto` → memilih mentah/1m/1h sesuai lebar rentang | 2 |
| FR-HIS-6 | Tren multi-tag | Sampai 8 tag, sumbu ganda, zoom & pan, penanda jeda quality | 2 |
| FR-HIS-7 | Ekspor tren tampil | CSV/XLSX sesuai rentang & resolusi yang sedang dilihat | 2 |
| FR-HIS-8 | Perbandingan periode | Shift ini vs shift lalu, hari ini vs kemarin | 3 |

### M8 — Realtime & dasbor

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-RT-1 | Frame gabungan | Coalesce 250 ms; banyak tag per pesan; format kompak `[tagId, value, quality]` | 0 |
| FR-RT-2 | Nomor urut + tambalan | `seq` per frame; reconnect → `GET /history?since=` | 0 |
| FR-RT-3 | Langganan per tag-set | Klien mendaftarkan tag yang sedang tampil; hentikan `Clients.All` | 0 |
| FR-RT-4 | Indikator gateway | Terhubung/terputus di header (sudah ada) | ✅ |
| FR-RT-5 | Dasbor ringkas | Jumlah perangkat/tag, yang mengirim data, alarm aktif, kesehatan buffer | 0 |
| FR-RT-6 | Mode kios | Layar penuh tanpa chrome untuk layar besar (sudah ada) | ✅ |

### M9 — Alarm & event

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-ALM-1 | Aturan batas analog | HI/HIHI/LO/LOLO per tag | 3 |
| FR-ALM-2 | Aturan laju perubahan & penyimpangan | Naik/turun terlalu cepat; menyimpang dari nilai acuan | 3 |
| FR-ALM-3 | Aturan status digital | Bit tertentu = alarm | 3 |
| FR-ALM-4 | Quality buruk bertahan | Perangkat diam > N detik = alarm koneksi | 3 |
| FR-ALM-5 | Deadband & tunda per alarm | Cegah banjir alarm dari sinyal bergetar di sekitar batas | 3 |
| FR-ALM-6 | Prioritas 4 tingkat | Kritis/tinggi/sedang/rendah menentukan urutan & kanal notifikasi | 3 |
| FR-ALM-7 | Acknowledgement | Siapa, kapan, catatan | 3 |
| FR-ALM-8 | Shelving berbatas waktu | Untuk pemeliharaan; otomatis aktif kembali | 3 |
| FR-ALM-9 | Log append-only | Tabel terpisah dari nilai; tidak pernah di-update selain kolom ack | 3 |
| FR-ALM-10 | Notifikasi | Email / Telegram / WhatsApp dengan anti-banjir & jam kerja | 3 |
| FR-ALM-11 | Evaluasi di gateway | Alarm tercatat walau tidak ada halaman terbuka | 3 |

### M10 — Ekspor, proyeksi, laporan

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-EXP-1 | Storage Flow jadi proyeksi | Sumbernya historian, bukan perangkat langsung; bisa dijalankan ulang | 2 |
| FR-EXP-2 | Tabel dinamis sebagai tujuan | Skema kolom bebas untuk konsumsi sistem lain (sudah ada) | ✅ |
| FR-EXP-3 | Jadwal proyeksi | Cron sederhana + jalankan manual + riwayat eksekusi | 2 |
| FR-EXP-4 | Ekspor CSV/XLSX | Rentang & tag pilihan | 2 |
| FR-EXP-5 | Laporan shift/harian | Template: min/max/avg per tag per shift, downtime, jumlah alarm | 3 |
| FR-EXP-6 | Webhook / push ke MES-ERP | POST hasil proyeksi ke endpoint eksternal dengan retry | 4 |

### M11 — Berkas

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-FIL-1 | Unggah/hapus berkas | `FileController` sudah ada di backend; belum ada UI | 2 |
| FR-FIL-2 | Lampiran pada tabel dinamis | `upload-field` untuk kolom bertipe berkas | 2 |
| FR-FIL-3 | Batas ukuran & tipe dari konfigurasi | `GET /File/config` | 2 |

### M12 — Kesehatan sistem & audit

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-SYS-1 | Halaman kesehatan | Status per perangkat, sampel/detik, kedalaman WAL, lag penulisan, gap terbuka | 1 |
| FR-SYS-2 | Denyut gateway (watchdog) | Tag denyut; berhenti = alarm | 3 |
| FR-SYS-3 | Audit perubahan konfigurasi | Perubahan skala satu tag bisa menggeser seluruh laporan — wajib tertelusur | 1 |
| FR-SYS-4 | Audit akses & aksi | Sudah ada `AuditLog`; tambah pencarian & ekspor | 2 |
| FR-SYS-5 | Log aplikasi terstruktur | Korelasi per perangkat/scan untuk penelusuran | 1 |

### M13 — Pengaturan

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-SET-1 | Scan class | Tambah/ubah kelas laju | 0 |
| FR-SET-2 | Retensi & kompresi | Per sistem, dapat ditimpa per tag | 2 |
| FR-SET-3 | Zona waktu & format | Tampilan lokal, penyimpanan UTC | 1 |
| FR-SET-4 | Notifikasi | Kanal, penerima, jam kerja | 3 |
| FR-SET-5 | Bahasa & tema | ID/EN, terang/gelap (sudah ada) | ✅ |

### M14 — Dashboard builder (Fase 5A) / Mimic (Fase 5B)

| ID | Fitur | Detail | Fase |
|---|---|---|---|
| FR-VIZ-1 | Halaman pantau susunan sendiri | Grid drag-and-drop, simpan per pengguna/bersama | 5A |
| FR-VIZ-2 | Widget: angka besar, gauge, lampu status, bar level, sparkline, tabel tag | Tiap widget diikat ke tag | 5A |
| FR-VIZ-3 | Ambang warna per widget | Hijau/kuning/merah sesuai batas alarm tag | 5A |
| FR-VIZ-4 | Kanvas mimic + simbol ISA + animasi | Editor gambar proses dengan pengikatan tag | 5B |

---

## 6. UX — Pemilihan key untuk HTTP & MQTT ⭐

Bagian ini menjawab langsung: *"pemilihan key untuk entry nilai data pada HTTP atau MQTT"*.

Prinsipnya: **operator memilih dari yang benar-benar dikirim perangkat, bukan mengarang path.**
Sistem yang meminta pengguna mengetik `$.data.sensors[0].temperature` dengan benar dari ingatan
akan salah, dan salahnya baru terlihat berjam-jam kemudian sebagai kolom kosong.

### 6.1 Alur HTTP

```
Langkah 1 — Isi koneksi                    Langkah 2 — Ambil contoh
┌────────────────────────────────────┐     ┌─────────────────────────────────────────┐
│ Nama      : Panel Suhu Line 1      │     │  [ Ambil Contoh Data ]  ← satu klik     │
│ URL       : http://10.1.4.20/api   │     │                                          │
│ Metode    : GET                    │ ──► │  ✓ 200 OK · 43 ms · 512 B               │
│ Header    : (opsional, JSON)       │     │  ✓ 7 key terdeteksi                      │
│ Scan class: 1 detik            ▾   │     │                                          │
└────────────────────────────────────┘     └─────────────────────────────────────────┘
```

```
Langkah 3 — Pemilih key (dua panel)
┌──────────────────────────────────────────┬────────────────────────────────────────────┐
│  STRUKTUR DATA          [Semua] [Angka]  │  TAG YANG AKAN DIBUAT (3)                  │
│                                          │                                            │
│  ▾ $                                     │  ┌──────────────────────────────────────┐  │
│    ▾ data                                │  │ Nama      Oven_Temp                  │  │
│      ☑ temperature   FLOAT      72.4     │  │ Path      $.data.temperature   mono  │  │
│      ☑ pressure      FLOAT      4.02     │  │ Tipe      FLOAT ▾   Satuan  °C       │  │
│      ☐ status        STRING     "RUN"    │  │ Skala     ☐ aktifkan                 │  │
│      ▾ motor                             │  │ Simpan    Periodik penuh ▾           │  │
│        ☑ rpm         INT32      1480     │  └──────────────────────────────────────┘  │
│        ☐ running     BOOLEAN    true     │  ┌──────────────────────────────────────┐  │
│    ☐ timestamp       STRING     "2026…"  │  │ Nama      Oven_Pressure          …   │  │
│    ▾ sensors  [array · 4 elemen]         │  └──────────────────────────────────────┘  │
│      ⓘ Array — pilih penanganan:         │  ┌──────────────────────────────────────┐  │
│        ○ Elemen pertama  $.sensors[0]    │  │ Nama      Motor_Rpm              …   │  │
│        ● Semua elemen (4 tag)            │  └──────────────────────────────────────┘  │
│        ○ Lewati                          │                                            │
│                                          │  Berlaku untuk semua:                      │
│  ⓘ Key bertipe teks tidak bisa           │   Scan class [1 detik ▾]                   │
│    digambar di grafik, tapi tetap        │   Mode simpan [Periodik penuh ▾]           │
│    bisa disimpan.                        │                                            │
│                                          │        [ Batal ]  [ Buat 3 Tag ]           │
└──────────────────────────────────────────┴────────────────────────────────────────────┘
```

Detail yang membuat ini terasa ramah:

1. **Nama tag disarankan otomatis** dari path (`$.data.temperature` → `Oven_Temp`, memakai nama
   perangkat sebagai awalan bila membantu). Bisa diedit; bentrok diberi akhiran `_2`.
2. **Tipe data tersimpulkan** dari contoh nilai, bukan ditanya. `72.4` → FLOAT, `true` → BOOLEAN,
   `"RUN"` → STRING.
3. **Satuan ditebak** dari nama key sebagai *saran* yang ditandai jelas (`temp` → `°C`?) — pengguna
   mengonfirmasi, sistem tidak memutuskan diam-diam.
4. **Filter "Angka"** menyembunyikan key yang tidak bisa digrafikkan — mengurangi pohon 7 key jadi
   4 yang relevan.
5. **Array ditangani eksplisit** dengan tiga pilihan, bukan diam-diam mengambil elemen pertama.
6. **Contoh nilai selalu terlihat** di sebelah key. Ini yang membuat pengguna yakin memilih key
   yang benar tanpa memahami JSONPath.
7. **Pengaturan massal** di bawah panel kanan: scan class dan mode simpan untuk semua tag sekaligus.

### 6.2 Alur MQTT

MQTT berbeda pada satu hal penting: satu perangkat (koneksi broker) bisa membawa **banyak topik**,
dan filter `#` bisa berarti puluhan topik. Jadi ada satu langkah tambahan: **mendengarkan**.

```
Langkah 1 — Koneksi broker                 Langkah 2 — Dengarkan
┌────────────────────────────────────┐     ┌─────────────────────────────────────────┐
│ Broker    : 10.1.4.50              │     │  Durasi  [ 10 detik ▾ ]                 │
│ Port      : 1883                   │     │  [ ● Dengarkan Broker ]                 │
│ Client ID : synapse-line1  (tetap) │ ──► │                                          │
│ Filter    : plant/line1/#          │     │  ⏱  8.2s tersisa · 34 pesan · 3 topik   │
│ TLS       : ☐   User/Pass: …        │     │  ████████████░░░░░░                     │
└────────────────────────────────────┘     └─────────────────────────────────────────┘
```

```
Langkah 3 — Tiga panel: topik → key → tag
┌─────────────────────────────┬──────────────────────────────┬──────────────────────────┐
│  TOPIK TERDETEKSI (3)       │  STRUKTUR: oven/data          │  TAG (2)                 │
│                             │                              │                          │
│ ● plant/line1/oven/data     │  ▾ $                         │  Oven_Temp               │
│   28 pesan · 1.2/s · JSON   │    ☑ temp      FLOAT   72.4   │  topic oven/data         │
│                             │    ☑ setpoint  FLOAT   75.0   │  path  $.temp            │
│ ○ plant/line1/motor/rpm     │    ☐ mode      STRING  "AUTO" │  …                       │
│   4 pesan · 0.4/s · angka   │                              │                          │
│                             │  Payload terakhir:            │  Oven_Setpoint           │
│ ○ plant/line1/status        │  ┌──────────────────────────┐ │  …                       │
│   2 pesan · retained · JSON │  │ {                        │ │                          │
│                             │  │  "temp": 72.4,           │ │  [ Buat 2 Tag ]          │
│ ⓘ Topik yang belum muncul   │  │  "setpoint": 75.0,       │ │                          │
│   selama mendengarkan tidak │  │  "mode": "AUTO"          │ │                          │
│   terdaftar. Perpanjang     │  │ }                        │ │                          │
│   durasi bila perlu.        │  └──────────────────────────┘ │                          │
└─────────────────────────────┴──────────────────────────────┴──────────────────────────┘
```

Detail khusus MQTT:

1. **Frekuensi per topik ditampilkan** (`1.2/s`) — langsung memberi tahu topik mana yang aktif dan
   mana yang jarang, sekaligus membantu memilih scan class yang masuk akal.
2. **Payload non-JSON didukung.** Topik `motor/rpm` yang isinya hanya `1480` menghasilkan satu key
   `$` berlabel *"nilai mentah"* — tidak dipaksa jadi JSON.
3. **Pesan `retained` ditandai** — nilainya bisa lama, dan itu penting diketahui sebelum dipakai
   sebagai sumber realtime.
4. **Tag MQTT menyimpan dua hal**: `source_topic` (topik asal) dan `address` (JSONPath dalam
   payload). Ini yang memungkinkan satu perangkat broker melayani banyak topik tanpa ambiguitas.
5. **Peringatan durasi**: topik yang perangkatnya mengirim tiap 5 menit tidak akan muncul dalam
   pendengaran 10 detik. UI menyatakan ini, dan menawarkan durasi sampai 60 detik.

### 6.3 Sesudahnya

- Tag hasil pemilih langsung aktif (FR-WRK-3): worker menyusun rencana ulang < 2 detik.
- Halaman Tag Manager menampilkan kolom `Topik` untuk tag MQTT.
- FR-MAP-9 (Fase 2): menjalankan probe ulang membandingkan struktur dengan tag yang sudah ada —
  key baru ditandai *"belum dipetakan"*, key yang hilang ditandai *"tidak lagi dikirim"*.

---

## 7. Implementasi teknis HTTP & MQTT

Bagian ini menjawab: *"implementasinya seperti apa pada HTTP dan MQTT belum terbayang"*.

### 7.1 Perbedaan mendasar: tarik vs dorong

| | HTTP (tarik) | MQTT (dorong) |
|---|---|---|
| Siapa memulai | Gateway memanggil endpoint | Perangkat mengirim ke broker |
| Kapan data datang | Saat kita minta, sesuai scan class | Kapan saja perangkat mengirim |
| Peran scan class | Menentukan laju permintaan | Hanya batas atas untuk *sampling* nilai terakhir |
| Kehilangan saat gateway mati | Total, tak terpulihkan | Tertahan di broker (QoS 1 + sesi persisten) |
| Bentuk driver | `ReadAsync()` dipanggil scheduler | `SubscribeAsync()` mendorong ke tag engine |

Keduanya tetap memenuhi satu kontrak yang sama:

```csharp
public interface IDeviceDriver : IAsyncDisposable
{
    Protocol Protocol { get; }
    Task ConnectAsync(CancellationToken ct);
    // Driver tarik: benar-benar membaca. Driver dorong: mengembalikan nilai terakhir dari cache.
    Task<IReadOnlyList<TagSample>> ReadAsync(IReadOnlyList<TagPlan> tags, CancellationToken ct);
    // Driver dorong: memasang langganan. Driver tarik: no-op.
    Task SubscribeAsync(IReadOnlyList<TagPlan> tags, Func<TagSample, Task> onSample, CancellationToken ct);
    Task<DriverHealth> CheckHealthAsync(CancellationToken ct);
}
```

### 7.2 HTTP — implementasi

```
Scheduler (scan class 1s)
   │  tiap tick, untuk setiap perangkat HTTP di kelas ini
   ▼
HttpDeviceDriver.ReadAsync(tags)
   │  1 permintaan per perangkat, bukan per tag  ◄── penting
   ▼
HttpClient (keep-alive, timeout = min(scan, 5s))
   │
   ▼
Payload JSON  ──►  evaluasi JSONPath sekali per tag  ──►  TagSample[]
                   (Newtonsoft SelectToken)               value, sourceTs, quality
```

Keputusan implementasi:

1. **Satu permintaan melayani semua tag perangkat itu.** 12 tag dari satu endpoint = 1 HTTP call,
   bukan 12. JSONPath dievaluasi pada dokumen yang sama.
2. **`HttpClient` dari `IHttpClientFactory`** dengan keep-alive; tanpa itu setiap scan menanggung
   TCP + TLS handshake.
3. **Timestamp**: kalau payload memuat waktu (`$.timestamp`) dan tag perangkat dikonfigurasi
   menunjuknya, itu jadi `source_ts`; kalau tidak, `gateway_ts` dipakai untuk keduanya dan ditandai.
4. **Timeout = min(scan interval, 5s)**. Permintaan yang lebih lambat dari periodenya sendiri harus
   dibatalkan, bukan menumpuk.
5. **Quality**: HTTP non-2xx → `BAD` untuk semua tag perangkat itu. JSONPath tidak menemukan apa
   pun → `BAD` untuk tag itu saja (kesalahan pemetaan, bukan kesalahan perangkat) — dan
   dibedakan di pesan errornya.
6. **`ETag`/`If-None-Match`**: `304 Not Modified` berarti tidak ada sampel baru — bukan sampel
   dengan nilai sama. Membedakan keduanya mencegah historian dipenuhi nilai palsu.

### 7.3 MQTT — implementasi

```
Saat perangkat diaktifkan (bukan tiap tick):
   MqttDeviceDriver.ConnectAsync
   │   ClientId TETAP · CleanStart=false · SessionExpiry=24j · QoS 1
   ▼
   SubscribeAsync(filter topik gabungan dari semua tag perangkat)
   │
   ▼
   Broker mendorong pesan  ──►  cocokkan topik ke tag (wildcard-aware)
                                     │
                                     ▼
                           evaluasi JSONPath per tag pada topik itu
                                     │
                                     ▼
                           TagSample  ──►  tag engine  ──►  WAL + SignalR
```

Keputusan implementasi:

1. **Koneksi berumur panjang**, dibuka saat perangkat diaktifkan dan ditutup saat dinonaktifkan —
   bukan per scan. Scan class untuk tag MQTT hanya membatasi laju *penyimpanan*, bukan penerimaan.
2. **ClientId wajib tetap** (mis. `synapse-<deviceId>`). Ini yang membuat broker mengenali sesi yang
   sama saat gateway kembali dan mengirimkan antrean tertahan. Form perangkat sekarang membuat
   ClientId acak — itu harus diganti, kalau tidak seluruh mekanisme anti-kehilangan MQTT mati tanpa
   gejala apa pun.
3. **`CleanStart=false` + `SessionExpiryInterval` ≥ durasi outage terburuk** (usul: 24 jam).
4. **QoS 1**, bukan 2. QoS 1 berarti "minimal sekali", jadi duplikat mungkin — dan konsumen kita
   sudah idempoten lewat `UNIQUE(tag_id, source_ts)`. QoS 2 membayar dua kali round-trip untuk
   jaminan yang sudah kita punya.
5. **Pencocokan topik sadar wildcard**: satu langganan `plant/line1/#` melayani banyak tag dengan
   `source_topic` berbeda. Pencocokan dilakukan sekali per pesan ke indeks topik→tag, bukan iterasi
   seluruh tag.
6. **Payload non-JSON**: kalau tag `address` = `$`, seluruh payload diperlakukan sebagai nilai
   tunggal (angka atau teks). Ini kasus yang sangat umum di MQTT dan harus didukung sejak awal.
7. **Laju simpan**: perangkat yang mengirim 50 pesan/detik untuk tag ber-scan-class 1 detik →
   nilai terakhir yang disimpan per detik (mode DEADBAND/ON_CHANGE tetap dievaluasi). Untuk tag
   yang wajib menyimpan **setiap** pesan, scan class diset `sebagaimana-datang` (0 ms).
8. **Last Will (LWT)**: gateway mendaftarkan LWT sehingga sistem lain tahu saat ia mati; sebaliknya
   perangkat yang mendukung LWT dipakai untuk menandai quality `BAD` seketika, tanpa menunggu
   timeout.

### 7.4 Discovery — implementasi

| Endpoint | Fungsi |
|---|---|
| `POST /api/discovery/http` | Body: konfigurasi HTTP. Memanggil endpoint sekali, mengembalikan status, latensi, payload mentah, dan `keys[]` |
| `POST /api/discovery/mqtt` | Body: konfigurasi broker + filter + durasi. Menyambung, mendengarkan, mengembalikan `topics[]` masing-masing dengan `keys[]`, jumlah pesan, laju, dan flag `retained` |
| `POST /api/discovery/opcua` | (Fase 1) Browse address space, mengembalikan pohon node |
| `GET /api/discovery/modbus-template` | (Fase 1) Format CSV peta register |

Pendataran JSON dipakai bersama oleh keduanya:

```
{ "data": { "temp": 72.4, "motor": { "rpm": 1480 } }, "items": [ { "v": 1 } ] }

  →  $.data.temp        FLOAT    72.4
     $.data.motor.rpm   INT32    1480
     $.items[0].v       INT32    1          (+ metadata: array, 1 elemen)
```

Aturan pendataran: kedalaman maksimum 6 tingkat (payload lebih dalam dari itu hampir selalu berarti
respons dibungkus terlalu banyak lapisan); array dilaporkan beserta panjangnya dan **tidak**
didatarkan seluruhnya; `null` dilaporkan sebagai tipe tak diketahui dengan peringatan bahwa tipenya
belum bisa disimpulkan.

---

## 8. Kebutuhan non-fungsional

| ID | Kebutuhan | Target |
|---|---|---|
| NFR-1 | Tanpa kehilangan sampel | Setiap sampel terakuisisi → historian tepat satu kali, atau tercatat sebagai gap |
| NFR-2 | Skala | 2.000 tag @ 1 s berkelanjutan pada perangkat gateway 4 core / 8 GB |
| NFR-3 | Ketepatan scan | Deviasi periode < 10% pada beban penuh |
| NFR-4 | Latensi tampil | Perubahan nilai → terlihat di browser < 1 s (p95) |
| NFR-5 | Kueri tren | 1 tag / 1 tahun < 2 s lewat rollup |
| NFR-6 | Pemulihan | Restart proses tidak menghilangkan sampel yang sudah di-WAL |
| NFR-7 | Ketahanan outage DB | 24 jam tanpa DB tanpa kehilangan data (kapasitas WAL) |
| NFR-8 | Keamanan | Cookie HTTP-only, TLS, secret di luar repo, RBAC, audit |
| NFR-9 | Aksesibilitas | Kontras AA, navigasi keyboard, `prefers-reduced-motion` |
| NFR-10 | Dua bahasa | ID (acuan) + EN |
| NFR-11 | Jaringan terisolasi | Tanpa CDN/font eksternal; berjalan penuh tanpa internet |

---

## 9. Fase & definition of done

| Fase | Isi | DoD |
|---|---|---|
| **0 — Pondasi** | Skema baru + Postgres/Timescale · `IDeviceDriver` · scan scheduler + hot reload · WAL + batch idempoten · quality code end-to-end · discovery HTTP & MQTT · pemilih key di UI | Perangkat HTTP + MQTT nyata terbaca; matikan DB 10 menit → tidak ada sampel hilang; tambah 50 tag → aktif < 2 s tanpa restart |
| **1 — Driver & operasional** | Modbus TCP (blok register) · Modbus RTU · OPC UA subscription + browse · uji koneksi semua protokol · gap ledger terisi · halaman kesehatan · audit konfigurasi | Empat protokol berjalan bersamaan; cabut kabel → gap tercatat, sambung → quality kembali GOOD |
| **2 — Historian** | Hypertable + kompresi + rollup + retensi · API kueri rentang · tren multi-tag · Storage Flow jadi proyeksi terjadwal · ekspor CSV/XLSX · UI berkas | Tren 1 tahun < 2 s; proyeksi bisa dijalankan ulang tanpa duplikat |
| **3 — Alarm & laporan** | Mesin alarm + halaman alarm · notifikasi · laporan shift/harian · manajemen pengguna · watchdog | Alarm tercatat saat semua browser tertutup; notifikasi tidak membanjir saat sinyal bergetar |
| **4 — Pengerasan** | Gateway redundan · backoff & isolasi lanjutan · webhook keluar · tag turunan · (opsional) kendali tulis-balik dengan audit penuh | Failover gateway tanpa kehilangan sampel |
| **5A — Dashboard builder** | Grid widget + pengikatan tag + ambang warna | Operator menyusun halaman pantau sendiri tanpa bantuan developer |
| **5B — Mimic/HMI** | Kanvas + simbol ISA + animasi (bila 5A tidak cukup) | — |

---

## 10. Perombakan database

Disetujui. Ini biaya satu kali yang paling murah dilakukan sekarang karena belum ada data produksi.

| Langkah | Isi | Risiko |
|---|---|---|
| 1 | Ganti provider EF Core: `MySql.EntityFrameworkCore` → `Npgsql.EntityFrameworkCore.PostgreSQL` | Rendah — kode repository tidak berubah |
| 2 | Hapus migrasi lama, buat ulang dari nol dengan skema §4 | Rendah — tidak ada data yang perlu dipertahankan |
| 3 | Tulis ulang pembuatan tabel dinamis dengan kuoting `"nama"` + tipe Postgres | Sedang — menyentuh `MasterTableService`; sekalian menghapus interpolasi string SQL |
| 4 | Aktifkan ekstensi TimescaleDB, jadikan `tag_history` hypertable | Rendah — satu perintah SQL di migrasi |
| 5 | Pasang kebijakan kompresi, rollup (CAGG), retensi | Rendah |
| 6 | `docker-compose.yml`: MySQL → `timescale/timescaledb:latest-pg16` | Rendah |

Yang **tidak** berubah: seluruh frontend (kontraknya API, bukan database), struktur envelope
`ApiResponse`, dan pola cookie autentikasi.

---

## 11. Pertanyaan terbuka

| # | Pertanyaan | Dibutuhkan sebelum |
|---|---|---|
| Q1 | Daftar perangkat nyata: merek, tipe, jumlah, peta register/NodeId | Fase 1 |
| Q2 | Tag yang wajib disimpan penuh lebih lama dari 90 hari | Fase 2 |
| Q3 | Daftar alarm, batas, dan penerima notifikasi | Fase 3 |
| Q4 | Fase 5A cukup, atau perlu editor mimic 5B? | Fase 5 |
| Q5 | Sesi 1 jam terlalu pendek untuk shift 8 jam — sliding expiration atau refresh token? | Fase 0 |
