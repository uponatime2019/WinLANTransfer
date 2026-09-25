using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinLANTransfer.Services
{
    public class HttpTransferServer
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        public int Port { get; private set; } = 53317;
        public string SaveDirectory { get; set; }

        public event Action<TransferItem>? OnTransferReceived;

        public HttpTransferServer()
        {
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WinLANTransfer");
            Directory.CreateDirectory(downloadsPath);
            SaveDirectory = downloadsPath;
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();

            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();

                Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start HttpTransferServer: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
            }
            catch { }
            _listener = null;
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TcpListener accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    var memoryStream = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    int headerEndIndex = -1;
                    int readBytes;

                    while ((readBytes = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        memoryStream.Write(buffer, 0, readBytes);
                        byte[] currentData = memoryStream.ToArray();
                        headerEndIndex = FindHeaderEnd(currentData);
                        if (headerEndIndex != -1) break;
                    }

                    if (headerEndIndex == -1) return;

                    byte[] headerBytes = memoryStream.ToArray();
                    string headerText = Encoding.UTF8.GetString(headerBytes, 0, headerEndIndex);

                    string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 2) return;

                    string method = requestLine[0].ToUpperInvariant();
                    string path = Uri.UnescapeDataString(requestLine[1]);
                    int queryIdx = path.IndexOf('?');
                    if (queryIdx >= 0) path = path.Substring(0, queryIdx);

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colonIdx = lines[i].IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = lines[i].Substring(0, colonIdx).Trim();
                            string val = lines[i].Substring(colonIdx + 1).Trim();
                            headers[key] = val;
                        }
                    }

                    string remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();

                    int headerLengthInBytes = headerEndIndex + 4;
                    int leftoverCount = (int)memoryStream.Length - headerLengthInBytes;
                    byte[] leftoverBytes = new byte[leftoverCount];
                    if (leftoverCount > 0)
                    {
                        Buffer.BlockCopy(headerBytes, headerLengthInBytes, leftoverBytes, 0, leftoverCount);
                    }

                    if (method == "OPTIONS")
                    {
                        await SendResponseAsync(stream, 200, "OK", "text/plain", "");
                        return;
                    }

                    if (method == "POST" && path.Equals("/api/text", StringComparison.OrdinalIgnoreCase))
                    {
                        long contentLength = 0;
                        if (headers.TryGetValue("Content-Length", out string? clStr))
                            long.TryParse(clStr, out contentLength);

                        byte[] bodyBuffer = new byte[contentLength > 0 ? contentLength : leftoverCount];
                        int bodyReadTotal = 0;
                        if (leftoverCount > 0)
                        {
                            int copyLen = Math.Min(leftoverCount, bodyBuffer.Length);
                            Buffer.BlockCopy(leftoverBytes, 0, bodyBuffer, 0, copyLen);
                            bodyReadTotal = copyLen;
                        }

                        while (bodyReadTotal < contentLength)
                        {
                            int r = await stream.ReadAsync(bodyBuffer, bodyReadTotal, (int)contentLength - bodyReadTotal);
                            if (r <= 0) break;
                            bodyReadTotal += r;
                        }

                        string body = Encoding.UTF8.GetString(bodyBuffer, 0, bodyReadTotal);
                        dynamic? data = JsonConvert.DeserializeObject(body);
                        string text = data?.text?.ToString() ?? body;
                        string sender = data?.sender?.ToString() ?? remoteIp;

                        var item = new TransferItem
                        {
                            TransferType = "Text",
                            TextContent = text,
                            Direction = "Incoming",
                            PeerName = sender,
                            PeerIp = remoteIp,
                            Status = "Completed",
                            Progress = 100
                        };

                        OnTransferReceived?.Invoke(item);
                        await SendResponseAsync(stream, 200, "OK", "application/json", JsonConvert.SerializeObject(new { status = "OK" }));
                    }
                    else if (method == "POST" && path.Equals("/api/send", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = "received_file_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        if (headers.TryGetValue("X-File-Name", out string? xFileName) && !string.IsNullOrWhiteSpace(xFileName))
                        {
                            try { fileName = WebUtility.UrlDecode(xFileName); } catch { fileName = xFileName; }
                        }

                        string targetPath = Path.Combine(SaveDirectory, fileName);
                        int counter = 1;
                        while (File.Exists(targetPath))
                        {
                            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                            string ext = Path.GetExtension(fileName);
                            targetPath = Path.Combine(SaveDirectory, $"{nameNoExt}_{counter}{ext}");
                            counter++;
                        }

                        long contentLength = 0;
                        if (headers.TryGetValue("Content-Length", out string? clStr))
                            long.TryParse(clStr, out contentLength);

                        var item = new TransferItem
                        {
                            FileName = Path.GetFileName(targetPath),
                            FilePath = targetPath,
                            FileSize = contentLength,
                            TransferType = "File",
                            Direction = "Incoming",
                            PeerName = remoteIp,
                            PeerIp = remoteIp,
                            Status = "Receiving",
                            Progress = 0
                        };

                        OnTransferReceived?.Invoke(item);

                        using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            long receivedBytes = 0;

                            if (leftoverCount > 0)
                            {
                                await fs.WriteAsync(leftoverBytes, 0, leftoverCount);
                                receivedBytes += leftoverCount;
                            }

                            byte[] chunk = new byte[65536];
                            var startTime = DateTime.Now;

                            while (contentLength == 0 || receivedBytes < contentLength)
                            {
                                int toRead = chunk.Length;
                                if (contentLength > 0)
                                {
                                    long remaining = contentLength - receivedBytes;
                                    if (remaining <= 0) break;
                                    if (remaining < toRead) toRead = (int)remaining;
                                }

                                int r = await stream.ReadAsync(chunk, 0, toRead);
                                if (r <= 0) break;

                                await fs.WriteAsync(chunk, 0, r);
                                receivedBytes += r;

                                if (contentLength > 0)
                                {
                                    item.Progress = (double)receivedBytes / contentLength * 100.0;
                                    double elapsedSec = (DateTime.Now - startTime).TotalSeconds;
                                    if (elapsedSec > 0)
                                    {
                                        item.SpeedMbPerSec = (receivedBytes / (1024.0 * 1024.0)) / elapsedSec;
                                    }
                                }
                            }
                        }

                        item.Status = "Completed";
                        item.Progress = 100;
                        OnTransferReceived?.Invoke(item);

                        await SendResponseAsync(stream, 200, "OK", "application/json", JsonConvert.SerializeObject(new { status = "OK", path = targetPath }));
                    }
                    else
                    {
                        string html = GetWebPortalHtml();
                        await SendResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html);
                    }
                }
                catch { }
            }
        }

        private int FindHeaderEnd(byte[] data)
        {
            for (int i = 0; i < data.Length - 3; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                {
                    return i;
                }
            }
            return -1;
        }

        private async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            sb.AppendLine($"Content-Type: {contentType}");
            sb.AppendLine($"Content-Length: {bodyBytes.Length}");
            sb.AppendLine("Access-Control-Allow-Origin: *");
            sb.AppendLine("Access-Control-Allow-Headers: *");
            sb.AppendLine("Access-Control-Allow-Methods: *");
            sb.AppendLine("Connection: close");
            sb.AppendLine();

            byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            }
            await stream.FlushAsync();
        }

        private string GetWebPortalHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8""/>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
    <title>Win LAN Transfer - Web Upload</title>
    <style>
        body { font-family: system-ui, -apple-system, sans-serif; background: #0f172a; color: #f8fafc; text-align: center; padding: 20px; }
        .card { background: #1e293b; border-radius: 16px; max-width: 480px; margin: 20px auto; padding: 30px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
        h2 { margin-top: 0; color: #38bdf8; }
        .drop-zone { border: 2px dashed #38bdf8; border-radius: 12px; padding: 40px 20px; background: #0f172a; cursor: pointer; margin-bottom: 20px; }
        input[type=file] { display: none; }
        button { background: #0284c7; color: white; border: none; padding: 12px 24px; font-size: 16px; border-radius: 8px; cursor: pointer; font-weight: bold; width: 100%; }
        button:hover { background: #0369a1; }
        textarea { width: 100%; height: 80px; background: #0f172a; border: 1px solid #334155; border-radius: 8px; color: white; padding: 10px; margin-bottom: 10px; box-sizing: border-box; }
        #status { margin-top: 15px; font-weight: bold; color: #4ade80; }
    </style>
</head>
<body>
    <div class=""card"">
        <h2>⚡ Win LAN Transfer Web Portal</h2>
        <p>Send files directly to this Windows PC without installing an app!</p>
        
        <div class=""drop-zone"" onclick=""document.getElementById('fileInput').click()"">
            📁 Click to Select Files or Drag &amp; Drop
            <input type=""file"" id=""fileInput"" onchange=""uploadFile()"" />
        </div>

        <h3>Or Send Text / Link:</h3>
        <textarea id=""textInput"" placeholder=""Paste text or URL here...""></textarea>
        <button onclick=""sendText()"">Send Text to PC</button>

        <div id=""status""></div>
    </div>

    <script>
        async function uploadFile() {
            const input = document.getElementById('fileInput');
            if (!input.files.length) return;
            const file = input.files[0];
            const status = document.getElementById('status');
            status.innerText = 'Uploading ' + file.name + '...';

            try {
                const res = await fetch('/api/send', {
                    method: 'POST',
                    headers: { 'X-File-Name': encodeURIComponent(file.name) },
                    body: file
                });
                if (res.ok) {
                    status.innerText = '✅ ' + file.name + ' sent successfully!';
                } else {
                    status.innerText = '❌ Failed to send file.';
                }
            } catch (e) {
                status.innerText = '❌ Error: ' + e.message;
            }
        }

        async function sendText() {
            const text = document.getElementById('textInput').value;
            if (!text) return;
            const status = document.getElementById('status');
            status.innerText = 'Sending text...';

            try {
                const res = await fetch('/api/text', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ text: text, sender: 'Mobile Browser' })
                });
                if (res.ok) {
                    status.innerText = '✅ Text sent to PC!';
                    document.getElementById('textInput').value = '';
                } else {
                    status.innerText = '❌ Failed to send text.';
                }
            } catch (e) {
                status.innerText = '❌ Error: ' + e.message;
            }
        }
    </script>
</body>
</html>";
        }
    }
}
