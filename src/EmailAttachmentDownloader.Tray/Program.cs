namespace EmailAttachmentDownloader.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One tray icon per Windows session.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\EmailAttachmentDownloader.Tray", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Email Attachment Downloader is already running. Look for its icon in the system tray.",
                "Email Attachment Downloader",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
