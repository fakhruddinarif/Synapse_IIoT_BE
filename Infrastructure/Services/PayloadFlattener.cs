using System.Globalization;
using System.Text;
using Core.DTOs.Discovery;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Infrastructure.Services
{
	/// <summary>
	/// Mendatarkan payload perangkat menjadi daftar key yang bisa dipilih pengguna.
	///
	/// Dipakai bersama oleh discovery HTTP dan MQTT supaya path yang ditawarkan pemilih key
	/// PERSIS sama dengan path yang nanti dievaluasi driver saat akuisisi. Kalau keduanya
	/// dihitung dengan dua potongan kode berbeda, keduanya akan menyimpang, dan gejalanya
	/// adalah tag yang tampak benar tapi selalu kosong.
	/// </summary>
	public static class PayloadFlattener
	{
		/// <summary>
		/// Payload lebih dalam dari ini hampir selalu berarti responsnya dibungkus terlalu
		/// banyak lapisan; mendatarkannya seluruhnya hanya menghasilkan daftar yang tak
		/// terbaca.
		/// </summary>
		private const int MaxDepth = 6;

		/// <summary>Batas jumlah key supaya payload raksasa tidak membekukan UI.</summary>
		private const int MaxKeys = 500;

		/// <summary>
		/// Elemen array yang didatarkan. Array panjang dilaporkan panjangnya, tapi hanya
		/// elemen pertama yang dijadikan contoh — UI menawarkan penanganan eksplisit
		/// (elemen pertama / semua elemen / lewati).
		/// </summary>
		private const int ArraySampleElements = 1;

		/* ------------------------------------------------------------------ */

		public enum PayloadKind
		{
			Json,
			Number,
			Text,
			Empty,
			Binary
		}

		public static PayloadKind DetectKind(string? payload)
		{
			if (string.IsNullOrWhiteSpace(payload)) return PayloadKind.Empty;

			var trimmed = payload.TrimStart();
			if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
			{
				return IsParsableJson(payload) ? PayloadKind.Json : PayloadKind.Text;
			}

			// Sangat umum di MQTT: satu topik berisi satu angka polos, mis. "1480".
			if (double.TryParse(payload.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			{
				return PayloadKind.Number;
			}

			// Karakter kendali di luar whitespace menandakan payload biner, bukan teks.
			foreach (var c in payload)
			{
				if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') return PayloadKind.Binary;
			}

			return PayloadKind.Text;
		}

		private static bool IsParsableJson(string payload)
		{
			try
			{
				Parse(payload);
				return true;
			}
			catch (JsonReaderException)
			{
				return false;
			}
		}

		/// <summary>
		/// Mengurai JSON dengan <c>DateParseHandling.None</c>.
		///
		/// Tanpa itu Newtonsoft mengubah string ber-format tanggal menjadi <c>DateTime</c>,
		/// dan contoh nilai yang ditampilkan ke pengguna menjadi hasil format ulang menurut
		/// locale server ("08/20/2026 07:41:02") alih-alih apa yang benar-benar dikirim
		/// perangkat ("2026-08-20T07:41:02Z"). Pemilih key harus memperlihatkan payload
		/// sebagaimana adanya, kalau tidak pengguna memilih berdasarkan data yang salah.
		/// </summary>
		private static JToken Parse(string payload)
		{
			using var reader = new JsonTextReader(new StringReader(payload))
			{
				DateParseHandling = DateParseHandling.None
			};
			return JToken.ReadFrom(reader);
		}

		/// <summary>
		/// Merapikan JSON supaya bisa dibaca manusia di panel payload. Payload non-JSON
		/// dikembalikan apa adanya.
		/// </summary>
		public static string Prettify(string payload, int maxChars = 8000)
		{
			string result = payload;
			try
			{
				result = Parse(payload).ToString(Formatting.Indented);
			}
			catch (JsonReaderException)
			{
				// Bukan JSON — tampilkan mentah.
			}

			return result.Length > maxChars ? result[..maxChars] + "\n… (dipotong)" : result;
		}

		/* ------------------------------------------------------------------ */

		/// <summary>
		/// Mendatarkan payload menjadi daftar key. Payload non-JSON menghasilkan satu key
		/// <c>$</c> yang mewakili seluruh nilai — bentuk yang harus didukung sejak awal
		/// karena banyak perangkat MQTT mengirim angka polos, bukan objek.
		/// </summary>
		public static List<DiscoveredKeyDto> Flatten(string? payload)
		{
			var keys = new List<DiscoveredKeyDto>();
			var kind = DetectKind(payload);

			if (kind != PayloadKind.Json)
			{
				keys.Add(BuildRawValueKey(payload, kind));
				return keys;
			}

			try
			{
				var root = Parse(payload!);
				Walk(root, "$", 0, false, null, keys);
			}
			catch (JsonReaderException)
			{
				keys.Add(BuildRawValueKey(payload, PayloadKind.Text));
			}

			EnsureUniqueTagNames(keys);
			return keys;
		}

		private static DiscoveredKeyDto BuildRawValueKey(string? payload, PayloadKind kind)
		{
			var isNumeric = kind == PayloadKind.Number;
			return new DiscoveredKeyDto
			{
				Path = "$",
				Leaf = "(nilai mentah)",
				Depth = 0,
				DataType = isNumeric ? "FLOAT" : "STRING",
				SampleValue = kind == PayloadKind.Empty ? null : Truncate(payload, 200),
				SuggestedTagName = "Value",
				IsNumeric = isNumeric,
				Note = kind switch
				{
					PayloadKind.Number => "Seluruh payload adalah satu angka.",
					PayloadKind.Empty => "Payload kosong — tipe belum bisa disimpulkan.",
					PayloadKind.Binary => "Payload tampak biner; tidak bisa dipetakan sebagai nilai.",
					_ => "Payload bukan JSON — seluruh isinya diperlakukan sebagai satu nilai teks."
				}
			};
		}

		private static void Walk(
			JToken token,
			string path,
			int depth,
			bool inArray,
			int? arrayLength,
			List<DiscoveredKeyDto> keys)
		{
			if (keys.Count >= MaxKeys) return;

			switch (token.Type)
			{
				case JTokenType.Object:
					if (depth >= MaxDepth)
					{
						keys.Add(BuildLeaf(path, token, depth, inArray, arrayLength,
							"Terlalu dalam untuk didatarkan lebih jauh."));
						return;
					}

					foreach (var property in ((JObject)token).Properties())
					{
						Walk(property.Value, $"{path}.{property.Name}", depth + 1, inArray, arrayLength, keys);
						if (keys.Count >= MaxKeys) return;
					}
					return;

				case JTokenType.Array:
					var array = (JArray)token;
					if (array.Count == 0)
					{
						keys.Add(BuildLeaf(path, token, depth, inArray, 0,
							"Array kosong saat diprobe — tipe isinya belum bisa disimpulkan."));
						return;
					}

					if (depth >= MaxDepth)
					{
						keys.Add(BuildLeaf(path, token, depth, inArray, array.Count,
							"Terlalu dalam untuk didatarkan lebih jauh."));
						return;
					}

					// Hanya elemen pertama yang dijadikan contoh; panjangnya dilaporkan supaya
					// UI bisa menawarkan "buat N tag" tanpa membanjiri daftar dengan ratusan key.
					for (var i = 0; i < Math.Min(array.Count, ArraySampleElements); i++)
					{
						Walk(array[i], $"{path}[{i}]", depth + 1, true, array.Count, keys);
					}
					return;

				default:
					keys.Add(BuildLeaf(path, token, depth, inArray, arrayLength, null));
					return;
			}
		}

		private static DiscoveredKeyDto BuildLeaf(
			string path,
			JToken token,
			int depth,
			bool inArray,
			int? arrayLength,
			string? note)
		{
			var (dataType, isNumeric, typeNote) = InferType(token);
			var leaf = LeafOf(path);

			return new DiscoveredKeyDto
			{
				Path = path,
				Leaf = leaf,
				Depth = depth,
				DataType = dataType,
				SampleValue = SampleOf(token),
				SuggestedTagName = SuggestTagName(path),
				SuggestedUnit = SuggestUnit(leaf),
				IsNumeric = isNumeric,
				IsInArray = inArray,
				ArrayLength = arrayLength,
				Note = note ?? typeNote
			};
		}

		/* ------------------------------------------- penyimpulan tipe ------ */

		/// <summary>
		/// Menyimpulkan <c>Core.Enums.DataType</c> dari contoh nilai. Dikembalikan sebagai
		/// string agar Core tidak perlu direferensikan dari sisi klien, dan agar nilai yang
		/// tak dikenali bisa diberi catatan alih-alih memaksa satu tipe.
		/// </summary>
		private static (string DataType, bool IsNumeric, string? Note) InferType(JToken token)
		{
			switch (token.Type)
			{
				case JTokenType.Integer:
					// Rentang menentukan tipe: memilih INT16 untuk nilai yang kebetulan kecil
					// akan meluap begitu proses berjalan normal.
					var asLong = token.Value<long>();
					var type = asLong is >= short.MinValue and <= short.MaxValue ? "INT16" : "INT32";
					return (type, true, null);

				case JTokenType.Float:
					return ("FLOAT", true, null);

				case JTokenType.Boolean:
					return ("BOOLEAN", true, null);

				case JTokenType.Date:
					return ("STRING", false, "Nilai waktu disimpan sebagai teks ISO-8601.");

				case JTokenType.String:
					var text = token.Value<string>() ?? string.Empty;
					// Perangkat MQTT sering mengirim angka sebagai teks ("23.4"). Tetap
					// disarankan STRING supaya tidak menebak, tapi pengguna diberi tahu
					// bahwa mengubahnya ke FLOAT aman.
					if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
					{
						return ("STRING", false, "Berisi angka dalam bentuk teks — bisa diubah ke FLOAT bila memang nilai ukur.");
					}
					return ("STRING", false, null);

				case JTokenType.Null:
					return ("STRING", false, "Nilainya null saat diprobe — tipe belum bisa disimpulkan.");

				case JTokenType.Object:
				case JTokenType.Array:
					return ("STRING", false, "Nilai bersarang; pilih key di dalamnya, bukan induknya.");

				default:
					return ("STRING", false, null);
			}
		}

		private static object? SampleOf(JToken token)
		{
			return token.Type switch
			{
				JTokenType.Integer => token.Value<long>(),
				JTokenType.Float => token.Value<double>(),
				JTokenType.Boolean => token.Value<bool>(),
				JTokenType.Null => null,
				JTokenType.Object => "{…}",
				JTokenType.Array => "[…]",
				_ => Truncate(token.Value<string>(), 120)
			};
		}

		/* ------------------------------------------- saran nama & satuan --- */

		private static string LeafOf(string path)
		{
			var lastDot = path.LastIndexOf('.');
			var leaf = lastDot >= 0 ? path[(lastDot + 1)..] : path;
			var bracket = leaf.IndexOf('[');
			return bracket >= 0 ? leaf[..bracket] : leaf;
		}

		/// <summary>
		/// Nama leaf yang tidak memberi tahu apa pun sendirian. Kalau leaf-nya salah satu ini,
		/// segmen induk yang bermakna WAJIB disertakan.
		///
		/// Perhatikan "current" TIDAK ada di sini: di instrumentasi ia berarti arus listrik,
		/// bukan "nilai sekarang".
		/// </summary>
		private static readonly HashSet<string> GenericLeafNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"value", "val", "v", "reading", "result", "raw", "state"
		};

		/// <summary>
		/// Segmen pembungkus yang tidak menambah arti pada nama tag. "$.data.temperature"
		/// sebaiknya menjadi "Temperature", bukan "Data_Temperature".
		/// </summary>
		private static readonly HashSet<string> ContainerNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"data", "payload", "body", "root", "result", "results", "items", "list",
			"values", "readings", "measurements", "metrics", "attributes", "properties", "obj"
		};

		public static string SuggestTagName(string path)
		{
			var segments = path
				.Split('.', StringSplitOptions.RemoveEmptyEntries)
				.Where(s => s != "$")
				.Select(s =>
				{
					var bracket = s.IndexOf('[');
					var name = bracket >= 0 ? s[..bracket] : s;
					var index = bracket >= 0 ? s[(bracket + 1)..].TrimEnd(']') : null;
					return string.IsNullOrEmpty(index) ? name : $"{name}_{index}";
				})
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToList();

			if (segments.Count == 0) return "Value";

			// Aturannya: leaf selalu ikut, dan satu segmen induk yang BERMAKNA disertakan bila
			// ada. Tanpa induk, "$.data.motor.rpm" dan "$.data.pump.rpm" keduanya menjadi "Rpm"
			// dan bentrok — lalu dibedakan hanya oleh akhiran "_2" yang tidak berarti apa pun
			// bagi orang yang membaca daftar tag enam bulan kemudian.
			var leafSegment = segments[^1];
			var take = new List<string> { leafSegment };

			var leafIsGeneric = GenericLeafNames.Contains(StripIndex(leafSegment));
			for (var i = segments.Count - 2; i >= 0; i--)
			{
				var candidate = StripIndex(segments[i]);
				var isContainer = ContainerNames.Contains(candidate);

				// Induk pembungkus dilewati, KECUALI leaf-nya generik — "$.data.value" tanpa
				// induk apa pun hanya akan menjadi "Value".
				if (isContainer && !leafIsGeneric) continue;

				take.Insert(0, segments[i]);
				break;
			}

			var words = take.SelectMany(SplitWords).Where(w => w.Length > 0).ToList();
			if (words.Count == 0) return "Value";

			return string.Join('_', words.Select(Capitalize));
		}

		private static string StripIndex(string segment)
		{
			var underscore = segment.LastIndexOf('_');
			if (underscore <= 0) return segment;
			return int.TryParse(segment[(underscore + 1)..], out _) ? segment[..underscore] : segment;
		}

		/// <summary>
		/// Memecah "ovenTemp", "oven_temp", "oven-temp", dan "OvenTEMP" menjadi kata yang sama
		/// supaya nama tag konsisten apa pun gaya penamaan perangkatnya.
		/// </summary>
		private static IEnumerable<string> SplitWords(string input)
		{
			var buffer = new StringBuilder();

			for (var i = 0; i < input.Length; i++)
			{
				var c = input[i];

				if (!char.IsLetterOrDigit(c))
				{
					if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
					continue;
				}

				var startsNewWord =
					buffer.Length > 0 &&
					char.IsUpper(c) &&
					(char.IsLower(input[i - 1]) ||
					 (i + 1 < input.Length && char.IsLower(input[i + 1]) && char.IsUpper(input[i - 1])));

				if (startsNewWord)
				{
					yield return buffer.ToString();
					buffer.Clear();
				}

				buffer.Append(c);
			}

			if (buffer.Length > 0) yield return buffer.ToString();
		}

		private static string Capitalize(string word)
		{
			if (word.Length == 0) return word;
			if (word.All(char.IsDigit)) return word;
			return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
		}

		/// <summary>
		/// Dua key berbeda bisa menghasilkan nama yang sama ("$.a.temp" dan "$.b.temp").
		/// Nama tag harus unik per perangkat, jadi bentrokan diberi akhiran di sini —
		/// bukan dibiarkan gagal saat penyimpanan massal.
		/// </summary>
		private static void EnsureUniqueTagNames(List<DiscoveredKeyDto> keys)
		{
			var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			foreach (var key in keys)
			{
				var baseName = key.SuggestedTagName;
				if (!used.TryAdd(baseName, 1))
				{
					var suffix = ++used[baseName];
					key.SuggestedTagName = $"{baseName}_{suffix}";
				}
			}
		}

		/* ------------------------------------------------- saran satuan ---- */

		private static readonly (string[] Keywords, string Unit)[] UnitHints =
		{
			(new[] { "temperature", "temp", "suhu", "celsius" }, "°C"),
			(new[] { "pressure", "press", "tekanan", "bar" }, "bar"),
			(new[] { "humidity", "humid", "kelembaban", "rh" }, "%"),
			(new[] { "rpm", "speed", "putaran" }, "rpm"),
			(new[] { "energy", "kwh" }, "kWh"),
			(new[] { "power", "watt", "daya" }, "W"),
			(new[] { "voltage", "volt", "tegangan" }, "V"),
			(new[] { "current", "ampere", "amp", "arus" }, "A"),
			(new[] { "frequency", "freq", "frekuensi", "hertz" }, "Hz"),
			(new[] { "flow", "debit" }, "m³/h"),
			(new[] { "level", "ketinggian" }, "%"),
			(new[] { "weight", "mass", "berat" }, "kg"),
			(new[] { "distance", "jarak" }, "m"),
			(new[] { "percent", "persen", "pct" }, "%"),
		};

		/// <summary>
		/// Menebak satuan dari nama key. Hasilnya SARAN — UI wajib menandainya sebagai
		/// tebakan, karena "level" bisa berarti persen maupun meter, dan salah satuan
		/// menghasilkan laporan yang salah tanpa satu pun error.
		/// </summary>
		public static string? SuggestUnit(string leaf)
		{
			// Pencocokan per KATA, bukan substring. Dengan substring, "timestamp" cocok dengan
			// "amp" dan disarankan bersatuan Ampere — tebakan yang salah tapi terlihat
			// meyakinkan, jenis kesalahan yang paling mahal di konfigurasi instrumentasi.
			var words = SplitWords(leaf)
				.Select(w => w.ToLowerInvariant())
				.ToHashSet(StringComparer.Ordinal);

			foreach (var (keywords, unit) in UnitHints)
			{
				foreach (var keyword in keywords)
				{
					if (words.Contains(keyword)) return unit;
				}
			}

			return null;
		}

		private static string? Truncate(string? value, int max)
		{
			if (value is null) return null;
			return value.Length <= max ? value : value[..max] + "…";
		}
	}
}
