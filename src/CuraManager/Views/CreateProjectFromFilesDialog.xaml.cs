using CuraManager.Models;
using CuraManager.Resources;
using MaSch.Presentation.Translation;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface ICreateProjectFromFilesDialog_Props
{
    string ProjectName { get; set; }
}

public partial class CreateProjectFromFilesDialog : ICreateProjectFromFilesDialog_Props
{
    private readonly ITranslationManager _translationManager;
    private readonly string _targetPath;

    public IList<PrintElementFile> Files { get; }

    public CreateProjectFromFilesDialog(string targetPath, IEnumerable<string> files)
    {
        ServiceContext.GetService(out _translationManager);

        _targetPath = targetPath;
        Files = new ObservableCollection<PrintElementFile>(files?.Select(x => new PrintElementFile(x)) ?? Array.Empty<PrintElementFile>());

        InitializeComponent();
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ProjectName))
        {
            MessageBox.Show(this, _translationManager.GetTranslation(nameof(StringTable.Msg_SpecifyProjectName)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Files.Any())
        {
            MessageBox.Show(this, _translationManager.GetTranslation(nameof(StringTable.Msg_AddAtLeastOneFileToProject)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Directory.EnumerateDirectories(_targetPath, "*", SearchOption.TopDirectoryOnly).Any(x => string.Equals(Path.GetFileName(x), ProjectName)))
        {
            MessageBox.Show(this, string.Format(_translationManager.GetTranslation(nameof(StringTable.Msg_ProjectAlreadyExists)), ProjectName), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void DeleteFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.DataContext is PrintElementFile file)
        {
            Files.Remove(file);
        }
    }

    private void AddFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Multiselect = true,
        };
        if (ofd.ShowDialog(Application.Current.MainWindow) == true)
        {
            Files.Add(ofd.FileNames.Select(x => new PrintElementFile(x)));
            if (Files.Count == 1 && string.IsNullOrEmpty(ProjectName))
                ProjectName = Files[0].FileName;
        }
    }

    private void FilesPane_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                Files.Add(files.Select(x => new PrintElementFile(x)));
                if (Files.Count == 1 && string.IsNullOrEmpty(ProjectName))
                    ProjectName = Files[0].FileName;
            }
        }
    }

    public async Task<PrintElement> CreateProject()
    {
        var result = new PrintElement(Path.Combine(_targetPath, ProjectName));
        await Task.Run(() => Directory.CreateDirectory(Path.Combine(result.DirectoryLocation)));

        foreach (var file in Files)
        {
            var targetPath = Path.Combine(result.DirectoryLocation, Path.GetFileName(file.FilePath) ?? string.Empty);
            for (int i = 2; File.Exists(targetPath); i++)
            {
                targetPath = Path.Combine(result.DirectoryLocation, Path.GetFileNameWithoutExtension(file.FilePath) + $" ({i})" + Path.GetExtension(file.FilePath));
            }

            await Task.Run(() => File.Copy(file.FilePath, targetPath));
        }

        return result;
    }

    private void ProjectNameTextBoxOnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            CreateButton_OnClick(sender, e);
    }
}
