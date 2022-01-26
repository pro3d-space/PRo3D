namespace Aardvark.Opc

module EstimateGpuMemory =

    open System
    open Aardvark.Base
    open Aardvark.Rendering.SceneGraph.HierarchicalLoD
    open Aardvark.Rendering
    open Aardvark.SceneGraph.Opc
    open Aardvark.Opc

    module VertexGeometry = 

        let private sizes = 
            // maps to common backend sizes
            Dictionary.ofList [
                typeof<V2d>, sizeof<float32> * 2
                typeof<V2f>, sizeof<float32> * 2
                typeof<V3f>, sizeof<float32> * 3
                typeof<V3d>, sizeof<float32> * 3
                typeof<V4f>, sizeof<float32> * 4
                typeof<V4d>, sizeof<float32> * 4
            ]
    

        let estimateSizeInBytes (vg : IndexedGeometry) = 
            let indexSize = if vg.IsIndexed then (vg.IndexArray |> unbox<int[]>).Length * sizeof<int> else 0
            let attributes = 
                vg.IndexedAttributes |> Seq.sumBy (fun attribute ->
                    attribute.Value.Length * sizes.[attribute.Value.GetType().GetElementType()]
                )
            indexSize + attributes

    let estimateHierarchy (runtime : IRuntime) (paths : OpcPaths) (hierarchy : PatchHierarchy) = 

        let rec flattenLeaves (t : QTree<Patch>) = 
            match t with
            | QTree.Leaf patch -> Seq.singleton patch
            | QTree.Node(path, children) -> 
                children |> Seq.collect flattenLeaves 

        let estimateSize (p : Patch) = 
            let geometry = 
                try 
                    Patch.load paths ViewerModality.XYZ p.info |> Result.Ok
                with e -> 
                    Log.warn "could not load patch %A" p.info
                    Result.Error e

            let texture = 
                let texturePath = 
                    Patch.extractTexturePath paths p.info 0
                try 
                    Loaders.loadTexture true false runtime texturePath |> Result.Ok
                with e -> 
                    Log.warn "could not texture for patch %A" p.info
                    Result.Error e

            match geometry, texture with
            | Result.Ok (geometry, _), Result.Ok (texture,textureSizeInBytes,dispose) -> 
                let geometrySize = VertexGeometry.estimateSizeInBytes geometry
                dispose.Dispose()
                textureSizeInBytes + geometrySize
            | _ -> 
                0
        
        let estimatedMemory = flattenLeaves hierarchy.tree |> Seq.sumBy estimateSize

        estimatedMemory