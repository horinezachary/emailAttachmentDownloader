using EmailAttachmentDownloader.Core;
using QRCoder;

namespace EmailAttachmentDownloader.Tray;

/// <summary>
/// Shows the QR code and link that pair the iPhone app with one Phone share account. The code holds the account's name,
/// the address to mail photos to and the share tag. It never holds a password.
/// </summary>
internal sealed class PairingForm : Form
{
    private readonly string _link;
    private readonly PictureBox _picture = new() { Size = new Size(280, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly TextBox _linkBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button _copy = new() { Text = "Copy link", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };

    public PairingForm(AccountProfile profile)
    {
        _link = PairingLink.Build(profile.Name, profile.SendToAddress, profile.ShareTag);

        Text = $"Pair a phone with \"{profile.Name}\"";
        Icon = AppIcon.ForWindow();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AcceptButton = _close;
        CancelButton = _close;

        _picture.Image = RenderQr(_link);
        _linkBox.Text = _link;

        var instructions = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Text = "On the iPhone: open Email Attachment Downloader, go to Frames, tap Scan code, and point the camera here. "
                   + "You can also scan it with the Camera app, or copy the link and paste it into the app.\r\n\r\n"
                   + $"Photos are mailed to {profile.SendToAddress} with {profile.SubjectMarker} in the subject. "
                   + "The phone sends through its own mail account, which you enter in the app's Settings.",
        };

        var linkRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linkRow.Controls.Add(_linkBox, 0, 0);
        linkRow.Controls.Add(_copy, 1, 0);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        _picture.Margin = new Padding(0, 0, 0, 10);
        linkRow.Width = 420;
        linkRow.Margin = new Padding(0, 10, 0, 10);
        layout.Controls.Add(_picture);
        layout.Controls.Add(instructions);
        layout.Controls.Add(linkRow);
        layout.Controls.Add(_close);
        Controls.Add(layout);

        _copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_link);
                _copy.Text = "Copied";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another program is holding the clipboard; the link is still selectable in the box.
                _copy.Text = "Try again";
            }
        };
    }

    /// <summary>A QR code with a quiet zone, as an image that does not depend on a stream staying open.</summary>
    private static Bitmap RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        byte[] bytes = png.GetGraphic(8);

        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
