  
namespace Aardvark.Data.Opc

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.Opc

type Patch = Aardvark.SceneGraph.Opc.Patch

module Patch =
   let extractTexturePath (opcPaths : OpcPaths) (patchInfo : PatchFileInfo) (texNumber : int) =
        let t = patchInfo.Textures |> List.item texNumber
        let fn = t.fileName.Replace('\\',System.IO.Path.DirectorySeparatorChar)
        let sourcePath = opcPaths.Images_DirAbsPath +/ fn
        let extensions = [ ".dds"; ".tif"; ".tiff"; ".exr"]

        let rec tryFindTex exts path =
            match exts with
            | x::xs ->
                let current = System.IO.Path.ChangeExtension(path,x)
                if Prinziple.fileExists current then current else tryFindTex xs path

            | [] ->
                failwithf "texture not found: %s" path

        tryFindTex extensions sourcePath
