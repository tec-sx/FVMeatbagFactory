#version 450
// See-through overlay meshes (the muscle preview): one flat color, lightly lit.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

// Must match VulkanBackend.TintPushConstants (128 bytes, the guaranteed minimum).
layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 normalRow0;   // xyz: row 0 of the world normal matrix, w: direction to the light x
    vec4 normalRow1;   // xyz: row 1,                            w: direction to the light y
    vec4 normalRow2;   // xyz: row 2,                            w: direction to the light z
    vec4 color;        // rgb: display color, a: opacity
} pc;

layout(location = 0) out vec3 outNormal;

void main()
{
    outNormal = mat3(pc.normalRow0.xyz, pc.normalRow1.xyz, pc.normalRow2.xyz) * inNormal;
    gl_Position = pc.worldViewProjection * vec4(inPosition, 1.0);
}
