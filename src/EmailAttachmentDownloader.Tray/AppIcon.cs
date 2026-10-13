namespace EmailAttachmentDownloader.Tray;

/// <summary>The application icon (assets/app-icon.ico, embedded in the exe).</summary>
internal static class AppIcon
{
    private const string ResourceName = "app-icon.ico";

    /// <summary>For the notification area. Picks the embedded size closest to the current small-icon size.</summary>
    public static Icon ForTray() => Load(SystemInformation.SmallIconSize);

    /// <summary>For window title bars and the taskbar.</summary>
    public static Icon ForWindow() => Load(SystemInformation.IconSize);

    private static Icon Load(Size size)
    {
        try
        {
            using Stream? stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is not null)
            {
                return new Icon(stream, size);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            // Fall through to the stock icon; a missing or damaged icon must never stop the app starting.
        }

        return SystemIcons.Application;
    }
}
