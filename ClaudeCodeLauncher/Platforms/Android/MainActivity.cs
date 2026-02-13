using Android.App;
using Android.Content.PM;
using Android.OS;

namespace ClaudeCodeLauncher;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true, 
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Request Termux:RUN_COMMAND permission if needed
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            // Note: Termux RUN_COMMAND doesn't require runtime permissions,
            // but storage access does
            if (CheckSelfPermission(Android.Manifest.Permission.WriteExternalStorage) != Permission.Granted)
            {
                RequestPermissions(new[] { 
                    Android.Manifest.Permission.WriteExternalStorage,
                    Android.Manifest.Permission.ReadExternalStorage 
                }, 100);
            }
        }
    }
}
