using System.Collections.ObjectModel;
using System.Net.Sockets;

namespace PS5PayloadSender;

public partial class PayloadPage : ContentPage
{
    private readonly ObservableCollection<string> _logLines = new();
    private string? _selectedFilePath;
    private int _selectedBundledIndex = -1;

    private static readonly (string Asset, string Display)[] BundledPayloads =
    {
        ("payloads/etaHEN_v2.5B.bin", "etaHEN v2.5B"),
        ("payloads/ftpsrv_ps5-payload_v0.21.elf", "FTP Server v0.21"),
        ("payloads/kstuff-lite_v1.10.elf", "kstuff-lite v1.10"),
        ("payloads/nanoDNS_v0.4.elf", "nanoDNS v0.4"),
        ("payloads/Shadow-mount-plus_v1.7.elf", "Shadow mount plus v1.7"),
        ("payloads/Webkit-autoloader-v0.3.elf", "Webkit autoloader v0.3"),
    };

    public PayloadPage()
    {
        InitializeComponent();
        cvLog.ItemsSource = _logLines;

        foreach (var p in BundledPayloads)
            pickerBundled.Items.Add(p.Display);

        txtIp.Text = Preferences.Get("payload_ip", "192.168.1.100");
        txtPort.Text = Preferences.Get("payload_port", "9021");
        txtIp.TextChanged += (s, e) => Preferences.Set("payload_ip", txtIp.Text ?? "");
        txtPort.TextChanged += (s, e) => Preferences.Set("payload_port", txtPort.Text ?? "");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ThemeService.Apply(this);
    }

    private async void OnBundledSelected(object? sender, EventArgs e)
    {
        if (pickerBundled.SelectedIndex < 0 || pickerBundled.SelectedIndex >= BundledPayloads.Length) return;

        _selectedBundledIndex = pickerBundled.SelectedIndex;
        _selectedFilePath = null;
        var item = BundledPayloads[pickerBundled.SelectedIndex];

        lblFileName.Text = item.Display;
        lblFileName.TextColor = Colors.White;
        lblFileSize.Text = "الحمولة مدمجة داخل التطبيق";
        lblFileSize.TextColor = Colors.LightGray;

        Log($"[✓] تم اختيار حمولة مدمجة: {item.Display}", "#90EE90");
    }

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "*/*" } },
                    { DevicePlatform.iOS, new[] { "public.data" } },
                }),
                PickerTitle = "اختر ملف الحمولة",
            });

            if (result != null)
            {
                _selectedBundledIndex = -1;
                pickerBundled.SelectedIndex = -1;
                _selectedFilePath = result.FullPath;
                lblFileName.Text = result.FileName;
                lblFileName.TextColor = Colors.White;

                var fileInfo = new FileInfo(result.FullPath);
                lblFileSize.Text = $"الحجم: {fileInfo.Length:N0} بايت ({fileInfo.Length / 1024.0:F1} KB)";
                lblFileSize.TextColor = Colors.LightGray;

                Log($"[✓] تم اختيار ملف: {result.FileName}", "#90EE90");
            }
        }
        catch (Exception ex)
        {
            Log($"[!] خطأ في اختيار الملف: {ex.Message}", "#FF6B6B");
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        string ip = txtIp.Text?.Trim() ?? "192.168.1.100";
        int port = int.TryParse(txtPort.Text, out int p) ? p : 9021;

        string fileName = "";
        string sourceLabel = "";

        Stream sourceStream;
        if (_selectedBundledIndex >= 0 && _selectedBundledIndex < BundledPayloads.Length)
        {
            var item = BundledPayloads[_selectedBundledIndex];
            fileName = item.Display;
            sourceLabel = item.Asset;
            sourceStream = await FileSystem.OpenAppPackageFileAsync(item.Asset);
        }
        else if (!string.IsNullOrEmpty(_selectedFilePath) && File.Exists(_selectedFilePath))
        {
            fileName = Path.GetFileName(_selectedFilePath);
            sourceLabel = _selectedFilePath;
            sourceStream = File.OpenRead(_selectedFilePath);
        }
        else
        {
            await DisplayAlert("تنبيه", "اختر حمولة مدمجة أو استعرض ملفاً أولاً", "موافق");
            return;
        }

        btnSend.IsEnabled = false;
        btnSend.Text = "جاري الإرسال...";

        using var sendStream = sourceStream;

        await Task.Run(async () =>
        {
            try
            {
                Dispatcher.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] جاهز لإرسال الحمولة عبر المنفذ {port}...", "White");
                    Log($"[{DateTime.Now:HH:mm:ss}] ملف الحمولة: {fileName}", "LightGray");
                    if (sendStream.CanSeek)
                        Log($"    الحجم: {sendStream.Length:N0} بايت", "LightGray");
                });

                using var client = new TcpClient();

                Dispatcher.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الاتصال بـ {ip}:{port}...", "Yellow"));

                await client.ConnectAsync(ip, port);

                Dispatcher.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ✓ تم الاتصال بنجاح!", "#90EE90");
                    Log($"[{DateTime.Now:HH:mm:ss}] جاري إرسال الحمولة...", "Yellow");
                });

                using var stream = client.GetStream();

                byte[] buffer = new byte[8192];
                long totalSent = 0;
                long totalSize = sendStream.CanSeek ? sendStream.Length : 0;
                int bytesRead;
                int lastProgress = 0;

                sendStream.Position = 0;
                while ((bytesRead = await sendStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);
                    totalSent += bytesRead;

                    if (totalSize > 0)
                    {
                        int progress = (int)(totalSent * 100 / totalSize);
                        if (progress != lastProgress && progress % 10 == 0)
                        {
                            lastProgress = progress;
                            Dispatcher.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الإرسال... {progress}% ({totalSent:N0}/{totalSize:N0})", "LightGray"));
                        }
                    }
                    else if (totalSent % (512 * 1024) < 8192)
                    {
                        Dispatcher.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الإرسال... {totalSent:N0} بايت", "LightGray"));
                    }
                }

                await stream.FlushAsync();

                Dispatcher.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🚀 تم إرسال الحمولة بنجاح!", "#00FF7F");
                    Log($"    [{fileName}] → {ip}:{port}", "#90EE90");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ✗ فشل الإرسال: {ex.Message}", "#FF6B6B");
                });
            }
            finally
            {
                Dispatcher.Dispatch(() =>
                {
                    btnSend.IsEnabled = true;
                    btnSend.Text = "► إرسال الحمولة (منفذ 9021)";
                });
            }
        });
    }

    private void Log(string text, string colorHex)
    {
        _logLines.Add(text);
        if (_logLines.Count > 200)
            _logLines.RemoveAt(0);

        cvLog.ScrollTo(_logLines.Last(), position: ScrollToPosition.End, animate: false);
    }
}
