#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;

namespace SmartTerminal.Platforms.Android.Services;

/// <summary>
/// Foreground service that keeps the terminal session alive when the app is backgrounded.
/// Android kills background processes aggressively; a foreground service with a
/// persistent notification prevents this.
/// </summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public class TerminalForegroundService : Service
{
    private const int NotificationId = 9001;
    private const string ChannelId = "smartterminal_session";
    private const string ChannelName = "Terminal Session";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Smart Terminal")
            .SetContentText("Terminal session running")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuManage)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
        {
            Description = "Keeps terminal session alive in background",
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    /// <summary>Start the foreground service from any context.</summary>
    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(TerminalForegroundService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    /// <summary>Stop the foreground service.</summary>
    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(TerminalForegroundService));
        context.StopService(intent);
    }
}
#endif
