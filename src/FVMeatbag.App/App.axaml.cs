using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FVMeatbag.App.ViewModels;
using FVMeatbag.App.Views;

namespace FVMeatbag.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = new AppHost();
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(host) };
            desktop.Exit += (_, _) => host.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
