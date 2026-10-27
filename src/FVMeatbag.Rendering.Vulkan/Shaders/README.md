# Shaders

GLSL sources and the SPIR-V the backend embeds. The `.spv` files are committed so building the app
needs no shader compiler. After editing a shader, regenerate its `.spv` with glslang
(part of the Vulkan SDK):

```sh
glslangValidator -V clay.vert -o clay.vert.spv
glslangValidator -V clay.frag -o clay.frag.spv
glslangValidator -V line.vert -o line.vert.spv
glslangValidator -V line.frag -o line.frag.spv
```
