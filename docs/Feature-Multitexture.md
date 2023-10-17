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



 ## Currently available data in JRs data pipeline:

 This is an example of the currently available data:

 ```
  "attribute_layers": [
        {
            "label": "Texture0",
            "channels": 3,
            "channel_meaning": [
                {
                    "meaning": "Red channel",
                    "unit": ""
                },
                {
                    "meaning": "Green channel",
                    "unit": ""
                },
                {
                    "meaning": "Blue channel",
                    "unit": ""
                }
            ]
        },
        {
            "label": "Are",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Area",
                    "unit": "km^2"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 7.9786402926316e-09,
                    "value_offset": 1.042114604388189e-06
                }
            ]
        },
        {
            "label": "Ele",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Elevation",
                    "unit": "Meter"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.48938430490074897,
                    "value_offset": 0.0037061963230371475
                }
            ]
        },
        {
            "label": "GrM",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Gravitation Magnitude",
                    "unit": "m/s^2"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 5.19640585609279e-08,
                    "value_offset": 3.759760147659108e-05
                }
            ]
        },
        {
            "label": "GrP",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Gravitation Potential",
                    "unit": "J/kg"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 1.8399184034389305e-05,
                    "value_offset": -0.0058954209089279175
                }
            ]
        },
        {
            "label": "GrX",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Gravity Vector X",
                    "unit": "m/s^2"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 3.1842884752444746e-07,
                    "value_offset": -3.989627293776721e-05
                }
            ]
        },
        {
            "label": "GrY",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Gravity Vector Y",
                    "unit": "m/s^2"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 3.634925053306437e-07,
                    "value_offset": -4.6144461521180347e-05
                }
            ]
        },
        {
            "label": "GrZ",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Gravitaty Vector Z",
                    "unit": "m/s^2"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 3.9970011960927717e-07,
                    "value_offset": -5.078775211586617e-05
                }
            ]
        },
        {
            "label": "Lat",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Latitude",
                    "unit": "deg"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.7086614173228346,
                    "value_offset": -90.0
                }
            ]
        },
        {
            "label": "Lon",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Longitude",
                    "unit": "deg"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 1.4166667067159817,
                    "value_offset": 0.0
                }
            ]
        },
        {
            "label": "NmX",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Normal Vector X",
                    "unit": ""
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.00786956393812585,
                    "value_offset": -0.9992351531982422
                }
            ]
        },
        {
            "label": "NmY",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Normal Vector Y",
                    "unit": ""
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.007865285075555636,
                    "value_offset": -0.9980932474136353
                }
            ]
        },
        {
            "label": "NmZ",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Normal Vector Z",
                    "unit": ""
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.007873622920569472,
                    "value_offset": -0.9999253749847412
                }
            ]
        },
        {
            "label": "Rad",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Radius",
                    "unit": "Meter"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.00014005184877575853,
                    "value_offset": 0.056492120027542114
                }
            ]
        },
        {
            "label": "Slo",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Slope",
                    "unit": "deg"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.18442003839597929,
                    "value_offset": 0.3605306148529053
                }
            ]
        },
        {
            "label": "XCo",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "X Coordinate of vertices",
                    "unit": "km"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.0006963809526811434,
                    "value_offset": -0.08918076008558273
                }
            ]
        },
        {
            "label": "YCo",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Y Coordinate of vertices",
                    "unit": "km"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.0006845568226078364,
                    "value_offset": -0.08715743571519852
                }
            ]
        },
        {
            "label": "ZCo",
            "channels": 1,
            "channel_meaning": [
                {
                    "meaning": "Z Coordinate of vertices",
                    "unit": "km"
                }
            ],
            "no_data_value": 0,
            "pixel_value_scaling": [
                {
                    "scaling_factor": 0.00045456527548981464,
                    "value_offset": -0.056789569556713104
                }
            ]
        }
```