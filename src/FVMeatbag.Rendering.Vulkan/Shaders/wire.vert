#version 450
// Mesh edges for the wireframe display modes, drawn with polygon mode Line.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal; // unused, shares the mesh vertex layout

// Must match VulkanBackend.WirePushConstants.
layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 color;
} pc;

void main()
{
    gl_Position = pc.worldViewProjection * vec4(inPosition, 1.0);
}
