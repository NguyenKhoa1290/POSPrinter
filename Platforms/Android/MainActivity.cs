using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace POSPrinter;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Yêu cầu quyền Bluetooth khi chạy (Android 12+)
        RequestBluetoothPermissionsIfNeeded();
    }

    private void RequestBluetoothPermissionsIfNeeded()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) // Android 12+
        {
            var permissions = new[]
            {
                Android.Manifest.Permission.BluetoothScan,
                Android.Manifest.Permission.BluetoothConnect,
            };
            RequestPermissions(permissions, requestCode: 1001);
        }
        else
        {
            // Android < 12: cần ACCESS_FINE_LOCATION để scan BT
            RequestPermissions(
                [Android.Manifest.Permission.AccessFineLocation],
                requestCode: 1002);
        }
    }
}
