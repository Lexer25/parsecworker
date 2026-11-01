using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ParsecQueueViewer.ViewModels;

namespace ParsecQueueViewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // База Артонит — Firebird с Charset=win1251. В .NET кодовые страницы
            // (кроме UTF-8/Unicode) доступны только после регистрации провайдера.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            base.OnStartup(e);

            try
            {
                MainWindow window = new MainWindow();
                MainWindow = window;
                window.Show();

                string screenshotPath = GetScreenshotArgument(e.Args);
                if (!string.IsNullOrWhiteSpace(screenshotPath))
                {
                    _ = CaptureScreenshotAndExitAsync(window, screenshotPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть окно «Очередь интегратора».\n\n" + ex.Message,
                    "Очередь интегратора",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "Очередь интегратора: непредвиденная ошибка.\n\n" + e.Exception.Message,
                "Очередь интегратора",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private static async Task CaptureScreenshotAndExitAsync(MainWindow window, string path)
        {
            try
            {
                await window.InitializeAsync();

                if (window.DataContext is MainViewModel viewModel && viewModel.SelectedRow == null)
                {
                    viewModel.SelectedRow =
                        viewModel.Rows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.ErrorText))
                        ?? viewModel.Rows.FirstOrDefault(row => row.IsActive)
                        ?? viewModel.Rows.FirstOrDefault();
                }

                await Task.Delay(1400);

                await window.Dispatcher.InvokeAsync(() =>
                {
                    window.UpdateLayout();

                    DpiScale dpi = VisualTreeHelper.GetDpi(window);
                    int pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
                    int pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));

                    RenderTargetBitmap bitmap = new(
                        pixelWidth,
                        pixelHeight,
                        96 * dpi.DpiScaleX,
                        96 * dpi.DpiScaleY,
                        PixelFormats.Pbgra32);

                    bitmap.Render(window);

                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string fullPath = Path.GetFullPath(path);
                    string folder = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    using FileStream stream = new(fullPath, FileMode.Create, FileAccess.Write);
                    encoder.Save(stream);
                });
            }
            catch
            {
                // Режим скриншота служебный; при ошибке просто завершаемся.
            }
            finally
            {
                Current?.Shutdown(0);
            }
        }

        private static string GetScreenshotArgument(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--screenshot", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                const string prefix = "--screenshot=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }

            return null;
        }

    }
}
