using Core.DTOs;
using Core.DTOs.Discovery;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	/// <summary>
	/// Penemuan struktur data perangkat — sumber daftar key untuk pemilih key di UI.
	///
	/// Dibatasi ke ADMIN dan ENGINEER bukan karena datanya sensitif, tapi karena endpoint ini
	/// membuat SERVER memanggil alamat yang ditentukan pemanggil (SSRF). Di jaringan pabrik,
	/// gateway biasanya berkaki dua — bisa menjangkau segmen OT yang tidak bisa dijangkau
	/// browser — sehingga kemampuan ini harus dipegang peran yang memang berwenang
	/// mengonfigurasi akuisisi.
	/// </summary>
	[ApiController]
	[Route("api/discovery")]
	[Authorize(Roles = "ADMIN,ENGINEER")]
	public class DiscoveryController : ControllerBase
	{
		private readonly IDiscoveryService _discoveryService;
		private readonly ILogger<DiscoveryController> _logger;

		public DiscoveryController(IDiscoveryService discoveryService, ILogger<DiscoveryController> logger)
		{
			_discoveryService = discoveryService;
			_logger = logger;
		}

		/// <summary>
		/// Memanggil endpoint HTTP sekali dan mengembalikan daftar key yang bisa dipetakan
		/// menjadi tag.
		/// POST /api/discovery/http
		/// </summary>
		[HttpPost("http")]
		public async Task<IActionResult> ProbeHttp([FromBody] HttpProbeRequestDto dto, CancellationToken ct)
		{
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values
					.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
					.ToList();
				return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid", errors));
			}

			try
			{
				var result = await _discoveryService.ProbeHttpAsync(dto, ct);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Kesalahan tak terduga saat probe HTTP");
				return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat memprobe endpoint"));
			}
		}

		/// <summary>
		/// Menyambung ke broker MQTT, mendengarkan filter topik selama durasi yang diminta,
		/// dan mengembalikan topik beserta daftar key per topik.
		/// POST /api/discovery/mqtt
		/// </summary>
		[HttpPost("mqtt")]
		public async Task<IActionResult> SniffMqtt([FromBody] MqttSniffRequestDto dto, CancellationToken ct)
		{
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values
					.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
					.ToList();
				return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid", errors));
			}

			try
			{
				var result = await _discoveryService.SniffMqttAsync(dto, ct);
				return StatusCode(result.Status, result);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Kesalahan tak terduga saat sniff MQTT");
				return StatusCode(500, ApiResponse<object>.Fail(500, "Terjadi kesalahan saat mendengarkan broker"));
			}
		}
	}
}
