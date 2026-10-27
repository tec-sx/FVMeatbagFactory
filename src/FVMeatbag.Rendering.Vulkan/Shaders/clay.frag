#version 450

layout(push_constant) uniform Push
{
    mat4 worldViewProjection;
    vec4 normalRow0;
    vec4 normalRow1;
    vec4 normalRow2;
    vec4 toLight;
} pc;

layout(location = 0) in vec3 inNormal;
layout(location = 0) out vec4 outColor;

const vec3 Albedo = vec3(0.72);
const vec3 SkyAmbient = vec3(0.22, 0.23, 0.25);
const vec3 GroundAmbient = vec3(0.07, 0.07, 0.08);

void main()
{
    vec3 n = normalize(inNormal);
    // Imported meshes are not always closed or consistently wound, so light both sides.
    if (!gl_FrontFacing)
    {
        n = -n;
    }
    vec3 radiance = vec3(pc.normalRow0.w, pc.normalRow1.w, pc.normalRow2.w);
    float diffuse = max(dot(n, normalize(pc.toLight.xyz)), 0.0);
    vec3 ambient = mix(GroundAmbient, SkyAmbient, n.y * 0.5 + 0.5);
    vec3 linear = Albedo * (ambient + radiance * diffuse / 3.14159265);
    // The target is UNORM, so encode to display gamma here.
    outColor = vec4(pow(clamp(linear, 0.0, 1.0), vec3(1.0 / 2.2)), 1.0);
}
