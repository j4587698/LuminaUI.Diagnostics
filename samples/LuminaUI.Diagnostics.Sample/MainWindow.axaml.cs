using Avalonia.Controls;
using LuminaUI.Diagnostics.UI;

namespace LuminaUI.Diagnostics.Sample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var openButton = this.FindControl<Button>("OpenDevToolsButton");
        if (openButton is not null)
        {
            openButton.Click += (_, _) =>
            {
                LuminaDevTools.Open();
            };
        }
    }
}
