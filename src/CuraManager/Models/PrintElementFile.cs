using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CuraManager.Services;
using MaSch.Core;
using MaSch.Core.Attributes;
using MaSch.Core.Observable;

namespace CuraManager.Models
{
    [ObservablePropertyDefinition]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "Property definition")]
    internal interface IPrintElementFile_Props
    {
        ImageSource Icon { get; set; }
        string FileName { get; set; }
        string FileExtension { get; set; }
        string FilePath { get; set; }
    }

    public partial class PrintElementFile : ObservableObject, IPrintElementFile_Props
    {
        public PrintElementFile(string filePath)
        {
            RefreshFilePath(filePath);
        }

        public void RefreshFilePath(string filePath)
        {
            FilePath = filePath;
            FileName = Path.GetFileNameWithoutExtension(filePath);
            FileExtension = Path.GetExtension(filePath);

            Application.Current.Dispatcher.Invoke(
                () =>
                {
                    Icon = ServiceContext.GetService<IFileIconCache>().GetFileIcon(filePath);
                },
                DispatcherPriority.ApplicationIdle);
        }
    }
}
