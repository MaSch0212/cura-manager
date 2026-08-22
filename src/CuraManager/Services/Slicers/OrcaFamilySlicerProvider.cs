using System.IO;
using CuraManager.Extensions;
using CuraManager.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CuraManager.Services.Slicers;

/// <summary>
/// Shared behaviour for slicers descended from Bambu Studio (OrcaSlicer, Anycubic
/// Slicer Next and friends): the same 3mf layout and the same JSON app config.
/// </summary>
public abstract class OrcaFamilySlicerProvider : ISlicerProvider
{
    private const string ProjectSettingsEntry = "Metadata/project_settings.config";
    private const string SliceInfoEntry = "Metadata/slice_info.config";

    private static readonly string ProgramFilesDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles
    );
    private static readonly string AppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string IconResourceKey { get; }
    public abstract Version LatestSupportedVersion { get; }

    /// <summary>No Orca-family slicer rewrites profiles on open the way Cura does.</summary>
    public bool SupportsProfileUpdateOnOpen => false;

    /// <summary>Names the AppData directory and the <c>&lt;AppKey&gt;.conf</c> file inside it.</summary>
    protected abstract string AppKey { get; }

    protected abstract string[] ExecutableFileNames { get; }

    /// <summary>Matched case-insensitively against Program Files directory names.</summary>
    protected abstract string InstallDirNameFilter { get; }

    /// <summary>Producer key prefix in slice_info.config, for example <c>X-ACNext-</c>.</summary>
    protected abstract string SliceInfoHeaderPrefix { get; }

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate)
    {
        if (!string.Equals(candidate.Extension, ".3mf", StringComparison.OrdinalIgnoreCase))
            return SlicerMatch.None;

        var sliceInfo = candidate.ReadEntryText(SliceInfoEntry);
        if (
            sliceInfo != null
            && sliceInfo.Contains(SliceInfoHeaderPrefix, StringComparison.Ordinal)
        )
            return SlicerMatch.Exact;

        // Bambu lineage, but produced by an unidentified sibling. Older files from this
        // slicer also land here, because they emitted the inherited X-BBL- keys.
        if (candidate.HasEntry(ProjectSettingsEntry) || sliceInfo != null)
            return SlicerMatch.Probable;

        return SlicerMatch.None;
    }

    public IEnumerable<SlicerInstallation> FindInstallations()
    {
        if (!Directory.Exists(ProgramFilesDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(ProgramFilesDir))
        {
            var name = Path.GetFileName(dir);
            if (!name.Contains(InstallDirNameFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (GetExecutablePath(dir) == null)
                continue;

            var version = GetVersion(dir);
            if (version == null)
                continue;

            var appDataPath = Path.Combine(AppDataDir, AppKey);
            if (!Directory.Exists(appDataPath))
                continue;

            yield return new SlicerInstallation(
                version,
                name,
                dir,
                appDataPath,
                version <= LatestSupportedVersion
            );
        }
    }

    public Version GetVersion(string programFilesPath)
    {
        var exePath = GetExecutablePath(programFilesPath);
        if (exePath == null)
            return null;

        return VersionExtensions.SafeParse(FileVersionInfo.GetVersionInfo(exePath).FileVersion);
    }

    public bool ArePathsValid(SlicerSettings settings) =>
        GetExecutablePath(settings.ProgramFilesPath) != null
        && File.Exists(GetConfigFilePath(settings.AppDataPath));

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request)
    {
        SetLastExportPath(GetConfigFilePath(settings.AppDataPath), request.ProjectDirectory);
        Start(settings, request.ModelFiles);
    }

    public void OpenProject(SlicerSettings settings, string projectFilePath)
    {
        SetLastExportPath(
            GetConfigFilePath(settings.AppDataPath),
            Path.GetDirectoryName(projectFilePath)
        );
        Start(settings, new[] { projectFilePath });
    }

    private void Start(SlicerSettings settings, IReadOnlyList<string> files)
    {
        var exePath =
            GetExecutablePath(settings.ProgramFilesPath)
            ?? throw new FileNotFoundException($"Could not find the {DisplayName} executable.");

        Process.Start(
            new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"\"{string.Join("\" \"", files)}\"",
            }
        );
    }

    private string GetExecutablePath(string programFilesPath)
    {
        if (string.IsNullOrEmpty(programFilesPath) || !Directory.Exists(programFilesPath))
            return null;

        return ExecutableFileNames
            .Select(x => Path.Combine(programFilesPath, x))
            .FirstOrDefault(File.Exists);
    }

    private string GetConfigFilePath(string appDataPath) =>
        string.IsNullOrEmpty(appDataPath) ? null : Path.Combine(appDataPath, $"{AppKey}.conf");

    /// <summary>
    /// Points the slicer's save dialog at the project folder, the JSON equivalent of
    /// Cura's <c>dialog_save_path</c>. Every unrelated key is preserved. Internal and
    /// taking the config path directly so the round-trip behaviour can be pinned by a
    /// test without touching the filesystem layout of a real installation. Does nothing
    /// when <paramref name="configPath"/> is null/empty or does not point to an existing
    /// file — e.g. a fresh install where the slicer's AppData path is not configured yet.
    /// </summary>
    /// <remarks>
    /// The shipped Anycubic Slicer Next config file, like the PrusaSlicer-family
    /// <c>AppConfig</c> it inherits from, ends with a trailing <c>"# MD5 checksum ..."</c>
    /// comment line after the closing JSON brace. <see cref="JObject.Parse(string)"/>
    /// throws on that trailing content ("Additional text encountered after finished
    /// reading JSON content"), so the file is read with a raw <see cref="JsonTextReader"/>
    /// that stops as soon as the first JSON value is loaded instead. The comment line
    /// itself is not preserved on write-back — recomputing it would require an
    /// undocumented hash algorithm, and writing back a stale, wrong checksum seemed worse
    /// than omitting it. This has not been confirmed harmless against a real
    /// Anycubic Slicer Next launch; see the task-10 report.
    /// </remarks>
    internal static void SetLastExportPath(string configPath, string targetPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return;

        JObject config;
        using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(configPath))))
        {
            reader.Read();
            config = (JObject)JToken.Load(reader);
        }

        if (config["app"] is JObject app)
            app["last_export_path"] = targetPath;
        else
            config["last_export_path"] = targetPath;

        File.WriteAllText(configPath, config.ToString(Formatting.Indented));
    }
}
