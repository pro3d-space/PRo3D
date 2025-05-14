namespace PRo3D.Core


module Shaders =

    open Aardvark.Base
    open Aardvark.Rendering
    open FShade
    open Aardvark.Rendering.Effects

    let colorLayers =
        sampler2dArray {
            texture uniform?ColorLayers
            filter Filter.MinMagPoint
        }

    let depthLayers =
        sampler2dArray {
            texture uniform?DepthLayers
            filter Filter.MinMagPoint
        }
        
    let positionLayers =
        sampler2dArray {
            texture uniform?PositionsLayers
            filter Filter.MinMagPoint
        }

    let hatchingTexture =
        sampler2d {
            texture uniform?HatchingTexture
            addressU WrapMode.Mirror
            addressV WrapMode.Mirror
            filter Filter.MinMagMipLinear
            
        }

    let transferFunction =
        sampler2d {
            texture uniform?TransferFunction
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
            filter Filter.MinMagLinear
            
        }

    type UniformScope with
        member x.ActiveLayer : int = uniform?ActiveLayer
        member x.ActiveLayerCount : int = uniform?ActiveLayerCount

    type UniformScope with
        member x.LensLayer : int = uniform?LensLayer
        member x.LensEnabled : bool = uniform?LensEnabled

    type UniformScope with
        member x.ViewportSize : V2i = uniform?ViewportSize
        member x.SourceFragmentProjInv : M44d = uniform?SourceFragmentProjInv
        member x.SourceFragmentProj : M44d = uniform?SourceFragmentProj
        member x.SourceFragmentViewInv : M44d = uniform?SourceFragmentViewInv
        member x.Sky : V3d = uniform?Sky

    type Vertex = 
        {
            [<FragCoord>] fragCoord : V4d
            [<TexCoord>] tc : V2d
            [<Semantic("ViewPositions2")>] rvp : V4d
            [<Position>] pos : V4d
        }

    let lensMask =
        sampler2d {
            texture uniform?LensMask
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    [<ReflectedDefinition>]
    let isLensClipped (layer : int) (normalizedFragCoord : V2d) = 
        if uniform.LensEnabled && layer < uniform.LensLayer then
            let mask = lensMask.Sample(normalizedFragCoord)
            let clipped = mask.X > 0.99 && mask.Y > 0.99 && mask.Z > 0.99 
            clipped
        else
            false
        
    [<ReflectedDefinition>]
    let getViewPos (normalizedFragCoord : V2d) (depth : float) = 
        let ndc = V3d(normalizedFragCoord * 2.0 - V2d.II, depth * 2.0 - 1.0)
        let p = V4d(ndc.XYZ, 1.0)
        let viewPos = uniform.SourceFragmentProjInv * p
        viewPos.XYZ / viewPos.W

    let writeViewPos (v : Vertex) = 
        vertex {
            return { v with rvp = uniform.ModelViewTrafo * v.pos }
        }

    [<ReflectedDefinition>]
    let isBlocked (comparisonLayer : int) (referenceDepth : float) (viewPos : V3d) (dir : V3d) (searchDistance : float) =
        if comparisonLayer < 1 then false, 0
        else
            let mutable steps = 100
            let dt = dir * 0.2 // +z direction, move to eye
            let mutable currentViewPos = viewPos
            let mutable blocked = false
            while steps > 0 && not blocked do
                steps <- steps - 1
                currentViewPos <- currentViewPos + dt
                let clipPos = uniform.SourceFragmentProj * V4d(currentViewPos, 1.0)
                let ndc = clipPos.XYZ / clipPos.W
                if ndc.X > -1.0 && ndc.X < 1.0 && ndc.Y > -1.0 && ndc.Y < 1.0 && ndc.Z > -1.0 && ndc.Z < 1.0 then
                    let d = depthLayers.SampleLevel(ndc.XY * 0.5 + V2d(0.5,0.5), comparisonLayer, 0.0).X
                    let recoPos = V3d(ndc.XY, d * 2.0 - 1.0)
                    let recoViewPos = uniform.SourceFragmentProjInv * V4d(recoPos, 1.0)
                    if d < referenceDepth && Vec.distance recoViewPos.XYZ viewPos < 10.0  then
                        blocked <- true

            blocked, steps

    [<ReflectedDefinition>]
    let lineAlpha (v : float) (center : float) (lineWidth: float) (lineSmooth : float) = 
        let start = center - lineWidth * 0.5
        let stop = center + lineSmooth * 0.5
        if v >= start && v <= stop then
            1.0
        else
           let alpha = 
                Fun.Smoothstep(v, start - lineSmooth * 0.5, start) -
                Fun.Smoothstep(v, stop, stop + lineSmooth * 0.5)
           alpha

    let composeLayers (v : Vertex) =
        fragment {
            let normalizedFragCoord = v.fragCoord.XY / V2d(float uniform.ViewportSize.X, float uniform.ViewportSize.Y)
            let mutable dstColor = V4d.Zero
            let mutable dstDepth = 1.0
            let mutable dstViewDepth = -10000000.0
            let mutable lastViewPos = V3d.Zero
            let lensMask = lensMask.Sample(normalizedFragCoord)
            let maskThreshold = V3d 0.99
            let activeLens = uniform.LensEnabled && Vec.allGreater lensMask.XYZ maskThreshold
            for layer in 0 .. uniform.ActiveLayerCount - 1 do
                let sourceViewPosBuff = positionLayers.SampleLevel(v.tc, layer, 0.0)
                //let sourceViewDepth = sourceViewPosBuff.Z
                let sourceDepth = depthLayers.SampleLevel(v.tc, layer, 0.0).X
                let sourceColor = V4d(colorLayers.SampleLevel(v.tc, layer, 0.0).XYZ, 1.0)
                
                let viewPos = getViewPos normalizedFragCoord sourceDepth
                let srcViewDepth = viewPos.Z

                let validPixel = sourceDepth < 1.0
                let passDepthTest = sourceDepth <= dstDepth
                let withinDepthTolerance = srcViewDepth  > dstViewDepth 
                let lensDepthOverride = activeLens && layer = uniform.LensLayer
                let skyInViewSpace = (uniform.ViewTrafoInv * V4d(0.0, -1.0, 0.0, 0.0)).XYZ
                let skyInViewSpace = uniform.ViewTrafo * V4d(uniform.Sky, 0.0)
                let blockedByLowerQuality, steps = isBlocked (layer - 1) sourceDepth viewPos.XYZ skyInViewSpace.XYZ.Normalized 2.0
                let maxDistance = 0.0 //10.0
                let lastPosNear = Vec.distance lastViewPos viewPos < maxDistance
                if validPixel && (passDepthTest || lensDepthOverride || withinDepthTolerance) then
                    dstDepth <- sourceDepth
                    dstColor <- sourceColor 
                    dstViewDepth <- srcViewDepth
                    lastViewPos <- viewPos.XYZ
                    //if steps > 0 && blockedByLowerQuality && layer = 2 then dstColor <- V4d((float steps) / 1000.0, 0.0, 0.0, 1.0)
            
                elif validPixel && lastPosNear then
                    dstDepth <- sourceDepth
                    // transparency
                    ////dstColor <- sourceColor * 0.7 + dstColor * 0.3
                    //let world = uniform.SourceFragmentViewInv * V4d(lastViewPos, 1.0)
                    //let hatchUv = world.ZO * V2d(0.1, 0.1)
                    //let t = hatchingTexture.SampleLevel(hatchUv, 0.0).X
                    ////dstColor <- sourceColor * t + dstColor * (1.0 - t)

                    let wp = uniform.SourceFragmentViewInv * V4d(viewPos, 1.0)
                    let lwp = uniform.SourceFragmentViewInv * V4d(lastViewPos, 1.0)

                    let distance = maxDistance / 140.0
                    let my = Vec.distance wp.XYZ lwp.XYZ
                    let my = Vec.dot uniform.Sky (wp.XYZ - lwp.XYZ) |> abs

                    let contourDistance = my % distance
                    let lineAlpha = lineAlpha contourDistance (distance * 0.5) 0.004 0.002
                    let lineColor = (1.0 - lineAlpha) * V4d(0.0,0.0,0.0,1.0)

                    let distanceColor = transferFunction.SampleLevel(V2d(my / (maxDistance * 0.3), 0.5), 0.0)


                    let c = distanceColor * 0.3 + sourceColor * 0.7
                    //dstColor <- c * 0.8 * (1.0 - lineAlpha) + dstColor * 0.2
                    
                    let sourceAlpha = 0.7
                    let sourceAlpha = 0.7
                    dstColor <- sourceColor * sourceAlpha + dstColor * (1.0 - sourceAlpha)

                    dstViewDepth <- srcViewDepth
                    lastViewPos <- viewPos.XYZ

                    

            
            return V4d(dstColor.XYZ, 1.0)
            //let lum = dstDepth
            //let lum = dstViewDepth / 1000.0
            //if dstViewDepth <= 0.00000 then
            //    return V4d.IOOI
            //else
            //    return V4d(lum,lum,lum,1.0)
        }