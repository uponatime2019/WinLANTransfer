using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WinLANTransfer.Services
{
    public class FileSenderService
    {
        private readonly HttpClient _httpClient;

        public FileSenderService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        }

        public async Task<bool> SendFileAsync(string targetIp, int targetPort, string filePath, TransferItem item, Action<double, double>? onProgress = null)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                string fileName = Path.GetFileName(filePath);
                long totalBytes = new FileInfo(filePath).Length;
                item.FileSize = totalBytes;
                item.FileName = fileName;
                item.Status = "Sending";

                string url = $"http://{targetIp}:{targetPort}/api/send";

                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("X-File-Name", Uri.EscapeDataString(fileName));

                var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    item.Status = "Completed";
                    item.Progress = 100;
                    return true;
                }
            }
            catch (Exception ex)
            {
                item.Status = "Failed: " + ex.Message;
            }
            return false;
        }

        public async Task<bool> SendTextAsync(string targetIp, int targetPort, string text, TransferItem item)
        {
            try
            {
                item.Status = "Sending";
                string url = $"http://{targetIp}:{targetPort}/api/text";

                var payload = new
                {
                    text = text,
                    sender = Environment.MachineName
                };

                string json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    item.Status = "Completed";
                    item.Progress = 100;
                    return true;
                }
            }
            catch (Exception ex)
            {
                item.Status = "Failed: " + ex.Message;
            }
            return false;
        }
    }
}
