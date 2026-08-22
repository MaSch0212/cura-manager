using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services.Slicers;
using MaSch.Presentation.Translation;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface ICreateSlicerProjectDialog_Props
{
    string ProjectName { get; set; }
    bool ShowProjectName { get; set; }
}

public partial class CreateSlicerProjectDialog : ICreateSlicerProjectDialog_Props
{
    private readonly ITranslationManager _translationManager;
    private bool _disableAll = true;

    public IList<PrintElementFileSelection> Models { get; }

    public object SlicerIcon { get; }

    public CreateSlicerProjectDialog(
        PrintElement element,
        ISlicerProvider provider,
        bool showProjectName
    )
    {
        ServiceContext.GetService(out _translationManager);

        Models = new ObservableCollection<PrintElementFileSelection>(
            element?.ModelFiles.Select(x => new PrintElementFileSelection(x))
                ?? new List<PrintElementFileSelection>()
        );
        ProjectName =
            element?.Name ?? _translationManager.GetTranslation(nameof(StringTable.Untitled));
        ShowProjectName = showProjectName;
        SlicerIcon = Application.Current.TryFindResource(provider.IconResourceKey);
        Title = string.Format(
            _translationManager.GetTranslation(nameof(StringTable.Title_CreateSlicerProject)),
            provider.DisplayName
        );

        InitializeComponent();
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Models.Any(x => x.IsEnabled && x.Amount > 0))
        {
            MessageBox.Show(
                this,
                _translationManager.GetTranslation(nameof(StringTable.Msg_SelectAtLeastOneModel)),
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

    private void DisableAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        Models.ForEach(x => x.IsEnabled = !_disableAll);
        _disableAll ^= true;
        DisableAllButton.Content = _disableAll
            ? _translationManager.GetTranslation(nameof(StringTable.DisableAll))
            : _translationManager.GetTranslation(nameof(StringTable.EnableAll));
    }

    private void ProjectNameTextBoxOnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            CreateButton_OnClick(sender, e);
    }
}

[ObservablePropertyDefinition]
internal interface IPrintElementFileSelection_Props
{
    PrintElementFile Element { get; set; }
    bool IsEnabled { get; set; }
    int Amount { get; set; }
}

[SuppressMessage(
    "StyleCop.CSharp.MaintainabilityRules",
    "SA1402:File may only contain a single type",
    Justification = "Is part of the dialog."
)]
public partial class PrintElementFileSelection : ObservableObject, IPrintElementFileSelection_Props
{
    public PrintElementFileSelection(PrintElementFile element)
    {
        Element = element;
        IsEnabled = true;
        Amount = 1;
    }
}
