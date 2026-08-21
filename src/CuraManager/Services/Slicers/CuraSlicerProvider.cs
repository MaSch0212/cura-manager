using System.IO;
using System.IO.Compression;
using CuraManager.Extensions;
using CuraManager.Legacy.CuraAutomation;
using CuraManager.Models;
using IniParser;
using IniParser.Model;

namespace CuraManager.Services.Slicers;

/// <summary>
/// <see cref="ISlicerProvider"/> implementation for UltiMaker Cura.
/// </summary>
public class CuraSlicerProvider : ISlicerProvider
{
    public const string ProviderId = "cura";

    private static readonly string[] CuraProcessNames = ["Cura", "UltiMaker-Cura"];

    private static readonly string ProgramFilesDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles
    );
    private static readonly string AppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    private readonly ICuraProjectNameAutomation _automation;
    private readonly Func<bool> _isLegacyNamingEnabled;

    public CuraSlicerProvider(
        ICuraProjectNameAutomation automation,
        Func<bool> isLegacyNamingEnabled
    )
    {
        _automation = automation;
        _isLegacyNamingEnabled = isLegacyNamingEnabled;
    }

    public string Id => ProviderId;
    public string DisplayName => "UltiMaker Cura";
    public string IconResourceKey => "CuraIcon";
    public bool SupportsProfileUpdateOnOpen => true;
    public Version LatestSupportedVersion { get; } = new Version(5, 10, 0, 0);

    public IEnumerable<SlicerInstallation> FindInstallations()
    {
        return from programDir in Directory.EnumerateDirectories(ProgramFilesDir)
            let programName = Path.GetFileName(programDir)
            where
                programName.Contains("ultimaker", StringComparison.OrdinalIgnoreCase)
                && programName.Contains("cura", StringComparison.OrdinalIgnoreCase)
                && CheckCuraProgramFilesPath(programDir)
            let curaVersion = GetVersion(programDir)
            where curaVersion != null
            let curaAppDataPath = FindAppDataDir(curaVersion)
            where curaAppDataPath != null
            select new SlicerInstallation(
                curaVersion,
                programName,
                programDir,
                curaAppDataPath,
                curaVersion <= LatestSupportedVersion
            );

        static string FindAppDataDir(Version curaVersion)
        {
            for (int i = 4; i > 0; i--)
            {
                var path = Path.Combine(AppDataDir, "cura", curaVersion.ToString(i));
                if (CheckCuraAppDataPath(path))
                    return path;
            }

            return null;
        }
    }

    public Version GetVersion(string programFilesPath)
    {
        if (programFilesPath is null or "")
            return null;

        var exePath = GetCuraExecutableFilePath(programFilesPath);
        if (exePath is null)
            return null;

        var exeName = Path.GetFileName(exePath);
        var versionFilePath = exePath;

        if (!string.Equals(exeName, "cura.exe", StringComparison.OrdinalIgnoreCase))
            versionFilePath = Path.Combine(Path.GetDirectoryName(exePath), "uninstall.exe");

        if (File.Exists(versionFilePath))
            return VersionExtensions.SafeParse(
                FileVersionInfo.GetVersionInfo(versionFilePath).FileVersion
            );

        var curaVersionPy = Path.Combine(Path.GetDirectoryName(exePath), "cura", "CuraVersion.py");
        if (File.Exists(curaVersionPy))
        {
            var versionMatch = RegularExpressions
                .ExtractCuraVersionFromPython()
                .Match(File.ReadAllText(curaVersionPy));
            if (versionMatch.Success)
                return Version.Parse(versionMatch.Groups["version"].Value).Normalize();
        }

        return null;
    }

    public bool ArePathsValid(SlicerSettings settings)
    {
        return CheckCuraAppDataPath(settings.AppDataPath)
            && CheckCuraProgramFilesPath(settings.ProgramFilesPath);
    }

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate)
    {
        if (!string.Equals(candidate.Extension, ".3mf", StringComparison.OrdinalIgnoreCase))
            return SlicerMatch.None;

        if (candidate.HasEntryStartingWith("Cura/"))
            return SlicerMatch.Exact;

        // Unreadable because Cura itself has it open: weaker than a marker, so Probable.
        if (candidate.LockingProcessNames.Any(x => CuraProcessNames.Contains(x)))
            return SlicerMatch.Probable;

        return SlicerMatch.None;
    }

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request)
    {
        SetSaveDialogPath(GetCuraConfigPath(settings), request.ProjectDirectory);

        var curaPath =
            GetCuraExecutableFilePath(settings.ProgramFilesPath)
            ?? throw new FileNotFoundException("Could not find cura executable.");

        var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = curaPath,
                Arguments = $"\"{string.Join("\" \"", request.ModelFiles)}\"",
            }
        );

        if (string.IsNullOrEmpty(request.ProjectName) || !_isLegacyNamingEnabled())
            return;

        process.WaitForInputIdle();
        _automation.SetProjectName(
            process,
            Path.GetFileNameWithoutExtension(curaPath),
            request.ProjectName
        );
    }

    public void OpenProject(SlicerSettings settings, string projectFilePath)
    {
        SetSaveDialogPath(GetCuraConfigPath(settings), Path.GetDirectoryName(projectFilePath));

        if (settings.UpdateProjectsOnOpen)
            UpdateCuraProjectConfigs(projectFilePath, settings);

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    GetCuraExecutableFilePath(settings.ProgramFilesPath)
                    ?? throw new FileNotFoundException("Could not find cura executable."),
                Arguments = $"\"{projectFilePath}\"",
            }
        );
    }

    private static bool CheckCuraAppDataPath(string appDataPath)
    {
        return Directory.Exists(appDataPath) && File.Exists(Path.Combine(appDataPath, "cura.cfg"));
    }

    private static bool CheckCuraProgramFilesPath(string programFilesPath)
    {
        return Directory.Exists(programFilesPath)
            && (
                File.Exists(Path.Combine(programFilesPath, "Cura.exe"))
                || File.Exists(Path.Combine(programFilesPath, "Ultimaker-Cura.exe"))
            );
    }

    private static string GetCuraExecutableFilePath(string curaPath)
    {
        if (string.IsNullOrEmpty(curaPath))
            return null;

        string curaExecutableFile = Path.Combine(curaPath, "Ultimaker-Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = Path.Combine(curaPath, "Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = null;
        return curaExecutableFile;
    }

    /// <summary>
    /// Resolves the path to <c>cura.cfg</c> for the given settings, or <see langword="null"/>
    /// when the AppData path is not configured yet.
    /// </summary>
    private static string GetCuraConfigPath(SlicerSettings settings) =>
        string.IsNullOrEmpty(settings.AppDataPath)
            ? null
            : Path.Combine(settings.AppDataPath, "cura.cfg");

    /// <summary>
    /// Rewrites the <c>dialog_save_path</c> key in <c>cura.cfg</c>, preserving every other
    /// key. Internal and taking the config path directly so the round-trip behaviour can be
    /// pinned by a test without touching the filesystem layout of a real Cura installation.
    /// Does nothing when <paramref name="curaConfigPath"/> is null/empty or does not point to
    /// an existing file — e.g. a fresh install where Cura's AppData path is not configured yet.
    /// </summary>
    internal static void SetSaveDialogPath(string curaConfigPath, string targetPath)
    {
        if (string.IsNullOrEmpty(curaConfigPath) || !File.Exists(curaConfigPath))
            return;

        var targetPathForConfig = Uri.UnescapeDataString(new Uri(targetPath).PathAndQuery);

        var parser = new StreamIniDataParser();
        IniData iniData;

        using (var sr = new StreamReader(curaConfigPath, Encoding.UTF8))
            iniData = parser.ReadData(sr);

        iniData["local_file"]["dialog_save_path"] = targetPathForConfig;

        using (var sw = new StreamWriter(curaConfigPath, false, new UTF8Encoding(false)))
            parser.WriteData(sw, iniData);
    }

    private static void UpdateCuraProjectConfigs(string fileName, SlicerSettings settings)
    {
        // A fresh/partial install may have neither path configured; skip rather than let
        // Path.Combine throw ArgumentNullException before the caller's clearer
        // FileNotFoundException from GetCuraExecutableFilePath ever runs.
        if (
            string.IsNullOrEmpty(settings.AppDataPath)
            || string.IsNullOrEmpty(settings.ProgramFilesPath)
        )
            return;

        string curaResourcesPath4x = Path.Combine(settings.ProgramFilesPath, "resources");
        string curaResourcesPath5x = Path.Combine(
            settings.ProgramFilesPath,
            "share",
            "cura",
            "resources"
        );

        using var file = ZipFile.Open(fileName, ZipArchiveMode.Update);
        foreach (
            var cf in file
                .Entries.Where(x =>
                    x.FullName.StartsWith("Cura/", StringComparison.OrdinalIgnoreCase)
                    && x.Length > 0
                )
                .ToArray()
        )
        {
            if (
                cf.Name.EndsWith("_user.inst.cfg", StringComparison.OrdinalIgnoreCase)
                || cf.Name.EndsWith(".extruder.cfg", StringComparison.OrdinalIgnoreCase)
                || cf.Name.EndsWith(".global.cfg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cf.Name, "version.ini", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            if (string.Equals(cf.Name, "preferences.cfg", StringComparison.OrdinalIgnoreCase))
            {
                var parser = new StreamIniDataParser();
                IniData prefData;

                using (var prefStr = cf.Open())
                {
                    using (var sr = new StreamReader(prefStr))
                        prefData = parser.ReadData(sr);

                    IniData curaCfgData;
                    using (
                        var sr = new StreamReader(Path.Combine(settings.AppDataPath, "cura.cfg"))
                    )
                        curaCfgData = parser.ReadData(sr);

                    foreach (var s in prefData.Sections)
                    {
                        var cs = curaCfgData.Sections[s.SectionName];
                        if (cs == null)
                            continue;
                        foreach (var ss in s.Keys)
                        {
                            if (!cs.ContainsKey(ss.KeyName))
                                continue;
                            ss.Value = cs[ss.KeyName];
                        }
                    }
                }

                var name = cf.FullName;
                cf.Delete();
                var ne = file.CreateEntry(name);

                using (var nes = ne.Open())
                using (var sw = new StreamWriter(nes, new UTF8Encoding(false)))
                    sw.Write(prefData.ToString().Replace("\r\n", "\n"));
            }
            else
            {
                var escapedCfName = Uri.EscapeDataString(cf.Name).Replace("%20", "+");
                var of = Directory
                    .EnumerateFiles(
                        settings.AppDataPath,
                        escapedCfName,
                        SearchOption.AllDirectories
                    )
                    .FirstOrDefault();
                if (of == null && Directory.Exists(curaResourcesPath4x))
                    of = Directory
                        .EnumerateFiles(
                            curaResourcesPath4x,
                            escapedCfName,
                            SearchOption.AllDirectories
                        )
                        .FirstOrDefault();
                if (of == null && Directory.Exists(curaResourcesPath5x))
                    of = Directory
                        .EnumerateFiles(
                            curaResourcesPath5x,
                            escapedCfName,
                            SearchOption.AllDirectories
                        )
                        .FirstOrDefault();
                if (of == null)
                    continue;

                var name = cf.FullName;
                cf.Delete();
                var ne = file.CreateEntry(name);
                using (var nes = ne.Open())
                using (var sw = new StreamWriter(nes, new UTF8Encoding(false)))
                    sw.Write(File.ReadAllText(of).Replace("\r\n", "\n"));
            }
        }
    }
}
