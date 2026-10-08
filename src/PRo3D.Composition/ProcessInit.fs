namespace PRo3D.Composition

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Core
open PRo3D.Core.Surface
open OpcViewer.Base
open Aardvark.GeoSpatial.Opc.Load

/// Process-global setup every PRo3D host needs before it loads a surface. These are
/// module-level mutables in Core/Base (render runner, picklers, rendering switches), so they
/// are set once per process - one app per process is the supported configuration.
module ProcessInit =

    let private serializationLock = obj()
    let mutable private serializationDone = false

    /// Serialization registry + the KdTree picklers. Safe to call more than once.
    let initSerialization () =
        lock serializationLock (fun () ->
            if not serializationDone then
                HeadlessPicking.initKdTreeLoading ()
                serializationDone <- true
        )

    /// Rendering globals: sparse buffers off (driver workaround), the GL load runner the OPC
    /// scene graph needs (`Sg.createSgSurfaces` fails without it) and the annotation renderer.
    let initRuntime (runtime : IRuntime) (packedAnnotationRendering : bool) =
        Aardvark.Rendering.GL.RuntimeConfig.SuppressSparseBuffers <- true
        PRo3D.Core.Drawing.DrawingApp.usePackedAnnotationRendering <- packedAnnotationRendering
        Sg.hackRunner <- runtime.CreateLoadRunner 1 |> Some
