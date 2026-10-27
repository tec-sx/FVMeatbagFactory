#version 450

// Must match VulkanBackend.WirePushConstants.
layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 color;
} pc;

layout(location = 0) out vec4 outColor;

void main()
{
    // Given in display (sRGB) values already.
    outColor = pc.color;
}
