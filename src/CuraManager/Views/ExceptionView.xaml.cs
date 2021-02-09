using MaSch.Core.Attributes;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Media;
using System.Windows;

namespace CuraManager.Views
{
    [ObservablePropertyDefinition]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "Property definition")]
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
}
