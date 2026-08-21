using Core.DTOs;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
	/// <summary>
	/// Jendela ke dalam penjadwal akuisisi.
	///
	/// Tanpa endpoint ini, satu-satunya cara mengetahui apakah gateway benar-benar menarik data
	/// adalah menunggu grafik terlihat aneh. Perangkat yang gagal terhubung, kelas scan yang
	/// aktif, jumlah sampel yang tertahan di buffer, dan jeda yang sedang berlangsung semuanya
	/// harus bisa dilihat — sebelum operator yang menanyakannya.
	/// </summary>
	[ApiController]
	[Route("api/acquisition")]
	[Authorize]
	public class AcquisitionController(IAcquisitionControl acquisition) : ControllerBase
	{
		[HttpGet("status")]
		public ActionResult<ApiResponse<AcquisitionStatus>> GetStatus()
		{
			var status = acquisition.GetStatus();

			return Ok(ApiResponse<AcquisitionStatus>.Success(
				status,
				$"{status.DeviceCount} perangkat, {status.TagCount} tag diakuisisi"));
		}

		/// <summary>
		/// Memaksa penyusunan ulang rencana. Untuk pemulihan manual saat konfigurasi diubah
		/// langsung di database, di luar jalur API.
		/// </summary>
		[HttpPost("reload")]
		[Authorize(Roles = "ADMIN,ENGINEER")]
		public ActionResult<ApiResponse<object>> Reload()
		{
			acquisition.RequestReload("permintaan manual");
			return Ok(ApiResponse<object>.Success(null!, "Penyusunan ulang rencana diminta"));
		}
	}
}
