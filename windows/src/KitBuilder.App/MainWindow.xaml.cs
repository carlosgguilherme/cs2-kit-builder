using KitBuilder.App.Pages;
using KitBuilder.Data.Http;
using KitBuilder.Data.Prices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KitBuilder.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        ContentFrame.Navigate(typeof(KitBuilderPage));
        _ = RefreshPricesAsync();
    }

    private void OnNavigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        bool kit = (args.SelectedItem as NavigationViewItem)?.Tag as string == "kit";
        ContentFrame.Visibility = kit ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = kit ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Incremental price refresh in the background (resumable; waits when Steam rate-limits).</summary>
    private async Task RefreshPricesAsync()
    {
        var prices = App.Services.GetRequiredService<IPriceProvider>();
        var progress = new Progress<RefreshProgress>(p => StatusText.Text = p.RetryAfter is { } retry
            ? $"A Steam pediu uma pausa. Preços voltam a atualizar às {retry.ToLocalTime():HH:mm}."
            : $"Atualizando preços do Mercado da Steam: {p.Done} de {p.Total}…");
        while (true)
        {
            try
            {
                await Task.Run(() => prices.RefreshAllAsync(progress));
                StatusText.Text = $"Preço aproximado do Mercado da Steam, atualizado em {DateTime.Now:dd/MM/yyyy HH:mm}.";
                return;
            }
            catch (RateLimitedException e)
            {
                var wait = e.RetryAfter - DateTimeOffset.UtcNow;
                await Task.Delay(wait > TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                StatusText.Text = "Sem conexão com a Steam. Usando os últimos preços salvos.";
                return;
            }
        }
    }
}
