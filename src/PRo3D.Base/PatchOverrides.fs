  
namespace Aardvark.Data.Opc

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.Opc

type Patch = Aardvark.SceneGraph.Opc.Patch

module Patch =
   let private tryFindTex (extensions : string list) (path : string) =
        extensions |> List.tryPick (fun ext ->
            let current = System.IO.Path.ChangeExtension(path, ext)
            if Prinziple.fileExists current then Some current else None
        )

   let tryExtractTexturePath (opcPaths : OpcPaths) (patchInfo : PatchFileInfo) (texNumber : int) =
        let t = patchInfo.Textures |> List.item texNumber
        let fn = t.fileName.Replace('\\',System.IO.Path.DirectorySeparatorChar)
        let sourcePath = opcPaths.Images_DirAbsPath +/ fn
        // attribute name is the folder containing the per-patch texture file
        // (fn is typically "<AttributeName>/<PatchName>.<ext>")
        let attributeName =
            match System.IO.Path.GetDirectoryName(fn) with
            | null | "" -> System.IO.Path.GetFileNameWithoutExtension(fn)
            | d -> d
        match tryFindTex [ ".dds"; ".tif"; ".tiff"; ".exr"] sourcePath with
        | Some t -> Some (t, attributeName)
        | _      -> None

   let extractTexturePath (opcPaths : OpcPaths) (patchInfo : PatchFileInfo) (texNumber : int) =
        match tryExtractTexturePath opcPaths patchInfo texNumber with
        | Some (path, _) -> path
        | None ->
            let t = patchInfo.Textures |> List.item texNumber
            let fn = t.fileName.Replace('\\',System.IO.Path.DirectorySeparatorChar)
            failwithf "texture not found: %s" (opcPaths.Images_DirAbsPath +/ fn)
