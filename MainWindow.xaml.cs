using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinLANTransfer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WinLANTransfer
{
    public partial class MainWindow : Window
    {
        private readonly NetworkDiscoveryService _discoveryService = new();
        private readonly HttpTransferServer _transferServer = new();
        private readonly FileSenderService _senderService = new();

        public ObservableCollection<NetworkDevice> Devices { get; } = new();
        public ObservableCollection<TransferItem> Transfers { get; } = new();

        private NetworkDevice? _selectedDevice;

        private static string AppDisplayName => "Win LAN Transfer";

        public MainWindow()
        {
            InitializeComponent();
            Title = AppDisplayName;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            MainNav.SelectedItem = SendNavItem;

            LstDevices.ItemsSource = Devices;
            LstTransfers.ItemsSource = Transfers;

            string localIp = NetworkDiscoveryService.GetLocalIpAddress();
            TxtLocalIp.Text = $"IP: {localIp}:{_transferServer.Port}";
            TxtWebPortalUrl.Text = $"http://{localIp}:{_transferServer.Port}/";
            TxtSaveDir.Text = _transferServer.SaveDirectory;

            _discoveryService.OnDevicesUpdated += OnDevicesDiscovered;
            _transferServer.OnTransferReceived += OnTransferItemReceived;

            var savedHistory = TransferHistoryService.LoadHistory();
            if (savedHistory.Count > 0)
            {
                foreach (var item in savedHistory)
                {
                    Transfers.Add(item);
                }
            }
#if DEBUG
            else
            {
                LoadDemoData();
            }
#endif

            _discoveryService.StartDiscovery();
            _transferServer.Start();
        }

#if DEBUG
        private void LoadDemoData()
        {
            var demoDevices = new List<NetworkDevice>
            {
                new NetworkDevice { DeviceName = "MacBook Pro (Studio)", IpAddress = "192.168.1.102", Port = 53317 },
                new NetworkDevice { DeviceName = "Galaxy S24 Ultra", IpAddress = "192.168.1.115", Port = 53317 },
                new NetworkDevice { DeviceName = "Desktop-Gaming-PC", IpAddress = "192.168.1.140", Port = 53317 },
                new NetworkDevice { DeviceName = "iPad Air M2", IpAddress = "192.168.1.168", Port = 53317 }
            };

            foreach (var dev in demoDevices)
            {
                Devices.Add(dev);
            }
            EmptyDevicesPanel.Visibility = Visibility.Collapsed;
            if (Devices.Count > 0)
            {
                LstDevices.SelectedIndex = 0;
            }

            Transfers.Add(new TransferItem
            {
                FileName = "Project_Presentation_v2.pdf",
                FileSize = 14500000,
                TransferType = "File",
                Direction = "Incoming 📥",
                PeerName = "MacBook Pro (Studio)",
                PeerIp = "192.168.1.102",
                Status = "Completed",
                Progress = 100
            });

            Transfers.Add(new TransferItem
            {
                FileName = "Design_Assets_Archive.zip",
                FileSize = 345000000,
                TransferType = "File",
                Direction = "Outgoing 📤",
                PeerName = "Galaxy S24 Ultra",
                PeerIp = "192.168.1.115",
                Status = "Sending (82%)",
                Progress = 82
            });

            Transfers.Add(new TransferItem
            {
                TextContent = "https://github.com/microsoft/WinUI-Gallery",
                FileName = "Shared Link",
                FileSize = 0,
                TransferType = "Text",
                Direction = "Incoming 📥",
                PeerName = "Desktop-Gaming-PC",
                PeerIp = "192.168.1.140",
                Status = "Received",
                Progress = 100
            });

            Transfers.Add(new TransferItem
            {
                FileName = "4K_Benchmark_Video.mp4",
                FileSize = 1250000000,
                TransferType = "File",
                Direction = "Outgoing 📤",
                PeerName = "iPad Air M2",
                PeerIp = "192.168.1.168",
                Status = "Completed",
                Progress = 100
            });
        }
#endif

        private void MainNav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                SendTabContent.Visibility = tag == "SendTab" ? Visibility.Visible : Visibility.Collapsed;
                ReceiveTabContent.Visibility = tag == "ReceiveTab" ? Visibility.Visible : Visibility.Collapsed;
                HistoryTabContent.Visibility = tag == "HistoryTab" ? Visibility.Visible : Visibility.Collapsed;
                
            }
        }

        private void OnDevicesDiscovered(List<NetworkDevice> discoveredDevices)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Devices.Clear();
                foreach (var dev in discoveredDevices)
                {
                    Devices.Add(dev);
                }

