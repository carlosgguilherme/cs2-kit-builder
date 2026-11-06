using KitBuilder.Data.Catalog;
using KitBuilder.Data.Http;
using KitBuilder.Data.Inventory;
using KitBuilder.Data.Prices;
using KitBuilder.Data.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KitBuilder.Data;

public static class ServiceCollectionExtensions
{
    public const string SteamClient = "steam";
    public const string CatalogClient = "catalog";

    public static IServiceCollection AddKitBuilderData(this IServiceCollection services, Action<DataOptions>? configure = null)
    {
        var options = new DataOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new LocalDatabase(options.DatabasePath));
        services.AddHttpClient(SteamClient, c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                c.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddHttpMessageHandler(() => new SteamThrottlingHandler(options.SteamRequestInterval))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddHttpClient(CatalogClient, c => c.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent));
        services.AddSingleton(sp => new CachedDownloader(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(CatalogClient), sp.GetRequiredService<LocalDatabase>(),
            Path.Combine(options.DataDirectory, "cache")));
        services.AddSingleton<CatalogService>();
        services.AddSingleton<IPriceProvider>(sp => new SteamMarketPriceProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(SteamClient), sp.GetRequiredService<LocalDatabase>(), options));
        services.AddSingleton(sp => new SteamInventoryClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(SteamClient), sp.GetRequiredService<LocalDatabase>(), options));
        services.AddSingleton(sp => new SteamIdResolver(sp.GetRequiredService<IHttpClientFactory>().CreateClient(SteamClient)));
        return services;
    }
}
