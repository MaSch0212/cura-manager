using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using CuraManager.Resources;
using CuraManager.Services;
using CuraManager.Services.WebProviders;
using CuraManager.Views;
using MaSch.Core;
using MaSch.Core.Extensions;
using MaSch.Presentation;
using MaSch.Presentation.Translation;
using MaSch.Presentation.Wpf;
using MessageBox = System.Windows.MessageBox;

namespace CuraManager
{
    public partial class App
    {
        private int _languageResDictIndex = -1;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        }

        private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var wdw = new ExceptionView { ExceptionToDisplay = e.ExceptionObject as Exception };
                wdw.ShowDialog();
            }
            catch
            {
                MessageBox.Show(e.ExceptionObject?.ToString());
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var wdw = new ExceptionView
                {
                    ExceptionToDisplay = e.Exception,
                };
                wdw.ShowDialog();
            }
            catch
            {
                MessageBox.Show(e.Exception.ToString());
            }

            e.Handled = true;
        }

        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            InitializeServices();

            var settingsService = ServiceContext.GetService<ISettingsService>();
            var themeManager = ServiceContext.GetService<IThemeManager>();
            themeManager.LoadTheme(Theme.FromDefaultTheme(settingsService.LoadSettings().Theme));

            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        private void InitializeServices()
        {
            InitializeTranslationManager();

            var settingsService = new SettingsService();
            ServiceContext.AddService<IThemeManager>(ThemeManager.DefaultThemeManager);
            ServiceContext.AddService<ICachingService>(new CachingService());
            ServiceContext.AddService<ISettingsService>(settingsService);
            ServiceContext.AddService<ICuraService>(new CuraService());
            ServiceContext.AddService<IPrintsService>(new PrintsService());
            ServiceContext.AddService<IFileIconCache>(new FileIconCache());

            ServiceContext.AddService<IDownloadService>(new DownloadService(new IWebProvider[]
            {
                new ThingiverseProvider(),

                // Disabled MyMiniFactory because it requires a login now for direct download links.
                // new MyMiniFactoryProvider(),
                new YouMagineProvider(),
            }));

            var settings = settingsService.LoadSettings();
            if (settings.Language.HasValue)
                ServiceContext.GetService<ITranslationManager>().CurrentLanguage = CultureInfo.GetCultureInfo(settings.Language.Value);
        }

        private void InitializeTranslationManager()
        {
            void ChangeLanguageOfWindows(CultureInfo language)
            {
                var windows = Windows.OfType<Window>();
                var lang = XmlLanguage.GetLanguage(language.IetfLanguageTag);
                foreach (var window in windows)
                    window.Language = lang;

                string langName = language.GetNeutralCulture().LCID switch
                {
                    7 => "German",
                    _ => "English",
                };

                var resourceDictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/MaSch.Presentation.Wpf.Themes;component/Languages/{langName}.xaml") };
                if (_languageResDictIndex < 0)
                {
                    Resources.MergedDictionaries.Add(resourceDictionary);
                    _languageResDictIndex = Resources.MergedDictionaries.Count - 1;
                }
                else
                {
                    Resources.MergedDictionaries[_languageResDictIndex] = resourceDictionary;
                }
            }

            var transMan = new TranslationManager(new ResxTranslationProvider(typeof(StringTable)));
            transMan.LanguageChanged += (s, e) => ChangeLanguageOfWindows(e.NewLanguage);
            ServiceContext.Instance.AddService<ITranslationManager>(transMan);
            ChangeLanguageOfWindows(transMan.CurrentLanguage);
        }

        public static string GetCurrentVersion(bool formatted)
        {
            var v = Version.Parse(AssemblyVersionType.AssemblyFileVersion.GetVersion(typeof(App).Assembly));
            if (!formatted)
                return v.ToString();
            if (v.Revision > 0)
                return v.ToString();
            return v.ToString(3);
        }
    }
}
