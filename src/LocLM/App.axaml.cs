using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocLM.Services;
using LocLM.ViewModels;
using LocLM.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

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
        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize database
        var chatHistoryService = Services.GetRequiredService<IChatHistoryService>();
        await chatHistoryService.InitializeAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };

            // Start Python backend
            var pythonService = Services.GetRequiredService<IPythonBackendService>();
            pythonService.StartAsync();

            desktop.Exit += (s, e) => pythonService.Stop();
        }

        base.OnFrameworkInitializationCompleted();
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
        services.AddSingleton<IAgentService, AgentService>();
        services.AddSingleton<IOllamaService, OllamaService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IKeyboardService, KeyboardService>();
        services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddHttpClient<IAgentService, AgentService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<TerminalViewModel>();
    }
}
