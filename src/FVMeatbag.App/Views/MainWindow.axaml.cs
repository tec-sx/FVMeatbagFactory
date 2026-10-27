using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FVMeatbag.App.ViewModels;
using FVMeatbag.Core.Import;

namespace FVMeatbag.App.Views;

public partial class MainWindow : Window, IFileDialogs
{
    private static readonly FilePickerFileType MeshFiles = new("Meshes (glTF, FBX)")
    {
        Patterns = [.. MeshImporter.SupportedExtensions.Select(e => "*" + e)],
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Dialogs = this;
            }
        };
    }

    public async Task<string?> PickMeshFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a mesh",
            AllowMultiple = false,
            FileTypeFilter = [MeshFiles],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickProjectFolderAsync(string startFolder)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a project folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
