using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FVMeatbag.App.ViewModels;

namespace FVMeatbag.App;

/// <summary>Resolves <c>FooViewModel</c> to <c>FooView</c> by naming convention.</summary>
public sealed class ViewLocator : IDataTemplate
{
    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Views are referenced by the app assembly.")]
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
