#version 450
// Unlit overlay lines (bones), drawn on top of the meshes.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;

// Must match VulkanBackend.LinePushConstants.
layout(push_constant) uniform Push
{
    mat4 viewProjection;
} pc;

layout(location = 0) out vec3 outColor;

void main()
{
    outColor = inColor;
    gl_Position = pc.viewProjection * vec4(inPosition, 1.0);
}
