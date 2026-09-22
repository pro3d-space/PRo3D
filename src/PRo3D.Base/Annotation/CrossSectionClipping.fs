namespace PRo3D.Base.Annotation

open Aardvark.Base

module CrossSection =

    type ProjectionBasis2d = { x: V3d; y: V3d; n: V3d }

    /// Build orthonormal 2D basis from up vector (plane normal).
    let buildBasisFromUp (up : V3d) : ProjectionBasis2d =
        let n =
            if up.LengthSquared < 1e-30 then V3d.OOI else up.Normalized

        let x0 =
            let c = Vec.cross n V3d.OOI
            if c.LengthSquared < 1e-12 then Vec.cross n V3d.IOO else c

        let x = x0.Normalized
        let y = (Vec.cross n x).Normalized
        { x = x; y = y; n = n }

    /// Project 3D point to 2D via dot products with basis vectors.
    let projectTo2d (basis : ProjectionBasis2d) (v : V3d) : V2d =
        V2d(Vec.dot v basis.x, Vec.dot v basis.y)

    /// Build closed polygon from annotation points + ref point, ensure CCW winding.
    let buildPolygon (basis : ProjectionBasis2d) (points : V3d[]) (refPoint : V3d) : Polygon2d option =
        if points.Length < 2 then
            None
        else
            let pts2d =
                points
                |> Array.map (projectTo2d basis)
                |> Array.append [| projectTo2d basis refPoint |]

            // compute signed area to determine winding
            let mutable area = 0.0
            for i = 0 to pts2d.Length - 1 do
                let p = pts2d.[i]
                let q = pts2d.[(i + 1) % pts2d.Length]
                area <- area + (p.X * q.Y - q.X * p.Y)

            // ensure CCW
            let pts2d =
                if area < 0.0 then pts2d |> Array.rev else pts2d

            try Some (Polygon2d pts2d)
            with _ -> None
