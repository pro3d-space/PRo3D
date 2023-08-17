# Feature: Multitexturing

synopsis: This feature allows to visualize multiple (opc) texture layers and control visualization properties.

Instrument data and reconstruction information for example can be mapped onto the surface and blended with the albedo texture (here an accuracy map of the reconstruction):

![image](https://github.com/pro3d-space/PRo3D/assets/513281/dc35a265-5432-4631-80d4-a8530e25bb0f)


By using query parameters and a transfer function specific attributes can be filtered (e.g. here the elevation):

![image](https://github.com/pro3d-space/PRo3D/assets/513281/1593df6d-2d63-49a1-a804-9e5bb06354c2)


## UI

The UI is implemented in https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Core/Surface/Surface-Properties.fs#L175 and allows to specify what textures to use, and how to blend them.
Datatypes for transfer functions is found in base: https://github.com/pro3d-space/PRo3D/blob/features/multitexture/src/PRo3D.Base/Multitexturing.fs
The controls can be found in the surface properties when selecting a surface. 

![image](https://github.com/pro3d-space/PRo3D/assets/513281/35f16a45-1f50-43ac-bdb6-311e9d826fb5)

## Data & Rendering

Rendering is handled in Aardvark.Geospatial.Opc which allows to customize what uniforms, textures and varyings are passed into the renderer.
The entrypoints can be found here: https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Core/Surface/Surface.Sg.fs#L205

Inputs for the shader are prepared here: https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Viewer/Viewer/Viewer-Utils.fs#L409
The shader is here: https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Base/Utilities.fs#L653

In order to guide PRo3D how to handle the textures, opcx files are needed. Some logic finds it within the surface folder: https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Viewer/Scene.fs#L123

## Caveats and missing features

 - Currently we only read opcx files as opposed to opcx.json files which also provide a friendly name for the texture layers. In favor of reduced complexity we opted for the simpler, established opcx solution which could also be extended to contain a friendly name for the layer.
 - Textures are adressed via indices as opposed to by name (see https://github.com/pro3d-space/PRo3D/blob/045bafa5b167a5bc725b6dfa6519ac35e12f38e8/src/PRo3D.Viewer/Viewer/Viewer-Utils.fs#L382). This is historic and cannot be fixed easily. The 'Texture' record should be changed in aardvark.rendering, weights and textures separated into to arrays and so on..