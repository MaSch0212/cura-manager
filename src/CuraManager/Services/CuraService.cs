using System.IO;
using System.IO.Compression;
using CuraManager.Extensions;
using CuraManager.Legacy.CuraAutomation;
using CuraManager.Models;
using CuraManager.Views;
using IniParser;
using IniParser.Model;
using Application = System.Windows.Application;

namespace CuraManager.Services;

public class CuraService(ISettingsService settingsService) : ICuraService
{
    private static readonly string ProgramFilesDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles
    );
    private static readonly string AppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    public Version LatestSupportedCuraVersion { get; } = new Version(5, 10, 0, 0);

    internal CuraService()
        : this(ServiceContext.GetService<ISettingsService>()) { }

    public async Task<bool> CreateCuraProject(PrintElement element)
    {
        var dialog = new CreateCuraProjectDialog(element)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() == true)
        {
            var modelFiles =
                from x in dialog.Models
                where x.IsEnabled && x.Amount > 0
                from m in Enumerable.Range(0, x.Amount)
                select x.Element.FilePath;
            await Task.Run(() => OpenCura(element, dialog.ProjectName, modelFiles));
            return true;
        }

        return false;
    }

    public void OpenCura(PrintElement element, string printName, IEnumerable<string> modelsToAdd)
    {
        var settings = settingsService.LoadSettings();

        SetCuraSaveDialogPath(element.DirectoryLocation, settings);

        var curaPath =
            GetCuraExecutableFilePath(settings.CuraProgramFilesPath)
            ?? throw new FileNotFoundException("Could not find cura executable.");
        var curaFileName = Path.GetFileNameWithoutExtension(curaPath);

        var p = Process.Start(
            new ProcessStartInfo
            {
                FileName = curaPath,
                Arguments = $"\"{string.Join("\" \"", modelsToAdd)}\"",
            }
        );
        p.WaitForInputIdle();

        ServiceContext
            .GetService<ICuraProjectNameAutomation>()
            .SetProjectName(p, curaFileName, printName);
    }

    public void OpenCuraProject(string fileName)
    {
        var settings = settingsService.LoadSettings();

        SetCuraSaveDialogPath(Path.GetDirectoryName(fileName), settings);

        if (settings.UpdateCuraProjectsOnOpen)
            UpdateCuraProjectConfigs(fileName, settings);

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    GetCuraExecutableFilePath(settings.CuraProgramFilesPath)
                    ?? throw new FileNotFoundException("Could not find cura executable."),
                Arguments = $"\"{fileName}\"",
            }
        );
    }

    public Version GetCuraVersion(string curaPath)
    {
        if (curaPath is null or "")
            return null;

        var exePath = GetCuraExecutableFilePath(curaPath);
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

    public bool AreCuraPathsCorrect(AppSettings settings)
    {
        return CheckCuraAppDataPath(settings.CuraAppDataPath)
            && CheckCuraProgramFilesPath(settings.CuraProgramFilesPath);
    }

    public IEnumerable<CuraVersion> FindAvailableCuraVersions()
    {
        return from programDir in Directory.EnumerateDirectories(ProgramFilesDir)
            let programName = Path.GetFileName(programDir)
            where
                programName.Contains("ultimaker", StringComparison.OrdinalIgnoreCase)
                && programName.Contains("cura", StringComparison.OrdinalIgnoreCase)
                && CheckCuraProgramFilesPath(programDir)
            let curaVersion = GetCuraVersion(programDir)
            where curaVersion != null
            let curaAppDataPath = FindAppDataDir(curaVersion)
            where curaAppDataPath != null
            select new CuraVersion(
                curaVersion,
                programName,
                programDir,
                curaAppDataPath,
                curaVersion <= LatestSupportedCuraVersion
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
        string curaExecutableFile = Path.Combine(curaPath, "Ultimaker-Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = Path.Combine(curaPath, "Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = null;
        return curaExecutableFile;
    }

    private static void SetCuraSaveDialogPath(string targetPath, AppSettings settings)
    {
        var curaConfigPath = Path.Combine(settings.CuraAppDataPath, "cura.cfg");
        var targetPathForConfig = Uri.UnescapeDataString(new Uri(targetPath).PathAndQuery);

        var parser = new StreamIniDataParser();
        IniData iniData;

        using (var sr = new StreamReader(curaConfigPath, Encoding.UTF8))
            iniData = parser.ReadData(sr);

        iniData["local_file"]["dialog_save_path"] = targetPathForConfig;

        using (var sw = new StreamWriter(curaConfigPath, false, new UTF8Encoding(false)))
            parser.WriteData(sw, iniData);
    }

    private static void UpdateCuraProjectConfigs(string fileName, AppSettings settings)
    {
        string curaResourcesPath4x = Path.Combine(settings.CuraProgramFilesPath, "resources");
        string curaResourcesPath5x = Path.Combine(
            settings.CuraProgramFilesPath,
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
                        var sr = new StreamReader(
                            Path.Combine(settings.CuraAppDataPath, "cura.cfg")
                        )
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
                        settings.CuraAppDataPath,
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
