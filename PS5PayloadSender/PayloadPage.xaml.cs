using System.Collections.ObjectModel;
using System.Net.Sockets;

namespace PS5PayloadSender;

public partial class PayloadPage : ContentPage
{
    private readonly ObservableCollection<string> _logLines = new();
    private readonly List<PickerEntry> _pickerEntries = new();
    private string? _selectedFilePath;
    private int _selectedBundledIndex = -1;
    private TcpClient? _payloadClient;
    private NetworkStream? _payloadStream;
    private bool _payloadConnected;
    private bool _ftpConnected;

    private static readonly (string Asset, string Display)[] BundledPayloadsPS5 =
    {
        ("payloads/etaHEN_v2.5B.bin", "etaHEN v2.5B"),
        ("payloads/ftpsrv_ps5-payload_v0.21.elf", "FTP Server v0.21"),
        ("payloads/kstuff-lite_v1.10.elf", "kstuff-lite v1.10"),
        ("payloads/nanoDNS_v0.4.elf", "nanoDNS v0.4"),
        ("payloads/Shadow-mount-plus_v1.7.elf", "Shadow mount plus v1.7"),
        ("payloads/Webkit-autoloader-v0.3.elf", "Webkit autoloader v0.3"),
    };

    private static readonly (string Asset, string Display)[] BundledPayloadsPS4 =
    {
        // ("payloads4/goldhen.bin", "goldHEN"),
        // أضف حمولات PS4 هنا عند توفرها (مثال: ("payloads4/hen.bin", "HEN"))
    };

    private const string HeaderPS5 = "── حمولات PS5 ──";
    private const string HeaderPS4 = "── حمولات PS4 ──";

    private record PickerEntry(bool IsHeader, string Text, string? Asset);

    public PayloadPage()
    {
        InitializeComponent();
        cvLog.ItemsSource = _logLines;

        BuildBundledPicker();

        txtIp.Text = Preferences.Get("payload_ip", "192.168.1.100");
        txtPort.Text = Preferences.Get("payload_port", "9021");
        txtFtpPort.Text = Preferences.Get("ftp_port", "2121");
        txtIp.TextChanged += (s, e) => Preferences.Set("payload_ip", txtIp.Text ?? "");
        txtPort.TextChanged += (s, e) => Preferences.Set("payload_port", txtPort.Text ?? "");
        txtFtpPort.TextChanged += (s, e) => Preferences.Set("ftp_port", txtFtpPort.Text ?? "");
    }

    private void BuildBundledPicker()
    {
        _pickerEntries.Clear();
        pickerBundled.Items.Clear();

        _pickerEntries.Add(new PickerEntry(true, HeaderPS5, null));
        foreach (var p in BundledPayloadsPS5)
            _pickerEntries.Add(new PickerEntry(false, p.Display, p.Asset));

        _pickerEntries.Add(new PickerEntry(true, HeaderPS4, null));
        foreach (var p in BundledPayloadsPS4)
            _pickerEntries.Add(new PickerEntry(false, p.Display, p.Asset));

        if (BundledPayloadsPS4.Length == 0)
            _pickerEntries.Add(new PickerEntry(true, "لا توجد حمولات PS4 مضافة بعد", null));

        foreach (var entry in _pickerEntries)
            pickerBundled.Items.Add(entry.Text);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ThemeService.Apply(this);
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_payloadConnected)
        {
            DisconnectPayload();
            return;
        }

        string ip = txtIp.Text?.Trim() ?? "192.168.1.100";
        int port = int.TryParse(txtPort.Text, out int p) ? p : 9021;
        int ftpPort = int.TryParse(txtFtpPort.Text, out int fp) ? fp : 2121;

        btnConnect.IsEnabled = false;
        btnConnect.Text = "جاري...";
        actConn.IsRunning = true;
        actConn.IsVisible = true;
        lblConnState.Text = "جاري الاتصال...";
        lblConnState.TextColor = Colors.Yellow;
        SetPortLabel(lblS9021, port, false);
        SetPortLabel(lblS2121, ftpPort, false);

