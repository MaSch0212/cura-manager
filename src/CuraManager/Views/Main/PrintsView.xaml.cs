using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services;
using CuraManager.ViewModels.Main;
using MaSch.Core;
using MaSch.Core.Extensions;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf.MaterialDesign;

namespace CuraManager.Views.Main
{
    public partial class PrintsView
    {
        private readonly object _lastDragPointLock = new object();
        private Point? _lastDragPoint;
        private bool _isInitialized = false;

        public static readonly DependencyProperty PanelWidthProperty =
            DependencyProperty.Register("PanelWidth", typeof(int), typeof(PrintsView), new PropertyMetadata(350));

        public int PanelWidth
        {
            get { return (int)GetValue(PanelWidthProperty); }
            set { SetValue(PanelWidthProperty, value); }
        }

        public PrintsView()
        {
            InitializeComponent();

            var settingsService = ServiceContext.GetService<ISettingsService>();

            Loaded += (s, e) =>
            {
                if (_isInitialized)
                    return;

                var settings = settingsService.LoadGuiSettings();
                PanelWidth = settings.PrintFilesPanelWidth;

                Window.GetWindow(this).Closing += (s, e) =>
                {
                    var settings = settingsService.LoadGuiSettings();
                    settings.PrintFilesPanelWidth = PanelWidth;
                    settingsService.SaveGuiSettings(settings);
                };
                _isInitialized = true;
            };
        }

        private async void ProjectDetailsPane_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is PrintsViewModel viewModel && viewModel.SelectedElement != null)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files != null && !(files.Length == 1 && viewModel.SelectedElement.AllFiles.Any(x => x.FilePath == files[0])))
                    await viewModel.AddFilesToProject(viewModel.SelectedElement, files);
            }
        }

        private async void DataGrid_OnDrop(object sender, DragEventArgs e)
        {
            if (DataContext is PrintsViewModel viewModel)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                    if (files != null && !(files.Length == 1 && viewModel.PrintElements.Any(x => x.DirectoryLocation == files[0])))
                        await viewModel.CreateProjectFromFileDrop(files);
                }
                else if (e.Data.GetDataPresent(DataFormats.StringFormat))
                {
                    var data = (string)e.Data.GetData(DataFormats.StringFormat);

                    await viewModel.CreateProjectFromStringDrop(data);
                }
            }
        }

        private async void DataGrid_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement control && control.DataContext is PrintElement element)
            {
                await DoDrag(e, () =>
                {
                    var dataObject = new DataObject(DataFormats.FileDrop, new[] { element.DirectoryLocation });
                    DragDrop.DoDragDrop(control, dataObject, DragDropEffects.Copy);
                });
            }
        }

        private async void ProjectDetailsPane_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement depObj && depObj.DataContext is PrintElementFile file)
            {
                await DoDrag(e, () =>
                {
                    var dataObject = new DataObject(DataFormats.FileDrop, new[] { file.FilePath });
                    DragDrop.DoDragDrop(depObj, dataObject, DragDropEffects.Copy);
                });
            }
        }

        private void PrintElementFileContextMenu_OnOpened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu && contextMenu.DataContext is PrintElementFile file && DataContext is PrintsViewModel viewModel)
            {
                var translationManager = ServiceContext.GetService<ITranslationManager>();
                if (string.Equals(file.FileExtension, ".gcode", StringComparison.OrdinalIgnoreCase))
                {
                    var drives = DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Removable && IsDriveReady(x)).ToArray();
                    contextMenu.Items.RemoveWhere(x => x is FrameworkElement item && item.Tag != null);
                    if (drives.Length > 0)
                        contextMenu.Items.Add(new Separator { Tag = "Dump it!" });
                    foreach (var drive in drives)
                    {
                        var volumeName = string.IsNullOrEmpty(drive.VolumeLabel)
                            ? translationManager.GetTranslation(nameof(StringTable.RemovableDrive))
                            : drive.VolumeLabel;

                        var item = new MenuItem
                        {
                            Icon = new MaterialDesignIcon(MaterialDesignIconCode.ContentCopy),
                            Header = string.Format(translationManager.GetTranslation(nameof(StringTable.CopyTo)), $"{volumeName} ({drive.Name.TrimEnd('/', '\\')})"),
                            Command = viewModel.CopyProjectFileToCommand,
                            CommandParameter = new Tuple<PrintElementFile, string>(file, drive.RootDirectory.FullName),
                            Tag = drive,
                        };
                        contextMenu.Items.Add(item);
                    }
                }
            }
        }

        private async Task DoDrag(MouseEventArgs mouseEventArgs, Action dragAction)
        {
            lock (_lastDragPointLock)
            {
                if (_lastDragPoint.HasValue)
                    return;
                _lastDragPoint = mouseEventArgs.GetPosition(this);
            }

            try
            {
                while (Mouse.LeftButton == MouseButtonState.Pressed && (Mouse.GetPosition(this) - _lastDragPoint.Value).Length < 3)
                    await Task.Delay(10);

                if (Mouse.LeftButton != MouseButtonState.Pressed)
                    return;

                dragAction();
            }
            finally
            {
                _lastDragPoint = null;
            }
        }

        private static bool IsDriveReady(DriveInfo drive)
        {
            if (!drive.IsReady)
                return false;
            try
            {
                drive.RootDirectory.EnumerateFiles();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private void ContextMenuToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button && button.Tag is ContextMenu contextMenu)
            {
                button.ContextMenu = contextMenu;
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = PlacementMode.Bottom;
                contextMenu.IsOpen = true;
                contextMenu.Closed += ClosedHandler;
            }

            static void ClosedHandler(object sender, RoutedEventArgs e)
            {
                if (sender is ContextMenu menu)
                {
                    menu.Closed -= ClosedHandler;
                    if (menu.PlacementTarget is ToggleButton button)
                        button.IsChecked = false;
                }
            }
        }

        private void PrintsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var dataGrid = (DataGrid)sender;
                var collectionView = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
                if (!collectionView.SortDescriptions.Any(x => x.PropertyName == nameof(PrintElement.IsArchived)))
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(PrintElement.IsArchived), ListSortDirection.Ascending));
                if (!collectionView.SortDescriptions.Any(x => x.PropertyName == nameof(PrintElement.Name)))
                    collectionView.SortDescriptions.Add(new SortDescription(nameof(PrintElement.Name), ListSortDirection.Ascending));
            }));
        }
    }
}
