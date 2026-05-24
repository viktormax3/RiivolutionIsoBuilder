using Android.App;
using Android.Content.PM;
using Android.OS;
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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        BootstrapBundledData();
        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .UseAndroid()
            .WithInterFont()
            .LogToTrace();
    }

    private void BootstrapBundledData()
    {
        var root = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "RiivolutionIsoBuilder");

        CopyAssetDirectory("data", Path.Combine(root, "data"));
        MarkExecutable(Path.Combine(root, "data", "tools", "android-arm64", "wit"));
        MarkExecutable(Path.Combine(root, "data", "tools", "android-arm64", "wstrt"));
    }

    private void CopyAssetDirectory(string assetPath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        var entries = Assets?.List(assetPath) ?? [];
        foreach (var entry in entries)
        {
            var childAssetPath = $"{assetPath}/{entry}";
            var childDestinationPath = Path.Combine(destinationPath, entry);
            var children = Assets?.List(childAssetPath) ?? [];

            if (children.Length > 0)
            {
                CopyAssetDirectory(childAssetPath, childDestinationPath);
                continue;
            }

            CopyAssetFile(childAssetPath, childDestinationPath);
        }
    }

    private void CopyAssetFile(string assetPath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (File.Exists(destinationPath))
        {
            return;
        }

        using var source = Assets!.Open(assetPath);
        using var destination = File.Create(destinationPath);
        source.CopyTo(destination);
    }

    private static void MarkExecutable(string path)
    {
        if (File.Exists(path))
        {
            _ = new Java.IO.File(path).SetExecutable(true, false);
        }
    }
}
