using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using CuraManager.Models;
using CuraManager.Resources;
using HtmlAgilityPack;
using MaSch.Presentation.Translation;
using Microsoft.Win32;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface ICreateProjectFromArchiveDialog_Props
{
    string ProjectName { get; set; }
    string ArchivePath { get; set; }
}

public partial class CreateProjectFromArchiveDialog : ICreateProjectFromArchiveDialog_Props
{
    private readonly ITranslationManager _translationManager;
    private readonly string _targetPath;

    public CreateProjectFromArchiveDialog(string targetPath, string archivePath)
    {
        ServiceContext.GetService(out _translationManager);

        _targetPath = targetPath;
        if (archivePath != null)
        {
            ArchivePath = archivePath;
            ProjectName = Path.GetFileNameWithoutExtension(archivePath);
        }

        InitializeComponent();
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = $"{_translationManager.GetTranslation(nameof(StringTable.ZipArchive))}|*.zip",
            Multiselect = false,
        };
        if (ofd.ShowDialog(Application.Current.MainWindow) == true)
        {
            ArchivePath = ofd.FileName;
        }
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ProjectName))
        {
            MessageBox.Show(
                this,
                _translationManager.GetTranslation(nameof(StringTable.Msg_SpecifyProjectName)),
                "CuraManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (string.IsNullOrEmpty(ArchivePath))
        {
            MessageBox.Show(
                this,
                _translationManager.GetTranslation(nameof(StringTable.Msg_SelectArchiveToImport)),
                "CuraManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (!File.Exists(ArchivePath))
        {
            MessageBox.Show(
                this,
                _translationManager.GetTranslation(nameof(StringTable.Msg_ArchiveDoesNotExist)),
                "CuraManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (
            Directory
                .EnumerateDirectories(_targetPath, "*", SearchOption.TopDirectoryOnly)
                .Any(x => string.Equals(Path.GetFileName(x), ProjectName))
        )
        {
            MessageBox.Show(
                this,
                string.Format(
                    _translationManager.GetTranslation(
                        nameof(StringTable.Msg_ProjectAlreadyExists)
                    ),
                    ProjectName
                ),
                "CuraManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public async Task<PrintElement> CreateProject()
    {
        var result = new PrintElement(Path.Combine(_targetPath, ProjectName));
        await Task.Run(() => Directory.CreateDirectory(result.DirectoryLocation));

        using (var zipFile = ZipFile.OpenRead(ArchivePath))
        {
            if (
                zipFile.Entries.TryFirst(
                    x => x.FullName == "attribution_card.html",
                    out var htmlEntry
                )
            )
            {
                var tempFile = Path.GetTempFileName();
                htmlEntry.ExtractToFile(tempFile, true);
                var doc = new HtmlDocument();
                doc.Load(tempFile);
                result.Metadata.Website = doc.DocumentNode.SelectSingleNode("//h3")?.InnerText;
                result.SaveMetadata();
                File.Delete(tempFile);
            }

            await GetFilesToExtract(zipFile, result.DirectoryLocation)
                .ForEachAsync(x => Task.Run(() => x.Entry.ExtractToFile(x.TargetPath)));
        }

        return result;
    }

    public static IEnumerable<(ZipArchiveEntry Entry, string TargetPath)> GetFilesToExtract(
        ZipArchive zipFile,
        string targetDir
    )
    {
        string startsWith = string.Empty;
        if (zipFile.Entries.Any(x => x.FullName == "files/"))
            startsWith = "files/";

        foreach (
            var entry in zipFile.Entries.Where(x =>
                x.FullName.StartsWith(startsWith) && x.Length > 0
            )
        )
        {
            var newFileName = string.Join(
                " - ",
                entry.FullName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            );
            var targetPath = Path.Combine(targetDir, newFileName);
            yield return (entry, targetPath);
        }
    }

    private void ArchivePane_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            ArchivePath = files?.FirstOrDefault();
        }
    }

    private void ProjectNameTextBoxOnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            CreateButton_OnClick(sender, e);
    }
}
