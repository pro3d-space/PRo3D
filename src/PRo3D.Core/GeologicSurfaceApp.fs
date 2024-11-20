namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Base
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI

open Aardvark.UI.Primitives
open Aardvark.Rendering
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.SceneGraph.Assimp
open Aardvark.SceneGraph.SgPrimitives
//open Aardvark.Base.Rendering
open Aardvark.UI.Trafos  
open PRo3D.Core.Surface
open PRo3D.Core.Drawing
open OpcViewer.Base

open System
open System.IO
open System.Diagnostics

open Aardvark.UI.Primitives

open PRo3D.Base
//open CSharpUtils

module GeologicSurfacesUtils = 

    let calcMiddlePart 
        (start : int) 
        (stop : int) 
        (points1 : IndexList<V3d>) 
        (points2 : IndexList<V3d>) 
        (color : C4b) =
        [
            for i in start..stop do
                yield Triangle3d(points1.[i], points1.[i+1], points2.[i-start]), color
                yield Triangle3d(points1.[i+1], points2.[i-start], points2.[i-start + 1]), color
        ]

    let calcFirstPart 
        (stop : int) 
        (points1 : IndexList<V3d>) 
        (points2 : IndexList<V3d>) 
        (color : C4b) =
        [
            for i in 0..stop do
                yield Triangle3d(points1.[i], points1.[i+1], points2.[0]), color
        ]

    let calcLastPart 
        (start : int) 
        (stop : int) 
        (points1 : IndexList<V3d>) 
        (points2 : IndexList<V3d>) 
        (color : C4b) =
        [
            for i in start..stop do
                yield Triangle3d(points1.[i], points1.[i+1], points2.[points2.Count-1]), color
        ]

    let calculateMesh 
        (points1 : IndexList<V3d>) 
        (points2 : IndexList<V3d>) 
        (color : C4b) = 
        
            let diff = points1.Count - points2.Count
            if diff % 2 = 0 then 
                let plus = diff/2
                let firstPart =
                    [
                        for i in 0..(plus-1) do
                            yield Triangle3d(points1.[i], points1.[i+1], points2.[0]), color
                    ]
                let endMiddlePart = points1.Count - (plus + 2)
                let middlePart = 
                    [
                        for i in plus..endMiddlePart do
                            yield Triangle3d(points1.[i], points1.[i+1], points2.[i-plus]), color
                            yield Triangle3d(points1.[i+1], points2.[i-plus], points2.[i-plus + 1]), color
                    ]
                let endPart =
                    [
                        for i in (endMiddlePart+1)..(points1.Count - 2) do
                            yield Triangle3d(points1.[i], points1.[i+1], points2.[points2.Count-1]), color
                    ]

                firstPart@middlePart@endPart

            else
                let plusFirst = Math.Ceiling((float)diff/2.0)
                let plusEnd = diff/2
                let firstPart =
                    [
                        for i in 0..((int)plusFirst-1) do
                            yield Triangle3d(points1.[i], points1.[i+1], points2.[0]), color
                    ]

                let endMiddlePart = points1.Count - (plusEnd + 2)
                let middlePart = 
                    [
                        for i in (int)plusFirst..endMiddlePart do
                            yield Triangle3d(points1.[i], points1.[i+1], points2.[i-(int)plusFirst]), color
                            yield Triangle3d(points1.[i+1], points2.[i-(int)plusFirst], points2.[i-(int)plusFirst + 1]), color
                    ]
                let endPart =
                    if plusEnd > 0 then
                        [
                            for i in endMiddlePart..(points1.Count - 2) do
                                yield Triangle3d(points1.[i], points1.[i+1], points2.[points2.Count-1]), color
                        ]
                    else []

                firstPart@middlePart@endPart

    let getTrianglesForMesh 
        (points1 : IndexList<V3d>) 
        (points2 : IndexList<V3d>) 
        (color : C4b) 
        (alpha : float) =
        let colorAlpha = C4b(color.R, color.G, color.B, (byte)alpha) //color.ToC4d() |> fun x -> C4d(x.R, x.G, x.B, alpha).ToC4b()
        let triangles =
            if points1.Count > points2.Count then
                calculateMesh points1 points2 colorAlpha
            else if points1.Count = points2.Count then
                calcMiddlePart 0 (points1.Count-2) points1 points2 colorAlpha
            else
                calculateMesh points2 points1 colorAlpha

        triangles
    
    let invertMeshing (surf : GeologicSurface) =
        let points2' = surf.points2 |> IndexList.rev
        let triangles' = 
                getTrianglesForMesh 
                    surf.points1 
                    points2' 
                    surf.color.c
                    surf.transparency.value
        { surf with points2 = points2'; sgGeoSurface = triangles'}

    let calcFlytoView (points : IndexList<V3d>) (refSys : ReferenceSystem) =
        let v3dArray = points.AsList |> List.toArray 
        let plane = PlaneFitting.planeFit(v3dArray)
        let dir = -plane.Normal
        let dist = 
            points
            |> IndexList.toList
            |> List.pairwise
            |> List.map (fun (a,b) -> Vec.Distance(a,b))
            |> List.sum 
        let pos = points.[0] - (dist*0.5) * dir
        CameraView.lookAt pos dir refSys.up.value 
    
    let mk  (name : string) 
            (points1 : IndexList<V3d> )
            (points2 : IndexList<V3d> ) 
            (thickness : float)
            (view : CameraView) = 
            let triangles = getTrianglesForMesh points1 points2 C4b.Cyan 127.0
            //let view = calcFlytoView (points1 |> IndexList.append points2) refSys
            {
                version         = GeologicSurface.current
                guid            = Guid.NewGuid()
                name            = name
               
                isVisible       = true
                view            = view 

                points1         = points1  
                points2         = points2  

                color           = { c = C4b.Cyan }
                transparency    = InitGeologicSurfacesParams.transparency
                thickness       = InitGeologicSurfacesParams.thickness (thickness+2.0)

                invertMeshing   = false
                sgGeoSurface    = triangles //Sg.empty
            }

    let makeGeologicSurfaceFromAnnotations 
        (annotations : GroupsModel) 
        (model : GeologicSurfacesModel) =
        
        let selection = annotations.selectedLeaves
        if selection.Count = 2 then
            let ids = 
                selection 
                |> HashSet.map(fun x -> x.id)
                |> HashSet.toList
                
            let geoSurf = 
                match (annotations.flat.TryFind ids.[0]), (annotations.flat.TryFind ids.[1]) with
                | Some (Leaf.Annotations ann1), Some (Leaf.Annotations ann2) ->
                    Some (mk 
                            ("mesh"+ model.geologicSurfaces.Count.ToString())
                            ann1.points
                            ann2.points
                            ann1.thickness.value
                            ann1.view )
                | _,_-> None

            match geoSurf with
            | Some gs ->
                { model with geologicSurfaces = model.geologicSurfaces |> HashMap.add gs.guid gs; 
                             selectedGeologicSurface = Some gs.guid }
            | None -> model
        else
            failwith "select two annotations"
            model

    
