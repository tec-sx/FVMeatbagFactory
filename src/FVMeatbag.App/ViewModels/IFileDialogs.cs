namespace FVMeatbag.App.ViewModels;

/// <summary>File and folder pickers, provided by the view so view models stay free of windows.</summary>
public interface IFileDialogs
{
    Task<string?> PickMeshFileAsync();
    Task<string?> PickProjectFolderAsync(string startFolder);
}
