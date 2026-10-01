using System.Runtime.InteropServices;

namespace Tinysnap.Linux;

/// <summary>The Xlib and XRandR calls an X11 session needs, and nothing more.</summary>
internal static unsafe partial class X11
{
    private const string Lib = "libX11.so.6";
    private const string RandR = "libXrandr.so.2";

    [StructLayout(LayoutKind.Sequential)]
    public struct XRRMonitorInfo
    {
        public nuint Name;
        public int Primary, Automatic, Outputs, X, Y, Width, Height, WidthMm, HeightMm;
        public nint OutputList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public nint Visual;
        public nuint Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public nuint Colormap;
        public int MapInstalled, MapState;
        public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }

    public const int IsViewable = 2;
    public const int ZPixmap = 2;

    [LibraryImport(Lib)] public static partial nint XOpenDisplay(nint name);
    [LibraryImport(Lib)] public static partial int XCloseDisplay(nint display);
    [LibraryImport(Lib)] public static partial nuint XDefaultRootWindow(nint display);
    [LibraryImport(Lib)] public static partial nint XResourceManagerString(nint display);
    [LibraryImport(Lib)] public static partial nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, nuint planes, int format);
    [LibraryImport(Lib)] public static partial int XQueryPointer(nint display, nuint window, out nuint root, out nuint child, out int rootX, out int rootY, out int x, out int y, out uint mask);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nuint XInternAtom(nint display, string name, int onlyIfExists);
    [LibraryImport(Lib)] public static partial int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint type,
                                                                     out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out nint data);
    [LibraryImport(Lib)] public static partial int XGetWindowAttributes(nint display, nuint window, out XWindowAttributes attributes);
    [LibraryImport(Lib)] public static partial int XTranslateCoordinates(nint display, nuint from, nuint to, int x, int y, out int toX, out int toY, out nuint child);
    [LibraryImport(Lib)] public static partial int XFree(nint data);
    [LibraryImport(RandR)] public static partial XRRMonitorInfo* XRRGetMonitors(nint display, nuint window, int active, out int count);
    [LibraryImport(RandR)] public static partial void XRRFreeMonitors(XRRMonitorInfo* monitors);

    // XImage fields Tinysnap reads, at their offsets on 64 bit Linux.
    public static int ImageWidth(nint image) => *(int*)image;
    public static int ImageHeight(nint image) => *(int*)(image + 4);
    public static nint ImageData(nint image) => *(nint*)(image + 16);
    public static int ImageBytesPerLine(nint image) => *(int*)(image + 44);
    public static int ImageBitsPerPixel(nint image) => *(int*)(image + 48);

    /// <summary>XDestroyImage is a macro over the image's own function table; destroy_image is its
    /// second entry, after create_image, which sits at offset 88.</summary>
    public static void DestroyImage(nint image)
    {
        var destroy = (delegate* unmanaged<nint, int>)(*(nint*)(image + 88 + 8));
        destroy(image);
    }
}
