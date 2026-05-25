using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Provider;
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
    private string? activeRoot;
    private bool created;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        RegisterExceptionLogging();
        PlatformHooks.OpenAndroidStorageSettingsAsync = OpenAndroidStorageSettingsAsync;

        var root = ConfigureWorkspaceEnvironment();
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
        created = true;
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (!created)
        {
            return;
        }

        var previousRoot = activeRoot;
        var root = ConfigureWorkspaceEnvironment();
        if (!string.Equals(previousRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                BootstrapBundledData(root);
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, ex.ToString());
            }

            Log.Info(LogTag, $"Workspace changed from '{previousRoot}' to '{root}'. Recreating activity.");
            Recreate();
        }
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
        if (IsSharedStorageAvailable())
        {
            return GetPublicWorkspaceRoot();
        }

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

    private static bool IsSharedStorageAvailable()
    {
        return Build.VERSION.SdkInt < BuildVersionCodes.R || global::Android.OS.Environment.IsExternalStorageManager;
    }

    private static string GetPublicWorkspaceRoot()
    {
        var sharedStorage = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(sharedStorage))
        {
            sharedStorage = "/storage/emulated/0";
        }

        return Path.Combine(sharedStorage, "RiivolutionIsoBuilder");
    }

    private Task OpenAndroidStorageSettingsAsync()
    {
        try
        {
            var packageUri = global::Android.Net.Uri.Parse($"package:{PackageName}");
            var intent = Build.VERSION.SdkInt >= BuildVersionCodes.R
                ? new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission, packageUri)
                : new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings, packageUri);
            StartActivity(intent);
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Could not open all-files storage settings: {ex}");
            var fallback = new Intent(global::Android.Provider.Settings.ActionManageAllFilesAccessPermission);
            StartActivity(fallback);
        }

        return Task.CompletedTask;
    }

    private void BootstrapBundledData(string root)
    {
        CopyAssetDirectory("data", Path.Combine(root, "data"));
    }

    private string ConfigureWorkspaceEnvironment()
    {
        var root = GetAppRootDirectory();
        activeRoot = root;
        System.Environment.SetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_ROOT", root);
        System.Environment.SetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_PUBLIC_ROOT", GetPublicWorkspaceRoot());
        System.Environment.SetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_SHARED_WORKSPACE", IsSharedStorageAvailable() ? "1" : "0");
        return root;
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
