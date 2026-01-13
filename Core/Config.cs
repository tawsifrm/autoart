using System;
using System.IO;
using System.Numerics;
using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpHook.Native;
using SkiaSharp;

namespace AutoArt.Core;

public static class ImageExtensions
{
    public static Bitmap ConvertToAvaloniaBitmap(this SKBitmap bitmap)
    {
        using var encodedStream = new MemoryStream();
        bitmap.Encode(encodedStream, SKEncodedImageFormat.Png, 100);
        encodedStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(encodedStream);
    }
}

public class Config
{
    public static string FolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoArt");

    public static KeyCode Keybind_StartDrawing = KeyCode.VcLeftShift;
    public static KeyCode Keybind_StopDrawing = KeyCode.VcLeftAlt;
    public static KeyCode Keybind_PauseDrawing = KeyCode.VcBackslash;
    public static KeyCode Keybind_SkipRescan = KeyCode.VcBackspace;
    public static KeyCode Keybind_LockPreview = KeyCode.VcLeftControl;
    public static KeyCode Keybind_ClearLock = KeyCode.VcBackQuote;

    public static Vector2 Preview_LastLockPos = new(0, 0);

    // =====================================================================================
    // ACTION SET OPTIMIZER SETTINGS
    // These control how small action sets are merged to improve drawing performance.
    // =====================================================================================

    /// <summary>
    /// Enable/disable the action set optimizer. When enabled, small nearby action sets
    /// are merged to reduce drawing overhead.
    /// </summary>
    public static bool Optimizer_Enabled = true;

    /// <summary>
    /// Action sets with this many points or fewer are considered "small" and eligible for merging.
    /// </summary>
    public static int Optimizer_SmallSetThreshold = 10;

    /// <summary>
    /// Maximum distance (in pixels) between action set centroids for them to be clustered.
    /// </summary>
    public static float Optimizer_ClusterDistance = 25f;

    /// <summary>
    /// Minimum number of small action sets in a cluster before merging is applied.
    /// </summary>
    public static int Optimizer_MinClusterSize = 3;

    /// <summary>
    /// Maximum distance between consecutive points to be considered "connected".
    /// Points further apart will be in separate action sets (pen-up between them).
    /// Default 1.5 allows diagonal adjacency (8-connected pixels).
    /// </summary>
    public static float Optimizer_MaxConnectedDistance = 1.5f;

    /// <summary>
    /// When true, sorts action sets spatially (serpentine pattern) to minimize mouse travel.
    /// </summary>
    public static bool Optimizer_SpatialOrdering = true;

    public static string ConfigPath = Path.Combine(FolderPath, "config.json");
    public static string ThemesPath = Path.Combine(FolderPath, "Themes");
    public static string CachePath = Path.Combine(FolderPath, "Cache");

