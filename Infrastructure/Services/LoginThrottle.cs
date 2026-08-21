using System.Collections.Concurrent;
using Core.Interface;
using Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services
{
	/// <summary>
	/// Implementasi <see cref="ILoginThrottle"/> berbasis memori.
	///
	/// Disimpan di memori, bukan database, dengan alasan yang disengaja: penghitung ini
	/// ditulis pada setiap percobaan login gagal — termasuk saat sedang diserang — dan
	/// menulisnya ke database berarti serangan brute force berubah menjadi serangan
	/// penghabisan I/O database. Konsekuensinya hitungan hilang saat gateway restart, dan
	/// itu dapat diterima: restart bukan sesuatu yang bisa dipicu penyerang dari luar.
	///
	/// Untuk gateway ganda (Fase 4), penghitung ini perlu dipindah ke penyimpanan bersama
	/// (Redis) supaya penguncian berlaku di kedua node.
	/// </summary>
	public class LoginThrottle : ILoginThrottle
	{
		private sealed class Counter
		{
			public int Failures;
			public DateTimeOffset FirstFailureAt;
			public DateTimeOffset? LockedUntil;
		}

		private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
		private readonly SecuritySettings _settings;
		private readonly ILogger<LoginThrottle> _logger;
		private DateTimeOffset _lastPrune = DateTimeOffset.UtcNow;

		public LoginThrottle(IOptions<SecuritySettings> settings, ILogger<LoginThrottle> logger)
		{
			_settings = settings.Value;
			_logger = logger;
		}

		/// <summary>
		/// Dua kunci per percobaan: satu untuk username, satu untuk IP. Serangan yang
		/// memutar salah satunya tetap tertahan oleh yang lain.
		/// </summary>
		private static IEnumerable<string> KeysFor(string username, string? ipAddress)
		{
			yield return "u:" + username.Trim().ToLowerInvariant();
			if (!string.IsNullOrWhiteSpace(ipAddress)) yield return "i:" + ipAddress;
		}

		public (bool IsLocked, TimeSpan RetryAfter) Check(string username, string? ipAddress)
		{
			Prune();
			var now = DateTimeOffset.UtcNow;
			var longest = TimeSpan.Zero;

			foreach (var key in KeysFor(username, ipAddress))
			{
				if (!_counters.TryGetValue(key, out var counter)) continue;
				if (counter.LockedUntil is null) continue;

				var remaining = counter.LockedUntil.Value - now;
				if (remaining > longest) longest = remaining;
			}

			return longest > TimeSpan.Zero ? (true, longest) : (false, TimeSpan.Zero);
		}

		public int RegisterFailure(string username, string? ipAddress)
		{
			var now = DateTimeOffset.UtcNow;
			var window = TimeSpan.FromMinutes(_settings.LoginAttemptWindowMinutes);
			var remainingAttempts = _settings.MaxLoginAttempts;

			foreach (var key in KeysFor(username, ipAddress))
			{
				var counter = _counters.AddOrUpdate(
					key,
					_ => new Counter { Failures = 1, FirstFailureAt = now },
					(_, existing) =>
					{
						lock (existing)
						{
							// Jendela kedaluwarsa: hitungan dimulai ulang, kalau tidak satu
							// kesalahan ketik enam jam lalu ikut menghukum percobaan hari ini.
							if (now - existing.FirstFailureAt > window)
							{
								existing.Failures = 1;
								existing.FirstFailureAt = now;
								existing.LockedUntil = null;
							}
							else
							{
								existing.Failures++;
							}

							if (existing.Failures >= _settings.MaxLoginAttempts)
							{
								existing.LockedUntil = now.AddMinutes(_settings.LockoutMinutes);
							}

							return existing;
						}
					});

				remainingAttempts = Math.Min(remainingAttempts, Math.Max(0, _settings.MaxLoginAttempts - counter.Failures));

				if (counter.LockedUntil is not null && counter.Failures == _settings.MaxLoginAttempts)
				{
					// Dicatat sebagai peringatan, bukan informasi: ini sinyal keamanan yang
					// harus terlihat saat menelusuri insiden.
					_logger.LogWarning(
						"Login dikunci {LockoutMinutes} menit untuk kunci {Key} setelah {Failures} kegagalan",
						_settings.LockoutMinutes, key, counter.Failures);
				}
			}

			return remainingAttempts;
		}

		public void Reset(string username, string? ipAddress)
		{
			foreach (var key in KeysFor(username, ipAddress))
			{
				_counters.TryRemove(key, out _);
			}
		}

		/// <summary>
		/// Membersihkan entri kedaluwarsa. Dijalankan paling sering sekali per menit supaya
		/// login normal tidak menanggung biaya penyapuan, sekaligus mencegah kamus tumbuh
		/// tanpa batas saat diserang dari ribuan IP.
		/// </summary>
		private void Prune()
		{
			var now = DateTimeOffset.UtcNow;
			if (now - _lastPrune < TimeSpan.FromMinutes(1)) return;
			_lastPrune = now;

			var maxAge = TimeSpan.FromMinutes(_settings.LoginAttemptWindowMinutes + _settings.LockoutMinutes);

			foreach (var (key, counter) in _counters)
			{
				var expired = counter.LockedUntil is null
					? now - counter.FirstFailureAt > maxAge
					: now > counter.LockedUntil.Value;

				if (expired) _counters.TryRemove(key, out _);
			}
		}
	}
}
