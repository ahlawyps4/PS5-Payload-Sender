using Android.App;
using Android.Content;
using Android.Provider;
using Android.OS;

namespace PS5PayloadSender;

[Activity(Theme = "@android:style/Theme.Material.Light.NoActionBar", NoHistory = true)]
public class FolderPickerActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, 1001);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == 1001 && resultCode == Result.Ok && data?.Data != null)
        {
            FolderPickerCallback.Instance.SetResult(data.Data.ToString());
        }
        else
        {
            FolderPickerCallback.Instance.SetResult(null);
        }
        Finish();
    }
}

public class FolderPickerCallback
{
    public static FolderPickerCallback Instance { get; } = new();
    private TaskCompletionSource<string?>? _tcs;

    public void SetResult(TaskCompletionSource<string?> tcs)
    {
        _tcs = tcs;
    }

    public void SetResult(string? result)
    {
        _tcs?.TrySetResult(result);
    }
}