    public static void Init()
    {
        Directory.CreateDirectory(FolderPath);
        if (!File.Exists(ConfigPath))
        {
            JObject obj = new();
            var emptyJObject = JsonConvert.SerializeObject(obj);
            File.WriteAllText(ConfigPath, emptyJObject);
        }

        // Check Configuration Path for Themes
        if (GetEntry("SavedThemesPath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(ThemesPath);
            SetEntry("SavedThemesPath", ThemesPath);
        }
        else
        {
            ThemesPath = GetEntry("SavedThemesPath")!;
        }

        // Check Configuration Path for Cache
        if (GetEntry("SavedCachePath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(CachePath);
            SetEntry("SavedCachePath", CachePath);
        }
        else
        {
            CachePath = GetEntry("SavedCachePath")!;
        }

        // Get Keybinds
        if (GetEntry("Keybind_StartDrawing") is not null)
        {
            Keybind_StartDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StartDrawing")!);
        }
        if (GetEntry("Keybind_StopDrawing") is not null)
        {
            Keybind_StopDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StopDrawing")!);
        }
        if (GetEntry("Keybind_PauseDrawing") is not null)
        {
            Keybind_PauseDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_PauseDrawing")!);
        }
        if (GetEntry("Keybind_SkipRescan") is not null)
        {
            Keybind_SkipRescan = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_SkipRescan")!);
        }
        if (GetEntry("Keybind_LockPreview") is not null)
        {
            Keybind_LockPreview = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_LockPreview")!);
        }
        if (GetEntry("Keybind_ClearLock") is not null)
        {
            Keybind_ClearLock = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_ClearLock")!);
        }

        if (GetEntry("Preview_LastLockedX") is not null && GetEntry("Preview_LastLockedY") is not null)
        {
            Preview_LastLockPos = new Vector2(int.Parse(GetEntry("Preview_LastLockedX")!), int.Parse(GetEntry("Preview_LastLockedY")!));
        }

        // Load optimizer settings from config
        LoadOptimizerSettings();
    }

    /// <summary>
    /// Loads action set optimizer settings from config and applies them.
    /// </summary>
    private static void LoadOptimizerSettings()
    {
        if (GetEntry("Optimizer_Enabled") is not null)
        {
            Optimizer_Enabled = bool.Parse(GetEntry("Optimizer_Enabled")!);
        }
        if (GetEntry("Optimizer_SmallSetThreshold") is not null)
        {
            Optimizer_SmallSetThreshold = int.Parse(GetEntry("Optimizer_SmallSetThreshold")!);
        }
        if (GetEntry("Optimizer_ClusterDistance") is not null)
        {
            Optimizer_ClusterDistance = float.Parse(GetEntry("Optimizer_ClusterDistance")!);
        }
        if (GetEntry("Optimizer_MinClusterSize") is not null)
        {
            Optimizer_MinClusterSize = int.Parse(GetEntry("Optimizer_MinClusterSize")!);
        }
        if (GetEntry("Optimizer_MaxConnectedDistance") is not null)
        {
            Optimizer_MaxConnectedDistance = float.Parse(GetEntry("Optimizer_MaxConnectedDistance")!);
        }
        if (GetEntry("Optimizer_SpatialOrdering") is not null)
        {
            Optimizer_SpatialOrdering = bool.Parse(GetEntry("Optimizer_SpatialOrdering")!);
        }

        // Apply settings to the optimizer
        ActionSetOptimizer.EnableOptimization = Optimizer_Enabled;
        ActionSetOptimizer.Configure(
            Optimizer_SmallSetThreshold,
            Optimizer_ClusterDistance,
            Optimizer_MinClusterSize,
            Optimizer_MaxConnectedDistance,
            Optimizer_SpatialOrdering
        );
    }

    public static string? GetEntry(string entry)
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = File.ReadAllText(ConfigPath);
        var parse = JObject.Parse(json);
        return (string?)parse[entry];
    }

    public static bool SetEntry(string entry, string data)
    {
        if (!File.Exists(ConfigPath)) return false;
        var json = File.ReadAllText(ConfigPath);
        var jsonFile = JObject.Parse(json);
        jsonFile[entry] = data;
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile, Formatting.Indented));
        return true;
    }
}

public class Utils
{
    public static string LogFolder = Path.Combine(Config.FolderPath, "logs");
    public static string LogsPath = Path.Combine(LogFolder, $"{DateTime.Now:dd.MM.yyyy}.txt");
    public static bool LoggingEnabled = Config.GetEntry("logsEnabled") == "True";
    public static StreamWriter? LogObject;

    public static void Log(object data)
    {
        string text = data.ToString() ?? "null";
        Console.WriteLine(text);
        if (!LoggingEnabled) return;
        if (!Directory.Exists(LogFolder)) Directory.CreateDirectory(LogFolder);
        if (LogObject == null) LogObject = new StreamWriter(LogsPath);
        LogObject.WriteLine(text);
        LogObject.Flush();
    }

    public static void Copy(string sourceDirectory, string targetDirectory)
    {
        var diSource = new DirectoryInfo(sourceDirectory);
        var diTarget = new DirectoryInfo(targetDirectory);
        CopyAll(diSource, diTarget);
    }

    public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy each file into the new directory.
        foreach (var fi in source.GetFiles())
        {
            Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
            fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
        }

        // Copy each subdirectory using recursion.
        foreach (var diSourceSubDir in source.GetDirectories())
        {
            var nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }
}