#if DEBUG
                if (Devices.Count == 0)
                {
                    Devices.Add(new NetworkDevice { DeviceName = "MacBook Pro (Studio)", IpAddress = "192.168.1.102", Port = 53317 });
                    Devices.Add(new NetworkDevice { DeviceName = "Galaxy S24 Ultra", IpAddress = "192.168.1.115", Port = 53317 });
                    Devices.Add(new NetworkDevice { DeviceName = "Desktop-Gaming-PC", IpAddress = "192.168.1.140", Port = 53317 });
                    Devices.Add(new NetworkDevice { DeviceName = "iPad Air M2", IpAddress = "192.168.1.168", Port = 53317 });
                }
#endif

                EmptyDevicesPanel.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                if (_selectedDevice != null)
                {
                    var match = Devices.FirstOrDefault(d => d.IpAddress == _selectedDevice.IpAddress);
                    if (match != null)
                    {
                        LstDevices.SelectedItem = match;
                        _selectedDevice = match;
                    }
                    else
                    {
                        _selectedDevice = null;
                    }
                }

                if (_selectedDevice == null && Devices.Count > 0)
                {
                    LstDevices.SelectedIndex = 0;
                    _selectedDevice = Devices[0];
                }
            });
        }

        private void OnTransferItemReceived(TransferItem item)
        {
            if (item == null) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                var existing = Transfers.FirstOrDefault(x => x.FilePath == item.FilePath && x.FileName == item.FileName && !string.IsNullOrEmpty(x.FilePath));
                if (existing == null)
                {
                    Transfers.Insert(0, item);
                }

                if (item.Status == "Completed")
                {
                    if (item.TransferType == "File")
                    {
                        System.Diagnostics.Debug.WriteLine("📁 File Received" + ": " + $"Received '{item.FileName}' from {item.PeerName}");
                    }
                    else if (item.TransferType == "Text")
                    {
                        string textSnippet = item.TextContent?.Length > 60 ? item.TextContent.Substring(0, 57) + "..." : (item.TextContent ?? "");
                        System.Diagnostics.Debug.WriteLine("💬 Text Received" + ": " + $"Received from {item.PeerName}: \"{textSnippet}\"");
                    }
                }

                TransferHistoryService.SaveHistory(Transfers);
            });
        }

        private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await _discoveryService.TriggerAnnounceAsync();
        }

        private void LstDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstDevices.SelectedItem is NetworkDevice dev)
            {
                _selectedDevice = dev;
            }
        }

        private async void BtnPickFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null && Devices.Count > 0)
            {
                LstDevices.SelectedIndex = 0;
                _selectedDevice = Devices[0];
            }

            if (_selectedDevice == null)
            {
                ShowMessage("Please select a target device from the LAN list first.");
                return;
            }

            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await SendFileToDevice(_selectedDevice, file.Path);
            }
        }

        private async void BtnSendText_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null && Devices.Count > 0)
            {
                LstDevices.SelectedIndex = 0;
                _selectedDevice = Devices[0];
            }

            if (_selectedDevice == null)
            {
                ShowMessage("Please select a target device from the LAN list first.");
                return;
            }

            var xamlRoot = Content?.XamlRoot ?? MainNav?.XamlRoot;
            if (xamlRoot == null) return;

            SendTextDialog.XamlRoot = xamlRoot;
            await SendTextDialog.ShowAsync();
        }

        private async void SendTextDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string text = TxtSendInput.Text;
            if (string.IsNullOrWhiteSpace(text) || _selectedDevice == null) return;

            string targetName = _selectedDevice.DeviceName ?? "Device";
            var item = new TransferItem
            {
                TransferType = "Text",
                TextContent = text,
                Direction = "Outgoing",
                PeerName = targetName,
                PeerIp = _selectedDevice.IpAddress ?? "",
                Status = "Sending",
                Progress = 0
            };

            Transfers.Insert(0, item);
            bool success = await _senderService.SendTextAsync(_selectedDevice.IpAddress ?? "", _selectedDevice.Port, text, item);
            if (success)
            {
                System.Diagnostics.Debug.WriteLine("💬 Text Sent" + ": " + $"Successfully sent text to {targetName}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ Text Transfer Failed" + ": " + $"Failed to send text to {targetName}");
            }
            TransferHistoryService.SaveHistory(Transfers);
            TxtSendInput.Text = "";
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            ResetDropZoneBorder();

            if (_selectedDevice == null && Devices.Count > 0)
            {
                LstDevices.SelectedIndex = 0;
                _selectedDevice = Devices[0];
            }

            if (_selectedDevice == null)
            {
                ShowMessage("Please select a target device from the LAN list first.");
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                int count = 0;
                string lastFileName = "";
                foreach (var storageItem in items)
                {
                    if (storageItem is StorageFile file)
                    {
                        count++;
                        lastFileName = file.Name;
                        await SendFileToDevice(_selectedDevice, file.Path);
                    }
                }

                if (count > 0)
                {
                    ShowActiveDropPanel(count == 1 ? $"Sending '{lastFileName}' to {_selectedDevice.DeviceName}..." : $"Sending {count} files to {_selectedDevice.DeviceName}...");
                }
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DropZone.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
            DropZoneTitleText.Text = _selectedDevice != null ? $"Release to send to {_selectedDevice.DeviceName}" : "Select a device on the left first!";
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            ResetDropZoneBorder();
        }

        private void ResetDropZoneBorder()
        {
            DropZone.ClearValue(Border.BorderBrushProperty);
            DropZoneTitleText.Text = "Drag & Drop Files Here";
        }

        private void ShowActiveDropPanel(string detailMessage)
        {
            if (TxtDroppedStatusDetail != null)
                TxtDroppedStatusDetail.Text = detailMessage ?? "";
            if (DropZoneDefaultPanel != null)
                DropZoneDefaultPanel.Visibility = Visibility.Collapsed;
            if (DropZoneActivePanel != null)
                DropZoneActivePanel.Visibility = Visibility.Visible;
        }

        private async Task SendFileToDevice(NetworkDevice device, string filePath)
        {
            if (device == null || string.IsNullOrEmpty(filePath)) return;

            string deviceName = device.DeviceName ?? device.IpAddress ?? "Device";
            string fileName = Path.GetFileName(filePath) ?? "File";

            ShowActiveDropPanel($"Sending '{fileName}' to {deviceName}...");

            var item = new TransferItem
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
                TransferType = "File",
                Direction = "Outgoing",
                PeerName = deviceName,
                PeerIp = device.IpAddress ?? "",
                Status = "Sending",
                Progress = 0
            };

            Transfers.Insert(0, item);
            bool success = await _senderService.SendFileAsync(device.IpAddress ?? "", device.Port, filePath, item);
            if (success)
            {
                System.Diagnostics.Debug.WriteLine("📤 File Sent" + ": " + $"Successfully sent '{fileName}' to {deviceName}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ File Transfer Failed" + ": " + $"Failed to send '{fileName}' to {deviceName}");
            }
            TransferHistoryService.SaveHistory(Transfers);
        }

        private void BtnCopyWebLink_Click(object sender, RoutedEventArgs e)
        {
            var dp = new DataPackage();
            dp.SetText(TxtWebPortalUrl.Text);
            Clipboard.SetContent(dp);
        }

        private void BtnOpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_transferServer.SaveDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = _transferServer.SaveDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void LstTransfers_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TransferItem item)
            {
                OpenFileOrContainingFolder(item);
            }
        }

        private void OpenFileOrContainingFolder(TransferItem item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
                else
                {
                    string folder = _transferServer.SaveDirectory;
                    if (!string.IsNullOrWhiteSpace(item.FilePath))
                    {
                        string? dir = Path.GetDirectoryName(item.FilePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            folder = dir;
                        }
                    }

                    if (Directory.Exists(folder))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }

        private async void ShowMessage(string msg)
        {
            var xamlRoot = Content?.XamlRoot ?? MainNav?.XamlRoot;
            if (xamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = AppDisplayName,
                Content = msg,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
