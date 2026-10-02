using Avalonia;
using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App.Editing;

/// <summary>What an editor needs from the app: where copies go, the settings as they are now,
/// the dialogs, and how to pin a finished image, which the app owns because a pin outlives the
/// editor that made it.</summary>
/// <param name="Pin">Takes the export, whose image it then owns, whether the capture had a size
/// of its own, which the pin's copies and saves then keep, and the library entry it came from.</param>
/// <param name="RememberStyles">Takes the session's styles and colour once a change is
/// finished, so the next capture starts with them.</param>
/// <param name="Library">Where an editor with an entry keeps its edits.</param>
/// <param name="LibraryChanged">Told when an editor wrote a new image, so the library window
/// shows it.</param>
/// <param name="Time">Null for the system clock; tests fire the keep timer themselves.</param>
/// <param name="RememberLayers">Told when the layers panel is opened or closed, so the next
/// editor opens the same way.</param>
/// <param name="Read">Reads an image for text, or for QR codes, copies what it finds and says so
/// near the given point; the image stays the caller's.</param>
internal sealed record EditorServices(IClipboard Clipboard, Func<Preferences> Preferences, IDialogs Dialogs,
                                      Action<ExportedImage, bool, LibraryEntry?>? Pin = null,
                                      Action<IReadOnlyDictionary<Tool, Style>, string>? RememberStyles = null,
                                      LibraryStore? Library = null, Action? LibraryChanged = null, TimeProvider? Time = null,
                                      Action? OpenLibrary = null, Func<SKImage, bool, PixelPoint?, Task>? Read = null,
                                      Action<MeasureSettings>? RememberMeasure = null, Action<Backdrop>? RememberBackdrop = null,
                                      Func<BackdropWallpaper?>? ReadWallpaper = null, Action<bool>? RememberLayers = null);
