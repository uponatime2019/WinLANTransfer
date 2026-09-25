using System;

namespace WinLANTransfer.Services
{
    public class NetworkDevice
    {
        public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
        public string DeviceName { get; set; } = Environment.MachineName;
        public string IpAddress { get; set; } = "";
        public int Port { get; set; } = 53317;
        public string DeviceType { get; set; } = "Windows PC"; // Windows PC, Mac, Mobile, Web
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool IsOnline => (DateTime.Now - LastSeen).TotalSeconds < 15;

        public override string ToString() => $"{DeviceName} ({IpAddress})";
    }
}
