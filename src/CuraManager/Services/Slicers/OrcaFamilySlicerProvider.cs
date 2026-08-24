using System.IO;
using System.Security.Cryptography;
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
    private const string ModelFileEntry = "3D/3dmodel.model";

    /// <summary>
    /// Bound, in bytes, for the prefix of <c>3D/3dmodel.model</c> read when looking for a
    /// flavour marker. That file's &lt;model&gt; element opens with a run of
    /// &lt;metadata&gt; tags before any &lt;resources&gt;/mesh data, and the markers this
    /// class cares about all live in that header. Measured against a real OrcaSlicer
    /// 2.4.2 project (D:\temp\slicer projects\OrcaSlicer.3mf, read-only): the
    /// <c>name="OrcaSlicer"</c> marker starts at byte offset 382 into the uncompressed
    /// entry, well inside the metadata block. This bound is ~20x that offset -- generous
    /// headroom for verbose metadata (long designer/description strings, extra keys) on
    /// other files -- while still capping the read far below the size of a real mesh,
    /// which on a file from the user's library measured 73,389,740 bytes uncompressed
    /// (i.e. reading it in full would allocate a ~147 MB UTF-16 string per probe, on the
    /// UI thread, for every 3mf regardless of which slicer produced it).
    /// </summary>
    private const int ModelMetadataHeaderBoundBytes = 8192;

    private static readonly string ProgramFilesDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles
    );
    private static readonly string AppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    private readonly IMsixPackageLocator _msixPackageLocator;

    protected OrcaFamilySlicerProvider(IMsixPackageLocator msixPackageLocator = null)
    {
        _msixPackageLocator = msixPackageLocator;
    }

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

    /// <summary>
    /// MSIX package name prefix (e.g. <c>"OrcaSlicer."</c> for the package
    /// <c>OrcaSlicer.OrcaSlicer</c>) used to find a Microsoft Store install via
    /// <see cref="IMsixPackageLocator"/>. <see langword="null"/> means this flavour is
    /// not distributed via the Store, and Store discovery is skipped entirely.
    /// </summary>
    protected virtual string MsixPackageNamePrefix => null;

    /// <summary>
    /// Producer key prefix in <c>Metadata/slice_info.config</c>, for example
    /// <c>X-ACNext-</c>. Return <see langword="null"/> when the flavour cannot be
    /// identified that way and <see cref="IsProducedByThisFlavour"/> is overridden instead.
    /// </summary>
    protected abstract string SliceInfoHeaderPrefix { get; }

    /// <summary>
    /// Process names the flavour's own slicer runs under, used as a last-resort hint
    /// when the archive could not be read because that slicer currently has the file
    /// open. For example OrcaSlicer's executable name yields <c>orca-slicer</c> and
    /// <c>OrcaSlicer</c> — both are listed because the real process casing could not be
    /// verified on this machine.
    /// </summary>
    protected abstract string[] ProcessNames { get; }

    /// <summary>
    /// Whether this specific Bambu-lineage flavour produced the file. The default
    /// matches <see cref="SliceInfoHeaderPrefix"/> against slice_info.config.
    /// Override when the flavour needs a different marker — OrcaSlicer and Bambu
    /// Studio both emit <c>X-BBL-</c>, so the prefix alone cannot separate them.
    /// </summary>
    protected virtual bool IsProducedByThisFlavour(SlicerProjectFileCandidate candidate)
    {
        if (string.IsNullOrEmpty(SliceInfoHeaderPrefix))
            return false;

        var sliceInfo = candidate.ReadEntryText(SliceInfoEntry);
        return sliceInfo != null
            && sliceInfo.Contains(SliceInfoHeaderPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the header prefix of <c>3D/3dmodel.model</c> (see
    /// <see cref="ModelMetadataHeaderBoundBytes"/>); exposed for flavour discriminators.
    /// Bounded rather than reading the whole entry, because that entry is the full mesh
    /// and can be tens to hundreds of megabytes.
    /// </summary>
    protected static string ReadModelMetadata(SlicerProjectFileCandidate candidate) =>
        candidate.ReadEntryTextPrefix(ModelFileEntry, ModelMetadataHeaderBoundBytes);

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate)
    {
        if (!string.Equals(candidate.Extension, ".3mf", StringComparison.OrdinalIgnoreCase))
            return SlicerMatch.None;

        if (IsProducedByThisFlavour(candidate))
            return SlicerMatch.Exact;

        // Bambu lineage, but produced by an unidentified sibling. Older files from this
        // slicer also land here, because they emitted the inherited X-BBL- keys.
        var sliceInfo = candidate.ReadEntryText(SliceInfoEntry);
        if (candidate.HasEntry(ProjectSettingsEntry) || sliceInfo != null)
            return SlicerMatch.Probable;

        // Unreadable because this flavour's own slicer has it open: weaker than a
        // marker, so Probable.
        if (candidate.LockingProcessNames.Any(x => ProcessNames.Contains(x)))
            return SlicerMatch.Probable;

        return SlicerMatch.None;
    }

    public IEnumerable<SlicerInstallation> FindInstallations() =>
        DeduplicateByProgramFilesPath(
            FindProgramFilesInstallations().Concat(FindMsixInstallations())
        );

    private IEnumerable<SlicerInstallation> FindProgramFilesInstallations()
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

    /// <summary>
    /// Discovers a Microsoft Store install via <see cref="IMsixPackageLocator"/>. Does
    /// nothing when this flavour is not Store-distributed (<see cref="MsixPackageNamePrefix"/>
    /// is <see langword="null"/>) or no locator was supplied (e.g. a non-Windows port).
    /// The package's <c>WindowsApps</c> folder cannot be browsed to by the user, but its
    /// executable can still be probed with <see cref="File.Exists(string)"/>.
    /// </summary>
    private IEnumerable<SlicerInstallation> FindMsixInstallations()
    {
        if (MsixPackageNamePrefix == null || _msixPackageLocator == null)
            yield break;

        foreach (var package in _msixPackageLocator.FindPackages(MsixPackageNamePrefix))
        {
            if (GetExecutablePath(package.InstallLocation) == null)
                continue;

            var appDataPath = Path.Combine(AppDataDir, AppKey);
            if (!Directory.Exists(appDataPath))
                continue;

            yield return new SlicerInstallation(
                package.Version,
                $"{DisplayName} (Microsoft Store)",
                package.InstallLocation,
                appDataPath,
                package.Version <= LatestSupportedVersion
            );
        }
    }

    /// <summary>
    /// Drops later entries whose <see cref="SlicerInstallation.ProgramFilesPath"/> was
    /// already yielded -- guards against the same install surfacing from both program-files
    /// scanning and MSIX discovery. Pure and side-effect free so it can be unit-tested
    /// directly with hand-built installations, without touching the registry or filesystem.
    /// </summary>
    internal static IEnumerable<SlicerInstallation> DeduplicateByProgramFilesPath(
        IEnumerable<SlicerInstallation> installations
    )
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installation in installations)
        {
            if (seen.Add(installation.ProgramFilesPath))
                yield return installation;
        }
    }

    public Version GetVersion(string programFilesPath)
    {
        var exePath = GetExecutablePath(programFilesPath);
        if (exePath == null)
            return null;

        var version = VersionExtensions.SafeParse(
            FileVersionInfo.GetVersionInfo(exePath).FileVersion
        );
        if (version != null)
            return version;

        // A Store-packaged executable ships no version resource at all (both FileVersion
        // and ProductVersion come back empty), so fall back to the package identity --
        // matched by install location, since that is all a caller passes in here. Trim a
        // trailing separator from both sides before comparing: programFilesPath usually
        // comes from a user-editable settings TextBox, and a path pasted from Explorer's
        // address bar commonly carries a trailing '\' that InstallLocation never has.
        if (MsixPackageNamePrefix == null || _msixPackageLocator == null)
            return null;

        var trimmedPath = Path.TrimEndingDirectorySeparator(programFilesPath);
        return _msixPackageLocator
            .FindPackages(MsixPackageNamePrefix)
            .FirstOrDefault(p =>
                string.Equals(
                    Path.TrimEndingDirectorySeparator(p.InstallLocation),
                    trimmedPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?.Version;
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
    /// The shipped Anycubic Slicer Next (and OrcaSlicer) config file, like the
    /// PrusaSlicer-family <c>AppConfig</c> it inherits from, ends with a trailing
    /// <c>"# MD5 checksum ..."</c> comment line after the closing JSON brace.
    /// <see cref="JObject.Parse(string)"/> throws on that trailing content ("Additional
    /// text encountered after finished reading JSON content"), so the file is read with a
    /// raw <see cref="JsonTextReader"/> that stops as soon as the first JSON value is
    /// loaded instead. On write-back the checksum line is regenerated (see
    /// <see cref="ComputeChecksum"/>) and the file is always terminated with a trailing
    /// newline: per <c>AppConfig.cpp</c>'s loader, a file ending in <c>}</c> with nothing
    /// after it makes <c>substr(last_pos+2)</c> throw <c>std::out_of_range</c> — uncaught
    /// by the surrounding JSON-parse-error handler — the very next time the slicer starts.
    /// </remarks>
    internal static void SetLastExportPath(string configPath, string targetPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return;

        JObject config;
        try
        {
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(configPath)));
            reader.Read();
            config = (JObject)JToken.Load(reader);
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or InvalidCastException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            // A zero-byte or non-object config (JToken.Load throwing, or the file holding
            // a JSON array/scalar instead of an object) is treated like a missing config:
            // there is no sensible place to patch last_export_path into, and the slicer
            // will recreate the file on next launch regardless.
            return;
        }

        if (config["app"] is JObject app)
            app["last_export_path"] = targetPath;
        else
            config["last_export_path"] = targetPath;

        // Write LF-only so the checksum computed here matches what the slicer's own
        // text-mode read (which normalizes CRLF -> LF before hashing) recomputes.
        var json = config.ToString(Formatting.Indented).Replace("\r\n", "\n");
        var checksum = ComputeChecksum(json);
        File.WriteAllText(configPath, $"{json}\n# MD5 checksum {checksum}\n");
    }

    /// <summary>
    /// Reproduces the PrusaSlicer-family <c>AppConfig</c> checksum: MD5 over the config
    /// text up to and including its last <c>}</c>, uppercase hex. This is a file-format
    /// checksum imposed by a third-party binary, not a security control, so MD5 is
    /// required rather than a design choice. Normalizes CRLF to LF itself before hashing
    /// -- the same normalization <see cref="SetLastExportPath"/> already applies to the
    /// text it writes -- so this method's result matches the slicer's own recomputed
    /// checksum regardless of the line endings <paramref name="json"/> arrives with.
    /// </summary>
#pragma warning disable CA5351 // MD5 is broken for security purposes, but this checksum must match a fixed third-party file format.
    private static string ComputeChecksum(string json)
    {
        var normalized = json.Replace("\r\n", "\n");
        var lastBrace = normalized.LastIndexOf('}');
        var toHash = lastBrace >= 0 ? normalized[..(lastBrace + 1)] : normalized;
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(toHash)));
    }
#pragma warning restore CA5351
}
