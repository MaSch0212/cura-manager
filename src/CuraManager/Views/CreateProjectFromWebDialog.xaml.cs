using CuraManager.Common;
using CuraManager.Models;
using CuraManager.Resources;
using CuraManager.Services;
using MaSch.Presentation.Translation;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MessageBox = MaSch.Presentation.Wpf.MessageBox;

namespace CuraManager.Views;

[ObservablePropertyDefinition]
internal interface ICreateProjectFromWebDialog_Props
{
    bool IsLoadingName { get; set; }
    string ProjectName { get; set; }
}

public partial class CreateProjectFromWebDialog : ICreateProjectFromWebDialog_Props
{
    #region Fields
    private readonly ITranslationManager _translationManager;
    private readonly IDownloadService _downloadService;
    private readonly string _targetPath;
    private string _webAddress;
    #endregion

    #region Properties
    public string WebAddress
    {
        get => _webAddress;
        set
        {
            _webAddress = value;
            if (string.IsNullOrEmpty(ProjectName) && !string.IsNullOrEmpty(WebAddress))
            {
                try
                {
                    IsLoadingName = true;
                    _downloadService.GetProjectName(new Uri(WebAddress)).ContinueWith(result =>
                    {
                        if (string.IsNullOrEmpty(ProjectName))
                            ProjectName = result.Result;
                    }).ContinueWith(_ => IsLoadingName = false);
                }
                catch (Exception)
                {
                    IsLoadingName = false;
                }
            }
        }
    }
    #endregion

    #region Ctor
    public CreateProjectFromWebDialog(string targetPath, string webAddress)
    {
        ServiceContext.GetService(out _translationManager);
        ServiceContext.GetService(out _downloadService);

        _targetPath = targetPath;
        if (!string.IsNullOrEmpty(webAddress))
            WebAddress = webAddress;
        else if (Uri.TryCreate(Clipboard.GetText(), UriKind.Absolute, out var link) && _downloadService.IsLinkSupported(link))
            WebAddress = link.ToString();

        InitializeComponent();
    }
    #endregion

    #region Methods
    public async Task<PrintElement> CreateProject()
    {
        return await CreateProjectImpl(_downloadService, _targetPath, ProjectName, new Uri(WebAddress));
    }

    private void ProjectNameTextBoxOnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.IsRepeat && e.Key == Key.Enter)
            CreateButton_OnClick(sender, e);
    }
    #endregion

    #region Event Handlers
    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateLinkAndName(this, _translationManager, _downloadService, _targetPath, ProjectName, WebAddress))
            return;

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    #endregion

    #region Functions
    public static async Task<PrintElement> CreateProject(string targetPath, string webAddress, string name = null)
    {
        var translationManager = ServiceContext.GetService<ITranslationManager>();
        var downloadService = ServiceContext.GetService<IDownloadService>();

        if (Uri.TryCreate(webAddress, UriKind.Absolute, out var link) && string.IsNullOrEmpty(name) && downloadService.IsLinkSupported(link))
            name = await downloadService.GetProjectName(link);
        if (string.IsNullOrEmpty(name))
            name = Guid.NewGuid().ToString();

        if (!ValidateLinkAndName(Application.Current.MainWindow, translationManager, downloadService, targetPath, name, webAddress))
            return null;

        return await CreateProjectImpl(downloadService, targetPath, name, link);
    }

    private static async Task<PrintElement> CreateProjectImpl(IDownloadService downloadService, string targetPath, string name, Uri link)
    {
        var result = new PrintElement(Path.Combine(targetPath, name));
        await Task.Run(() => Directory.CreateDirectory(result.DirectoryLocation));

        result.Metadata.Website = link.ToString();
        result.SaveMetadata();

        bool successfull = false;
        try
        {
            await downloadService.DownloadFiles(link, result);
            successfull = true;
        }
        catch (WebProviderException ex)
        {
            MessageBox.Show(string.Format(ServiceContext.GetService<ITranslationManager>().GetTranslation(nameof(StringTable.Msg_ProjectCreationFailed)), ex.Message), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            if (!successfull)
                Directory.Delete(result.DirectoryLocation, true);
        }

        return result;
    }

    private static bool ValidateLinkAndName(Window parentWindow, ITranslationManager translationManager, IDownloadService downloadService, string targetPath, string name, string link)
    {
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(parentWindow, translationManager.GetTranslation(nameof(StringTable.Msg_SpecifyProjectName)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrEmpty(link))
        {
            MessageBox.Show(parentWindow, translationManager.GetTranslation(nameof(StringTable.Msg_TypeInUrl)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!downloadService.IsLinkSupported(new Uri(link)))
        {
            MessageBox.Show(parentWindow, translationManager.GetTranslation(nameof(StringTable.Msg_UnsupportedProjectPageUrl)), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (Directory.EnumerateDirectories(targetPath, "*", SearchOption.TopDirectoryOnly).Any(x => string.Equals(Path.GetFileName(x), name)))
        {
            MessageBox.Show(parentWindow, string.Format(translationManager.GetTranslation(nameof(StringTable.Msg_ProjectAlreadyExists)), name), "CuraManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return true;
    }
    #endregion
}
