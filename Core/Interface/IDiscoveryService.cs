using Core.DTOs;
using Core.DTOs.Discovery;

namespace Core.Interface
{
	/// <summary>
	/// Penemuan struktur data dari perangkat, sebelum tag dibuat.
	///
	/// Tanpa ini, membuat tag berarti mengetik JSONPath dengan benar dari ingatan — dan
	/// salahnya baru terlihat berjam-jam kemudian sebagai kolom kosong di tabel tujuan.
	/// </summary>
	public interface IDiscoveryService
	{
		/// <summary>
		/// Memanggil endpoint HTTP sekali, lalu mendatarkan payload-nya menjadi daftar key.
		/// Kegagalan panggilan bukan exception: hasilnya dikembalikan dengan
		/// <c>IsSuccess = false</c> beserta alasannya, karena UI perlu menampilkannya
		/// sebagai umpan balik form, bukan sebagai error 500.
		/// </summary>
		Task<ApiResponse<HttpProbeResultDto>> ProbeHttpAsync(HttpProbeRequestDto request, CancellationToken ct = default);

		/// <summary>
		/// Menyambung ke broker MQTT, berlangganan filter topik, dan mengumpulkan pesan
		/// selama durasi yang diminta. Mengembalikan topik yang benar-benar muncul beserta
		/// daftar key per topik.
		/// </summary>
		Task<ApiResponse<MqttSniffResultDto>> SniffMqttAsync(MqttSniffRequestDto request, CancellationToken ct = default);
	}
}
