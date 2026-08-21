using Core.Acquisition;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Menyusun rencana akuisisi dari konfigurasi di database.
	///
	/// Satu-satunya tempat entity EF diterjemahkan menjadi rencana runtime. Setelah titik ini,
	/// tidak ada satu pun bagian jalur akuisisi yang menyentuh <c>DbContext</c> — jalur panas
	/// bekerja dengan record immutable saja. Itu bukan kerapian belaka: <c>DbContext</c> tidak
	/// aman dipakai bersamaan, dan sebuah scan loop yang memegangnya akan meledak secara acak
	/// begitu dua perangkat membaca pada saat yang sama.
	/// </summary>
	public sealed class DbAcquisitionPlanSource(
		IServiceScopeFactory scopeFactory,
		ILogger<DbAcquisitionPlanSource> logger) : IAcquisitionPlanSource
	{
		public async Task<IReadOnlyList<DevicePlan>> GetActivePlansAsync(CancellationToken ct)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

			var devices = await db.Devices
				.AsNoTracking()
				.Where(d => d.IsEnabled && d.DeletedAt == null)
				.ToListAsync(ct);

			if (devices.Count == 0) return [];

			var deviceIds = devices.Select(d => d.Id).ToList();

			var tags = await db.Tags
				.AsNoTracking()
				.Where(t => deviceIds.Contains(t.DeviceId) && t.IsActive && t.DeletedAt == null)
				.ToListAsync(ct);

			var byDevice = tags.GroupBy(t => t.DeviceId).ToDictionary(g => g.Key, g => g.ToList());
			var plans = new List<DevicePlan>(devices.Count);

			foreach (var device in devices)
			{
				if (!byDevice.TryGetValue(device.Id, out var deviceTags) || deviceTags.Count == 0)
				{
					// Perangkat tanpa tag tidak dibaca. Menariknya tanpa tag berarti membebani
					// jaringan untuk data yang tidak ada tujuannya.
					logger.LogDebug("Perangkat {Device} tidak punya tag aktif; dilewati", device.Name);
					continue;
				}

				var scanInterval = Math.Max(100, device.PollingInterval);

				plans.Add(new DevicePlan
				{
					DeviceId = device.Id,
					DeviceName = device.Name,
					Protocol = device.Protocol,
					ConnectionConfigJson = device.ConnectionConfigJson,
					ScanIntervalMs = scanInterval,
					Tags = deviceTags.Select(t => ToTagPlan(t, scanInterval)).ToList()
				});
			}

			return plans;
		}

		private static TagPlan ToTagPlan(Core.Entities.Tag tag, int deviceScanMs)
		{
			var scan = tag.ScanIntervalMs > 0 ? tag.ScanIntervalMs : deviceScanMs;

			return new TagPlan
			{
				TagId = tag.Id,
				DeviceId = tag.DeviceId,
				Name = tag.Name,
				Address = tag.Address,
				SourceTopic = tag.SourceTopic,
				DataType = tag.DataType,
				ScanIntervalMs = scan,
				IsScaled = tag.IsScaled,
				RawMin = tag.RawMin ?? 0,
				RawMax = tag.RawMax ?? 0,
				EuMin = tag.EuMin ?? 0,
				EuMax = tag.EuMax ?? 0,
				StoreMode = tag.StoreMode switch
				{
					1 => StoreMode.Deadband,
					2 => StoreMode.OnChange,
					_ => StoreMode.Full
				},
				DeadbandAbs = tag.DeadbandAbs,
				DeadbandPct = tag.DeadbandPct,
				MaxStoreGapMs = tag.MaxStoreGapMs > 0 ? tag.MaxStoreGapMs : 60_000,

				// Toleransi basi diturunkan dari laju scan, bukan angka tetap: tag 5 detik yang
				// dinilai basi setelah 5 detik akan berkedip Stale pada setiap tick yang
				// sedikit terlambat, dan operator berhenti mempercayai penandanya.
				StaleAfterMs = Math.Max(5_000, scan * 3)
			};
		}
	}
}
