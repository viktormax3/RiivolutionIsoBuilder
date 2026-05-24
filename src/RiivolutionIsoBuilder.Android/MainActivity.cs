using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using RiivolutionIsoBuilder.UI;

namespace RiivolutionIsoBuilder.Android;

[Activity(
    Label = "Riivolution ISO Builder",
    Theme = "@style/AppTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .UseAndroid()
            .WithInterFont()
            .LogToTrace();
    }
}
