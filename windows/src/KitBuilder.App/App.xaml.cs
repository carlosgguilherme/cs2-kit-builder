using KitBuilder.App.Services;
using KitBuilder.Data;
using KitBuilder.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace KitBuilder.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        UnhandledException += (_, e) => AppLog.Write("Unhandled UI exception: " + e.Message, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Write("Unobserved task exception", e.Exception);
        AppLog.Write($"Starting KitBuilder {typeof(App).Assembly.GetName().Version}");
        InitializeComponent();
        Services = new ServiceCollection()
            .AddKitBuilderData()
            .AddSingleton<Texts>(_ => new Texts("pt-BR"))
            .AddSingleton<KitCatalog>()
            .AddSingleton<IKitCatalog>(sp => sp.GetRequiredService<KitCatalog>())
            .AddTransient<KitBuilderViewModel>()
            .BuildServiceProvider();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
            AppLog.Write("Main window activated");
        }
        catch (Exception e)
        {
            AppLog.Write("Failed to create the main window", e);
            throw;
        }
    }
}
