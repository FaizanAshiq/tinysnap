using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Tinysnap.App.Tests;

public class ShellTests
{
    [AvaloniaFact]
    public void TheAppShowsTooltipsAfterAThirdOfASecond()
    {
        // The system's second felt slow on a toolbar of seventeen tools.
        var button = new Button { Content = "Arrow" };
        ToolTip.SetTip(button, "Arrow (A)");
        var window = new Window { Content = button };
        window.Show();
        Assert.Equal(300, ToolTip.GetShowDelay(button));
    }

    [AvaloniaFact]
    public void EveryWindowCarriesTheAppIcon()
    {
        var window = new Window();
        window.Show();
        Assert.NotNull(window.Icon);
        Assert.NotNull(TestServices.Editor(TestServices.Make()).Icon);
    }
}
