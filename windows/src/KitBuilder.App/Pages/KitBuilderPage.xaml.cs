using KitBuilder.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace KitBuilder.App.Pages;

public sealed partial class KitBuilderPage : Page
{
    public KitBuilderPage()
    {
        ViewModel = App.Services.GetRequiredService<KitBuilderViewModel>();
        InitializeComponent();
        Picker.Color = Ui.ParseColor(ViewModel.TargetColorHex);
    }

    public KitBuilderViewModel ViewModel { get; }

    private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args) => ViewModel.TargetColorHex = Ui.Hex(args.NewColor);

    private void OnAlternativeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlternativeViewModel alternative)
        {
            ViewModel.Swap(alternative);
        }
    }
}
