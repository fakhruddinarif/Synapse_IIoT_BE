using Core.Interface;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Mengirim frame realtime lewat SignalR, hanya ke grup perangkat yang bersangkutan.
	///
	/// KENAPA BUKAN <c>Clients.All</c>:
	///
	/// Implementasi sebelumnya menyiarkan data setiap perangkat ke setiap koneksi. Dua
	/// akibatnya sama-sama serius. Pertama beban: sepuluh perangkat dan dua puluh tab menjadi
	/// dua ratus aliran ketika yang benar-benar dibutuhkan mungkin dua. Kedua kerahasiaan:
	/// operator yang hanya membuka satu lini tetap menerima data seluruh pabrik di soketnya —
	/// tidak terlihat di layar, tetapi ada di jaringan dan di konsol peramban.
	/// </summary>
	public sealed class SignalRRealtimePublisher<THub>(
		IHubContext<THub> hubContext,
		IConfiguration configuration) : IRealtimePublisher where THub : Hub
	{
		private readonly string _groupPrefix =
			configuration["SignalRSettings:GroupPrefix:Device"] ?? "device_";

		public Task PublishAsync(Guid deviceId, RealtimeFrame frame, CancellationToken ct) =>
			hubContext.Clients
				.Group($"{_groupPrefix}{deviceId}")
				.SendAsync("TagFrame", frame, ct);
	}
}