        try
        {
            // Test 9021 - persistent payload channel
            var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(5000);
            var done = await Task.WhenAny(connectTask, timeoutTask);
            if (done == timeoutTask)
            {
                tcp.Close();
                throw new TimeoutException($"الجهاز لم يستجب على المنفذ {port}");
            }
            await connectTask;

            _payloadClient = tcp;
            _payloadStream = tcp.GetStream();
            _payloadConnected = true;
            SetPortLabel(lblS9021, port, true);
            Log($"[✓] تم الاتصال بمنفذ الحمولة {port}", "#90EE90");

            // Test FTP - reachability (read welcome banner, expect 220)
            try
            {
                using var ftpTcp = new TcpClient();
                var ftpTask = ftpTcp.ConnectAsync(ip, ftpPort);
                var ftpTimeout = Task.Delay(5000);
                var ftpDone = await Task.WhenAny(ftpTask, ftpTimeout);
                if (ftpDone == ftpTimeout)
                {
                    ftpTcp.Close();
                    throw new TimeoutException("FTP لم يستجب");
                }
                await ftpTask;

                using var ftpStream = ftpTcp.GetStream();
                var bannerBuf = new byte[256];
                int n = await ftpStream.ReadAsync(bannerBuf, 0, bannerBuf.Length);
                string banner = System.Text.Encoding.ASCII.GetString(bannerBuf, 0, n);
                _ftpConnected = banner.StartsWith("220");
                SetPortLabel(lblS2121, ftpPort, _ftpConnected);
                Log(_ftpConnected ? $"[✓] منفذ FTP {ftpPort} متصل (خدمة FTP تعمل)" : $"[!] منفذ FTP {ftpPort} استجاب بدون ترحيب 220", _ftpConnected ? "#90EE90" : "#FFD700");
            }
            catch (Exception ftpEx)
            {
                _ftpConnected = false;
                SetPortLabel(lblS2121, ftpPort, false);
                Log($"[!] منفذ FTP {ftpPort} غير متاح: {ftpEx.Message}", "#FF6B6B");
            }

            if (_payloadConnected && _ftpConnected)
            {
                lblConnState.Text = "متصل بالكامل";
                lblConnState.TextColor = Colors.LimeGreen;
                btnConnect.Text = "قطع";
                btnConnect.BackgroundColor = Colors.Red;
            }
            else
            {
                lblConnState.Text = "متصل (FTP غير متاح)";
                lblConnState.TextColor = Color.FromArgb("#FFB74D");
                btnConnect.Text = "قطع";
                btnConnect.BackgroundColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            DisconnectPayload();
            lblConnState.Text = "غير متصل";
            lblConnState.TextColor = Colors.Red;
            SetPortLabel(lblS9021, port, false);
            SetPortLabel(lblS2121, ftpPort, false);
            Log($"[✗] فشل الاتصال: {ex.Message}", "#FF6B6B");
            await DisplayAlert("فشل الاتصال", ex.Message, "موافق");
        }
        finally
        {
            btnConnect.IsEnabled = true;
            actConn.IsRunning = false;
            actConn.IsVisible = false;
            if (btnConnect.Text != "قطع")
                btnConnect.Text = "اتصال";
        }
    }

    private void DisconnectPayload()
    {
        try { _payloadStream?.Close(); } catch { }
        try { _payloadClient?.Close(); } catch { }
        _payloadStream = null;
        _payloadClient = null;
        _payloadConnected = false;
        _ftpConnected = false;

        lblConnState.Text = "غير متصل";
        lblConnState.TextColor = Colors.Gray;
        btnConnect.Text = "اتصال";
        btnConnect.BackgroundColor = Color.FromArgb("#34A853");
        SetPortLabel(lblS9021, int.TryParse(txtPort.Text, out int p) ? p : 9021, false);
        SetPortLabel(lblS2121, int.TryParse(txtFtpPort.Text, out int fp) ? fp : 2121, false);
    }

