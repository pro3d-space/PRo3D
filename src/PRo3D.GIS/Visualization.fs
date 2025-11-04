namespace PRo3D.InstrumentProjection

open System
open System.IO

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.SceneGraph
open Aardvark.Rendering.Text
open Aardvark.Geometry
open Aardvark.FontProvider


open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.SPICE
open PRo3D.Core
open PRo3D.Core.InstrumentMetadata
open Aardvark.PixImage.LibTiff
open PRo3D.InstrumentData

type Self = Self

module Visualization =

    let createProjectedTexture (currentProjectedImage : aval<Option<string>>) : aval<ITexture> =
        currentProjectedImage 
        |> AVal.bind (fun img -> 
            match img with
            | Some img -> 
                match MultiBandReader.tryReadMultiBandTiff img false with
                | Result.Ok img -> 
                    let images = InstrumentImageTextures.instrumentImageToTexture true img 
                    match Array.tryItem 0 images with
                    | Some img -> 
                        PixTexture2d(img.pi, TextureParams.empty) :> ITexture |> AVal.constant
                    | _ -> 
                        Log.warn "channel of out of bounds"
                        DefaultTextures.checkerboard
                | _ -> 
                    Log.warn "could not load texture"
                    DefaultTextures.checkerboard
            | _ -> 
                DefaultTextures.checkerboard
        )

