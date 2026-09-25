using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinLANTransfer.Services
{
    public class TransferItem : INotifyPropertyChanged
    {
        private string _status = "Pending";
        private double _progress = 0;
        private double _speedMbPerSec = 0;
        private string _formattedSize = "";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; } = 0;
        public string TransferType { get; set; } = "File"; // "File" or "Text"
        public string TextContent { get; set; } = "";
        public string Direction { get; set; } = "Outgoing"; // "Outgoing" or "Incoming"
        public string PeerName { get; set; } = "";
        public string PeerIp { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public double SpeedMbPerSec
        {
            get => _speedMbPerSec;
            set { _speedMbPerSec = value; OnPropertyChanged(); }
        }

        public string FormattedSize
        {
            get
            {
                if (!string.IsNullOrEmpty(_formattedSize)) return _formattedSize;
                if (FileSize <= 0) return TransferType == "Text" ? $"{TextContent.Length} chars" : "0 B";
                double kb = FileSize / 1024.0;
                double mb = kb / 1024.0;
                double gb = mb / 1024.0;
                if (gb >= 1.0) return $"{gb:F2} GB";
                if (mb >= 1.0) return $"{mb:F2} MB";
                if (kb >= 1.0) return $"{kb:F1} KB";
                return $"{FileSize} B";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