    private void SetPortLabel(Label label, int port, bool ok)
    {
        label.Text = $"{(ok ? "\u25CF" : "\u25CB")} {port}";
        label.TextColor = ok ? Colors.LimeGreen : Colors.Red;
    }

    protected override void OnDisappearing()
    {
        DisconnectPayload();
        base.OnDisappearing();
    }

    private async void OnBundledSelected(object? sender, EventArgs e)
    {
        int idx = pickerBundled.SelectedIndex;
        if (idx < 0 || idx >= _pickerEntries.Count) return;

        var entry = _pickerEntries[idx];
        if (entry.IsHeader)
        {
            pickerBundled.SelectedIndex = -1;
            return;
        }

        _selectedBundledIndex = idx;
        _selectedFilePath = null;

        lblFileName.Text = entry.Text;
        lblFileName.TextColor = Colors.White;
        lblFileSize.Text = "الحمولة مدمجة داخل التطبيق";
        lblFileSize.TextColor = Colors.LightGray;

        Log($"[✓] تم اختيار حمولة مدمجة: {entry.Text}", "#90EE90");
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
        if (_selectedBundledIndex >= 0 && _selectedBundledIndex < _pickerEntries.Count
            && !_pickerEntries[_selectedBundledIndex].IsHeader)
        {
            var item = _pickerEntries[_selectedBundledIndex];
            fileName = item.Text;
            sourceLabel = item.Asset ?? "";
            sourceStream = await FileSystem.OpenAppPackageFileAsync(item.Asset!);
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

                TcpClient? client = null;
                NetworkStream? stream = null;
                bool localClient = false;

                try
                {
                    if (_payloadClient != null && _payloadClient.Connected)
                    {
                        client = _payloadClient;
                        stream = _payloadStream;
                    }
                    else
                    {
                        Dispatcher.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الاتصال بـ {ip}:{port}...", "Yellow"));
                        client = new TcpClient();
                        localClient = true;
                        await client.ConnectAsync(ip, port);
                        stream = client.GetStream();
                    }

                    Dispatcher.Dispatch(() =>
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ✓ تم الاتصال بنجاح!", "#90EE90");
                        Log($"[{DateTime.Now:HH:mm:ss}] جاري إرسال الحمولة...", "Yellow");
                    });

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

                    if (localClient)
                    {
                        try { stream.Close(); } catch { }
                        try { client.Close(); } catch { }
                    }
                }
                catch
                {
                    // Persistent connection may have been closed by the server -
                    // reconnect with a fresh connection and retry once.
                    Dispatcher.Dispatch(() => Log($"[!] الاتصال الدائم انقطع، جاري إعادة الاتصال والمحاولة مجدداً...", "#FFD700"));

                    try { _payloadStream?.Close(); } catch { }
                    try { _payloadClient?.Close(); } catch { }
                    _payloadStream = null;
                    _payloadClient = null;
                    _payloadConnected = false;

                    using var freshClient = new TcpClient();
                    await freshClient.ConnectAsync(ip, port);
                    using var freshStream = freshClient.GetStream();

                    sendStream.Position = 0;
                    byte[] buffer = new byte[8192];
                    long totalSent = 0;
                    long totalSize = sendStream.CanSeek ? sendStream.Length : 0;
                    int bytesRead;

                    while ((bytesRead = await sendStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await freshStream.WriteAsync(buffer, 0, bytesRead);
                        totalSent += bytesRead;

                        if (totalSize > 0)
                        {
                            int progress = (int)(totalSent * 100 / totalSize);
                            if (progress % 10 == 0)
                                Dispatcher.Dispatch(() => Log($"[{DateTime.Now:HH:mm:ss}] جاري الإرسال... {progress}% ({totalSent:N0}/{totalSize:N0})", "LightGray"));
                        }
                    }

                    await freshStream.FlushAsync();

                    Dispatcher.Dispatch(() =>
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 🚀 تم إرسال الحمولة بنجاح (بعد إعادة الاتصال)!", "#00FF7F");
                        Log($"    [{fileName}] → {ip}:{port}", "#90EE90");
                    });
                }
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
