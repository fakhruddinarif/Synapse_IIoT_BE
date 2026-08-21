namespace Core.Acquisition
{
	/// <summary>
	/// Mutu satu nilai. Diambil dari model OPC UA / OPC DA karena itulah kosakata yang sudah
	/// dipahami orang instrumentasi.
	///
	/// Ini bukan pelengkap opsional: nilai basi yang tampak segar adalah kegagalan terburuk di
	/// sistem SCADA. Grafik yang datar sempurna selama lima menit terbaca sebagai "proses
	/// stabil", padahal artinya "gateway kehilangan kontak". Quality-lah yang membedakan
	/// keduanya, dan karena itu ia menempel pada SETIAP sampel, bukan pada perangkat.
	/// </summary>
	public enum Quality : byte
	{
		/// <summary>Nilai sah dan baru.</summary>
		Good = 0,

		/// <summary>Sah tapi diragukan — mis. di luar rentang skala yang dikonfigurasi.</summary>
		Uncertain = 1,

		/// <summary>Gagal baca: timeout, perangkat menolak, path tidak ditemukan.</summary>
		Bad = 2,

		/// <summary>Nilai lama yang belum diperbarui melewati batas toleransi.</summary>
		Stale = 3
	}

	/// <summary>
	/// Satu pembacaan satu tag. Struct karena jumlahnya sangat banyak (ribuan per detik) dan
	/// tidak pernah dimutasi setelah dibuat — mengalokasikan objek untuk setiap sampel akan
	/// membuat GC bekerja terus-menerus pada beban normal.
	/// </summary>
	public readonly record struct TagSample
	{
		public required Guid TagId { get; init; }
		public required Guid DeviceId { get; init; }

		/// <summary>Nilai dalam satuan teknis (sudah diskalakan). <c>null</c> saat quality Bad.</summary>
		public double? Numeric { get; init; }

		/// <summary>Nilai boolean untuk tag digital.</summary>
		public bool? Boolean { get; init; }

		/// <summary>Nilai teks untuk tag non-numerik.</summary>
		public string? Text { get; init; }

		/// <summary>Nilai mentah sebelum penskalaan; disimpan untuk penelusuran saat skala
		/// dicurigai salah dikonfigurasi.</summary>
		public double? Raw { get; init; }

		/// <summary>
		/// Waktu dari SUMBER bila protokolnya menyediakan (OPC UA, DNP3, payload bertimestamp).
		/// Dipisahkan dari <see cref="GatewayTs"/> karena keduanya bisa berbeda jauh: perangkat
		/// yang menyangga data selama link putus mengirimkan sampel lama dengan source_ts lama,
		/// dan menyimpannya dengan waktu kedatangan akan meratakan seluruh riwayat outage ke
		/// satu titik.
		/// </summary>
		public DateTime SourceTs { get; init; }

		/// <summary>Waktu gateway menerima/mencuplik nilai ini. Selalu terisi.</summary>
		public DateTime GatewayTs { get; init; }

		public Quality Quality { get; init; }

		/// <summary>Sebab quality bukan Good. Masuk log dan gap ledger, bukan ke setiap baris historian.</summary>
		public string? Note { get; init; }

		public static TagSample Failed(Guid tagId, Guid deviceId, string note, DateTime? at = null)
		{
			var now = at ?? DateTime.UtcNow;
			return new TagSample
			{
				TagId = tagId,
				DeviceId = deviceId,
				SourceTs = now,
				GatewayTs = now,
				Quality = Quality.Bad,
				Note = note
			};
		}

		/// <summary>Ada nilai yang layak disimpan. Sampel Bad tetap disimpan sebagai penanda
		/// jeda, tapi tanpa nilai.</summary>
		public bool HasValue => Numeric.HasValue || Boolean.HasValue || Text is not null;
	}
}
