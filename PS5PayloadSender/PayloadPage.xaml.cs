using System.Collections.ObjectModel;
using System.Net.Sockets;

namespace PS5PayloadSender;

public partial class PayloadPage : ContentPage
{
    private readonly ObservableCollection<string> _logLines = new();
    private string? _selectedFilePath;

    public PayloadPage()
    {
        InitializeComponent();
        cvLog.ItemsSource = _logLines;
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
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            await DisplayAlert("تنبيه", "يرجى اختيار ملف حمولة أولاً", "موافق");
            return;
        }

        if (!File.Exists(_selectedFilePath))
        {
            await DisplayAlert("خطأ", "الملف المحدد غير موجود", "موافق");
            return;
        }

        btnSend.IsEnabled = false;
        btnSend.Text = "جاري الإرسال...";

        string ip = txtIp.Text?.Trim() ?? "192.168.1.100";
        int port = int.TryParse(txtPort.Text, out int p) ? p : 9021;
        string filePath = _selectedFilePath;
        string fileName = Path.GetFileName(filePath);

        await Task.Run(async () =>
        {
            try
            {
                this.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] جاهز لإرسال الحمولة عبر المنفذ {port}...", "White");
                    var fi = new FileInfo(filePath);
                    Log($"[{DateTime.Now:HH:mm:ss}] ملف الحمولة: {fileName}", "LightGray");
                    Log($"    الحجم: {fi.Length:N0} بايت", "LightGray");
                });

                using var client = new TcpClient();

                this.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الاتصال بـ {ip}:{port}...", "Yellow"));

                await client.ConnectAsync(ip, port);

                this.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ✓ تم الاتصال بنجاح!", "#90EE90");
                    Log($"[{DateTime.Now:HH:mm:ss}] جاري إرسال الحمولة...", "Yellow");
                });

                using var stream = client.GetStream();
                using var fileStream = File.OpenRead(filePath);

                byte[] buffer = new byte[8192];
                long totalSent = 0;
                int bytesRead;
                int lastProgress = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);
                    totalSent += bytesRead;

                    int progress = (int)(totalSent * 100 / fileStream.Length);
                    if (progress != lastProgress && progress % 10 == 0)
                    {
                        lastProgress = progress;
                        this.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الإرسال... {progress}% ({totalSent:N0}/{fileStream.Length:N0})", "LightGray"));
                    }
                }

                await stream.FlushAsync();

                this.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🚀 تم إرسال الحمولة بنجاح!", "#00FF7F");
                    Log($"    [{fileName}] → {ip}:{port}", "#90EE90");
                });
            }
            catch (Exception ex)
            {
                this.Dispatch(() =>
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ✗ فشل الإرسال: {ex.Message}", "#FF6B6B");
                });
            }
            finally
            {
                this.Dispatch(() =>
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
