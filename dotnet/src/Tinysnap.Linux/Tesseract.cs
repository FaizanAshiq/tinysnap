using System.Reflection;
using System.Runtime.InteropServices;

namespace Tinysnap.Linux;

/// <summary>Tesseract's C API, the few calls reading lines needs. The library is the one bundled
/// beside Tinysnap when there is one, else the system's.</summary>
internal static unsafe partial class Tesseract
{
    private const string Lib = "libtesseract.so.5";

    /// <summary>Iterator level for a line of text.</summary>
    public const int TextLine = 2;

    static Tesseract() => NativeLibrary.SetDllImportResolver(typeof(Tesseract).Assembly, Resolve);

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? path) =>
        name == Lib && NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "tesseract", Lib), out var bundled) ? bundled : 0;

    [LibraryImport(Lib)] public static partial nint TessBaseAPICreate();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial int TessBaseAPIInit3(nint api, string datapath, string language);
    [LibraryImport(Lib)] public static partial void TessBaseAPISetImage(nint api, byte* data, int width, int height, int bytesPerPixel, int bytesPerLine);
    [LibraryImport(Lib)] public static partial void TessBaseAPISetSourceResolution(nint api, int ppi);
    [LibraryImport(Lib)] public static partial int TessBaseAPIRecognize(nint api, nint monitor);
    [LibraryImport(Lib)] public static partial nint TessBaseAPIGetIterator(nint api);
    [LibraryImport(Lib)] public static partial void TessBaseAPIClear(nint api);
    [LibraryImport(Lib)] public static partial void TessBaseAPIDelete(nint api);
    [LibraryImport(Lib)] public static partial nint TessResultIteratorGetPageIterator(nint iterator);
    [LibraryImport(Lib)] public static partial nint TessResultIteratorGetUTF8Text(nint iterator, int level);
    [LibraryImport(Lib)] public static partial int TessResultIteratorNext(nint iterator, int level);
    [LibraryImport(Lib)] public static partial void TessResultIteratorDelete(nint iterator);
    [LibraryImport(Lib)] public static partial int TessPageIteratorBoundingBox(nint iterator, int level, out int left, out int top, out int right, out int bottom);
    [LibraryImport(Lib)] public static partial void TessDeleteText(nint text);
}
