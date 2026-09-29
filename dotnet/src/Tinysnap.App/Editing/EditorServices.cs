using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App.Editing;

/// <summary>What an editor needs from the app: where copies go, the settings as they are now,
/// the dialogs, and how to pin a finished image, which the app owns because a pin outlives the
/// editor that made it.</summary>
/// <param name="Pin">Takes the export, whose image it then owns, and whether the capture had a
/// size of its own, which the pin's copies and saves then keep.</param>
/// <param name="RememberStyles">Takes the session's styles and colour once a change is
/// finished, so the next capture starts with them.</param>
internal sealed record EditorServices(IClipboard Clipboard, Func<Preferences> Preferences, IDialogs Dialogs,
                                      Action<ExportedImage, bool>? Pin = null,
                                      Action<IReadOnlyDictionary<Tool, Style>, string>? RememberStyles = null);
