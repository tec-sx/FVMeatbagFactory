#version 450
// Untextured "clay" shading: geometry, one directional light and a hemisphere ambient.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

// Must match VulkanBackend.PushConstants (128 bytes, the guaranteed minimum).
layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 normalRow0;   // xyz: row 0 of the world normal matrix, w: light radiance r
    vec4 normalRow1;   // xyz: row 1,                            w: light radiance g
    vec4 normalRow2;   // xyz: row 2,                            w: light radiance b
    vec4 toLight;      // xyz: unit vector towards the light (world)
} pc;

layout(location = 0) out vec3 outNormal;

void main()
{
    outNormal = mat3(pc.normalRow0.xyz, pc.normalRow1.xyz, pc.normalRow2.xyz) * inNormal;
    gl_Position = pc.worldViewProjection * vec4(inPosition, 1.0);
}
