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
        window = new MainWindow();
        window.Activate();
    }
}
