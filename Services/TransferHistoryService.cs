using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinLANTransfer.Services
{
    public class TransferHistoryService
    {
        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinLANTransfer",
            "history.json");

        public static List<TransferItem> LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    string json = File.ReadAllText(HistoryFilePath);
                    var list = JsonConvert.DeserializeObject<List<TransferItem>>(json);
                    return list ?? new List<TransferItem>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load transfer history: {ex.Message}");
            }
            return new List<TransferItem>();
        }

        public static void SaveHistory(IEnumerable<TransferItem> items)
        {
            try
            {
                string? dir = Path.GetDirectoryName(HistoryFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Limit history to 500 items max
                var itemList = items.Take(500).ToList();
                string json = JsonConvert.SerializeObject(itemList, Formatting.Indented);
                File.WriteAllText(HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save transfer history: {ex.Message}");
            }
        }
    }
}
