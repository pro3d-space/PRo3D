namespace PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base

open Chiron

open Aether
open Aether.Operators

#nowarn "0686"

type CrossSectionGeometry =
    | LineOnSurface of array<V3d>

type CrossSection = {
    geometry : CrossSectionGeometry
    refPoint : V3d
}

[<ModelType>]
type CrossSectionModel = {
    crossSection          : Option<CrossSection>
    curtainEnabled        : bool
    curtainTexturePath    : Option<string>
    curtainExtrusionDepth : NumericInput
    curtainAbsoluteMode   : bool
    curtainTargetAltitude : NumericInput
    curtainTextureDepth          : NumericInput
    curtainTextureStartAltitude  : NumericInput
    curtainBaseColor             : ColorInput
}

module CrossSectionModel =

    let initial : CrossSectionModel = {
        crossSection          = None
        curtainEnabled        = false
        curtainTexturePath    = None
        curtainExtrusionDepth = {
            value = 100.0; min = 1.0; max = 10000.0
            step = 10.0; format = "{0:0}"
        }
        curtainAbsoluteMode   = false
        curtainTargetAltitude = {
            value = 0.0; min = -10000.0; max = 100000.0
            step = 10.0; format = "{0:0}"
        }
        curtainTextureDepth = {
            value = 50.0; min = 1.0; max = 10000.0
            step = 10.0; format = "{0:0}"
        }
        curtainTextureStartAltitude = {
            value = 0.0; min = -10000.0; max = 100000.0
            step = 10.0; format = "{0:0}"
        }
        curtainBaseColor = { c = C4b.Gray }
    }

module CrossSectionGeometry =
    let toJson (g : CrossSectionGeometry) =
        match g with
        | LineOnSurface pts ->
            json {
                do! Json.write "type" "LineOnSurface"
                do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (pts |> Array.toList)
            }

    let fromJson =
        json {
            let! t = Json.read "type"
            match t with
            | "LineOnSurface" ->
                let! pts = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
                return LineOnSurface (pts |> Array.ofList)
            | other ->
                return! other |> sprintf "unknown CrossSectionGeometry type %A" |> Json.error
        }

type CrossSectionGeometry with
    static member ToJson (x : CrossSectionGeometry) = CrossSectionGeometry.toJson x
    static member FromJson (_ : CrossSectionGeometry) = CrossSectionGeometry.fromJson

type CrossSection with
    static member FromJson (_ : CrossSection) =
        json {
            let! geometry = Json.read "geometry"
            let! refPoint = Json.read "refPoint"
            return { geometry = geometry; refPoint = refPoint |> V3d.Parse }
        }
    static member ToJson (x : CrossSection) =
        json {
            do! Json.write "geometry" x.geometry
            do! Json.write "refPoint" (x.refPoint.ToString())
        }

type CrossSectionModel with
    static member FromJson (_ : CrossSectionModel) =
        json {
            let! crossSection          = Json.tryRead "crossSection"
            let! curtainEnabled        = Json.tryRead "curtainEnabled"
            let! curtainTexturePath    = Json.tryRead "curtainTexturePath"
            let! curtainAbsoluteMode   = Json.tryRead "curtainAbsoluteMode"
            let! curtainExtrusionDepth = Json.readWith Ext.fromJson<NumericInput,Ext> "curtainExtrusionDepth"
            let! curtainTargetAltitude = Json.readWith Ext.fromJson<NumericInput,Ext> "curtainTargetAltitude"
            let initial = CrossSectionModel.initial
            let! curtainTextureDepthOpt = Json.tryRead "curtainTextureDepth"
            let! curtainTextureDepth =
                match curtainTextureDepthOpt with
                | Some (_ : Chiron.Json) -> Json.readWith Ext.fromJson<NumericInput,Ext> "curtainTextureDepth"
                | None -> json { return initial.curtainTextureDepth }
            let! curtainTextureStartAltOpt = Json.tryRead "curtainTextureStartAltitude"
            let! curtainTextureStartAltitude =
                match curtainTextureStartAltOpt with
                | Some (_ : Chiron.Json) -> Json.readWith Ext.fromJson<NumericInput,Ext> "curtainTextureStartAltitude"
                | None -> json { return initial.curtainTextureStartAltitude }
            let! curtainBaseColorOpt = Json.tryRead "curtainBaseColor"
            let! curtainBaseColor =
                match curtainBaseColorOpt with
                | Some (_ : Chiron.Json) -> Json.readWith Ext.fromJson<ColorInput,Ext> "curtainBaseColor"
                | None -> json { return initial.curtainBaseColor }

            return {
                crossSection          = crossSection          |> Option.flatten
                curtainEnabled        = curtainEnabled        |> Option.defaultValue initial.curtainEnabled
                curtainTexturePath    = curtainTexturePath    |> Option.flatten
                curtainExtrusionDepth = curtainExtrusionDepth
                curtainAbsoluteMode   = curtainAbsoluteMode   |> Option.defaultValue initial.curtainAbsoluteMode
                curtainTargetAltitude = curtainTargetAltitude
                curtainTextureDepth   = curtainTextureDepth
                curtainTextureStartAltitude = curtainTextureStartAltitude
                curtainBaseColor      = curtainBaseColor
            }
        }
    static member ToJson (x : CrossSectionModel) =
        json {
            do! Json.write "crossSection"          x.crossSection
            do! Json.write "curtainEnabled"        x.curtainEnabled
            do! Json.write "curtainTexturePath"    x.curtainTexturePath
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "curtainExtrusionDepth" x.curtainExtrusionDepth
            do! Json.write "curtainAbsoluteMode"   x.curtainAbsoluteMode
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "curtainTargetAltitude" x.curtainTargetAltitude
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "curtainTextureDepth"  x.curtainTextureDepth
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "curtainTextureStartAltitude" x.curtainTextureStartAltitude
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>)   "curtainBaseColor"     x.curtainBaseColor
        }
