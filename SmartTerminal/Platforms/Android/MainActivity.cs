using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace SmartTerminal;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
                         | ConfigChanges.Orientation
                         | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout
                         | ConfigChanges.SmallestScreenSize
                         | ConfigChanges.Density
                         | ConfigChanges.Keyboard
                         | ConfigChanges.KeyboardHidden)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Fullscreen immersive — maximize terminal real estate
        if (Window != null)
        {
#pragma warning disable CA1422 // Deprecated on Android 35+, but still needed for older versions
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#1a1a2e"));
            Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#1a1a2e"));
#pragma warning restore CA1422

            // Keep screen on while terminal is active
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            // Adjust resize for soft keyboard
            Window.SetSoftInputMode(SoftInput.AdjustResize);
        }
    }
}