module GeologicSurfaceProperties =        

    type Action =
        | SetName           of string
        | ToggleVisible 
        | SetThickness      of Numeric.Action
        | SetTransparency   of Numeric.Action
        | ChangeColor       of ColorPicker.Action
        | InvertMeshing
        | HomePosition     

    let update (model : GeologicSurface) (view : CameraView) (act : Action) =
        match act with
        | SetName s ->
            { model with name = s }
        | ToggleVisible ->
            { model with isVisible = not model.isVisible }
        | SetThickness a ->
            { model with thickness = Numeric.update model.thickness a}
        | SetTransparency a ->
            let trans = Numeric.update model.transparency a
            let col = model.color.c
            let alphaCol = C4b(col.R, col.G, col.B, (byte)trans.value) 
            let geoSurf = 
                model.sgGeoSurface
                |> List.map(fun tri -> fst(tri), alphaCol)
            { model with transparency = trans; sgGeoSurface = geoSurf}
        | ChangeColor a ->
            let col = ColorPicker.update model.color a
            let alphaCol = C4b(col.c.R, col.c.G, col.c.B, (byte)model.transparency.value) 
            let geoSurf = 
                model.sgGeoSurface
                |> List.map(fun tri -> fst(tri), alphaCol)
            { model with color = col; sgGeoSurface = geoSurf }
        | InvertMeshing ->
            let m = model |> GeologicSurfacesUtils.invertMeshing
            { m with invertMeshing = not model.invertMeshing }
        | HomePosition -> 
             { model with view = view }
          
    let view (model : AdaptiveGeologicSurface) =        
      require GuiEx.semui (
        Html.table [               
          Html.row "Name:"          [Html.SemUi.textBox model.name SetName ]
          Html.row "Visible:"       [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
          Html.row "Outline thickness:"     [Numeric.view' [NumericInputType.Slider]   model.thickness  |> UI.map SetThickness ]
          Html.row "Transparency:"  [Numeric.view' [NumericInputType.Slider]   model.transparency  |> UI.map SetTransparency ]
          Html.row "Color:"         [ColorPicker.view model.color |> UI.map ChangeColor ]
          Html.row "Invert meshing:"       [GuiEx.iconCheckBox model.invertMeshing InvertMeshing ]
          Html.row "Set Homeposition:"  [button [clazz "ui button tiny"; onClick (fun _ -> HomePosition )] []]
        ]
      )
 
type GeologicSurfaceAction =
    | FlyToGS               of Guid
    | RemoveGS              of Guid
    | IsVisible             of Guid
    | SelectGS              of Guid
    | AddGS                 
    | PropertiesMessage     of GeologicSurfaceProperties.Action

module GeologicSurfacesApp = 

    let update 
        (view : CameraView)
        (model : GeologicSurfacesModel) 
        (act : GeologicSurfaceAction) = 

        match act with
        | IsVisible id ->
            let geologicSurfaces =  
                model.geologicSurfaces 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible })
            { model with geologicSurfaces = geologicSurfaces }
        | RemoveGS id -> 
            let selGS = 
                match model.selectedGeologicSurface with
                | Some so -> if so = id then None else Some so
                | None -> None

            let geologicSurfaces = HashMap.remove id model.geologicSurfaces
            { model with geologicSurfaces = geologicSurfaces; selectedGeologicSurface = selGS }
        
        | SelectGS id ->
            let so = model.geologicSurfaces |> HashMap.tryFind id
            match so, model.selectedGeologicSurface with
            | Some a, Some b ->
                if a.guid = b then 
                    { model with selectedGeologicSurface = None }
                else 
                    { model with selectedGeologicSurface = Some a.guid }
            | Some a, None -> 
                { model with selectedGeologicSurface = Some a.guid }
            | None, _ -> model
        | PropertiesMessage msg ->  
            match model.selectedGeologicSurface with
            | Some id -> 
                let geoSurf = model.geologicSurfaces |> HashMap.tryFind id
                match geoSurf with
                | Some gs ->
                    let geoSurf = (GeologicSurfaceProperties.update gs view msg)
                    let geologicSurfaces = model.geologicSurfaces |> HashMap.alter gs.guid (function | Some _ -> Some geoSurf | None -> None )
                    { model with geologicSurfaces = geologicSurfaces} 
                | None -> model
            | None -> model
        |_-> model


    module UI =

        let viewHeader (m:AdaptiveGeologicSurface) (gsid:Guid) toggleMap = 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon"; onClick (fun _ -> FlyToGS gsid)] []
                |> UI.wrapToolTip DataPosition.Bottom "Fly to geologic surface" 

                Incremental.i toggleMap AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                i [clazz "Remove icon red"; onClick (fun _ -> RemoveGS gsid)] [] 
                |> UI.wrapToolTip DataPosition.Bottom "Remove"     
            ]    


        let viewGeologicSurfaces
            (m : AdaptiveGeologicSurfacesModel) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedGeologicSurface
                    let geologicSurfaces = m.geologicSurfaces |> AMap.toASetValues |> ASet.toAList// (fun a -> )
        
                    for gs in geologicSurfaces do
            
                        let infoc = sprintf "color: %s" (Html.color C4b.White)
            
                        let! scbid = gs.guid  
                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") gs.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible scbid)
                            } |> AttributeMap.ofAMap  

                       
                        let color =
                            match selected with
                              | Some sel -> 
                                AVal.constant (if sel = (gs.guid |> AVal.force) then C4b.VRVisGreen else C4b.Gray) 
                              | None -> AVal.constant C4b.Gray

                        let headerText = 
                            AVal.map (fun a -> sprintf "%s" a) gs.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectGS scbid)
                            } 
                            |> AttributeMap.ofAMap
            
                        let! c = color
                        let bgc = sprintf "color: %s" (Html.color c)
                        yield div [clazz "item"; style infoc] [
                            div [clazz "content"; style infoc] [                     
                                yield Incremental.div (AttributeMap.ofList [style infoc])(
                                    alist {
                                        //let! hc = headerColor
                                        yield div [clazz "header"; style bgc] [
                                            Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                         ]                
                                        //yield i [clazz "large cube middle aligned icon"; style bgc; onClick (fun _ -> SelectSO soid)][]           
            
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToGS scbid)] []
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to geologic surface"          
            
                                        yield Incremental.i toggleMap AList.empty 
                                        |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveGS scbid)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                       
                                    } 
                                )                                     
                            ]
                        ]
                } )

        let viewProperties (model:AdaptiveGeologicSurfacesModel) =
            adaptive {
                let! guid = model.selectedGeologicSurface
                let empty = div [style "font-style:italic"] [text "no geologic surface selected" ] |> UI.map PropertiesMessage 
                
                match guid with
                | Some id -> 
                    let! gs = model.geologicSurfaces |> AMap.tryFind id
                    match gs with
                    | Some s -> return (GeologicSurfaceProperties.view s |> UI.map PropertiesMessage)
                    | None -> return empty
                | None -> return empty
            } 
            
        let addMesh = 
            div [clazz "ui buttons inverted"] [
                       //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                           button [clazz "ui icon button"; onMouseClick (fun _ -> AddGS )] [ //
                                   i [clazz "plus icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "calculate surface"
                      // )
                   ] 

    module StencilAreaMasking =
   
      let private writeZFailFront, writeZFailBack = 
          let front = 
            { StencilMode.None with
                DepthFail = StencilOperation.DecrementWrap
                CompareMask = StencilMask 0xff }

          let back = { front with DepthFail = StencilOperation.IncrementWrap }
          front, back
      

      let private readMaskAndReset = 
         { StencilMode.None with
             Comparison = ComparisonFunction.NotEqual
             CompareMask = StencilMask 0xff
             Pass = StencilOperation.Zero
             DepthFail = StencilOperation.Zero
             Fail = StencilOperation.Zero
             Reference = 1
         }
     
      let maskSG maskPass sg = 
        sg
          |> Sg.pass maskPass
          |> Sg.stencilModes' writeZFailFront writeZFailBack
          |> Sg.depthTest (AVal.init DepthTest.None)
          |> Sg.cullMode (AVal.init CullMode.None)
          |> Sg.blendMode (AVal.init BlendMode.Blend)
          |> Sg.fillMode (AVal.init FillMode.Fill)
          |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Stencil])

      let fillSG areaPass sg =
        sg
          |> Sg.pass areaPass
          |> Sg.stencilMode (AVal.constant (readMaskAndReset))
          //|> Sg.cullMode (Mod.constant CullMode.CounterClockwise)  // for zpass -> backface-culling
          |> Sg.depthTest (AVal.constant DepthTest.Less)        // for zpass -> active depth-test
          //|> Sg.depthTest (AVal.init DepthTest.None)
          |> Sg.cullMode (AVal.init CullMode.None)
          |> Sg.blendMode (AVal.init BlendMode.Blend)
          //|> Sg.fillMode (AVal.init FillMode.Fill)
          |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Color DefaultSemantic.Colors; WriteBuffer.Stencil])

      let stencilAreaSG pass1 pass2 sg =
        [
          maskSG pass1 sg   // one pass by using EXT_stencil_two_side :)
          fillSG pass2 sg
        ] |> Sg.ofList

     
      let colorAlpha (color:aval<C4b>) (alpha:aval<float>) : aval<V4f> = 
          AVal.map2 (fun (c:C4b) a -> c.ToC4f() |> fun x -> C4f(x.R, x.G, x.B, float32 a).ToV4f()) color alpha

    module Sg =

        let getMeshOutline 
            (points1 : alist<V3d>) 
            (points2 : alist<V3d>) 
            (thickness : aval<float>) =
            let posTrafo1 = points1 |> AList.toAVal |> AVal.map (fun list -> Trafo3d.Translation list.[0])
            let posTrafo2 = points2 |> AList.toAVal |> AVal.map (fun list -> Trafo3d.Translation list.[0])
            let posTrafo3 = points1 |> AList.toAVal |> AVal.map (fun list -> Trafo3d.Translation list.[list.Count - 1])

            let pts1 = points1 |> AList.toAVal |> AVal.map (fun list -> list|> IndexList.toArray)
            let pts2 = points2 |> AList.toAVal |> AVal.map (fun list -> list|> IndexList.toArray)

            let l1 = AVal.map2(fun (list1:IndexList<V3d>) (list2:IndexList<V3d>) -> 
                                [|list1.[0]; list2.[0]|]) (points1 |> AList.toAVal) (points2 |> AList.toAVal)
            let l2 = AVal.map2(fun (list1:IndexList<V3d>) (list2:IndexList<V3d>) -> 
                                [|list1.[list1.Count-1]; list2.[list2.Count-1]|]) (points1 |> AList.toAVal) (points2 |> AList.toAVal)
           
            Sg.ofList [
                Sg.drawLines pts1 (AVal.constant 0.0) (C4b.VRVisGreen |> AVal.constant) thickness posTrafo1
                Sg.drawLines pts2 (AVal.constant 0.0) (C4b.VRVisGreen |> AVal.constant) thickness posTrafo2
                Sg.drawLines l1  (AVal.constant 0.0) (C4b.VRVisGreen |> AVal.constant) thickness posTrafo1
                Sg.drawLines l2 (AVal.constant 0.0) (C4b.VRVisGreen |> AVal.constant) thickness posTrafo3
            ]
            

        let viewSingleGeologicSurface 
            (geoSurface : AdaptiveGeologicSurface) 
            (id : Guid) 
            (selected : aval<Option<Guid>>) =

           
            adaptive {
                
                let! selected' = selected
                let selected =
                    match selected' with
                    | Some sel -> sel = id
                    | None -> false
                
                let! triangles = geoSurface.sgGeoSurface
                let surf =
                    triangles
                        |> Aardvark.SceneGraph.IndexedGeometryPrimitives.triangles 
                        |> Sg.ofIndexedGeometry
                        |> Sg.effect [
                          toEffect DefaultSurfaces.stableTrafo
                          toEffect DefaultSurfaces.vertexColor
                        ] 

                let selectionSg = 
                    if selected then
                        getMeshOutline
                            geoSurface.points1
                            geoSurface.points2
                            geoSurface.thickness.value
                                
                        //let outline = 
                        //    triangles
                        //    |> Aardvark.SceneGraph.IndexedGeometryPrimitives.triangles 
                        //    |> Sg.ofIndexedGeometry
                        //OutlineEffect.createForSg 2 (RenderPass.after "" RenderPassOrder.Arbitrary RenderPass.main) C4f.VRVisGreen outline
                    else Sg.empty

                return Sg.ofList [
                    selectionSg //|> Sg.dynamic
                    surf 
                ] |> Sg.onOff geoSurface.isVisible
               
            } |> Sg.dynamic

        let view (geologicSurfacesModel : AdaptiveGeologicSurfacesModel) =
            
            let geologicSurfaces = geologicSurfacesModel.geologicSurfaces
            let selected = geologicSurfacesModel.selectedGeologicSurface

            let mutable maskPass1 = (RenderPass.after "" RenderPassOrder.Arbitrary RenderPass.main)
            let mutable maskPass = (RenderPass.after "" RenderPassOrder.Arbitrary maskPass1)
            let mutable areaPass = RenderPass.after "" RenderPassOrder.Arbitrary maskPass

            let test =
                geologicSurfaces |> AMap.map( fun id geoSurf ->
                    let surf = 
                        viewSingleGeologicSurface
                            geoSurf
                            id
                            selected
                        |> StencilAreaMasking.stencilAreaSG maskPass areaPass

                    maskPass <- RenderPass.after "" RenderPassOrder.Arbitrary areaPass
                    areaPass <- RenderPass.after "" RenderPassOrder.Arbitrary maskPass

                    surf
                    )
                    |> AMap.toASet 
                    |> ASet.map snd 
                    |> Sg.set
                  

            test//, RenderPass.after "" RenderPassOrder.Arbitrary areaPass  
    
    