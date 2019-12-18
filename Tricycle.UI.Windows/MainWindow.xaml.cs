﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tricycle.Diagnostics;
using Tricycle.Diagnostics.Utilities;
using Tricycle.IO;
using Tricycle.IO.Windows;
using Tricycle.Media;
using Tricycle.Media.FFmpeg;
using Tricycle.Media.FFmpeg.Models.Config;
using Tricycle.Media.FFmpeg.Serialization.Argument;
using Tricycle.Models;
using Tricycle.Models.Config;
using Tricycle.Utilities;
using Xamarin.Forms;
using Xamarin.Forms.Platform.WPF;
using Xamarin.Forms.Platform.WPF.Controls;
using Container = StructureMap.Container;
using ControlTemplate = System.Windows.Controls.ControlTemplate;
using Menu = System.Windows.Controls.Menu;
using MenuItem = System.Windows.Controls.MenuItem;
using Thickness = System.Windows.Thickness;

namespace Tricycle.UI.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FormsApplicationPage
    {
        static readonly Brush MENU_BACKGROUND_BRUSH = new SolidColorBrush(Colors.WhiteSmoke);
        static readonly Thickness MENU_BORDER_THICKNESS = new Thickness(0);

        IAppManager _appManager;
        MenuItem _openFileItem;
        MenuItem _optionsItem;
        MenuItem _previewItem;

        public MainWindow()
        {
            _appManager = new AppManager();

            _appManager.Busy += OnBusyChange;
            _appManager.Ready += OnBusyChange;
            _appManager.SourceSelected += OnSourceSelected;
            _appManager.QuitConfirmed += Close;

            InitializeAppState();
            InitializeComponent();
            Forms.Init();
            LoadApplication(new UI.App(_appManager));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_appManager.IsQuitConfirmed)
            {
                e.Cancel = true;

                _appManager.RaiseQuitting();
            }

            base.OnClosing(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            var backButton = Template.FindName("PART_Previous_Modal", this) as Control;

            if (backButton != null)
            {
                backButton.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
        {
            base.OnTemplateChanged(oldTemplate, newTemplate);

            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(CreateMenu));
        }

        void CreateMenu()
        {
            var contentContainer = Template.FindName("PART_ContentControl", this) as FormsContentControl;

            if (contentContainer == null)
            {
                return;
            }

            var panel = new StackPanel();
            var menu = new Menu()
            {
                IsMainMenu = true,
                Background = MENU_BACKGROUND_BRUSH,
                BorderThickness = MENU_BORDER_THICKNESS,
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
            var content = contentContainer.Content as UIElement;

            contentContainer.Content = panel;

            panel.Children.Add(menu);
            panel.Children.Add(content);

            var fileItem = CreateMenuItem("_File");

            menu.Items.Add(fileItem);

            _openFileItem = CreateMenuItem("_Open…");

            _openFileItem.Click += OnOpenFileClick;
            fileItem.Items.Add(_openFileItem);

            var exitItem = CreateMenuItem("E_xit");

            exitItem.Click += (sender, args) => Close();
            fileItem.Items.Add(exitItem);

            var viewItem = CreateMenuItem("_View");
        
            menu.Items.Add(viewItem);

            _previewItem = CreateMenuItem("_Preview…");
            _previewItem.IsEnabled = false;

            _previewItem.Click += OnPreviewClick;
            viewItem.Items.Add(_previewItem);

            var toolsItem = CreateMenuItem("_Tools");

            menu.Items.Add(toolsItem);

            _optionsItem = CreateMenuItem("_Options…");

            _optionsItem.Click += OnOptionsClick;
            toolsItem.Items.Add(_optionsItem);

            var helpItem = CreateMenuItem("_Help");

            menu.Items.Add(helpItem);

            var aboutItem = CreateMenuItem("_About Tricycle");

            aboutItem.Click += OnAboutClick;
            helpItem.Items.Add(aboutItem);
        }

        MenuItem CreateMenuItem(string header)
        {
            return new MenuItem()
            {
                Background = MENU_BACKGROUND_BRUSH,
                BorderThickness = MENU_BORDER_THICKNESS,
                Header = header
            };
        }

        void InitializeAppState()
        {
            const string FFMPEG_CONFIG_NAME = "ffmpeg.json";
            const string TRICYCLE_CONFIG_NAME = "tricycle.json";

            string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string assetsPath = Path.Combine(appPath, "Assets");
            string defaultConfigPath = Path.Combine(assetsPath, "Config");
            string ffmpegPath = Path.Combine(assetsPath, "Tools", "FFmpeg");
            string ffmpegFileName = Path.Combine(ffmpegPath, "ffmpeg.exe");
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userConfigPath = Path.Combine(appDataPath, "Tricycle");
            var processCreator = new Func<IProcess>(() => new ProcessWrapper());
            var processRunner = new ProcessRunner(processCreator);
            var fileSystem = new FileSystem();
            var ffmpegConfigManager =
                new JsonConfigManager<FFmpegConfig>(fileSystem,
                                                    Path.Combine(defaultConfigPath, FFMPEG_CONFIG_NAME),
                                                    Path.Combine(userConfigPath, FFMPEG_CONFIG_NAME));
            var tricycleConfigManager =
                new JsonConfigManager<TricycleConfig>(fileSystem,
                                                      Path.Combine(defaultConfigPath, TRICYCLE_CONFIG_NAME),
                                                      Path.Combine(userConfigPath, TRICYCLE_CONFIG_NAME));

            ffmpegConfigManager.Load();
            tricycleConfigManager.Load();

            var ffmpegArgumentGenerator = new FFmpegArgumentGenerator(new ArgumentPropertyReflector());

            AppState.IocContainer = new Container(_ =>
            {
                _.For<IConfigManager<FFmpegConfig>>().Use(ffmpegConfigManager);
                _.For<IConfigManager<TricycleConfig>>().Use(tricycleConfigManager);
                _.For<IFileBrowser>().Use<FileBrowser>();
                _.For<IProcessUtility>().Use(ProcessUtility.Self);
                _.For<IMediaInspector>().Use(new MediaInspector(Path.Combine(ffmpegPath, "ffprobe.exe"),
                                                                processRunner,
                                                                ProcessUtility.Self));
                _.For<ICropDetector>().Use(new CropDetector(ffmpegFileName,
                                                            processRunner,
                                                            ffmpegConfigManager,
                                                            ffmpegArgumentGenerator));
                _.For<IFileSystem>().Use(fileSystem);
                _.For<ITranscodeCalculator>().Use<TranscodeCalculator>();
                _.For<IMediaTranscoder>().Use(new MediaTranscoder(ffmpegFileName,
                                                                  processCreator,
                                                                  ffmpegConfigManager,
                                                                  ffmpegArgumentGenerator));
                _.For<IDevice>().Use(DeviceWrapper.Self);
                _.For<IAppManager>().Use(_appManager);
                _.For<IPreviewImageGenerator>().Use(new PreviewImageGenerator(ffmpegFileName,
                                                                              processRunner,
                                                                              ffmpegArgumentGenerator,
                                                                              ffmpegConfigManager,
                                                                              fileSystem));
            });
            AppState.DefaultDestinationDirectory =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        }

        void OnBusyChange()
        {
            _openFileItem.IsEnabled = !_appManager.IsBusy;
            _optionsItem.IsEnabled = !_appManager.IsBusy;
            _previewItem.IsEnabled = !_appManager.IsBusy && _appManager.IsValidSourceSelected;
        }

        void OnSourceSelected(bool isValid)
        {
            _previewItem.IsEnabled = isValid && !_appManager.IsBusy;
        }

        void OnOpenFileClick(object sender, RoutedEventArgs e)
        {
            var browser = new FileBrowser();
            var result = browser.BrowseToOpen().GetAwaiter().GetResult();

            if (result.Confirmed)
            {
                _appManager.RaiseFileOpened(result.FileName);
            }
        }

        void OnOptionsClick(object sender, RoutedEventArgs e)
        {
            _appManager.RaiseModalOpened(Modal.Config);
        }

        void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow()
            {
                Owner = this
            };

            window.ShowDialog();
        }

        void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            _appManager.RaiseModalOpened(Modal.Preview);
        }
    }
}
