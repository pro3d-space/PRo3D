//----------------------------------------------------------------

// Quick & Dirty setup to test ellipse drawing as used in PRo3D.
// Use in F# interactive and run the `run()` function.
// Click to set up to 3 control points, then the ellipse is drawn.

// Prerequisites:
//   dotnet add package SFML.Net
//   Install matching native CSFML libraries.
//----------------------------------------------------------------
#r "nuget: SFML.Graphics, 2.6.1"
#r "nuget: Aardvark.Base.FSharp, 5.3.17"

open System
open SFML.System
open SFML.Window
open SFML.Graphics
open Aardvark.Base

// load the ellipse implementation code
#load "../PRo3D.Core/Drawing/EllipseConstruction.fs"
open PRo3D.Core.Drawing

let toSfml (v : V2d) : Vector2f =
    Vector2f(float32 v.X, float32 v.Y)

let ofSfml (v : Vector2f) : V2d =
    V2d(float v.X, float v.Y)



/// Simpler compute function: straight parametric from control tris.
let computeEllipsePoints
    (p0 : V2d)
    (p1 : V2d)
    (p2 : V2d)
    (samples : int)
    : V2d[] =

    let center = (p0 + p1) * 0.5
    let major  = p1 - center
    let minor  = p2 - center

    Array.init (samples + 1) (fun i ->
        let t = 2.0 * Math.PI * (float i / float samples)
        let c = cos t
        let s = sin t
        center + major * c + minor * s
    )


let run() =
    let window = new RenderWindow(
        VideoMode(800u, 600u),
        "Interactive Ellipse Drawer"
    )
    window.SetFramerateLimit 60u
    window.Closed.Add(fun _ -> window.Close())

    // Keep control points and current ellipse in V2d
    let points    = new System.Collections.Generic.List<V2d>()
    let mutable ellipsePts : V2d[] = [||]

    // Mouse click handler: accumulate up to 3 points, then recompute
    window.MouseButtonPressed.Add(fun e ->
        if e.Button = Mouse.Button.Left then
            if points.Count < 3 then
                points.Add (V2d(float e.X, float e.Y))
            else
                points.Clear()
                points.Add (V2d(float e.X, float e.Y))

            if points.Count = 3 then
                let ellipse = EllipseConstruction.constructEllipseOrtho2d points.[0] points.[1] points.[2]
                ellipsePts <-
                    EllipseConstruction.computeEllipsePoints ellipse 100
    )

    // Render loop
    while window.IsOpen do
        window.DispatchEvents()
        window.Clear Color.White

        // Draw each control point as a red circle
        for p in points do
            use circle = new CircleShape(6.0f)
            circle.FillColor <- Color.Red
            // center the SFML shape at the V2d location
            circle.Position <- toSfml (p - V2d(6.0, 6.0))
            window.Draw circle

        // Draw the ellipse in blue
        if ellipsePts.Length > 0 then
            let count = uint32 (ellipsePts.Length + 1)
            let va = new VertexArray(PrimitiveType.LineStrip, count)

            ellipsePts
            |> Array.iteri (fun i v ->
                va.[uint32 i] <- Vertex(toSfml v, Color.Blue)
            )

            // close loop
            va.[uint32 ellipsePts.Length] <- Vertex(toSfml ellipsePts.[0], Color.Blue)
            window.Draw va

        window.Display()

    0