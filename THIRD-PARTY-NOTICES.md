# Third-party notices

FV Meatbag Factory includes the following third-party assets. NuGet packages are listed in
`Directory.Packages.props` and come with their own licenses.

## Material Design Icons

The viewport toolbar and tool overlay icons in `src/FVMeatbag.App/Assets/Icons.axaml` are SVG paths from
[Material Design Icons](https://pictogrammers.com/library/mdi/) 7.4.47 by Pictogrammers,
released under the Pictogrammers Free License; the icons themselves are licensed under the
[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

| Icon | Used for | Author |
| --- | --- | --- |
| `cube-outline` | Wireframe | Austin Andrews |
| `cube` | Solid | Austin Andrews |
| `hexagon-slice-6` | Solid + wireframe | SarinManS |
| `circle-opacity` | Shaded | Michael Irigoyen |
| `bone` | Show bones | Austin Andrews |
| `fit-to-screen-outline` | Frame all | Google |
| `cursor-default-outline` | Select tool | Austin Andrews |
| `arrow-all` | Move tool | Austin Andrews |
| `rotate-3d-variant` | Rotate tool | Austin Andrews |
| `resize` | Scale tool | Austin Andrews |

## Open Asset Import Library (Assimp)

The FBX importer uses the native [Assimp](https://github.com/assimp/assimp) 6.0.2 library, shipped
in the `Ultz.Native.Assimp` package that `Silk.NET.Assimp` brings in, under the
BSD 3-Clause License:

```
Open Asset Import Library (assimp)

Copyright (c) 2006-2021, assimp team
All rights reserved.

Redistribution and use of this software in source and binary forms,
with or without modification, are permitted provided that the
following conditions are met:

* Redistributions of source code must retain the above
  copyright notice, this list of conditions and the
  following disclaimer.

* Redistributions in binary form must reproduce the above
  copyright notice, this list of conditions and the
  following disclaimer in the documentation and/or other
  materials provided with the distribution.

* Neither the name of the assimp team, nor the names of its
  contributors may be used to endorse or promote products
  derived from this software without specific prior
  written permission of the assimp team.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## 3D Male Base Mesh

`assets/Samples/Demo/Meshes/MaleBaseMesh.glb` by orange-juice-games, CC0 1.0 (public domain).
See the [Demo project README](assets/Samples/Demo/README.md) and `MaleBaseMesh.LICENSE.txt` next to the file.
The FBX test fixtures in `tests/FVMeatbag.Scene.Tests/Fixtures` are Blender exports of the same character.
