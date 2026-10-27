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
            var viewModel = new MainWindowViewModel(host);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            // "FVMeatbagFactory Suzanne.glb" (or dropping a file on the exe) imports it into the startup project.
            foreach (var path in desktop.Args ?? [])
            {
                viewModel.ImportMeshFile(path);
            }
            desktop.Exit += (_, _) => host.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
