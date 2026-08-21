using System.Collections.Concurrent;
using Core.Acquisition;
using Core.Enums;
using Core.Interface;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Basis data nilai-sekarang + penerjemah sampel mentah menjadi sampel final.
	///
	/// Seluruh keadaan ada di memori. Itu disengaja: nilai sekarang berubah beberapa kali per
	/// detik per tag, dan menuliskannya ke database setiap kali berarti beban tulis yang sama
	/// besarnya dengan historian tapi tanpa nilai historisnya. Baris <c>tag_current</c> di
	/// database hanyalah cermin yang ditulis berkala, supaya dasbor punya nilai awal setelah
	/// restart tanpa menyapu historian.
	/// </summary>
	public class TagEngine : ITagEngine
	{
		private sealed class State
		{
			public TagSample Last;
			public long Seq;

			/// <summary>Nilai yang TERAKHIR DISIMPAN — bukan nilai terakhir dibaca. Deadband
			/// dan on-change harus dibandingkan terhadap yang tersimpan, kalau tidak sinyal
			/// yang menanjak pelan akan lolos selamanya karena setiap langkah kecilnya
			/// dibandingkan dengan langkah sebelumnya, bukan dengan titik simpan terakhir.</summary>
			public double? LastStoredNumeric;
			public bool? LastStoredBoolean;
			public string? LastStoredText;
			public Quality LastStoredQuality = Quality.Bad;
			public DateTime LastStoredAt = DateTime.MinValue;
			public bool HasStored;
		}

		private readonly ConcurrentDictionary<Guid, State> _states = new();
		private long _seq;

		public (TagSample Sample, bool ShouldStore, long Seq) Process(TagPlan plan, TagSample raw)
		{
			var state = _states.GetOrAdd(plan.TagId, _ => new State());
			var final = Normalize(plan, raw);
			var seq = Interlocked.Increment(ref _seq);

			bool shouldStore;
			lock (state)
			{
				shouldStore = ShouldStore(plan, state, final);
				state.Last = final;
				state.Seq = seq;

				if (shouldStore)
				{
					state.LastStoredNumeric = final.Numeric;
					state.LastStoredBoolean = final.Boolean;
					state.LastStoredText = final.Text;
					state.LastStoredQuality = final.Quality;
					state.LastStoredAt = final.SourceTs;
					state.HasStored = true;
				}
			}

			return (final, shouldStore, seq);
		}

		/* ------------------------------------------------------ penskalaan & quality */

		/// <summary>
		/// Menskalakan nilai mentah ke satuan teknis dan menilai quality. Satu-satunya tempat
		/// aturan ini berlaku, untuk semua protokol dan semua tipe data.
		/// </summary>
		private static TagSample Normalize(TagPlan plan, TagSample raw)
		{
			if (raw.Quality == Quality.Bad) return raw;

			// Tag non-numerik tidak diskalakan; memaksa skala pada teks atau boolean hanya
			// menghasilkan angka yang tidak berarti.
			if (raw.Numeric is null || plan.DataType is DataType.STRING or DataType.BOOLEAN)
			{
				return raw;
			}

			var rawValue = raw.Numeric.Value;

			if (!plan.IsScaled)
			{
				return raw with { Raw = rawValue };
			}

			var rawSpan = plan.RawMax - plan.RawMin;

			if (Math.Abs(rawSpan) < double.Epsilon)
			{
				// Rentang raw nol berarti skala salah dikonfigurasi. Nilainya diteruskan apa
				// adanya dan ditandai Uncertain — membuang sampel akan menyembunyikan kesalahan
				// konfigurasi, sementara mengembalikan NaN akan meracuni agregasi historian.
				return raw with
				{
					Raw = rawValue,
					Quality = Quality.Uncertain,
					Note = "Rentang raw nol; penskalaan dilewati"
				};
			}

			var scaled = plan.EuMin + (rawValue - plan.RawMin) * (plan.EuMax - plan.EuMin) / rawSpan;

			// Di luar rentang raw yang dikonfigurasi: nilainya tetap disimpan (itu data nyata,
			// dan bisa jadi justru gejala yang dicari) tapi ditandai Uncertain supaya bisa
			// dibedakan dari pembacaan yang berada di rentang.
			var outOfRange = rawValue < Math.Min(plan.RawMin, plan.RawMax) - double.Epsilon
							 || rawValue > Math.Max(plan.RawMin, plan.RawMax) + double.Epsilon;

			return raw with
			{
				Numeric = scaled,
				Raw = rawValue,
				Quality = outOfRange ? Quality.Uncertain : raw.Quality,
				Note = outOfRange
					? $"Nilai mentah {rawValue:0.###} di luar rentang {plan.RawMin:0.###}–{plan.RawMax:0.###}"
					: raw.Note
			};
		}

		/* -------------------------------------------------------- keputusan simpan */

		private static bool ShouldStore(TagPlan plan, State state, TagSample sample)
		{
			// Sampel pertama selalu disimpan: tanpa titik awal, grafik tidak punya apa pun
			// untuk digambar sampai perubahan pertama terjadi.
			if (!state.HasStored) return true;

			// Perubahan quality SELALU disimpan, apa pun mode simpannya. Peralihan
			// Good→Bad→Good adalah batas jeda data; kalau tidak disimpan, grafik akan
			// menyambung dua titik yang terpisah lima menit sebagai garis lurus yang mulus,
			// dan pembacanya menyimpulkan proses berjalan normal selama itu.
			if (sample.Quality != state.LastStoredQuality) return true;

			// Sampel Bad berturut-turut tidak diulang: perangkat mati selama sejam pada scan
			// 1 detik akan menulis 3.600 baris yang isinya sama. Satu penanda saat jeda dimulai
			// sudah cukup, dan jeda panjangnya dicatat di acquisition_gap.
			if (sample.Quality == Quality.Bad)
			{
				return ExceededMaxGap(plan, state, sample);
			}

			switch (plan.StoreMode)
			{
				case StoreMode.Full:
					return true;

				case StoreMode.OnChange:
					return HasChanged(state, sample) || ExceededMaxGap(plan, state, sample);

				case StoreMode.Deadband:
					if (ExceededMaxGap(plan, state, sample)) return true;

					// Nilai non-numerik tidak punya deadband yang berarti; diperlakukan
					// sebagai on-change.
					if (sample.Numeric is null || state.LastStoredNumeric is null)
					{
						return HasChanged(state, sample);
					}

					var deadband = plan.EffectiveDeadband();
					if (deadband <= 0) return true;

					return Math.Abs(sample.Numeric.Value - state.LastStoredNumeric.Value) >= deadband;

				default:
					return true;
			}
		}

		private static bool HasChanged(State state, TagSample sample)
		{
			if (sample.Numeric is not null || state.LastStoredNumeric is not null)
			{
				// Perbandingan tepat memang benar di sini: yang dibandingkan adalah nilai
				// hasil skala dari sumber yang sama, dan mode OnChange justru dipakai untuk
				// tag yang nilainya diskrit.
				return sample.Numeric != state.LastStoredNumeric;
			}

			if (sample.Boolean is not null || state.LastStoredBoolean is not null)
			{
				return sample.Boolean != state.LastStoredBoolean;
			}

			return !string.Equals(sample.Text, state.LastStoredText, StringComparison.Ordinal);
		}

		private static bool ExceededMaxGap(TagPlan plan, State state, TagSample sample)
		{
			if (plan.MaxStoreGapMs <= 0) return false;
			return (sample.SourceTs - state.LastStoredAt).TotalMilliseconds >= plan.MaxStoreGapMs;
		}

		/* --------------------------------------------------------------- snapshot */

		public TagSnapshot? GetSnapshot(Guid tagId)
		{
			if (!_states.TryGetValue(tagId, out var state)) return null;

			lock (state)
			{
				if (state.Seq == 0) return null;
				return new TagSnapshot
				{
					TagId = tagId,
					DeviceId = state.Last.DeviceId,
					Sample = Freshen(state.Last),
					Seq = state.Seq
				};
			}
		}

		public IReadOnlyCollection<TagSnapshot> GetSnapshots()
		{
			var result = new List<TagSnapshot>(_states.Count);

			foreach (var (tagId, state) in _states)
			{
				lock (state)
				{
					if (state.Seq == 0) continue;
					result.Add(new TagSnapshot
					{
						TagId = tagId,
						DeviceId = state.Last.DeviceId,
						Sample = Freshen(state.Last),
						Seq = state.Seq
					});
				}
			}

			return result;
		}

		/// <summary>
		/// Menurunkan quality menjadi <see cref="Quality.Stale"/> bila nilainya sudah lama
		/// tidak diperbarui. Dihitung saat DIBACA, bukan lewat timer penyapu: dengan ribuan
		/// tag, timer yang memeriksa semuanya setiap detik menghabiskan CPU untuk tag yang
		/// tidak sedang dilihat siapa pun.
		/// </summary>
		private static TagSample Freshen(TagSample sample)
		{
			if (sample.Quality != Quality.Good) return sample;

			var age = DateTime.UtcNow - sample.GatewayTs;
			// Ambang tetap 30 detik untuk pembacaan snapshot; ambang per-tag dipakai jalur
			// alarm yang memang tahu scan class tiap tag.
			return age.TotalSeconds > 30
				? sample with { Quality = Quality.Stale, Note = $"Belum diperbarui {age.TotalSeconds:0} detik" }
				: sample;
		}

		public void MarkDeviceBad(Guid deviceId, IEnumerable<TagPlan> tags, string reason)
		{
			foreach (var plan in tags)
			{
				if (plan.DeviceId != deviceId) continue;
				Process(plan, TagSample.Failed(plan.TagId, deviceId, reason));
			}
		}

		public void Forget(IEnumerable<Guid> tagIds)
		{
			foreach (var id in tagIds) _states.TryRemove(id, out _);
		}
	}
}
