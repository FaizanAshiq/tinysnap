namespace Tinysnap.Linux.Tests;

/// <summary>Tests that need a Linux desktop session: they skip everywhere else.</summary>
internal static class Linux
{
    public static void Only() => Assert.SkipUnless(OperatingSystem.IsLinux(), "needs Linux");

    public static string SessionBus => Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")
        ?? throw new InvalidOperationException("no session bus: run under dbus-run-session");
}
