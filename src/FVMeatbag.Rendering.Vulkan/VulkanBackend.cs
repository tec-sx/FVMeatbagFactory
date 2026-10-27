using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace FVMeatbag.Rendering.Vulkan;

/// <summary>
/// Vulkan backend. Renders into an offscreen BGRA8 image (4x MSAA where supported), then copies
/// the result into a host-visible buffer that the viewport reads back. This is step 1 of the
/// presentation plan in docs/ARCHITECTURE.md; zero-copy sharing with Avalonia comes later.
/// </summary>
/// <remarks>
/// Shading is untextured "clay": one directional light plus a hemisphere ambient. Overlay lines
/// (bones) are drawn last, without depth testing, so they show through the mesh. Frames are
/// rendered synchronously (submit, then wait), which keeps resource lifetimes trivial while the
/// renderer is small.
/// </remarks>
public sealed unsafe class VulkanBackend : IRenderBackend
{
    private const Format ColorFormat = Format.B8G8R8A8Unorm;
    private static readonly Vector4 ClearColor = new(0.13f, 0.13f, 0.14f, 1f);

    private readonly Dictionary<Guid, GpuMesh> _meshes = [];
    private Vk? _vk;
    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _queue;
    private uint _queueFamily;
    private Format _depthFormat;
    private SampleCountFlags _samples;
    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;
    private Fence _fence;
    private RenderPass _renderPass;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private PipelineLayout _linePipelineLayout;
    private Pipeline _linePipeline;
    private HostBuffer? _lineVertices;
    private RenderTarget? _target;

    public string Name => "Vulkan";
    public bool IsInitialized => _vk is not null;
    public uint Width => _target?.Width ?? 0;
    public uint Height => _target?.Height ?? 0;

    /// <summary>Name of the GPU in use, once initialized.</summary>
    public string DeviceName { get; private set; } = "";

    /// <summary>MSAA sample count in use, once initialized.</summary>
    public int SampleCount => (int)_samples;

    public void Initialize()
    {
        if (_vk is not null)
        {
            return;
        }
        var vk = Vk.GetApi();
        try
        {
            _instance = CreateInstance(vk);
            (_physicalDevice, _queueFamily) = PickDevice(vk, _instance);
            vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
            DeviceName = SilkMarshal.PtrToString((nint)properties.DeviceName) ?? "GPU";
            _samples = PickSampleCount(properties.Limits);
            _depthFormat = PickDepthFormat(vk, _physicalDevice);
            _device = CreateDevice(vk, _physicalDevice, _queueFamily);
            vk.GetDeviceQueue(_device, _queueFamily, 0, out _queue);
            _vk = vk;
            CreateCommandObjects();
            _renderPass = CreateRenderPass();
            (_pipelineLayout, _pipeline) = CreatePipeline(
                "clay", PrimitiveTopology.TriangleList, depthTest: true, (uint)sizeof(PushConstants), ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit);
            (_linePipelineLayout, _linePipeline) = CreatePipeline(
                "line", PrimitiveTopology.LineList, depthTest: false, (uint)sizeof(LinePushConstants), ShaderStageFlags.VertexBit);
        }
        catch
        {
            _vk = vk;
            Dispose();
            throw;
        }
    }

    public void Resize(uint width, uint height)
    {
        var vk = Api;
        if (_target is { } current && current.Width == width && current.Height == height)
        {
            return;
        }
        vk.DeviceWaitIdle(_device);
        _target?.Dispose();
        _target = width == 0 || height == 0 ? null : new RenderTarget(this, width, height);
    }

