﻿using CuraManager.Models;
using CuraManager.Views;
using IniParser;
using IniParser.Model;
using System.IO;
using System.IO.Compression;
using System.Windows.Automation;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace CuraManager.Services;

public class CuraService : ICuraService
{
    private readonly ISettingsService _settingsService;

    internal CuraService()
        : this(ServiceContext.GetService<ISettingsService>())
    {
    }

    public CuraService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<bool> CreateCuraProject(PrintElement element)
    {
        var dialog = new CreateCuraProjectDialog(element) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
        {
            var modelFiles = from x in dialog.Models
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
        var settings = _settingsService.LoadSettings();

        SetCuraSaveDialogPath(element.DirectoryLocation, settings);

        var curaPath = GetCuraExecutableFilePath(settings) ?? throw new FileNotFoundException("Could not find cura executable.");
        var curaFileName = Path.GetFileNameWithoutExtension(curaPath);

        var p = Process.Start(new ProcessStartInfo
        {
            FileName = curaPath,
            Arguments = $"\"{string.Join("\" \"", modelsToAdd)}\"",
        });
        p.WaitForInputIdle();

        if (curaFileName == "Cura")
            SetName4x();
        else
            SetName5x();

        void SetName4x()
        {
            if (TryFindChild(AutomationElement.RootElement, CheckWindow, TimeSpan.FromMinutes(5), out var curaWindow) &&
                TryFindChild(curaWindow, CheckEditNameButton, TimeSpan.FromSeconds(30), out var editNameButton) &&
                editNameButton.TryGetCurrentPattern(InvokePattern.Pattern, out var objInvokePattern) && objInvokePattern is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                SendKeys.SendWait($"{printName}{{ENTER}}");
            }

            bool CheckWindow(TreeWalker treeWalker, AutomationElement e)
            {
                return e.Current.ProcessId == p.Id &&
                       e.Current.Name?.Contains("Ultimaker Cura") == true;
            }

            bool CheckEditNameButton(TreeWalker treeWalker, AutomationElement e)
            {
                return ReferenceEquals(e.Current.ControlType, ControlType.Button) &&
                        string.IsNullOrEmpty(e.Current.Name) &&
                        !e.Current.IsOffscreen &&
                        ReferenceEquals(treeWalker.GetNextSibling(e)?.Current.ControlType, ControlType.Edit);
            }
        }

        void SetName5x()
        {
            if (TryFindChild(AutomationElement.RootElement, CheckWindow, TimeSpan.FromMinutes(5), out var curaWindow) &&
                TryFindChild(curaWindow, CheckEditNameButton, TimeSpan.FromSeconds(30), out var editNameButton) &&
                editNameButton.TryGetCurrentPattern(ValuePattern.Pattern, out var objValuePattern) && objValuePattern is ValuePattern valuePattern)
            {
                valuePattern.SetValue(printName);
            }

            bool CheckWindow(TreeWalker treeWalker, AutomationElement e)
            {
                return e.Current.ProcessId == p.Id &&
                       e.Current.Name?.Contains("Ultimaker Cura") == true;
            }

            bool CheckEditNameButton(TreeWalker treeWalker, AutomationElement e)
            {
                return ReferenceEquals(e.Current.ControlType, ControlType.Edit) && !e.Current.IsOffscreen;
            }
        }
    }

    public void OpenCuraProject(string fileName)
    {
        var settings = _settingsService.LoadSettings();

        SetCuraSaveDialogPath(Path.GetDirectoryName(fileName), settings);

        if (settings.UpdateCuraProjectsOnOpen)
            UpdateCuraProjectConfigs(fileName, settings);

        Process.Start(new ProcessStartInfo
        {
            FileName = GetCuraExecutableFilePath(settings) ?? throw new FileNotFoundException("Could not find cura executable."),
            Arguments = $"\"{fileName}\"",
        });
    }

    public bool AreCuraPathsCorrect(CuraManagerSettings settings)
    {
        return Directory.Exists(settings.CuraAppDataPath)
            && Directory.Exists(settings.CuraProgramFilesPath)
            && File.Exists(Path.Combine(settings.CuraAppDataPath, "cura.cfg"))
            && (File.Exists(Path.Combine(settings.CuraProgramFilesPath, "Cura.exe")) || File.Exists(Path.Combine(settings.CuraProgramFilesPath, "Ultimaker-Cura.exe")));
    }

    private static string GetCuraExecutableFilePath(CuraManagerSettings settings)
    {
        string curaExecutableFile = Path.Combine(settings.CuraProgramFilesPath, "Ultimaker-Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = Path.Combine(settings.CuraProgramFilesPath, "Cura.exe");
        if (!File.Exists(curaExecutableFile))
            curaExecutableFile = null;
        return curaExecutableFile;
    }

    private static void SetCuraSaveDialogPath(string targetPath, CuraManagerSettings settings)
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

    private static bool TryFindChild(AutomationElement parent, Func<TreeWalker, AutomationElement, bool> checkFunc, TimeSpan timeout, out AutomationElement element)
    {
        var treeWalker = TreeWalker.RawViewWalker;
        element = Waiter.WaitUntil(
            () =>
            {
                var e = treeWalker.GetFirstChild(parent);
                while (e != null && !checkFunc(treeWalker, e))
                    e = treeWalker.GetNextSibling(e);
                return e;
            },
            new WaiterOptions { ThrowException = false, Timeout = timeout });
        return element != null;
    }

    private static void UpdateCuraProjectConfigs(string fileName, CuraManagerSettings settings)
    {
        string curaResourcesPath4x = Path.Combine(settings.CuraProgramFilesPath, "resources");
        string curaResourcesPath5x = Path.Combine(settings.CuraProgramFilesPath, "share", "cura", "resources");

        using var file = ZipFile.Open(fileName, ZipArchiveMode.Update);
        foreach (var cf in file.Entries.Where(x => x.FullName.StartsWith("Cura/", StringComparison.OrdinalIgnoreCase) && x.Length > 0).ToArray())
        {
            if (cf.Name.EndsWith("_user.inst.cfg", StringComparison.OrdinalIgnoreCase) ||
                cf.Name.EndsWith(".extruder.cfg", StringComparison.OrdinalIgnoreCase) ||
                cf.Name.EndsWith(".global.cfg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cf.Name, "version.ini", StringComparison.OrdinalIgnoreCase))
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
                    using (var sr = new StreamReader(Path.Combine(settings.CuraAppDataPath, "cura.cfg")))
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
                var of = Directory.EnumerateFiles(settings.CuraAppDataPath, escapedCfName, SearchOption.AllDirectories).FirstOrDefault();
                if (of == null && Directory.Exists(curaResourcesPath4x))
                    of = Directory.EnumerateFiles(curaResourcesPath4x, escapedCfName, SearchOption.AllDirectories).FirstOrDefault();
                if (of == null && Directory.Exists(curaResourcesPath5x))
                    of = Directory.EnumerateFiles(curaResourcesPath5x, escapedCfName, SearchOption.AllDirectories).FirstOrDefault();
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
