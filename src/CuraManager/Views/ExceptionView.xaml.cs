using System.Media;
using System.Windows;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface IExceptionView_Props
{
    Exception ExceptionToDisplay { get; set; }
    string ExtraMessage { get; set; }
}

public partial class ExceptionView : IExceptionView_Props
{
    public ExceptionView()
    {
        InitializeComponent();
    }

    private void ToClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ExceptionToDisplay.ToString());
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExceptionView_OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemSounds.Hand.Play();
    }
}
