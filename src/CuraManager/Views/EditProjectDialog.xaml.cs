using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CuraManager.Common;
using CuraManager.Models;
using CuraManager.Resources;
using MaSch.Presentation.Translation;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface IEditProjectDialog_Props
{
    string ProjectName { get; set; }
    string ProjectUrl { get; set; }
    string NewTagName { get; set; }
    bool IsArchived { get; set; }
}

/// <summary>
/// Edits everything about a print project that is not one of its files: its name, the URL of the
/// page it came from, its tags and whether it is archived. The dialog works on copies of those
/// values; nothing is written to the project until the caller applies the result.
/// </summary>
public partial class EditProjectDialog : IEditProjectDialog_Props
{
    private readonly ITranslationManager _translationManager;
    private readonly PrintElement _project;

    /// <summary>
    /// Gets the tags that are currently checked. This is the working copy the checkboxes write
    /// through to; the edited project is only updated once the dialog has been confirmed.
    /// </summary>
    public ObservableCollection<string> SelectedTags { get; }

    /// <summary>
    /// Gets every tag the user can pick from, including any tag created inside this dialog.
    /// </summary>
    public ObservableCollection<ObservableTag> Tags { get; }

    public EditProjectDialog(PrintElement project, IEnumerable<string> availableTags)
    {
        ServiceContext.GetService(out _translationManager);

        _project = project;
        ProjectName = project.Name;
        ProjectUrl = project.Website;
        IsArchived = project.IsArchived;
        SelectedTags = new ObservableCollection<string>(project.Tags);
        // Ordinal throughout, matching how tags are compared everywhere else (AddIfNotExists,
        // Contains). Folding case here would collapse two tags the rest of the app keeps apart,
        // and the survivor would then read as unchecked and be dropped on save.
        Tags = new ObservableCollection<ObservableTag>(
            availableTags
                .Concat(project.Tags)
                .Distinct(StringComparer.Ordinal)
                .Select(x => new ObservableTag(x, SelectedTags))
        );

        InitializeComponent();

        Loaded += (s, e) => ProjectNameTextBox.SelectAll();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ShowMessage(nameof(StringTable.Msg_SpecifyProjectName));
            return;
        }

        if (!ProjectWebsite.IsValid(ProjectUrl))
        {
            ShowMessage(nameof(StringTable.Msg_InvalidProjectUrl));
            return;
        }

        // Only a name that actually changed can collide -- comparing case insensitively so that
        // changing just the casing of a name is still allowed on a case insensitive file system.
        if (
            !string.Equals(_project.Name, ProjectName, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(
                Path.Combine(Path.GetDirectoryName(_project.DirectoryLocation), ProjectName)
            )
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

    private void AddTagButton_OnClick(object sender, RoutedEventArgs e)
    {
        var tagName = NewTagName?.Trim();
        if (string.IsNullOrEmpty(tagName))
            return;

        var existing = Tags.FirstOrDefault(x =>
            string.Equals(x.Name, tagName, StringComparison.Ordinal)
        );
        if (existing == null)
        {
            existing = new ObservableTag(tagName, SelectedTags);
            Tags.Add(existing);
        }

        existing.IsSet = true;
        NewTagName = string.Empty;
    }

    private void InputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            SaveButton_OnClick(sender, e);
    }

    private void NewTagTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        // Enter adds the tag instead of submitting the dialog, so that typing a tag name and
        // pressing Enter does not close the dialog with the tag silently dropped.
        if (!e.IsRepeat && e.Key == Key.Enter)
        {
            e.Handled = true;
            AddTagButton_OnClick(sender, e);
        }
    }

    private void ShowMessage(string translationKey)
    {
        MessageBox.Show(
            this,
            _translationManager.GetTranslation(translationKey),
            "CuraManager",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }
}