    public void RenderFrame(in FrameContext frame, IReadOnlyList<MeshDraw> draws, IReadOnlyList<LineSegment> lines)
    {
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(lines);
        var vk = Api;
        Resize(frame.Width, frame.Height);
        if (_target is not { } target)
        {
            return;
        }

        var used = new HashSet<Guid>();
        foreach (var draw in draws)
        {
            used.Add(draw.Mesh.Id);
            Upload(draw.Mesh);
        }

        Check(vk.ResetCommandBuffer(_commandBuffer, 0), "vkResetCommandBuffer");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(vk.BeginCommandBuffer(_commandBuffer, in begin), "vkBeginCommandBuffer");

        var clears = stackalloc ClearValue[3];
        clears[0] = new ClearValue(color: new ClearColorValue(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W));
        clears[1] = new ClearValue(depthStencil: new ClearDepthStencilValue(1f, 0));
        clears[2] = clears[0];
        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = target.Framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height)),
            ClearValueCount = target.AttachmentCount,
            PClearValues = clears,
        };
        vk.CmdBeginRenderPass(_commandBuffer, in renderPassBegin, SubpassContents.Inline);

        var viewport = new Viewport(0, 0, target.Width, target.Height, 0, 1);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height));
        vk.CmdSetViewport(_commandBuffer, 0, 1, in viewport);
        vk.CmdSetScissor(_commandBuffer, 0, 1, in scissor);
        vk.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        // System.Numerics clip space is +Y up; Vulkan's is +Y down.
        var viewProjection = frame.View * frame.Projection * Matrix4x4.CreateScale(1, -1, 1);
        var radiance = frame.LightRadiance;
        foreach (var draw in draws)
        {
            var mesh = _meshes[draw.Mesh.Id];
            if (mesh.IndexCount == 0)
            {
                continue;
            }
            var normal = Matrix4x4.Invert(draw.World, out var inverse) ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;
            var push = new PushConstants
            {
                WorldViewProjection = draw.World * viewProjection,
                NormalRow0 = new Vector4(normal.M11, normal.M12, normal.M13, radiance.X),
                NormalRow1 = new Vector4(normal.M21, normal.M22, normal.M23, radiance.Y),
                NormalRow2 = new Vector4(normal.M31, normal.M32, normal.M33, radiance.Z),
                ToLight = new Vector4(frame.DirectionToLight, 0),
            };
            vk.CmdPushConstants(_commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PushConstants), &push);
            ulong offset = 0;
            var vertexBuffer = mesh.Vertices.Buffer;
            vk.CmdBindVertexBuffers(_commandBuffer, 0, 1, &vertexBuffer, &offset);
            vk.CmdBindIndexBuffer(_commandBuffer, mesh.Indices.Buffer, 0, IndexType.Uint32);
            vk.CmdDrawIndexed(_commandBuffer, mesh.IndexCount, 1, 0, 0, 0);
        }
        DrawLines(lines, viewProjection);
        vk.CmdEndRenderPass(_commandBuffer);

        // The render pass leaves the presented image in TransferSrcOptimal.
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(target.Width, target.Height, 1),
        };
        vk.CmdCopyImageToBuffer(_commandBuffer, target.PresentImage, ImageLayout.TransferSrcOptimal, target.Readback.Buffer, 1, in region);
        var toHost = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = target.Readback.Buffer,
            Offset = 0,
            Size = Vk.WholeSize,
        };
        vk.CmdPipelineBarrier(_commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit, 0, 0, null, 1, &toHost, 0, null);
        Check(vk.EndCommandBuffer(_commandBuffer), "vkEndCommandBuffer");

        var commandBuffer = _commandBuffer;
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &commandBuffer };
        Check(vk.ResetFences(_device, 1, in _fence), "vkResetFences");
        Check(vk.QueueSubmit(_queue, 1, in submit, _fence), "vkQueueSubmit");
        Check(vk.WaitForFences(_device, 1, in _fence, true, ulong.MaxValue), "vkWaitForFences");

        // The GPU is idle now, so meshes no longer drawn can go.
        foreach (var id in _meshes.Keys.Where(id => !used.Contains(id)).ToList())
        {
            _meshes.Remove(id, out var stale);
            stale?.Dispose();
        }
    }

    public void CopyPixels(nint destination, int rowBytes)
    {
        if (_target is not { } target)
        {
            return;
        }
        var source = (byte*)target.Readback.Mapped;
        var sourceRow = (int)target.Width * 4;
        var count = Math.Min(sourceRow, rowBytes);
        for (var y = 0; y < target.Height; y++)
        {
            System.Buffer.MemoryCopy(source + (y * sourceRow), (byte*)destination + (y * rowBytes), rowBytes, count);
        }
    }

    public void Dispose()
    {
        if (_vk is not { } vk)
        {
            return;
        }
        if (_device.Handle != 0)
        {
            vk.DeviceWaitIdle(_device);
            foreach (var mesh in _meshes.Values)
            {
                mesh.Dispose();
            }
            _meshes.Clear();
            _target?.Dispose();
            _target = null;
            _lineVertices?.Dispose();
            _lineVertices = null;
            vk.DestroyPipeline(_device, _linePipeline, null);
            vk.DestroyPipelineLayout(_device, _linePipelineLayout, null);
            vk.DestroyPipeline(_device, _pipeline, null);
            vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            vk.DestroyRenderPass(_device, _renderPass, null);
            vk.DestroyFence(_device, _fence, null);
            vk.DestroyCommandPool(_device, _commandPool, null);
            vk.DestroyDevice(_device, null);
            _device = default;
        }
        if (_instance.Handle != 0)
        {
            vk.DestroyInstance(_instance, null);
            _instance = default;
        }
        vk.Dispose();
        _vk = null;
    }

    /// <summary>True if a Vulkan loader is present on this machine.</summary>
    public static bool IsLoaderAvailable() =>
        NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1", out var handle)
        && FreeAndReturnTrue(handle);

    private Vk Api => _vk ?? throw new InvalidOperationException("The Vulkan backend is not initialized.");

    private static bool FreeAndReturnTrue(nint handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    private static void Check(Result result, string call)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{call} failed: {result}.");
        }
    }

    private static Instance CreateInstance(Vk vk)
    {
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
                ApiVersion = Vk.Version12,
            };
            var createInfo = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &appInfo };
            Check(vk.CreateInstance(in createInfo, null, out var instance), "vkCreateInstance");
            return instance;
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)engineName);
        }
    }

    /// <summary>Prefers a discrete GPU, then an integrated one, then anything with a graphics queue.</summary>
    private static (PhysicalDevice Device, uint QueueFamily) PickDevice(Vk vk, Instance instance)
    {
        uint count = 0;
        Check(vk.EnumeratePhysicalDevices(instance, ref count, null), "vkEnumeratePhysicalDevices");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
        {
            Check(vk.EnumeratePhysicalDevices(instance, ref count, p), "vkEnumeratePhysicalDevices");
        }

        var candidates = new List<(PhysicalDevice Device, uint QueueFamily, int Score)>();
        foreach (var device in devices)
        {
            uint familyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(device, ref familyCount, null);
            var families = new QueueFamilyProperties[familyCount];
            fixed (QueueFamilyProperties* f = families)
            {
                vk.GetPhysicalDeviceQueueFamilyProperties(device, ref familyCount, f);
            }
            var graphics = Array.FindIndex(families, f => f.QueueFlags.HasFlag(QueueFlags.GraphicsBit));
            if (graphics < 0)
            {
                continue;
            }
            vk.GetPhysicalDeviceProperties(device, out var properties);
            var score = properties.DeviceType switch
            {
                PhysicalDeviceType.DiscreteGpu => 3,
                PhysicalDeviceType.IntegratedGpu => 2,
                PhysicalDeviceType.VirtualGpu => 1,
                _ => 0,
            };
            candidates.Add((device, (uint)graphics, score));
        }
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No GPU with Vulkan graphics support was found.");
        }
        var best = candidates.MaxBy(c => c.Score);
        return (best.Device, best.QueueFamily);
    }

    private static SampleCountFlags PickSampleCount(PhysicalDeviceLimits limits)
    {
        var supported = limits.FramebufferColorSampleCounts & limits.FramebufferDepthSampleCounts;
        return supported.HasFlag(SampleCountFlags.Count4Bit) ? SampleCountFlags.Count4Bit : SampleCountFlags.Count1Bit;
    }

    private static Format PickDepthFormat(Vk vk, PhysicalDevice device)
    {
        foreach (var format in (ReadOnlySpan<Format>)[Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint])
        {
            vk.GetPhysicalDeviceFormatProperties(device, format, out var properties);
            if (properties.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.DepthStencilAttachmentBit))
            {
                return format;
            }
        }
        throw new InvalidOperationException("The GPU supports no depth buffer format.");
    }

    private static Device CreateDevice(Vk vk, PhysicalDevice physicalDevice, uint queueFamily)
    {
        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var features = new PhysicalDeviceFeatures();
        var createInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            PEnabledFeatures = &features,
        };
        Check(vk.CreateDevice(physicalDevice, in createInfo, null, out var device), "vkCreateDevice");
        return device;
    }

    private void CreateCommandObjects()
    {
        var vk = Api;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Check(vk.CreateCommandPool(_device, in poolInfo, null, out _commandPool), "vkCreateCommandPool");
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(vk.AllocateCommandBuffers(_device, in allocateInfo, out _commandBuffer), "vkAllocateCommandBuffers");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(vk.CreateFence(_device, in fenceInfo, null, out _fence), "vkCreateFence");
    }

    private bool Multisampled => _samples != SampleCountFlags.Count1Bit;

    /// <summary>
    /// Color (multisampled when MSAA is on), depth, and with MSAA a single-sample resolve target.
    /// Whichever image is presented ends the pass in TransferSrcOptimal, ready to copy out.
    /// </summary>
    private RenderPass CreateRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[3];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = _samples,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = Multisampled ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = Multisampled ? ImageLayout.ColorAttachmentOptimal : ImageLayout.TransferSrcOptimal,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = _depthFormat,
            Samples = _samples,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        attachments[2] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal,
        };

        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var resolveRef = new AttachmentReference(2, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PResolveAttachments = Multisampled ? &resolveRef : null,
            PDepthStencilAttachment = &depthRef,
        };

        var dependencies = stackalloc SubpassDependency[2];
        // Previous frame's copy and depth use must finish before this frame writes the attachments.
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.TransferBit | PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.TransferReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        // Rendering (and the MSAA resolve) must finish before the image is copied out.
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.TransferBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = Multisampled ? 3u : 2u,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(Api.CreateRenderPass(_device, in createInfo, null, out var renderPass), "vkCreateRenderPass");
        return renderPass;
    }

    /// <summary>Draws overlay lines with the line pipeline. Called inside the render pass, after the meshes.</summary>
    private void DrawLines(IReadOnlyList<LineSegment> lines, Matrix4x4 viewProjection)
    {
        if (lines.Count == 0)
        {
            return;
        }
        var vk = Api;
        var vertexCount = lines.Count * 2;
        var bytes = (ulong)(vertexCount * sizeof(Vertex));
        if (_lineVertices is null || _lineVertices.Size < bytes)
        {
            // The previous frame has finished (RenderFrame waits for it), so the old buffer is free.
            _lineVertices?.Dispose();
            _lineVertices = new HostBuffer(this, Math.Max(bytes * 2, 4096), BufferUsageFlags.VertexBufferBit);
        }
        var vertices = new Span<Vertex>(_lineVertices.Mapped, vertexCount);
        for (var i = 0; i < lines.Count; i++)
        {
            // Same layout as mesh vertices; the second attribute carries the color.
            vertices[i * 2] = new Vertex { Position = lines[i].From, Normal = lines[i].Color };
            vertices[(i * 2) + 1] = new Vertex { Position = lines[i].To, Normal = lines[i].Color };
        }

        vk.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Graphics, _linePipeline);
        var push = new LinePushConstants { ViewProjection = viewProjection };
        vk.CmdPushConstants(_commandBuffer, _linePipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(LinePushConstants), &push);
        ulong offset = 0;
        var buffer = _lineVertices.Buffer;
        vk.CmdBindVertexBuffers(_commandBuffer, 0, 1, &buffer, &offset);
        vk.CmdDraw(_commandBuffer, (uint)vertexCount, 1, 0, 0);
    }

    /// <summary>
    /// A pipeline over the shared vertex layout (two vec3 attributes). <paramref name="shader"/>
    /// names the embedded <c>.vert.spv</c>/<c>.frag.spv</c> pair.
    /// </summary>
    private (PipelineLayout Layout, Pipeline Pipeline) CreatePipeline(
        string shader, PrimitiveTopology topology, bool depthTest, uint pushConstantSize, ShaderStageFlags pushConstantStages)
    {
        var vk = Api;
        var pushRange = new PushConstantRange(pushConstantStages, 0, pushConstantSize);
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        Check(vk.CreatePipelineLayout(_device, in layoutInfo, null, out var layout), "vkCreatePipelineLayout");

        var vertexModule = CreateShaderModule($"{shader}.vert.spv");
        var fragmentModule = CreateShaderModule($"{shader}.frag.spv");
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexModule,
                PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentModule,
                PName = entry,
            };

            var binding = new VertexInputBindingDescription(0, (uint)sizeof(Vertex), VertexInputRate.Vertex);
            var attributes = stackalloc VertexInputAttributeDescription[2];
            attributes[0] = new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0);
            attributes[1] = new VertexInputAttributeDescription(1, 0, Format.R32G32B32Sfloat, 12);
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 2,
                PVertexAttributeDescriptions = attributes,
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = topology,
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };
            // No culling: imported meshes are not always closed or consistently wound; the shader lights back faces.
            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = _samples,
            };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = depthTest,
                DepthWriteEnable = depthTest,
                DepthCompareOp = CompareOp.Less,
            };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            var createInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = layout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Check(vk.CreateGraphicsPipelines(_device, default, 1, in createInfo, null, out var pipeline), "vkCreateGraphicsPipelines");
            return (layout, pipeline);
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
            vk.DestroyShaderModule(_device, vertexModule, null);
            vk.DestroyShaderModule(_device, fragmentModule, null);
        }
    }

    private ShaderModule CreateShaderModule(string name)
    {
        using var stream = typeof(VulkanBackend).Assembly.GetManifestResourceStream($"FVMeatbag.Rendering.Vulkan.Shaders.{name}")
            ?? throw new InvalidOperationException($"Shader {name} is not embedded.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        fixed (byte* p = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)p,
            };
            Check(Api.CreateShaderModule(_device, in createInfo, null, out var module), "vkCreateShaderModule");
            return module;
        }
    }

    private void Upload(RenderMesh mesh)
    {
        if (_meshes.TryGetValue(mesh.Id, out var existing))
        {
            if (existing.Version == mesh.Version)
            {
                return;
            }
            Api.DeviceWaitIdle(_device);
            existing.Dispose();
            _meshes.Remove(mesh.Id);
        }
        _meshes[mesh.Id] = new GpuMesh(this, mesh);
    }

    private uint FindMemoryType(uint typeBits, MemoryPropertyFlags required, MemoryPropertyFlags preferred = 0)
    {
        Api.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var properties);
        foreach (var wanted in (ReadOnlySpan<MemoryPropertyFlags>)[required | preferred, required])
        {
            for (var i = 0; i < properties.MemoryTypeCount; i++)
            {
                if ((typeBits & (1u << i)) != 0 && (properties.MemoryTypes[i].PropertyFlags & wanted) == wanted)
                {
                    return (uint)i;
                }
            }
        }
        throw new InvalidOperationException($"No GPU memory type with {required}.");
    }

    private DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags required, MemoryPropertyFlags preferred = 0)
    {
        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, required, preferred),
        };
        Check(Api.AllocateMemory(_device, in allocateInfo, null, out var memory), "vkAllocateMemory");
        return memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
    }

    /// <summary>Must match the push_constant block in Shaders/clay.vert and clay.frag.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public Matrix4x4 WorldViewProjection;
        public Vector4 NormalRow0;
        public Vector4 NormalRow1;
        public Vector4 NormalRow2;
        public Vector4 ToLight;
    }

    /// <summary>Must match the push_constant block in Shaders/line.vert.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LinePushConstants
    {
        public Matrix4x4 ViewProjection;
    }

    /// <summary>A buffer in host-visible, coherent memory, mapped for its whole life.</summary>
    private sealed class HostBuffer : IDisposable
    {
        private readonly VulkanBackend _owner;
        private readonly DeviceMemory _memory;

        public HostBuffer(VulkanBackend owner, ulong size, BufferUsageFlags usage, MemoryPropertyFlags preferred = 0)
        {
            _owner = owner;
            var vk = owner.Api;
            var createInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = Math.Max(size, 4),
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            Check(vk.CreateBuffer(owner._device, in createInfo, null, out var buffer), "vkCreateBuffer");
            Buffer = buffer;
            Size = createInfo.Size;
            vk.GetBufferMemoryRequirements(owner._device, buffer, out var requirements);
            _memory = owner.Allocate(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, preferred);
            Check(vk.BindBufferMemory(owner._device, buffer, _memory, 0), "vkBindBufferMemory");
            void* mapped;
            Check(vk.MapMemory(owner._device, _memory, 0, Vk.WholeSize, 0, &mapped), "vkMapMemory");
            Mapped = mapped;
        }

        public VkBuffer Buffer { get; }
        public ulong Size { get; }
        public void* Mapped { get; }

        public void Dispose()
        {
            var vk = _owner.Api;
            vk.UnmapMemory(_owner._device, _memory);
            vk.DestroyBuffer(_owner._device, Buffer, null);
            vk.FreeMemory(_owner._device, _memory, null);
        }
    }

    private sealed class GpuMesh : IDisposable
    {
        public GpuMesh(VulkanBackend owner, RenderMesh mesh)
        {
            Version = mesh.Version;
            var positions = mesh.Positions.Span;
            var normals = mesh.Normals.Span;
            var vertexCount = Math.Min(positions.Length, normals.Length);
            Vertices = new HostBuffer(owner, (ulong)(vertexCount * sizeof(Vertex)), BufferUsageFlags.VertexBufferBit);
            var vertices = new Span<Vertex>(Vertices.Mapped, vertexCount);
            for (var i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vertex { Position = positions[i], Normal = normals[i] };
            }

            var indices = mesh.Indices.Span;
            Indices = new HostBuffer(owner, (ulong)(indices.Length * sizeof(uint)), BufferUsageFlags.IndexBufferBit);
            indices.CopyTo(new Span<uint>(Indices.Mapped, indices.Length));
            IndexCount = (uint)(indices.Length - (indices.Length % 3));
        }

        public long Version { get; }
        public HostBuffer Vertices { get; }
        public HostBuffer Indices { get; }
        public uint IndexCount { get; }

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }

    /// <summary>The offscreen images, their framebuffer and the readback buffer for one viewport size.</summary>
    private sealed class RenderTarget : IDisposable
    {
        private readonly VulkanBackend _owner;
        private readonly List<(Image Image, DeviceMemory Memory, ImageView View)> _images = [];

        public RenderTarget(VulkanBackend owner, uint width, uint height)
        {
            _owner = owner;
            Width = width;
            Height = height;
            var color = CreateImage(ColorFormat, owner._samples, ImageUsageFlags.ColorAttachmentBit | (owner.Multisampled ? 0 : ImageUsageFlags.TransferSrcBit), ImageAspectFlags.ColorBit);
            var depth = CreateImage(owner._depthFormat, owner._samples, ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit);
            var views = stackalloc ImageView[3];
            views[0] = color.View;
            views[1] = depth.View;
            PresentImage = color.Image;
            if (owner.Multisampled)
            {
                var resolve = CreateImage(ColorFormat, SampleCountFlags.Count1Bit, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, ImageAspectFlags.ColorBit);
                views[2] = resolve.View;
                PresentImage = resolve.Image;
            }
            AttachmentCount = owner.Multisampled ? 3u : 2u;

            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = owner._renderPass,
                AttachmentCount = AttachmentCount,
                PAttachments = views,
                Width = width,
                Height = height,
                Layers = 1,
            };
            Check(owner.Api.CreateFramebuffer(owner._device, in createInfo, null, out var framebuffer), "vkCreateFramebuffer");
            Framebuffer = framebuffer;
            Readback = new HostBuffer(owner, (ulong)width * height * 4, BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostCachedBit);
        }

        public uint Width { get; }
        public uint Height { get; }
        public uint AttachmentCount { get; }
        public Framebuffer Framebuffer { get; }

        /// <summary>The single-sample image holding the finished frame.</summary>
        public Image PresentImage { get; }

        public HostBuffer Readback { get; }

        public void Dispose()
        {
            var vk = _owner.Api;
            Readback.Dispose();
            vk.DestroyFramebuffer(_owner._device, Framebuffer, null);
            foreach (var (image, memory, view) in _images)
            {
                vk.DestroyImageView(_owner._device, view, null);
                vk.DestroyImage(_owner._device, image, null);
                vk.FreeMemory(_owner._device, memory, null);
            }
        }

        private (Image Image, ImageView View) CreateImage(Format format, SampleCountFlags samples, ImageUsageFlags usage, ImageAspectFlags aspect)
        {
            var vk = _owner.Api;
            var device = _owner._device;
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(Width, Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = samples,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            Check(vk.CreateImage(device, in imageInfo, null, out var image), "vkCreateImage");
            vk.GetImageMemoryRequirements(device, image, out var requirements);
            var memory = _owner.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
            Check(vk.BindImageMemory(device, image, memory, 0), "vkBindImageMemory");
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1),
            };
            Check(vk.CreateImageView(device, in viewInfo, null, out var view), "vkCreateImageView");
            _images.Add((image, memory, view));
            return (image, view);
        }
    }
}
