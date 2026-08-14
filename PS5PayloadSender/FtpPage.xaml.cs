#if ANDROID
using Android.Content;
using Android.Provider;
#endif

using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Text;

namespace PS5PayloadSender;

public class FtpItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public string Info { get; set; } = "";
    public string SizeText { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string Perms { get; set; } = "";
}

public partial class FtpPage : ContentPage
{
    private TcpClient? _ftpClient;
    private NetworkStream? _ftpStream;
    private readonly ObservableCollection<FtpItem> _items = new();
    private string _currentPath = "/";
    private bool _connected;

    public FtpPage()
    {
        InitializeComponent();
        cvFiles.ItemsSource = _items;
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_connected)
        {
            Disconnect();
            return;
        }

        string ip = txtFtpIp.Text?.Trim() ?? "192.168.1.100";
        int port = int.TryParse(txtFtpPort.Text, out int p) ? p : 2121;

        try
        {
            btnConnect.IsEnabled = false;
            lblStatus.Text = "جاري الاتصال...";
            lblStatus.TextColor = Colors.Yellow;

            _ftpClient = new TcpClient();
            await _ftpClient.ConnectAsync(ip, port);
            _ftpStream = _ftpClient.GetStream();

            await ReadResponse();
            await SendCmd("USER anonymous");
            await SendCmd("PASS anonymous@");

            _connected = true;
            lblStatus.Text = "● متصل";
            lblStatus.TextColor = Colors.LimeGreen;
            btnConnect.Text = "قطع";
            btnConnect.BackgroundColor = Colors.Red;
            btnUpload.IsEnabled = true;
            btnUploadFolder.IsEnabled = true;
            btnDownload.IsEnabled = true;
            btnUp.IsEnabled = true;
            btnRefresh.IsEnabled = true;

            _currentPath = "/";
            txtPath.Text = "/";

            await LoadDirectory();
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", $"فشل الاتصال: {ex.Message}", "موافق");
            lblStatus.Text = "✗ خطأ";
            lblStatus.TextColor = Colors.Red;
            Disconnect();
        }
        finally
        {
            btnConnect.IsEnabled = true;
        }
    }

    private void Disconnect()
    {
        try { _ftpStream?.Close(); } catch { }
        try { _ftpClient?.Close(); } catch { }
        _ftpStream = null;
        _ftpClient = null;
        _connected = false;

        lblStatus.Text = "غير متصل";
        lblStatus.TextColor = Colors.Gray;
        btnConnect.Text = "اتصال";
        btnConnect.BackgroundColor = Color.FromArgb("#34A853");
        btnUpload.IsEnabled = false;
        btnUploadFolder.IsEnabled = false;
        btnDownload.IsEnabled = false;
        btnUp.IsEnabled = false;
        btnRefresh.IsEnabled = false;
        _items.Clear();
    }

    private async Task<string> SendCmd(string command)
    {
        if (_ftpStream == null) throw new InvalidOperationException("Not connected");
        byte[] data = Encoding.ASCII.GetBytes(command + "\r\n");
        await _ftpStream.WriteAsync(data, 0, data.Length);
        return await ReadResponse();
    }

    private async Task<string> ReadResponse()
    {
        if (_ftpStream == null) throw new InvalidOperationException("Not connected");
        byte[] buffer = new byte[4096];
        int bytesRead = await _ftpStream.ReadAsync(buffer, 0, buffer.Length);
        return Encoding.ASCII.GetString(buffer, 0, bytesRead);
    }

    private async Task<string> SendPasvCmd(string command)
    {
        if (_ftpStream == null) throw new InvalidOperationException("Not connected");
        byte[] data = Encoding.ASCII.GetBytes(command + "\r\n");
        await _ftpStream.WriteAsync(data, 0, data.Length);

        var response = new StringBuilder();
        byte[] buffer = new byte[4096];
        bool firstLine = true;
        string lastCode = "";

        while (true)
        {
            int bytesRead = await _ftpStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;
            response.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

            string[] lines = response.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.Length < 3) continue;
                string code = line[..3];
                if (firstLine)
                {
                    lastCode = code;
                    if (line.Length > 3 && line[3] == ' ')
                        return response.ToString().Trim();
                    firstLine = false;
                }
                else if (code == lastCode && line.Length > 3 && line[3] == ' ')
                {
                    return response.ToString().Trim();
                }
            }
        }
        return response.ToString().Trim();
    }

    private async Task LoadDirectory()
    {
        if (!_connected || _ftpStream == null) return;

        try
        {
            _items.Clear();
            await SendPasvCmd("TYPE I");

            string pasvResp = await SendPasvCmd("PASV");
            var pasvInfo = ParsePsv(pasvResp);
            if (pasvInfo == null)
            {
                lblStatus.Text = "PASV failed";
                return;
            }

            using var dataClient = new TcpClient();
            await dataClient.ConnectAsync(pasvInfo.Value.ip, pasvInfo.Value.port);

            await SendPasvCmd($"CWD {_currentPath}");
            await SendPasvCmd("LIST");

            using var dataStream = dataClient.GetStream();
            var data = new StringBuilder();
            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await dataStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                data.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

            await ReadResponse();

            if (_currentPath != "/")
            {
                _items.Add(new FtpItem
                {
                    Name = "..",
                    Icon = "⬆️",
                    Info = "المجلد الأب",
                    SizeText = "-",
                    IsDirectory = true,
                });
            }

            string[] lines = data.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) continue;

                string name = string.Join(' ', parts, 8, parts.Length - 8);
                if (name == "." || name == "..") continue;

                bool isDir = line.StartsWith("d", StringComparison.OrdinalIgnoreCase);
                string perms = parts[0];
                long.TryParse(parts[4], out long size);

                _items.Add(new FtpItem
                {
                    Name = name,
                    Icon = isDir ? "📁" : GetFileIcon(name),
                    Info = isDir ? "مجلد" : $"{perms}  •  {FormatSize(size)}",
                    SizeText = isDir ? "-" : FormatSize(size),
                    IsDirectory = isDir,
                    Size = size,
                    Perms = perms,
                });
            }

            txtPath.Text = _currentPath;
            lblStatus.Text = $"● متصل  •  {_items.Count} عنصر";
            lblStatus.TextColor = Colors.LimeGreen;
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", ex.Message, "موافق");
        }
    }

    private string GetFileIcon(string name)
    {
        string ext = Path.GetExtension(name).ToLower();
        return ext switch
        {
            ".elf" or ".bin" or ".pkg" => "📦",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼️",
            ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            ".mp3" or ".wav" or ".ogg" or ".flac" => "🎵",
            ".txt" or ".log" or ".cfg" or ".ini" => "📄",
            ".zip" or ".rar" or ".7z" or ".tar" => "🗜️",
            ".js" or ".html" or ".css" or ".json" => "💻",
            _ => "📄",
        };
    }

    private async void OnFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is FtpItem item)
        {
            ((CollectionView)sender).SelectedItem = null;

            if (item.IsDirectory)
            {
                if (item.Name == "..")
                {
                    _currentPath = Path.GetDirectoryName(_currentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/";
                    if (string.IsNullOrEmpty(_currentPath)) _currentPath = "/";
                }
                else
                {
                    _currentPath = _currentPath.TrimEnd('/') + "/" + item.Name;
                }
                await LoadDirectory();
            }
        }
    }

    private async void OnUpClicked(object? sender, EventArgs e)
    {
        if (_currentPath == "/") return;
        _currentPath = Path.GetDirectoryName(_currentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/";
        if (string.IsNullOrEmpty(_currentPath)) _currentPath = "/";
        await LoadDirectory();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadDirectory();
    }

    private async void OnUploadClicked(object? sender, EventArgs e)
    {
        if (!_connected) return;

        try
        {
            var result = await FilePicker.PickAsync(new PickOptions { PickerTitle = "اختر ملف للرفع" });
            if (result == null) return;

            string fileName = result.FileName;
            string remotePath = _currentPath.TrimEnd('/') + "/" + fileName;

            string pasvResp = await SendPasvCmd("PASV");
            var pasvInfo = ParsePsv(pasvResp);
            if (pasvInfo == null) { await DisplayAlert("خطأ", "PASV failed", "OK"); return; }

            using var dataClient = new TcpClient();
            await dataClient.ConnectAsync(pasvInfo.Value.ip, pasvInfo.Value.port);

            await SendPasvCmd($"STOR {remotePath}");

            using var dataStream = dataClient.GetStream();
            using var fileStream = File.OpenRead(result.FullPath);
            await fileStream.CopyToAsync(dataStream);
            await ReadResponse();

            await DisplayAlert("نجاح", $"تم رفع '{fileName}' بنجاح!", "OK");
            await LoadDirectory();
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", $"خطأ في الرفع: {ex.Message}", "OK");
        }
    }

    private async void OnUploadFolderClicked(object? sender, EventArgs e)
    {
        if (!_connected) return;

#if ANDROID
        var tcs = new TaskCompletionSource<string?>();
        FolderPickerCallback.Instance.SetResult(tcs);

        var intent = new Intent(Platform.CurrentActivity, typeof(FolderPickerActivity));
        Platform.CurrentActivity!.StartActivity(intent);

        string? treeUri = await tcs.Task;
        if (treeUri == null) return;

        var uri = Android.Net.Uri.Parse(treeUri);
        if (uri == null) return;

        string folderName = GetFolderName(Platform.CurrentActivity!, uri) ?? "folder";
        string remoteDirPath = _currentPath.TrimEnd('/') + "/" + folderName;

        try { await SendPasvCmd($"MKD {remoteDirPath}"); } catch { }

        var files = GetFilesInTree(Platform.CurrentActivity!, uri);
        if (files.Count == 0)
        {
            await DisplayAlert("تنبيه", "الفلدر فارغ!", "OK");
            return;
        }

        lblStatus.Text = $"جاري رفع {files.Count} ملف...";
        lblStatus.TextColor = Colors.Yellow;

        int success = 0;
        int failed = 0;

        foreach (var fileInfo in files)
        {
            try
            {
                string remotePath = remoteDirPath + "/" + fileInfo.Name;

                string pv = await SendPasvCmd("PASV");
                var pi = ParsePsv(pv);
                if (pi == null) { failed++; continue; }

                using var dc = new TcpClient();
                await dc.ConnectAsync(pi.Value.ip, pi.Value.port);
                await SendPasvCmd($"STOR {remotePath}");

                using var ds = dc.GetStream();
                using var stream = Platform.CurrentActivity!.ContentResolver.OpenInputStream(fileInfo.Uri);
                if (stream != null)
                    await stream.CopyToAsync(ds);

                await ReadResponse();
                success++;
            }
            catch
            {
                failed++;
            }
        }

        await DisplayAlert("اكتمل", $"تم رفع {success} ملف" + (failed > 0 ? $"\n{failed} ملف فشل" : ""), "OK");
        lblStatus.Text = $"● متصل  •  {_items.Count} عنصر";
        lblStatus.TextColor = Colors.LimeGreen;
        await LoadDirectory();
#else
        await DisplayAlert("تنبيه", "اختيار الفلدر متاح على الأندرويد فقط", "OK");
#endif
    }

#if ANDROID
    private string? GetFolderName(Android.Content.Context context, Android.Net.Uri treeUri)
    {
        try
        {
            var cursor = context.ContentResolver.Query(treeUri, null, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                int nameIndex = cursor.GetColumnIndex(DocumentsContract.ColumnDocumentId);
                if (nameIndex >= 0)
                {
                    string docId = cursor.GetString(nameIndex) ?? "";
                    cursor.Close();
                    string[] parts = docId.Split(':');
                    return parts.Length > 1 ? parts[^1] : docId;
                }
                cursor.Close();
            }
        }
        catch { }
        return "folder";
    }

    private List<(Android.Net.Uri Uri, string Name)> GetFilesInTree(Android.Content.Context context, Android.Net.Uri treeUri)
    {
        var result = new List<(Android.Net.Uri, string)>();
        try
        {
            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, DocumentsContract.GetTreeDocumentId(treeUri));
            var cursor = context.ContentResolver.Query(childrenUri,
                new[] { DocumentsContract.ColumnDocumentId, DocumentsContract.ColumnDisplayName, DocumentsContract.ColumnMimeType },
                null, null, null);

            if (cursor != null)
            {
                while (cursor.MoveToNext())
                {
                    string docId = cursor.GetString(0) ?? "";
                    string name = cursor.GetString(1) ?? "";
                    string mimeType = cursor.GetString(2) ?? "";

                    if (mimeType.Contains("vnd.android.document/directory"))
                    {
                        var subUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId);
                        result.AddRange(GetFilesInTree(context, subUri));
                    }
                    else
                    {
                        var fileUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId);
                        result.Add((fileUri, name));
                    }
                }
                cursor.Close();
            }
        }
        catch { }
        return result;
    }
