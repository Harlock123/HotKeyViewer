using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HotKeyViewer.Services;
using HotKeyViewer.ViewModels;
using HotKeyViewer.Views;

namespace HotKeyViewer;

public partial class App : Application
{
    /// <summary>
    /// Read once before the UI starts, since both the window size and every
    /// font size depend on it.
    /// </summary>
    public static DisplayMetrics Metrics { get; set; } = DisplayMetrics.Default;

    /// <summary>Held for the app's lifetime; disposing it stops theme updates.</summary>
    private FileSystemWatcher? _themeWatcher;

    /// <summary>
    /// Coalesces the burst of filesystem events a single theme switch produces,
    /// and gives the writer a moment to finish before the file is read back.
    /// </summary>
    private DispatcherTimer? _themeDebounce;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Both must land before the window is built so the first frame is
        // already themed and correctly sized.
        ScaledResources.Apply(this, Metrics.TextScale);
        ThemeResources.Apply(this, OmarchyTheme.Read());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var (width, height) = Metrics.WindowSize;

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                Width = width,
                Height = height,
            };

            StartWatchingTheme();

            // Load after the window exists so the shell appears immediately and
            // fills in, rather than the app looking hung while hyprctl and lua run.
            _ = viewModel.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingTheme()
    {
        _themeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _themeDebounce.Tick += (_, _) =>
        {
            _themeDebounce!.Stop();
            ThemeResources.Apply(this, OmarchyTheme.Read());
        };

        // Watcher callbacks arrive on a background thread; restarting the timer
        // on the UI thread both marshals the work and collapses repeats.
        _themeWatcher = OmarchyTheme.Watch(() => Dispatcher.UIThread.Post(() =>
        {
            _themeDebounce!.Stop();
            _themeDebounce.Start();
        }));
    }
}
