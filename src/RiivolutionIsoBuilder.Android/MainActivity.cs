using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
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
    private const string LogTag = "RiivolutionIsoBuilder";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        RegisterExceptionLogging();

        var root = GetAppRootDirectory();
        System.Environment.SetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_ROOT", root);
        ConfigureNativeToolDirectory();

        try
        {
            BootstrapBundledData(root);
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, ex.ToString());
        }

        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .UseAndroid()
            .WithInterFont()
            .LogToTrace();
    }

    private static void RegisterExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AndroidEnvironment.UnhandledExceptionRaiser -= OnAndroidUnhandledException;
        AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Log.Error(LogTag, args.ExceptionObject?.ToString() ?? "Unhandled exception");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Log.Error(LogTag, args.Exception.ToString());
    }

    private static void OnAndroidUnhandledException(object? sender, RaiseThrowableEventArgs args)
    {
        Log.Error(LogTag, args.Exception.ToString());
    }

    private string GetAppRootDirectory()
    {
        var baseDirectory = GetExternalFilesDir(null)?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = FilesDir?.AbsolutePath;
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
        }

        return Path.Combine(baseDirectory, "RiivolutionIsoBuilder");
    }

    private void BootstrapBundledData(string root)
    {
        CopyAssetDirectory("data", Path.Combine(root, "data"));
    }

    private void ConfigureNativeToolDirectory()
    {
        var nativeLibraryDirectory = ApplicationInfo?.NativeLibraryDir;
        if (!string.IsNullOrWhiteSpace(nativeLibraryDirectory))
        {
            System.Environment.SetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_TOOLS", nativeLibraryDirectory);
        }
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

        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
        {
            return;
        }

        try
        {
            using var source = Assets!.Open(assetPath);
            using var destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }
        catch (FileNotFoundException)
        {
            Log.Warn(LogTag, $"Skipping asset that is not a file: {assetPath}");
        }
    }

}
