using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>A small panel at the top of the screen saying what was copied, with Join Lines and
/// Open Link when they apply. It stays five seconds after the pointer leaves it and never takes
/// the focus from the app being worked in.</summary>
internal sealed class TextToast : Window
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private const double ToastWidth = 380;

    private readonly TextBlock heading = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock preview = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 4,
        TextTrimming = TextTrimming.CharacterEllipsis,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
    };
    private readonly PixelPoint? on;
    private readonly ITimer timer;

    internal string Heading => heading.Text ?? "";
    internal string Preview => preview.Text ?? "";
    internal Button JoinLines { get; } = new() { Content = "Join Lines", FontSize = 12 };
    internal Button OpenLink { get; } = new() { Content = "Open Link", FontSize = 12 };
    internal bool IsGone { get; private set; }

    /// <param name="on">A point on the monitor the text came from, in physical pixels.</param>
    public TextToast(string title, string shown, TextReading? reading, IClipboard clipboard, IFileActions files, PixelPoint? on,
                     TimeProvider? time = null)
    {
        this.on = on;
        heading.Text = title;
        preview.Text = shown;
        preview.IsVisible = shown.Length > 0;
        // Read out as it appears, since it never takes the focus.
        AutomationProperties.SetLiveSetting(heading, AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(this, shown.Length == 0 ? title : $"{title}. {shown}");

        JoinLines.IsVisible = reading is { Codes.IsEmpty: true, Lines.Length: > 1 };
        JoinLines.Click += (_, _) =>
        {
            var joined = Tinysnap.Core.TextReader.Join(reading!.Text);
            clipboard.SetText(joined);
            heading.Text = "Joined and copied";
            preview.Text = joined;
        };
        OpenLink.IsVisible = reading?.Link is not null;
        OpenLink.Click += (_, _) =>
        {
            files.Open(reading!.Link!);
            Dismiss();
        };

        Title = title;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = ToastWidth;
        SizeToContent = SizeToContent.Height;
        Background = Brushes.Transparent;
        // See-through, never acrylic: acrylic fills the whole square window, and where blur is
        // off it showed as black corners round the rounded card.
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    heading,
                    preview,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        IsVisible = JoinLines.IsVisible || OpenLink.IsVisible,
                        Children = { JoinLines, OpenLink },
                    },
                },
            },
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumBrush"),
        };
        card.PointerEntered += (_, _) => timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        card.PointerExited += (_, _) =>
        {
            if (!IsGone) timer!.Change(Wait, Timeout.InfiniteTimeSpan);
        };
        Content = card;

        timer = (time ?? TimeProvider.System).CreateTimer(_ => Dispatcher.UIThread.Post(Dismiss), null, Wait, Timeout.InfiniteTimeSpan);
        Opened += (_, _) => Place();
    }

    /// <summary>Centred at the top of the work area, 12 points down.</summary>
    private void Place()
    {
        var screen = (on is { } at ? Screens.ScreenFromPoint(at) : null) ?? Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        var width = (int)(Bounds.Width * screen.Scaling);
        Position = new PixelPoint(work.X + (work.Width - width) / 2, work.Y + (int)(12 * screen.Scaling));
    }

    public void Dismiss()
    {
        if (IsGone) return;
        IsGone = true;
        timer.Dispose();
        Close();
    }
}
