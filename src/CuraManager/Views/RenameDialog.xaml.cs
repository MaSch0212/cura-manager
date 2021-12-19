using CuraManager.Resources;
using MaSch.Presentation.Translation;
using System.Windows;
using System.Windows.Input;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface IRenameDialog_Props
{
    string Description { get; set; }
    string NewName { get; set; }
    string SubmitButtonContent { get; set; }
}

public partial class RenameDialog : IRenameDialog_Props
{
    private readonly ITranslationManager _translationManager;
    private readonly Func<string, string> _validationFunction;

    public RenameDialog(string title, string description, string currentName, Func<string, string> validationFunction)
    {
        ServiceContext.GetService(out _translationManager);

        Description = description;
        NewName = currentName;
        _validationFunction = validationFunction;
        SubmitButtonContent = _translationManager.GetTranslation(nameof(StringTable.Rename));

        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(title))
            Title = title;

        Loaded += (s, e) => NameTextBox.SelectAll();
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NewName))
        {
            MessageBox.Show(this, _translationManager.GetTranslation(nameof(StringTable.Msg_EnterNewName)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var error = _validationFunction(NewName);
        if (!string.IsNullOrEmpty(error))
        {
            MessageBox.Show(this, error, "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void NewNameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            RenameButton_OnClick(sender, e);
    }
}
