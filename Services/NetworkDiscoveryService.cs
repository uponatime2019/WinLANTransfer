using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinLANTransfer.Services
{
    public class NetworkDiscoveryService
    {
        private const int DISCOVERY_PORT = 53317;
        private UdpClient? _udpListener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, NetworkDevice> _devices = new();

        public event Action<List<NetworkDevice>>? OnDevicesUpdated;

        public static string GetLocalIpAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            string ipStr = addr.Address.ToString();
                            if (!ipStr.StartsWith("169.254.")) // Exclude APIPA
                                return ipStr;
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void StartDiscovery()
        {
            StopDiscovery();
            _cts = new CancellationTokenSource();

            Task.Run(() => ListenLoopAsync(_cts.Token));
            Task.Run(() => BroadcastLoopAsync(_cts.Token));
            Task.Run(() => CleanupLoopAsync(_cts.Token));
        }

        public void StopDiscovery()
        {
            _cts?.Cancel();
            _udpListener?.Close();
            _udpListener = null;
        }

        public async Task TriggerAnnounceAsync()
        {
            try
            {
                using var client = new UdpClient();
                client.EnableBroadcast = true;
                string localIp = GetLocalIpAddress();

                var payload = new
                {
                    type = "PING",
                    deviceName = Environment.MachineName,
                    ipAddress = localIp,
                    port = DISCOVERY_PORT,
                    deviceType = "Windows PC"
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
                var broadcastEP = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
                await client.SendAsync(bytes, bytes.Length, broadcastEP);
            }
            catch { }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            try
            {
                _udpListener = new UdpClient();
                _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));

                while (!token.IsCancellationRequested)
                {
                    var result = await _udpListener.ReceiveAsync(token);
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    string senderIp = result.RemoteEndPoint.Address.ToString();

                    string localIp = GetLocalIpAddress();
                    if (senderIp == localIp || senderIp == "127.0.0.1") continue;

                    dynamic? data = JsonConvert.DeserializeObject(json);
                    if (data != null)
                    {
                        string name = data.deviceName?.ToString() ?? senderIp;
                        int port = data.port != null ? (int)data.port : DISCOVERY_PORT;
                        string devType = data.deviceType?.ToString() ?? "Device";

                        var device = new NetworkDevice
                        {
                            DeviceId = $"{senderIp}:{port}",
                            DeviceName = name,
                            IpAddress = senderIp,
                            Port = port,
                            DeviceType = devType,
                            LastSeen = DateTime.Now
                        };

                        _devices[device.DeviceId] = device;
                        NotifyUpdate();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task BroadcastLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await TriggerAnnounceAsync();
                try { await Task.Delay(4000, token); } catch { break; }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool changed = false;
                var now = DateTime.Now;

                foreach (var kvp in _devices.ToList())
                {
                    if ((now - kvp.Value.LastSeen).TotalSeconds > 12)
                    {
                        _devices.TryRemove(kvp.Key, out _);
                        changed = true;
                    }
                }

                if (changed) NotifyUpdate();
                try { await Task.Delay(3000, token); } catch { break; }
            }
        }

        private void NotifyUpdate()
        {
            var list = _devices.Values.Where(x => x.IsOnline).OrderBy(x => x.DeviceName).ToList();
            OnDevicesUpdated?.Invoke(list);
        }
    }
}
