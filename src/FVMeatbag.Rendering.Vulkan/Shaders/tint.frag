#version 450

layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 normalRow0;
    vec4 normalRow1;
    vec4 normalRow2;
    vec4 color;
} pc;

layout(location = 0) in vec3 inNormal;
layout(location = 0) out vec4 outColor;

void main()
{
    vec3 n = normalize(inNormal);
    if (!gl_FrontFacing)
    {
        n = -n;
    }
    vec3 toLight = normalize(vec3(pc.normalRow0.w, pc.normalRow1.w, pc.normalRow2.w));
    // Mostly the flat color so overlapping muscles stay readable, with enough light to show form.
    float light = 0.55 + 0.45 * abs(dot(n, toLight));
    outColor = vec4(pc.color.rgb * light, pc.color.a);
}
