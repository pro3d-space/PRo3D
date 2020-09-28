namespace PRo3D.Linking

open Aardvark.Base

open System
open Aardvark.UI
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Rendering.Text 
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Application

open PRo3D.Minerva
open FShade
open OpcViewer.Base
open Aardvark.UI
open Aardvark.UI.Primitives

module LinkingApp =

    // in Aardvark.Base.FSharp/Datastructures/Geometry/Boundable.fs as well as KdTreeFinds.fs (in both cases private)
    let toHull3d (viewProj : Trafo3d) =
        let r0 = viewProj.Forward.R0
        let r1 = viewProj.Forward.R1
        let r2 = viewProj.Forward.R2
        let r3 = viewProj.Forward.R3

        let inline toPlane (v : V4d) =
            Plane3d(-v.XYZ, v.W)

        Hull3d [|
            r3 - r0 |> toPlane  // right
            r3 + r0 |> toPlane  // left
            r3 + r1 |> toPlane  // bottom
            r3 - r1 |> toPlane  // top
            r3 + r2 |> toPlane  // near
            //r3 - r2 |> toPlane  // far
        |]

    /// called once at initialization time, takes minerva features
    /// and populates the linking model with used feature representations
    let initFeatures (features: IndexList<Feature>) (m: LinkingModel) : LinkingModel =

        // sensor sizes
        let mastcamRLSensor = V2i(1600, 1200)

        let instrumentParameter = HashMap.ofList [
            // https://msl-scicorner.jpl.nasa.gov/Instruments/Mastcam/
            (Instrument.MastcamL, { horizontalFoV = 15.0; sensorSize = mastcamRLSensor }) //34 mm
            (Instrument.MastcamR, { horizontalFoV = 5.1; sensorSize = mastcamRLSensor }) // 100 mm

            // https://msl-scicorner.jpl.nasa.gov/Instruments/ChemCam/
            (Instrument.ChemLib, { horizontalFoV = 0.000000001; sensorSize = V2i.One})
            (Instrument.ChemRmi, { horizontalFoV = 0.005729578; sensorSize = V2i(1024, 1024)})
            // The detector is a 1024 x 1024 pixel CCD. The RMI has a field of view of 19 milliradians. 
            // Due to optimization of the telescope for LIBS, the RMI resolution is not pixel-limited, and is approximately 100 microradians. 
            
            // https://msl-scicorner.jpl.nasa.gov/Instruments/MAHLI/
            (Instrument.MAHLI, { horizontalFoV = 34.0; sensorSize = V2i(1600, 1200)}) // f/9.8 and 34° to f/8.5 and 39.4°

            // https://msl-scicorner.jpl.nasa.gov/Instruments/APXS/
            (Instrument.APXS, { horizontalFoV = 0.000000001; sensorSize = V2i.One})

            // https://an.rsl.wustl.edu/mer/help/Content/About%20the%20mission/MSL/Instruments/MSL%20Hazcam.htm
            (Instrument.FrontHazcamL, { horizontalFoV = 124.0; sensorSize = V2i(1024, 1024)}) // 124 degree x 124 degree
            (Instrument.FrontHazcamR, { horizontalFoV = 124.0; sensorSize = V2i(1024, 1024)}) // 1024 x 1024

        ]
        
        // only interested in products of known instruments
        let reducedFeatures = features.Filter (fun _ f -> instrumentParameter.ContainsKey f.instrument)

        // creating frustums by specifying fov 
        let createFrustumProj (p: InstrumentParameter) =
            let aspectRatio = (float p.sensorSize.X) / (float p.sensorSize.Y)
            let fov = p.horizontalFoV * aspectRatio
            let frustum = Frustum.perspective fov 0.01 15.0 aspectRatio
            let fullFrustum = Frustum.perspective fov 0.01 1000.0 aspectRatio
            let proj = Frustum.projTrafo(frustum)
            (proj, proj.Inverse, fullFrustum, p.sensorSize)

        let frustumData =
            instrumentParameter
            |> HashMap.map (fun _ v ->
                createFrustumProj(v)
            )

        let angleToRad = V3d(Math.PI / 180.0) * V3d(1.0,1.0,2.0)
        
        let originTrafo = 
            match reducedFeatures.TryGet 0 with
            | Some v -> Trafo3d.Translation v.geometry.positions.Head
            | None -> Trafo3d.Identity

        let originTrafoInv = originTrafo.Backward

        // map minerva features to linking features
        let linkingFeatures : HashMap<string, LinkingFeature> = 
            reducedFeatures.Map (fun _ f ->

                let position = originTrafoInv.TransformPos(f.geometry.positions.Head)
                let angles = f.geometry.coordinates.Head

                let frustumTrafo, frustumTrafoInv, fullFrustum, sensorSize = 
                    frustumData
                    |> HashMap.tryFind f.instrument
                    |> Option.defaultValue (Trafo3d.Scale 0.0, Trafo3d.Scale 0.0, Frustum.ofTrafo Trafo3d.Identity, V2i.One) // ignored
             
                let rotation = Rot3d.FromAngleAxis(angles * angleToRad)
                let translation = Trafo3d.Translation position

                let innerRot = Trafo3d.Rotation(V3d.OOI, -angles.Z * Math.PI / 180.0)

                let rotTranslateTrafo = innerRot * Trafo3d(rotation) * translation
                let trafo = frustumTrafoInv * rotTranslateTrafo
                let trafoInv = rotTranslateTrafo.Inverse * frustumTrafo

                let hull = trafoInv |> toHull3d 

                let imageOffset =
                    match f.instrument with
                    | Instrument.MastcamL -> V2i(305 + 48, 385) // ATTENTION/TODO hardcoded data value, replace with database!
                    | _ -> (sensorSize - f.dimensions) / 2 // TODO: hardcoded center

                (f.id, {
                    id = f.id
                    hull = hull
                    position = position
                    rotation = rotation
                    trafo = trafo
                    trafoInv = trafoInv
                    camTrafo = originTrafo.Inverse * rotTranslateTrafo.Inverse
                    camFrustum = fullFrustum
                    instrument = f.instrument
                    imageDimensions = f.dimensions
                    imageOffset = imageOffset
                })
            )
            |> IndexList.toList
            |> HashMap.ofList

        let filterProducts =
            instrumentParameter
            |> HashMap.keys
            |> HashSet.toSeq
            |> Seq.map (fun k -> k,true)
            |> HashMap.ofSeq 

        { m with frustums = linkingFeatures; trafo = originTrafo; instrumentParameter = instrumentParameter; filterProducts = filterProducts }

    /// intersects given point (p) with all frustums specified by their id from "filtered"
    /// emits linking action and minerva action which have to be delivered to their respective models
    /// by upper level app
    let checkPoint (p: V3d) (filtered: HashSet<string>) (m: LinkingModel) : (LinkingAction * MinervaAction) =

        let originP = m.trafo.Backward.TransformPos p

        let intersected =
            filtered
            |> HashSet.toSeq
            |> Seq.map (fun s -> s, HashMap.tryFind s m.frustums)
            |> HashMap.ofSeq
            // TODO v5: rebecca: review this
            |> HashMap.choose (fun _ v -> match v with | None -> None | Some v -> if v.hull.Contains originP then Some v else None) 

        let currentInstruments =
            intersected
            |> HashMap.values
            |> HashSet.ofSeq
            |> HashSet.map (fun p -> p.instrument)
            
        let filterProducts =
            m.filterProducts
            |> HashMap.map (fun k v -> if HashSet.contains k currentInstruments then true else v)

        let linkingAction = LinkingAction.UpdatePickingPoint (Some(originP), filterProducts)
        let minervaAction = MinervaAction.SelectByIds (intersected |> HashMap.keys |> HashSet.toList)
        (linkingAction, minervaAction)


    //---UPDATE
    let rec update (m: LinkingModel) (msg: LinkingAction) : LinkingModel =
            
        match msg with
        | ToggleView i ->
            match m.filterProducts.TryFind i with
            | Some b -> 
                { m with filterProducts = m.filterProducts.Add (i, not b) }
            | None -> m

        | UpdatePickingPoint (pos, filterProducts) -> { m with pickingPos = pos; filterProducts = filterProducts }
        | OpenFrustum f -> { m with overlayFeature = Some(f) }  // also handled by upper app
        | CloseFrustum -> { m with overlayFeature = None }
        | ChangeFrustumOpacity v -> { m with frustumOpacity = v }
        | _ -> failwith "Not implemented yet"


    //---Helpers

    /// resource dependencies for linking view
    let dependencies =
        Html.semui @ [
            { kind = Stylesheet; name = "linkingstyle.css"; url = "./resources/linkingstyle.css" }
        ]

    /// takes C4b color and returns string color value useable by css
    let cssColor (c: C4b) =
        sprintf "rgba(%d, %d, %d, %f)" c.R c.G c.B c.Opacity

    /// takes insrtument and returns string color value useable by css
    let instrumentColor (i: Instrument) =
        i |> MinervaModel.instrumentColor |> cssColor

    /// takes LinkingFeature and returns fitting image source path
    let imageSrc (f: LinkingFeature) =
        sprintf "MinervaData/%s.png" (f.id.ToLower())

    /// takes two float tuples as well as a color and a storke width and returns an svg line object
    let svgLine (p1: float * float) (p2: float * float) (color: string) (storkeWidth: float) =
        let (x1, y1) = p1
        let (x2, y2) = p2
        Svg.line[
            attribute "x1" (string x1)
            attribute "y1" (string y1)
            attribute "x2" (string x2)
            attribute "y2" (string y2)
            attribute "stroke" color
            attribute "stroke-width" (string storkeWidth)
        ]
        
    /// takes two V2ds as well as a color and a storke width and returns an svg line object
    let svgLine' (p1: V2d) (p2: V2d) (color: C4b) (storkeWidth: float) =
        svgLine (p1.X, p1.Y) (p2.X, p2.Y) (color |> cssColor) storkeWidth


    //---VIEWS

    /// main view function (3d view) 
    /// hoveredFrustum: if Some, visualizes hovered frustum in a thicker line style
    /// selectedFrustums: set of frustum ids that will be rendered in the 3d view
    let view (hoveredFrustum: aval<Option<SelectedProduct>>) (selectedFrustums: aset<string>) (m: AdaptiveLinkingModel) =

        /// helper function creating frustum box for given LinkingFeature
        let sgFrustum (f: LinkingFeature) =
            Sg.wireBox' (f.instrument |> MinervaModel.instrumentColor) (Box3d(V3d.NNN,V3d.III))
            |> Sg.noEvents
            |> Sg.transform f.trafo

        // frustum that is currently hovered
        let hoverFrustum =
            hoveredFrustum
            |> AVal.bind (fun f -> 
                f
                |> Option.map (fun x -> m.frustums |> AVal.map (fun f -> f |> HashMap.tryFind x.id))
                |> Option.defaultValue (AVal.constant None)
            )
            |> AVal.map (fun f -> f |> Option.defaultValue { LinkingFeature.initial with trafo = Trafo3d.Scale 0.0 })  
            |> AVal.map (fun x -> 
                x 
                |> sgFrustum
                |> Sg.shader {
                    do! DefaultSurfaces.stableTrafo
                    do! DefaultSurfaces.vertexColor
                    do! DefaultSurfaces.thickLine
                    do! DefaultSurfaces.thickLineRoundCaps
                }
                |> Sg.uniform "LineWidth" (AVal.constant 5.0)
            )
            |> Sg.dynamic

        // selected frustums
        let frustra =
            selectedFrustums
            |> ASet.chooseA (fun s -> m.frustums |> AVal.map (fun f -> f |> HashMap.tryFind s))
            |> ASet.map sgFrustum 
            |> Sg.set
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.vertexColor
            }

        // point on the opc that was picked
        let pickingIndicator =
            Sg.sphere 3 (AVal.constant C4b.VRVisGreen) (AVal.constant 0.05)
            |> Sg.noEvents
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.vertexColor
            }
            |> Sg.trafo (
                m.pickingPos 
                |> AVal.map (fun p -> 
                    p 
                    |> Option.map Trafo3d.Translation 
                    |> Option.defaultValue (Trafo3d.Scale 0.0)
                )
            )

        // scene with frustums (hidden when in overlay mode)
        let defaultScene = 
            [|
                frustra
                hoverFrustum
            |]
            |> Sg.ofArray 

        // scene that is always shown (in both normal 3d and overlay mode)
        let commonScene =
            [|
                pickingIndicator
            |]
            |> Sg.ofArray 

        let scene = 
            [|
                (
                    m.overlayFeature 
                    |> AVal.map(fun o ->
                        match o with
                        | None -> defaultScene
                        | Some _ -> Sg.empty) // featureScene
                    |> Sg.dynamic
                )
                commonScene
            |]
            |> Sg.ofArray 
            |> Sg.trafo m.trafo

        scene

    /// side bar view containing controls for overlay mode
    let viewSideBar (m: AdaptiveLinkingModel) =

        let modD = 
            m.overlayFeature
            |> AVal.map (fun od -> 
                match od with
                | None -> (None, None)
                | Some(d) -> 
                    let before = 
                        d.before.TryGet (d.before.Count - 1) // get last element of before list (before f)
                        |> Option.map (fun f -> 
                            { 
                                before = d.before.Remove f
                                f = f
                                after = d.after.Prepend d.f
                            }
                        )

                    let after =
                        d.after.TryGet 0 // get first element of after list (after f)
                        |> Option.map (fun f -> 
                            {
                                before = IndexList.add d.f d.before // TODO v5: rebecca - check this
                                f = f
                                after = d.after.Remove f
                            }
                        )

                    (before, after)
            )

        require dependencies (
            div [][
                Incremental.div 
                    (AttributeMap.ofAMap (amap { 
                        let! d = m.overlayFeature
                        if d.IsNone then
                            yield style "display: none;"
                    }))
                    (AList.ofList [
                        Incremental.div AttributeMap.empty (alist {
                            let! df = m.overlayFeature

                            match df with
                            | Some(d) -> 
                                let f = d.f
                                yield table[style "color: white;" ][
                                    tr[][ 
                                        td[][ text "ID:" ]
                                        td[][ text f.id ]
                                    ]
                                    tr[][ 
                                        td[][ text "Instrument:" ]
                                        td[style (sprintf "color: %s;" (f.instrument |> instrumentColor))]
                                            [ text (string f.instrument) ]
                                    ]
                                    tr[][ 
                                        td[][ text "Image Size:" ]
                                        td[][ text (sprintf "%d x %d" f.imageDimensions.X f.imageDimensions.Y) ]
                                    ]
                                ]
                            | None -> ()
                        })

                        slider 
                            {min = 0.0; max = 1.0; step = 0.01} 
                            [clazz "ui blue slider"]
                            m.frustumOpacity
                            ChangeFrustumOpacity

                        div [clazz "inverted fluid ui buttons"] [
                            Incremental.button (AttributeMap.ofAMap (amap {
                                let classString = "inverted labeled ui icon button"
                                
                                let! (before, _) = modD
                                match before with
                                | Some(d) -> 
                                    yield onClick (fun _ -> (LinkingAction.OpenFrustum d))
                                    yield clazz classString
                                | None -> yield clazz (classString + " disabled")

                            })) (AList.ofList [
                                i [clazz "caret left icon"][]
                                text "Previous"
                            ])
                            Incremental.button (AttributeMap.ofAMap (amap {
                                let classString = "inverted right labeled ui icon button"
                                                           
                                let! (_, after) = modD
                                match after with
                                | Some(d) -> 
                                    yield onClick (fun _ -> (LinkingAction.OpenFrustum d))
                                    yield clazz classString
                                | None -> yield clazz (classString + " disabled")

                            })) (AList.ofList [
                                i [clazz "caret right icon"][]
                                text "Next"
                            ])
                        ]

                        button [clazz "fluid inverted ui button"; onClick (fun _ -> LinkingAction.CloseFrustum)][
                            i [clazz "close icon"][]
                            text "Close"
                        ]
                    ])
            ]
        )

    /// html scene overlay for the 3d scene, showing the selected product
    let sceneOverlay (m: AdaptiveLinkingModel) : DomNode<LinkingAction> =

        let overlayDom (f: LinkingFeature, dim: V2i) : DomNode<LinkingAction> =

            let offset = f.imageOffset //let border = (dim - V2d(f.imageDimensions)) * 0.5
            let sensor = 
                m.instrumentParameter
                |> AMap.tryFind f.instrument 
                |> AVal.map (fun i -> 
                    i 
                    |> Option.map (fun o -> o.sensorSize) 
                    |> Option.defaultValue V2i.One
                )

            let frustumRect = [
                attribute "x" (string offset.X) //border.x
                attribute "y" (string offset.Y) // border.y
                attribute "width" (string f.imageDimensions.X)
                attribute "height" (string f.imageDimensions.Y)
            ]

            div [clazz "ui scene-overlay"] [
                Incremental.Svg.svg (AttributeMap.ofAMap (amap {
                    yield clazz "frustum-svg"
                    yield attribute "id" "frustum-overlay-svg"
                    yield style (sprintf "border-color: %s" (instrumentColor f.instrument))

                    let! s = sensor
                    yield attribute "viewBox" (sprintf "0 0 %d %d" s.X s.Y)
                })) (AList.ofList [
                    Svg.path [
                        attribute "fill" "rgba(0,0,0,0.5)"
                        attribute "d" (sprintf "M0 0 h%d v%d h-%dz M%d %d v%d h%d v-%dz"
                            dim.X dim.Y dim.X offset.X offset.Y f.imageDimensions.Y f.imageDimensions.X f.imageDimensions.Y)
                    ]
                    DomNode.Node ("g", "http://www.w3.org/2000/svg",
                        AttributeMap.ofAMap (amap {
                            let! a = m.frustumOpacity
                            yield attribute "opacity" (string a)
                        }),
                        AList.ofList [
                            Svg.image (frustumRect @ [
                                attribute "href" (imageSrc f)
                            ])
                        ]
                    )
                ])
            ]

        let dom =
            m.overlayFeature
            |> AVal.bind (fun op -> 
                op 
                |> Option.map (fun d -> 
                    m.instrumentParameter 
                    |> AMap.tryFind d.f.instrument
                    |> AVal.map (fun ip -> (d.f, ip))
                )
                |> Option.defaultValue (AVal.constant (LinkingFeature.initial, None))
            )
            |> AVal.map(fun (f: LinkingFeature, s: Option<InstrumentParameter>) ->
                match s with
                | None -> div[][] // DomNode.empty requires unit?
                | Some(o) -> overlayDom (f, o.sensorSize)
            )
            |> AList.ofAValSingle
            |> Incremental.div AttributeMap.empty
            
        require dependencies dom

    /// horizontal "film strip" showing the images of the products given in selectedFrustums
    let viewHorizontalBar (selectedFrustums: aset<string>) (m: AdaptiveLinkingModel) =
        
        // getting LinkingFeature from given ids
        let products =
            selectedFrustums
            |> ASet.chooseA (fun s -> m.frustums |> AVal.map (fun f -> f |> HashMap.tryFind s))

        // applying filter from checkbox-labels in film strip (single insrtument toggles)
        let filteredProducts =
            products
            |> ASet.filterA (fun p -> 
                m.filterProducts
                |> AMap.tryFind p.instrument
                |> AVal.map (fun m -> m |> Option.defaultValue false))

        // getting the projected picking point position in image space for every product
        let productsAndPoints =
            filteredProducts
            |> ASet.map(fun prod ->
                m.pickingPos 
                |> AVal.map(fun f -> 
                    f 
                    |> Option.defaultValue V3d.Zero
                    |> fun p -> V4d(p, 1.0)
                    |> prod.trafoInv.Forward.Transform
                    |> fun p -> V3d((p.XY / p.W), p.Z)
                )
                |> AVal.map(fun pp -> (prod, pp))
            )
            |> ASet.mapA id

        // counts per instrument for display in the toggle buttons
        let countStringPerInstrument =
            products
            |> ASet.groupBy (fun f -> f.instrument)
            |> AMap.map (fun _ v -> v.Count)
            |> AMap.toASet
            |> ASet.toAList
            |> AList.sortBy (fun (i, _) -> i)
            |> AList.map (fun (i, c) -> 
                let s = sprintf "%A: %d" i c
                (i, AVal.constant s)
            )

        let fullCount = products |> ASet.count 
        let filteredCount = filteredProducts |> ASet.count
        let countString =
            AVal.map2 (fun full filtered -> 
                if full = filtered then
                    full |> string
                else 
                    (sprintf "%d (%d)" full filtered)
            ) fullCount filteredCount

        require dependencies (
            body [style "width: 100%; height:100%; background: transparent; min-width: 0; min-height: 0"] [
                div[clazz "noselect"; style "color:white; padding: 5px; width: 100%; height: 100%; position: absolute;"][
                    div[style "padding: 5px; position: fixed"][
                        span[clazz "ui label inverted"; style "margin-right: 10px; width: 10em; position: relative;"][
                            text "Products"
                            div[clazz "detail"; style "position: absolute; right: 1em;"][Incremental.text countString]
                        ]
                        Incremental.span AttributeMap.Empty (
                            countStringPerInstrument
                            |> AList.map (fun (i, s) ->
                                let o = AMap.tryFind i m.filterProducts |> AVal.map (fun b -> b |> Option.defaultValue false)
                                a[clazz "instrument-toggle"; onClick (fun _ -> ToggleView i)][
                                    span[clazz "ui inverted label";
                                        style (sprintf "background-color: %s;" (instrumentColor i))][
                                        //Html.SemUi.iconCheckBox o (ToggleView i)
                                        Html.SemUi.iconCheckBox o (ToggleView Instrument.NotImplemented) //checkbox [clazz "ui inverted checkbox"] o (ToggleView i) s
                                        Incremental.text s
                                    ]
                                ]
                            )
                        )
                        ]

                    Incremental.div (AttributeMap.ofList[style "overflow-x: scroll; overflow-y: hidden; white-space: nowrap; height: 100%; padding-top: 2.8em;"]) (
                        let sortedProducts = 
                            productsAndPoints
                            |> ASet.toAList
                            |> AList.map (fun (f, p) ->
                                // check if inside image!
                                m.instrumentParameter 
                                |> AMap.tryFind f.instrument 
                                |> AVal.map (Option.map (fun par ->

                                    let sensor = V2d(par.sensorSize)
                                    let image = V2d(f.imageDimensions)

                                    let max = image / sensor // ratio is inside
                                        
                                    f, p, (image, sensor, max)
                                ))
                            )
                            |> AList.chooseA id
                            |> AList.sortBy (fun (f, p, (image, sensor, max)) ->
                                let ratioP = p.XY * V2d(1.0, sensor.Y / sensor.X)
                                let dist = Vec.Dot(ratioP, ratioP) // euclidean peseudo distance (w/o root)

                                if abs(p.X) > max.X || abs(p.Y) > max.Y
                                then infinity
                                else dist
                            )
                        
                        // if you have a cleaner way of doing this I would be so happy to know it
                        let neighborList =
                            sortedProducts 
                            |> AList.toAVal
                            |> AVal.map (fun l -> 
                                l
                                |> IndexList.mapi (fun i e -> 
                                    let onlyFrustra = l |> IndexList.map(fun a -> 
                                        let (f, _, _) = a
                                        f
                                    )
                                    let before, w, after = IndexList.split i onlyFrustra // TODO find out what is w
                                    (before, e, after)                             
                                )
                            )
                            |> AList.ofAVal
                            
                        neighborList
                        |> AList.map (fun (before, (f, p, (image, sensor, max)), after) -> 
                        
                            let webSrc = imageSrc f
                            
                            let (w, h) = (f.imageDimensions.X, f.imageDimensions.Y)
                            let c = (p.XY * V2d(1.0, -1.0)) // flip y

                            let cc = c / max // correct for max
                            
                            let ratio = (float h)/(float w)
                            let invRatio = 1.0/ratio

                            let rc = cc * V2d(invRatio, 1.0)

                            Incremental.div (AttributeMap.ofAMap (amap {
                                yield style (sprintf "border-color: %s" (instrumentColor f.instrument))
                                yield onClick (fun _ -> OpenFrustum { before = before; f = f; after = after })
                                yield onMouseEnter (fun _ -> MinervaAction (HoverProduct (Some { id = f.id; pos = f.position })))
                                yield onMouseLeave (fun _ -> MinervaAction (HoverProduct None))

                                let! selected = m.overlayFeature

                                match selected with
                                | Some d when d.f = f -> yield clazz "product-view selected"
                                | _ -> yield clazz "product-view"

                            })) (AList.ofList [
                                div[
                                    clazz "selection-box"
                                ][]
                                img[
                                    clazz f.id; 
                                    attribute "alt" f.id; 
                                    attribute "src" webSrc;
                                ]
                                Svg.svg[attribute "viewBox" (sprintf "%f -1 %f 2" (-invRatio) (invRatio * 2.0))
                                ][
                                    Svg.circle[
                                        attribute "cx" (sprintf "%f" rc.X)
                                        attribute "cy" (sprintf "%f" rc.Y)
                                        attribute "r" "1.0"
                                        attribute "fill" "transparent"
                                        attribute "stroke" (C4b.VRVisGreen |> cssColor)
                                        attribute "stroke-width" "0.03"
                                    ]
                                    svgLine (0.0, -1.0) (0.0, 1.0) "black" 0.01 // vertical line
                                    svgLine (-invRatio, 0.0) (invRatio, 0.0) "black" 0.01 // horizontal line
                                    svgLine (0.0, 0.0) (rc.X, rc.Y) "black" 0.01 // direction line
                                ]
                                text f.id
                                div [clazz "product-indexed"; style "position: absolute; bottom: 1.2em"][
     (* ༼つಠ益ಠ༽つ ─=≡ΣO) *)        text (string (before.Count + 1)) // geologists probably start their indices at 1
                                ]
                            ])
                        )
                    )
                ]
            ]
        )
        