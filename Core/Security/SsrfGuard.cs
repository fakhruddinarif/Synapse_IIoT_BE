using System.Net;
using System.Net.Sockets;

namespace Core.Security
{
	/// <summary>
	/// Penyaring tujuan untuk endpoint discovery yang membuat SERVER memanggil alamat
	/// pilihan pemanggil (SSRF).
	///
	/// KENAPA TIDAK MEMBLOKIR SELURUH ALAMAT PRIVAT — pertanyaan yang wajar, karena itulah
	/// mitigasi SSRF standar di aplikasi web biasa:
	///
	/// Synapse adalah gateway industri. Perangkat lapangan HAMPIR SELALU berada di
	/// 192.168.x.x atau 10.x.x.x, dan memprobenya adalah fungsi utama produk ini.
	/// Memblokir alamat privat berarti memblokir seluruh gunanya. Jadi pertahanannya
	/// disusun berlapis, bukan bergantung pada satu daftar:
	///
	/// <list type="number">
	/// <item>Endpoint dibatasi peran ADMIN/ENGINEER — orang yang memang berwenang
	/// mengonfigurasi akuisisi.</item>
	/// <item>Setiap pemanggilan tercatat di jejak audit beserta pelakunya.</item>
	/// <item>Alamat yang TIDAK MUNGKIN berisi perangkat industri diblokir di sini:
	/// link-local (termasuk endpoint metadata cloud 169.254.169.254 yang menyimpan
	/// kredensial instans), multicast, dan alamat tak tentu.</item>
	/// </list>
	///
	/// BATAS YANG DIKETAHUI: pemeriksaan ini hanya melihat alamat literal. Nama host
	/// diperiksa terhadap daftar nama metadata yang dikenal, tapi resolusi DNS-nya tidak
	/// diperiksa — sehingga DNS rebinding (nama yang mula-mula menunjuk alamat sah lalu
	/// berubah) masih mungkin. Menutupnya butuh resolusi di sini plus pengikatan koneksi
	/// ke alamat hasil resolusi itu; dicatat sebagai pekerjaan lanjutan, dan sementara ini
	/// ditanggung oleh dua lapisan pertama di atas.
	/// </summary>
	public static class SsrfGuard
	{
		/// <summary>
		/// Nama host yang dipakai penyedia cloud untuk melayani metadata instans. Berbeda
		/// dari alamat 169.254.169.254, nama-nama ini tidak terlihat sebagai link-local.
		/// </summary>
		private static readonly string[] MetadataHosts =
		{
			"metadata.google.internal",
			"metadata.goog",
			"instance-data",
			"instance-data.ec2.internal",
			"metadata.azure.com",
			"nimbula"
		};

		/// <summary>
		/// <c>null</c> berarti tujuan boleh dihubungi. Selain itu, isinya alasan penolakan
		/// yang siap ditampilkan ke pengguna.
		/// </summary>
		public static string? Inspect(Uri uri, bool allowLinkLocal)
		{
			var host = uri.DnsSafeHost;

			if (string.IsNullOrWhiteSpace(host))
			{
				return "URL tidak memuat host.";
			}

			foreach (var metadataHost in MetadataHosts)
			{
				if (host.Equals(metadataHost, StringComparison.OrdinalIgnoreCase))
				{
					return "Alamat metadata instans cloud tidak boleh diprobe.";
				}
			}

			if (!IPAddress.TryParse(host, out var address))
			{
				// Nama host biasa. Diteruskan — lihat catatan "batas yang diketahui".
				return null;
			}

			// IPv6 yang membungkus IPv4 (::ffff:169.254.169.254) harus dinilai sebagai
			// IPv4-nya, kalau tidak seluruh pemeriksaan di bawah bisa dilewati hanya dengan
			// menulis alamat dalam bentuk lain.
			if (address.IsIPv4MappedToIPv6)
			{
				address = address.MapToIPv4();
			}

			if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
			{
				return "Alamat tak tentu (0.0.0.0) tidak bisa diprobe.";
			}

			if (address.Equals(IPAddress.Broadcast))
			{
				return "Alamat broadcast tidak bisa diprobe.";
			}

			if (IsLinkLocal(address) && !allowLinkLocal)
			{
				return "Alamat link-local (169.254.0.0/16) diblokir karena di sana terdapat " +
					   "endpoint metadata instans, bukan perangkat lapangan.";
			}

			if (IsMulticast(address))
			{
				return "Alamat multicast tidak bisa diprobe.";
			}

			return null;
		}

		private static bool IsLinkLocal(IPAddress address)
		{
			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				return address.IsIPv6LinkLocal;
			}

			var bytes = address.GetAddressBytes();
			return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
		}

		private static bool IsMulticast(IPAddress address)
		{
			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				return address.IsIPv6Multicast;
			}

			var bytes = address.GetAddressBytes();
			// 224.0.0.0/4
			return bytes.Length == 4 && bytes[0] >= 224 && bytes[0] <= 239;
		}
	}
}
