using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class ModbusTcpConfig
    {
        [Required]
        public string IPAddress { get; set; } = "127.0.0.1";

        [Required]
        public int Port { get; set; } = 502;

        [Required]
        public int SlaveId { get; set; } = 1;

        [Required]
        public int ConnectionTimeout { get; set; } = 5000; // Connection timeout in milliseconds

        /// <summary>
        /// Urutan dua register yang membentuk satu angka 32-bit (INT32/UINT32/FLOAT).
        /// <c>false</c> = word tinggi lebih dulu (paling umum), <c>true</c> = word rendah dulu.
        ///
        /// Modbus TIDAK membakukan hal ini, jadi ia berbeda antar vendor. Salah pilih tidak
        /// memunculkan galat apa pun — nilainya sekadar salah, dan biasanya salah dengan cara
        /// yang mencolok (mis. 1,2 bar menjadi 4,6E+37). Kalau nilai 32-bit terlihat ngawur
        /// sementara nilai 16-bit benar, setelan inilah yang perlu dibalik.
        /// </summary>
        public bool WordSwap { get; set; }
    }
}