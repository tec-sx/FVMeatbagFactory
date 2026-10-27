using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace FVMeatbag.Rendering.Vulkan;

/// <summary>
/// Vulkan backend. Currently creates the instance only; device selection, the offscreen render
/// target and the PBR pipeline follow in the rendering milestone.
/// </summary>
public sealed unsafe class VulkanBackend : IRenderBackend
{
    private Vk? _vk;
    private Instance _instance;

    public string Name => "Vulkan";
    public bool IsInitialized => _vk is not null;

    public void Initialize()
    {
        if (_vk is not null)
        {
            return;
        }

        var vk = Vk.GetApi();
        var appName = (byte*)SilkMarshal.StringToPtr("FVMeatbagFactory");
        var engineName = (byte*)SilkMarshal.StringToPtr("FVMeatbag");
        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                ApplicationVersion = Vk.MakeVersion(0, 1, 0),
                PEngineName = engineName,
                EngineVersion = Vk.MakeVersion(0, 1, 0),
                ApiVersion = Vk.Version13,
            };
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
            };

            var result = vk.CreateInstance(in createInfo, null, out _instance);
            if (result != Result.Success)
            {
                vk.Dispose();
                throw new InvalidOperationException($"vkCreateInstance failed: {result}.");
            }
            _vk = vk;
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)engineName);
        }
    }

    public void Resize(uint width, uint height)
    {
        // Offscreen target is (re)created here once the device exists.
    }

    public void RenderFrame(in FrameContext frame)
    {
        // Record and submit the frame here once the pipeline exists.
    }

    public void Dispose()
    {
        if (_vk is null)
        {
            return;
        }
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
        _vk = null;
    }

    /// <summary>True if a Vulkan loader is present on this machine.</summary>
    public static bool IsLoaderAvailable() =>
        NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1", out var handle)
        && FreeAndReturnTrue(handle);

    private static bool FreeAndReturnTrue(nint handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }
}
