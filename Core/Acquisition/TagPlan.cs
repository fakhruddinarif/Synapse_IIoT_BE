using Core.Enums;

namespace Core.Acquisition
{
	/// <summary>Kapan sebuah sampel layak masuk historian.</summary>
	public enum StoreMode : byte
	{
		/// <summary>Setiap sampel disimpan. Baku, dan satu-satunya mode yang memenuhi
		/// "tidak ada data yang boleh hilang" secara literal.</summary>
		Full = 0,

		/// <summary>Disimpan bila berubah melebihi deadband, plus satu sampel wajib tiap
		/// <c>MaxStoreGapMs</c>. Hemat 10–100× pada sinyal analog yang tenang, dengan
		/// konsekuensi yang harus disepakati sadar: nilai antara sengaja tidak disimpan.</summary>
		Deadband = 1,

		/// <summary>Disimpan pada setiap perubahan nilai, tanpa deadband. Untuk tag digital,
		/// di mana satu transisi yang hilang berarti satu kejadian yang hilang.</summary>
		OnChange = 2
	}

	/// <summary>
	/// Rencana akuisisi satu tag — bentuk yang dibaca driver dan tag engine saat runtime.
	///
	/// Ini SALINAN datar dari entity <c>Tag</c>, bukan entity-nya sendiri, dengan dua alasan:
	/// entity EF membawa referensi navigasi dan pelacakan perubahan yang tidak ada gunanya di
	/// jalur panas, dan rencana harus <b>immutable</b> supaya bisa ditukar secara atomik saat
	/// konfigurasi berubah tanpa mengunci scheduler yang sedang berjalan.
	/// </summary>
	public sealed record TagPlan
	{
		public required Guid TagId { get; init; }
		public required Guid DeviceId { get; init; }
		public required string Name { get; init; }

		/// <summary>Alamat sesuai protokol: register Modbus, NodeId OPC UA, atau JSONPath
		/// untuk HTTP/MQTT.</summary>
		public required string Address { get; init; }

		/// <summary>MQTT saja: topik asal. Satu koneksi broker melayani banyak topik, jadi
		/// alamat (JSONPath) sendirian tidak cukup untuk menentukan tag mana yang dimaksud.</summary>
		public string? SourceTopic { get; init; }

		public DataType DataType { get; init; } = DataType.FLOAT;

		/// <summary>
		/// Kelas scan tag ini dalam ms. <c>null</c> berarti ikut interval perangkat.
		///
		/// Ada di tingkat tag, bukan hanya perangkat, karena satu PLC biasanya membawa campuran:
		/// suhu yang cukup dibaca 5 detik sekali dan interlock yang harus 500 ms. Memaksa satu
		/// interval per perangkat berarti memilih antara membebani jaringan demi tag lambat atau
		/// melewatkan kejadian pada tag cepat.
		/// </summary>
		public int? ScanIntervalMs { get; init; }

		/* ----------------------------- penskalaan ----------------------------- */

		public bool IsScaled { get; init; }
		public double RawMin { get; init; }
		public double RawMax { get; init; }
		public double EuMin { get; init; }
		public double EuMax { get; init; }

		/* --------------------------- penyimpanan ------------------------------ */

		public StoreMode StoreMode { get; init; } = StoreMode.Full;

		/// <summary>Deadband absolut dalam satuan teknis. Diutamakan bila keduanya terisi.</summary>
		public double? DeadbandAbs { get; init; }

		/// <summary>Deadband sebagai persen dari rentang EU.</summary>
		public double? DeadbandPct { get; init; }

		/// <summary>Simpan paksa tiap N ms walau nilainya tidak berubah, supaya grafik jangka
		/// panjang tidak berlubang dan pembaca bisa membedakan "tidak berubah" dari
		/// "tidak terpantau".</summary>
		public int MaxStoreGapMs { get; init; } = 60_000;

		/// <summary>Ambang toleransi sebelum nilai terakhir dianggap <see cref="Quality.Stale"/>.</summary>
		public int StaleAfterMs { get; init; } = 5_000;

		/// <summary>
		/// Menghitung deadband efektif dalam satuan teknis. Persen dihitung dari rentang EU,
		/// bukan dari nilai sekarang: deadband relatif terhadap nilai membuat sinyal yang
		/// mendekati nol tersimpan pada setiap gerakan sekecil apa pun.
		/// </summary>
		public double EffectiveDeadband()
		{
			if (DeadbandAbs is > 0) return DeadbandAbs.Value;

			if (DeadbandPct is > 0)
			{
				var span = Math.Abs(EuMax - EuMin);
				if (span > 0) return span * DeadbandPct.Value / 100.0;
			}

			return 0;
		}
	}

	/// <summary>
	/// Rencana untuk satu perangkat pada satu kelas scan. Dibuat ulang seluruhnya setiap kali
	/// konfigurasi berubah — menambal rencana yang ada di tempat jauh lebih mudah salah
	/// daripada menyusunnya ulang, dan penyusunannya murah.
	/// </summary>
	public sealed record DevicePlan
	{
		public required Guid DeviceId { get; init; }
		public required string DeviceName { get; init; }
		public required Protocol Protocol { get; init; }
		public required string ConnectionConfigJson { get; init; }
		public required int ScanIntervalMs { get; init; }
		public required IReadOnlyList<TagPlan> Tags { get; init; }
	}
}
