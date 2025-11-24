using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocLM.Services;
using LocLM.ViewModels;
using LocLM.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Layout;
using System.IO;

namespace LocLM;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        Log("App initialization start");
        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize database
        var chatHistoryService = Services.GetRequiredService<IChatHistoryService>();
        await chatHistoryService.InitializeAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log("Desktop lifetime detected");
            var pythonService = Services.GetRequiredService<IPythonBackendService>();
            var agentService = Services.GetRequiredService<IAgentService>();
            var vm = Services.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow.Show();
            Log("Main window created");

            // Start backend in background so UI shows immediately
            _ = Task.Run(async () =>
            {
                Log("Starting backend...");
                var started = await pythonService.StartAsync();
                Log($"Backend start result: {started}");
                if (started)
                {
                    var healthy = await agentService.CheckHealthAsync();
                    Log($"Backend health: {healthy}");
                    if (!healthy)
                    {
                        vm.BackendErrorMessage = "Backend started but health check failed. Ensure requirements are installed and port is free.";
                        vm.IsBackendErrorVisible = true;
                    }
                    else
                    {
                        vm.IsBackendErrorVisible = false;
                        vm.BackendErrorMessage = string.Empty;
                    }
                }
                else
                {
                    vm.BackendErrorMessage = "Failed to start Python backend. Ensure Python and dependencies are installed.";
                    vm.IsBackendErrorVisible = true;
                }
            });

            desktop.Exit += (s, e) => pythonService.Stop();
        }

        base.OnFrameworkInitializationCompleted();
        Log("Framework initialization completed");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<ICommandRunner>(sp =>
        {
            var platform = sp.GetRequiredService<IPlatformService>();
            return platform.IsWindows
                ? new WindowsCommandRunner()
                : new UnixCommandRunner();
        });
        services.AddSingleton<IPythonBackendService, PythonBackendService>();
        services.AddSingleton<IOllamaService, OllamaService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IKeyboardService, KeyboardService>();
        services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        services.AddTransient<ITerminalService, TerminalService>();
        services.AddTransient<Func<ITerminalService>>(sp => () => sp.GetRequiredService<ITerminalService>());
        services.AddHttpClient<AgentService>();
        services.AddTransient<IAgentService>(sp => sp.GetRequiredService<AgentService>());

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<TerminalViewModel>();
    }

    private Task<bool> ShowBackendErrorDialog(string message, string primary, string secondary)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Avalonia.Media.Brushes.Black,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock{ Text="Backend Error", FontSize=18, FontWeight= Avalonia.Media.FontWeight.SemiBold},
                    new TextBlock{ Text=message, TextWrapping= TextWrapping.Wrap, Foreground= Avalonia.Media.Brushes.LightGray},
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { }
                    }
                }
            }
        };

        var secondaryButton = new Button { Content = secondary, Padding = new Thickness(10, 6) };
        secondaryButton.Click += (_, __) =>
        {
            tcs.TrySetResult(false);
            dialog.Close();
        };
        var primaryButton = new Button { Content = primary, Padding = new Thickness(10, 6) };
        primaryButton.Click += (_, __) =>
        {
            tcs.TrySetResult(true);
            dialog.Close();
        };

        ((StackPanel)((StackPanel)dialog.Content).Children[2]).Children.Add(secondaryButton);
        ((StackPanel)((StackPanel)dialog.Content).Children[2]).Children.Add(primaryButton);

        dialog.Closed += (_, __) => tcs.TrySetResult(false);
        dialog.Show();
        return tcs.Task;
    }

    private static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocLM");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
