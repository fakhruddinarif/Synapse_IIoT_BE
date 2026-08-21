using Core.Enums;

namespace Infrastructure.Drivers
{
	public enum RegisterKind
	{
		/// <summary>Function 3 — dapat dibaca dan ditulis oleh PLC.</summary>
		Holding,

		/// <summary>Function 4 — hanya dibaca.</summary>
		Input,

		/// <summary>Function 1 — bit, dapat ditulis.</summary>
		Coil,

		/// <summary>Function 2 — bit, hanya dibaca.</summary>
		DiscreteInput
	}

	/// <summary>
	/// Alamat Modbus yang sudah diurai: jenis register plus offset berbasis nol.
	///
	/// KENAPA INI PERLU KELAS SENDIRI:
	///
	/// Modbus punya dua cara menuliskan alamat yang sama, dan keduanya dipakai di lapangan.
	/// Dokumentasi vendor menulis "40001" (penomoran "data model" berbasis satu), sementara
	/// protokolnya di kabel mengirim offset 0. Salah menafsirkannya menghasilkan pembacaan yang
	/// bergeser satu register — bukan galat, tetapi angka yang salah dari register di sebelahnya.
	/// Itulah kesalahan konfigurasi Modbus yang paling umum dan paling sulit dilihat.
	///
	/// Karena itu bentuk eksplisit (<c>HR:0</c>) selalu tersedia dan didahulukan, dan bentuk
	/// klasik lima digit diterjemahkan dengan aturan yang tertulis di sini, bukan ditebak.
	/// </summary>
	public readonly record struct ModbusAddress(RegisterKind Kind, int Offset)
	{
		public bool IsBit => Kind is RegisterKind.Coil or RegisterKind.DiscreteInput;

		/// <summary>
		/// Menguraikan alamat. Bentuk yang diterima:
		///
		/// <list type="bullet">
		/// <item><c>HR:100</c>, <c>IR:100</c>, <c>C:10</c>, <c>DI:10</c> — eksplisit, offset berbasis nol</item>
		/// <item><c>40001</c>–<c>49999</c> — holding register, offset = n − 40001</item>
		/// <item><c>30001</c>–<c>39999</c> — input register, offset = n − 30001</item>
		/// <item><c>10001</c>–<c>19999</c> — discrete input, offset = n − 10001</item>
		/// <item><c>00001</c>–<c>09999</c> (ditulis dengan nol di depan) — coil, offset = n − 1</item>
		/// <item>angka biasa &lt; 10000 — holding register, offset apa adanya</item>
		/// </list>
		///
		/// Baris terakhir adalah satu-satunya tafsir yang tidak bisa dipastikan dari tulisannya,
		/// jadi ia dipilih menjadi yang paling sering dimaksud (holding register) DAN ditulis di
		/// sini supaya bisa diperiksa, bukan ditemukan lewat angka yang salah.
		/// </summary>
		public static bool TryParse(string? address, out ModbusAddress result, out string? error)
		{
			result = default;
			error = null;

			if (string.IsNullOrWhiteSpace(address))
			{
				error = "Alamat Modbus kosong";
				return false;
			}

			var text = address.Trim();
			var colon = text.IndexOf(':');

			if (colon > 0)
			{
				var prefix = text[..colon].Trim().ToUpperInvariant();
				var rest = text[(colon + 1)..].Trim();

				var kind = prefix switch
				{
					"HR" or "H" or "HOLDING" => RegisterKind.Holding,
					"IR" or "I" or "INPUT" => RegisterKind.Input,
					"C" or "COIL" => RegisterKind.Coil,
					"DI" or "DISCRETE" => RegisterKind.DiscreteInput,
					_ => (RegisterKind?)null
				} ?? default;

				if (prefix is not ("HR" or "H" or "HOLDING" or "IR" or "I" or "INPUT" or "C" or "COIL" or "DI" or "DISCRETE"))
				{
					error = $"Awalan alamat '{prefix}' tidak dikenal. Gunakan HR, IR, C, atau DI";
					return false;
				}

				if (!int.TryParse(rest, out var offset) || offset < 0 || offset > 65535)
				{
					error = $"Offset '{rest}' bukan angka 0–65535";
					return false;
				}

				result = new ModbusAddress(kind, offset);
				return true;
			}

			if (!int.TryParse(text, out var number) || number < 0)
			{
				error = $"Alamat '{text}' bukan alamat Modbus yang sah";
				return false;
			}

			// Nol di depan pada lima/enam digit berarti penomoran klasik untuk coil ("00001"),
			// dan itu satu-satunya cara membedakannya dari offset biasa.
			var looksClassicCoil = text.Length >= 5 && text[0] == '0';

			// Hasilnya dikembalikan lewat penanda keberhasilan, BUKAN dengan membandingkan
			// terhadap `default`: `HR:0` — holding register offset 0, alamat paling umum di
			// dunia — nilainya persis sama dengan `default(ModbusAddress)`. Memakai `default`
			// sebagai penanda gagal berarti menolak alamat yang paling sering dipakai. (Itu
			// terjadi di versi pertama berkas ini, dan hanya terlihat karena diuji.)
			ModbusAddress? parsed = number switch
			{
				>= 40001 and <= 49999 => new ModbusAddress(RegisterKind.Holding, number - 40001),
				>= 30001 and <= 39999 => new ModbusAddress(RegisterKind.Input, number - 30001),
				>= 20001 and <= 29999 => null, // blok 2xxxx tidak dipakai Modbus standar
				>= 10001 and <= 19999 => new ModbusAddress(RegisterKind.DiscreteInput, number - 10001),
				_ when looksClassicCoil && number >= 1 => new ModbusAddress(RegisterKind.Coil, number - 1),
				<= 9999 => new ModbusAddress(RegisterKind.Holding, number),
				_ => null
			};

			if (parsed is null)
			{
				error = $"Alamat '{text}' di luar rentang yang dikenali";
				return false;
			}

			result = parsed.Value;
			return true;
		}

		/// <summary>
		/// Berapa register yang dibutuhkan satu tipe data. Inilah alasan tag 32-bit harus
		/// membaca dua register: membacanya satu register menghasilkan separuh angka, yang
		/// tetap terlihat seperti angka.
		/// </summary>
		public static int RegisterSpan(DataType type) => type switch
		{
			DataType.INT32 or DataType.UINT32 or DataType.FLOAT => 2,
			_ => 1
		};
	}
}
