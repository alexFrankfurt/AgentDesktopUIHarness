using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ihsbmodern;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            _window = new Window();
            var tb = new TextBlock
            {
                Text = $"EXCEPTION:\n\n{ex}\n\n--- INNER ---\n\n{ex.InnerException}",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(16),
                IsTextSelectionEnabled = true,
            };
            var sv = new ScrollViewer { Content = tb };
            _window.Content = sv;
            _window.Title = "ihsbmodern - Startup Error";
            _window.Activate();
        }
    }
}
