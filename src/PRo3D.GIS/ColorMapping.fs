namespace PRo3D.InstrumentVisualization

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph
open Aardvark.Rendering

type DataType = 
    | Int16 = 1
    | Float32 = 2


type VisualizationProperties = 
    {
        visualizationRange : aval<Range1d>
        // TODO sophie
        dataType : aval<DataType>
        instrumentImage : aval<ITexture>
        colorMapping : aval<Option<ITexture>>
        projectionOpacity : aval<float>
    }

module VisualizationProperties =
    let empty = 
        {
            visualizationRange = AVal.constant Range1d.Unit
            dataType = AVal.constant DataType.Float32
            instrumentImage = DefaultTextures.checkerboard
            colorMapping = AVal.constant None
            projectionOpacity = AVal.constant 1.0
        }

[<AutoOpen>]
module InstrumentImageVisualization =


    type Self = Self
    let private getResourceStream (resourceName: string) () =
        let assembly = typeof<Self>.Assembly
        let resourcePath = assembly.GetName().Name + ".resources." + resourceName
        let s = assembly.GetManifestResourceStream(resourcePath)
        if isNull s then
            let names = assembly.GetManifestResourceNames()
            Log.warn "could not find resource, embeded names are: %A" names
            failwithf "could not find resource: %s" resourcePath
        else
            s

    let getColorMapTexture (name : string) = 
        StreamTexture(getResourceStream name) :> ITexture

    let applyProperties (p : VisualizationProperties) (sg : ISg) = 
        sg 
        |> Sg.uniform "MinValue" (p.visualizationRange |> AVal.map _.Min)
        |> Sg.uniform "MaxValue" (p.visualizationRange |> AVal.map _.Max)
        |> Sg.uniform "UseFalseColor" (p.colorMapping |> AVal.map Option.isSome)
        |> Sg.texture "InstrumentImage" p.instrumentImage
        |> Sg.uniform "DataType" (p.dataType |> AVal.map int)
        |> Sg.uniform "ProjectedImageOpacity" p.projectionOpacity
        |> Sg.texture "ColormapTexture" (
            p.colorMapping |> AVal.bind (function
                | None -> DefaultTextures.blackTex 
                | Some t -> AVal.constant t)
        )


module Shaders = 
    open FShade
    open Aardvark.Rendering.Effects

    let instrumentSampler = 
        sampler2d {
            texture uniform?InstrumentImage
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let colormapTextureSampler =
        sampler2d {
            texture uniform?ColormapTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    type UniformScope with
        member x.MinValue : float = uniform?MinValue
        member x.MaxValue : float = uniform?MaxValue
        member x.UseFalseColor : bool = uniform?UseFalseColor
        member x.DataType : int = uniform?DataType

    [<ReflectedDefinition>]
    let remap (v : float) = 
        let remappedClampedNormalizedXInt16 =
            ((min uniform.MaxValue (max uniform.MinValue (v * 65000.0))) - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
        let remappedClampedNormalizedXFloat =
            (v - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
        let remapClampNormalize =
            if uniform.UseFalseColor then
                colormapTextureSampler.Sample(V2d ((if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16), 0.0))
            else 
                V4d(
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    1.0
                )
        remapClampNormalize

    let remapInstrumentImage (v : Vertex)  = 
        fragment {
            let instrumentValue = instrumentSampler.Sample(v.tc).X 
            return remap instrumentValue
        }



