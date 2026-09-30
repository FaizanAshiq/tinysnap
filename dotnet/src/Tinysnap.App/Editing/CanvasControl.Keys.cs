using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Tinysnap.Core;
using Size = Tinysnap.Core.Size;

namespace Tinysnap.App.Editing;

/// <summary>The editor's keys, pasting and dropping images, as the Mac's CanvasView has them.</summary>
internal sealed partial class CanvasControl
{
    /// <summary>Esc with nothing left to finish or deselect.</summary>
    public event Action? CloseRequested;

    /// <summary>Tab: copy the colour under the pointer.</summary>
    public event Action? CopyColorRequested;

    /// <summary>The image tool was picked, so an image needs choosing.</summary>
    public event Action? ImagePickRequested;

    /// <summary>A style change from the keyboard is finished, so it can be remembered.</summary>
    public event Action? StylesCommitted;

    private partial bool HandleKey(KeyEventArgs e)
    {
        // While Copy Text is on, Esc ends it; a tool key picks its tool, which ends it too.
        if (IsPickingText && e.Key == Key.Escape)
        {
            StopPickingText();
            return true;
        }
        if (Session.TypingId is null && MeasureKey(e)) return true;
        // While text is typed the field has the keys, so a letter typed never picks a tool.
        if (Session.TypingId is not null)
        {
            if (e.Key != Key.Escape) return false;
            Session.Escape();
            SessionChanged();
            Focus();
            return true;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (IsCommand(e.KeyModifiers))
        {
            switch (e.Key)
            {
                case Key.Z when shift:
                case Key.Y:
                    Session.Redo();
                    break;
                case Key.Z:
                    Session.Undo();
                    break;
                case Key.V:
                    _ = PasteAsync();
                    return true;
                default:
                    return false;
            }
            SessionChanged();
            return true;
        }

        var step = shift ? 10 : 1;
        switch (e.Key)
        {
            case Key.Escape:
                if (Session.Escape() == EscapeResult.Close) CloseRequested?.Invoke();
                SessionChanged();
                return true;
            case Key.Tab:
                CopyColorRequested?.Invoke();
                return true;
            case Key.OemOpenBrackets or Key.OemCloseBrackets:
                StepSize(thicker: e.Key == Key.OemCloseBrackets);
                return true;
            case Key.Delete or Key.Back:
                Session.DeleteSelection();
                SessionChanged();
                return true;
            case Key.Left: Nudge(-step, 0); return true;
            case Key.Right: Nudge(step, 0); return true;
            case Key.Up: Nudge(0, -step); return true;
            case Key.Down: Nudge(0, step); return true;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false;
        // Digits set a selected pasted image's opacity, 1 to 9 for 10% to 90% and 0 for solid,
        // as on pins.
        if (Session.SelectedAnnotation?.Tool == Tool.Image && Digit(e.Key) is { } digit)
        {
            Session.Restyle(style => style with { Opacity = digit == 0 ? 1 : digit / 10.0 });
            StylesCommitted?.Invoke();
            SessionChanged();
            return true;
        }
        if (e.KeySymbol is { Length: 1 } symbol && ToolInfo.ForKey(symbol[0]) is { } tool)
        {
            Choose(tool);
            return true;
        }
        return false;
    }

    /// <summary>For the toolbar and the keys alike. Picking the image tool asks for an image; the
    /// tool stays out whether one is chosen or not.</summary>
    public void Choose(Tool tool)
    {
        StopPickingText();
        Session.Choose(tool);
        SessionChanged();
        if (tool == Tool.Image) ImagePickRequested?.Invoke();
    }

    /// <summary>For the style bar: only the part <paramref name="change"/> touches moves.
    /// <paramref name="merging"/> is for a stream of changes that undoes as one.</summary>
    public void Restyle(Func<Style, Style> change, bool merging = false)
    {
        Session.Restyle(change, merging);
        if (!merging) StylesCommitted?.Invoke();
        SessionChanged();
        // A stream from the colour spectrum keeps its drag; a finished change hands the keys back.
        if (!merging) Focus();
    }

    public void DeleteSelection()
    {
        Session.DeleteSelection();
        SessionChanged();
        Focus();
    }

    private void Nudge(double dx, double dy)
    {
        Session.Nudge(dx, dy);
        SessionChanged();
    }

    /// <summary>[ and ] step the size, as in Photoshop. A run of them undoes as one step.</summary>
    private void StepSize(bool thicker)
    {
        var target = Session.SelectedAnnotation?.Tool ?? Session.Tool;
        if (!target.HasSize()) return;
        Session.Restyle(style => style with { Size = thicker ? style.Size.Thicker() : style.Size.Thinner() }, merging: true);
        StylesCommitted?.Invoke();
        SessionChanged();
    }

    private static int? Digit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };

    // Images

    /// <summary>Adds an image at <paramref name="scale"/> pixels per point, centred and shrunk to
    /// fit the capture when larger, then selects it; the tool in hand stays.</summary>
    public void InsertImage(SKImage image, double scale)
    {
        Session.Insert(new PastedImage(image), new Size(image.Width / scale, image.Height / scale));
        SessionChanged();
    }

    /// <summary>An image file, dropped or picked, at the scale its PNG records, as the Mac reads
    /// one. False when it cannot be read as an image.</summary>
    public bool InsertFile(string path)
    {
        try
        {
            if (Png.Decode(File.ReadAllBytes(path)) is not { } decoded) return false;
            InsertImage(decoded.Image, decoded.Scale);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void AcceptDrops()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) =>
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            foreach (var file in e.DataTransfer.TryGetFiles() ?? [])
                if (file.TryGetLocalPath() is { } path && InsertFile(path)) break;
        });
    }

    /// <summary>A clipboard image carries no scale of its own, so its pixels land one to one on the
    /// capture's: an older capture of the same screen pasted in lines up with this one.</summary>
    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is not null && ToSkia(bitmap) is { } image) InsertImage(image, Session.Scale);
    }

    private static SKImage? ToSkia(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, stride);
            var type = bitmap.Format == PixelFormats.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            var alpha = bitmap.AlphaFormat == AlphaFormat.Unpremul ? SKAlphaType.Unpremul : SKAlphaType.Premul;
            return SKImage.FromPixelCopy(new SKImageInfo(size.Width, size.Height, type, alpha), handle.AddrOfPinnedObject(), stride);
        }
        finally { handle.Free(); }
    }
}
