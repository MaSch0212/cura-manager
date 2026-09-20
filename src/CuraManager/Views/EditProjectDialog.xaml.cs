using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    /// Index of the highlighted suggestion, or -1 when none is.
    /// </summary>
    private int _highlightedSuggestion = -1;

    /// <summary>
    /// Gets the tags assigned to the project, shown as chips. This is a working copy; the edited
    /// project is only updated once the dialog has been confirmed.
    /// </summary>
    public ObservableCollection<string> SelectedTags { get; }

    /// <summary>
    /// Gets every tag that exists, including any tag created inside this dialog. This is the pool
    /// the input box suggests from, and it is reported back so new tags become available app-wide.
    /// </summary>
    public ObservableCollection<string> KnownTags { get; }

    /// <summary>
    /// Gets the tags currently offered below the input box: every known tag that is not assigned
    /// yet and matches what has been typed so far.
    /// </summary>
    public ObservableCollection<string> TagSuggestions { get; }

    public EditProjectDialog(PrintElement project, IEnumerable<string> availableTags)
    {
        ServiceContext.GetService(out _translationManager);

        _project = project;
        ProjectName = project.Name;
        ProjectUrl = project.Website;
        IsArchived = project.IsArchived;
        SelectedTags = new ObservableCollection<string>(project.Tags);

        // Ordinal throughout, matching how tags are compared everywhere else (AddIfNotExists,
        // Contains). Folding case here would collapse two tags the rest of the app keeps apart.
        KnownTags = new ObservableCollection<string>(
            availableTags.Concat(project.Tags).Distinct(StringComparer.Ordinal)
        );
        TagSuggestions = new ObservableCollection<string>();

        InitializeComponent();

        Loaded += (s, e) => ProjectNameTextBox.SelectAll();
    }

    #region Tag input

    partial void OnNewTagNameChanged(string previous, string value)
    {
        // Only a real edit rebuilds the list. The two-way binding on the input box re-pushes the
        // unchanged text after events like an arrow key moving the caret, and refreshing on that
        // would drop the highlighted suggestion in the middle of keyboard navigation -- Enter
        // would then add the typed text instead of the tag the user had arrowed onto.
        if (!string.Equals(previous, value, StringComparison.Ordinal))
            RefreshTagSuggestions();
    }

    /// <summary>
    /// Recomputes the suggestion list and shows it whenever it has something to offer and the
    /// input box is where the keyboard focus is.
    /// </summary>
    private void RefreshTagSuggestions()
    {
        var filter = NewTagName?.Trim() ?? string.Empty;
        var matches = KnownTags
            .Where(x => !SelectedTags.Contains(x, StringComparer.Ordinal))
            .Where(x =>
                filter.Length == 0 || x.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            )
            .OrderBy(x => x, StringComparer.CurrentCulture)
            .ToArray();

        TagSuggestions.Clear();
        foreach (var match in matches)
            TagSuggestions.Add(match);

        SetHighlightedSuggestion(-1);
        TagSuggestionsPopup.IsOpen = matches.Length > 0 && TagInputTextBox.IsKeyboardFocusWithin;
    }

    /// <summary>
    /// Gets the highlighted suggestion, or <c>null</c> when nothing is highlighted.
    /// </summary>
    private string HighlightedSuggestion =>
        _highlightedSuggestion >= 0 && _highlightedSuggestion < TagSuggestions.Count
            ? TagSuggestions[_highlightedSuggestion]
            : null;

    private void SetHighlightedSuggestion(int index)
    {
        _highlightedSuggestion = index;
        TagSuggestionsListBox.SelectedIndex = index;
    }

    /// <summary>
    /// Assigns a tag, creating it if it does not exist yet. This is what lets the input box serve
    /// as one control for both picking an existing tag and adding a brand new one.
    /// </summary>
    private void AddTag(string tagName)
    {
        tagName = tagName?.Trim();
        if (string.IsNullOrEmpty(tagName))
            return;

        KnownTags.AddIfNotExists(tagName);
        SelectedTags.AddIfNotExists(tagName);

        NewTagName = string.Empty;

        // No explicit close: the refresh reopens the list with whatever is left as long as the
        // input still has the focus, so several tags can be added in a row without reaching for
        // the mouse. It closes on its own once nothing is left to suggest.
        RefreshTagSuggestions();
    }

    private void AddTagButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddTag(NewTagName);
        TagInputTextBox.Focus();
    }

    private void RemoveTagButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.DataContext is string tag)
        {
            SelectedTags.Remove(tag);
            RefreshTagSuggestions();
        }
    }

    private void TagInput_OnIsKeyboardFocusWithinChanged(
        object sender,
        DependencyPropertyChangedEventArgs e
    )
    {
        if (e.NewValue is true)
            RefreshTagSuggestions();
        else
            TagSuggestionsPopup.IsOpen = false;
    }

    /// <summary>
    /// Reopens the suggestions when the input is clicked. Focus alone is not enough: clicking an
    /// input that already has the focus raises no focus change, which would otherwise leave the
    /// user with no way to bring the list back after it was closed.
    /// </summary>
    private void TagInput_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RefreshTagSuggestions();
    }

    private void TagInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSuggestion(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSuggestion(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                // Handled here so Enter adds the tag instead of bubbling out to the dialog. A
                // highlighted suggestion wins over the raw text, so arrowing down to a tag and
                // pressing Enter picks that tag. Deliberately not guarded on e.IsRepeat: an
                // unhandled Enter reaches the add button instead and adds the raw text, quietly
                // discarding the highlighted suggestion.
                e.Handled = true;
                AddTag(HighlightedSuggestion ?? NewTagName);
                break;
            case Key.Escape when TagSuggestionsPopup.IsOpen:
                // Only swallowed while the suggestions are showing, so Escape still closes the
                // dialog when they are not.
                TagSuggestionsPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void TagSuggestions_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (
            e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(TagSuggestionsListBox, source)
                is ListBoxItem { DataContext: string tag }
        )
        {
            e.Handled = true;
            AddTag(tag);
            TagInputTextBox.Focus();
        }
    }

    /// <summary>
    /// Moves the highlight through the suggestion list without ever giving it the keyboard focus,
    /// so that typing keeps going to the input box.
    /// </summary>
    private void MoveSuggestion(int offset)
    {
        if (TagSuggestions.Count == 0)
            return;

        TagSuggestionsPopup.IsOpen = true;

        var index = _highlightedSuggestion + offset;
        if (index < 0)
            index = TagSuggestions.Count - 1;
        else if (index >= TagSuggestions.Count)
            index = 0;

        SetHighlightedSuggestion(index);
        TagSuggestionsListBox.ScrollIntoView(TagSuggestions[index]);
    }

    #endregion

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

    private void InputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            SaveButton_OnClick(sender, e);
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
