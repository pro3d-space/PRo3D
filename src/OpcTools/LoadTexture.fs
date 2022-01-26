namespace Aardvark.Opc

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.GL
open System.IO
open System
open Aardvark.Prinziple

module Loaders = 

    let private isZipped path = 
      let split = path |> Path.GetFullPath |> Prinziple.splitPath
      split.IsSome

    let loadTexture (generateMipmaps : bool) (useCompressed : bool) (runtime : IRuntime) (texturePath : string) =  
        let config = { wantMipMaps = true; wantSrgb = false; wantCompressed = false }
        if useCompressed then
            let t, dispose = CompressedImage.load runtime texturePath config false
            t, 0, dispose
        else 
            let size (pi : PixImage) = float pi.Size.X * float pi.Size.Y * 4.0 * (if generateMipmaps then 1.33 else 1.0) |> int
            if texturePath |> isZipped then                  
                use s = Prinziple.openRead (texturePath |> Path.GetFullPath)
                let p = PixImage.Create s
                let t = PixTexture2d(PixImageMipMap [| p |], generateMipmaps) :> ITexture
                let prep = runtime.PrepareTexture t
                prep :> ITexture, size p, { new IDisposable with member x.Dispose() = runtime.DeleteTexture prep }                   
            else
                // read from stream to escape devil lock (a bit)
                use s = new FileStream(texturePath, FileMode.Open)
                use t = new MemoryStream(int s.Length)
                s.CopyTo(t)
                t.Seek(0L,SeekOrigin.Begin) |> ignore
                let pi = PixImageDevil.CreateRawDevil(t)
                let t = PixTexture2d(PixImageMipMap [| pi |], generateMipmaps) :> ITexture
                let prep = runtime.PrepareTexture(t)
                prep :> ITexture, size pi, { new IDisposable with member x.Dispose() = runtime.DeleteTexture(prep) }