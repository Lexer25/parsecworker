using System.IO;
using System.Windows;
using Microsoft.Win32;
using ParsecQueueViewer.ViewModels;

namespace ParsecQueueViewer
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private Task _initializationTask;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(GetConfigArgument());
            DataContext = _viewModel;
        }

        public Task InitializeAsync()
        {
            return _initializationTask ??= InitializeCoreAsync();
        }

        private async Task InitializeCoreAsync()
        {
            await _viewModel.InitializeAsync();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel?.Dispose();
        }

        private async void ChooseConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Выберите appsettings.json интегратора",
                Filter = "Настройки интегратора (appsettings.json)|appsettings.json|JSON-файлы (*.json)|*.json",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = GetInitialDirectory()
            };

            if (dialog.ShowDialog(this) == true)
            {
                await _viewModel.LoadConfigurationAsync(dialog.FileName);
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private string GetInitialDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.ConfigPath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(_viewModel.ConfigPath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
                catch
                {
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        private static string GetConfigArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                const string prefix = "--config=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }

            return null;
        }
    }
}