#endif

    private async void OnDownloadClicked(object? sender, EventArgs e)
    {
        if (!_connected) return;
        if (cvFiles.SelectedItem is not FtpItem item || item.IsDirectory)
        {
            await DisplayAlert("تنبيه", "اختر ملفاً أولاً", "OK");
            return;
        }

        try
        {
            string remotePath = _currentPath.TrimEnd('/') + "/" + item.Name;

            string pasvResp = await SendPasvCmd("PASV");
            var pasvInfo = ParsePsv(pasvResp);
            if (pasvInfo == null) { await DisplayAlert("خطأ", "PASV failed", "OK"); return; }

            using var dataClient = new TcpClient();
            await dataClient.ConnectAsync(pasvInfo.Value.ip, pasvInfo.Value.port);

            await SendPasvCmd($"RETR {remotePath}");

            using var dataStream = dataClient.GetStream();
            string localPath = Path.Combine(FileSystem.CacheDirectory, item.Name);
            using (var fs = File.Create(localPath))
                await dataStream.CopyToAsync(fs);

            await ReadResponse();

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = $"حفظ {item.Name}",
                File = new ShareFile(localPath),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("خطأ", $"خطأ في التحميل: {ex.Message}", "OK");
        }
    }

    private (string ip, int port)? ParsePsv(string response)
    {
        try
        {
            int start = response.IndexOf('(');
            int end = response.IndexOf(')');
            if (start < 0 || end < 0) return null;

            string[] parts = response.Substring(start + 1, end - start - 1).Split(',');
            if (parts.Length != 6) return null;

            string ip = $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
            int port = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);
            return (ip, port);
        }
        catch { return null; }
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    protected override void OnDisappearing()
    {
        Disconnect();
        base.OnDisappearing();
    }
}
