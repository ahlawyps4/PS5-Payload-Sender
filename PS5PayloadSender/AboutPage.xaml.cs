namespace PS5PayloadSender;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        lblVersion.Text = $"الإصدار {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    private async void OnStoreClicked(object? sender, EventArgs e)
    {
        await Launcher.OpenAsync("https://ahlawy-store.vercel.app/");
    }

    private async void OnFacebookClicked(object? sender, EventArgs e)
    {
        await Launcher.OpenAsync("https://www.facebook.com/AHLAWYSTORE");
    }

    private async void OnCatalogClicked(object? sender, EventArgs e)
    {
        await Launcher.OpenAsync("https://wa.me/c/201018251103");
    }
}
