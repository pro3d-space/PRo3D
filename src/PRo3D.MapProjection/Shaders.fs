namespace PRo3D.MapProjection

open Aardvark.Base
open Aardvark.Rendering
open FShade

/// The map projection on the GPU. Mirrors `Projection` (double precision, CPU), which is
/// what the tests check the rendered pixels against.
///
/// Precision: the body-centred position is formed in float32 (`ModelTrafo * pos`). That
/// breaks the local -> view rule of the 3D viewer on purpose: the panel is only offered for
/// small bodies (`CooTransformation.isSmallBody`), where it costs at most ~1 mm (Phobos,
/// ~11 km). Planets would need a per-patch double anchor plus float32 offsets (#772).
module Shaders =

    type MapVertex =
        {
            [<Position>]              pos : V4f
            [<TexCoord>]              tc  : V2f
            /// (longitude, latitude, radius) — longitude unwrapped per triangle by the geometry
            /// stage, so interpolation never runs across the +-180 degree seam.
            [<Semantic("MapLonLatR")>] llr : V3f
            /// body-centred position (float32, see the module comment); diagnostic output only
            [<Semantic("MapBodyPos")>] bp  : V3f
        }

    type UniformScope with
        /// map space -> NDC, composed in double on the CPU (`Projection.viewProj`)
        member x.MapViewProj      : M44f    = uniform?MapViewProj
        /// radius interval normalised into depth; the outermost surface wins
        member x.MapRadiusRange   : V2f     = uniform?MapRadiusRange
        /// +1 north polar map, -1 south polar map
        member x.MapPolarSign     : float32 = uniform?MapPolarSign
        /// polar maps drop triangles entirely beyond this angular distance from their pole
        member x.MapMaxColatitude : float32 = uniform?MapMaxColatitude

    [<Literal>]
    let private Pi = 3.14159265358979f
    [<Literal>]
    let private TwoPi = 6.28318530717959f
    [<Literal>]
    let private HalfPi = 1.57079632679490f

    [<ReflectedDefinition>]
    let private wrapPi (x : float32) = x - TwoPi * floor ((x + Pi) / TwoPi)

    /// Clip position of a map-space point; depth from the radius (larger radius = nearer).
    [<ReflectedDefinition>]
    let private clip (x : float32) (y : float32) (r : float32) =
        let range = uniform.MapRadiusRange
        let d = clamp 0.0f 1.0f ((r - range.X) / (range.Y - range.X))
        let p = uniform.MapViewProj * V4f(x, y, 0.0f, 1.0f)
        V4f(p.X, p.Y, 1.0f - 2.0f * d, 1.0f)

    /// Body-centred (longitude, latitude, radius). The clip position is written by the
    /// geometry stage, which needs all three corners to handle the seam and the poles.
    let lonLatRadius (v : MapVertex) =
        vertex {
            let p = (uniform.ModelTrafo * v.pos).XYZ
            let r = p.Length
            let lon = atan2 p.Y p.X
            let lat = asin (clamp -1.0f 1.0f (p.Z / r))
            return { v with llr = V3f(lon, lat, r); bp = p }
        }

    /// Equirectangular: x = longitude, y = latitude (radians).
    ///
    /// Longitudes are unwrapped relative to the first corner (`Projection.unwrap`):
    /// - no seam: drawn once;
    /// - crossing +-180 degrees: drawn unwrapped, plus a copy shifted by 2 pi into the map
    ///   (the viewport clips the overhang);
    /// - containing a pole (the unwrapped longitudes wind once around): drawn as a cap — a
    ///   strip from the pole row to the three corners — plus its shifted copy.
    let equirectangular (t : Triangle<MapVertex>) =
        triangle {
            let a = t.P0.llr
            let b = t.P1.llr
            let c = t.P2.llr
            let dab = wrapPi (b.X - a.X)
            let dbc = wrapPi (c.X - b.X)
            let dca = wrapPi (a.X - c.X)
            let winding = dab + dbc + dca
            let xa = a.X
            let xb = a.X + dab
            let xc = xb + dbc

            if abs winding < Pi then
                let minX = min xa (min xb xc)
                let maxX = max xa (max xb xc)

                yield { t.P0 with pos = clip xa a.Y a.Z; llr = V3f(xa, a.Y, a.Z) }
                yield { t.P1 with pos = clip xb b.Y b.Z; llr = V3f(xb, b.Y, b.Z) }
                yield { t.P2 with pos = clip xc c.Y c.Z; llr = V3f(xc, c.Y, c.Z) }
                restartStrip()

                if maxX > Pi || minX < -Pi then
                    let s = if maxX > Pi then -TwoPi else TwoPi
                    yield { t.P0 with pos = clip (xa + s) a.Y a.Z; llr = V3f(xa + s, a.Y, a.Z) }
                    yield { t.P1 with pos = clip (xb + s) b.Y b.Z; llr = V3f(xb + s, b.Y, b.Z) }
                    yield { t.P2 with pos = clip (xc + s) c.Y c.Z; llr = V3f(xc + s, c.Y, c.Z) }
                    restartStrip()
            else
                let poleY = if a.Y + b.Y + c.Y > 0.0f then HalfPi else -HalfPi
                let xa2 = xa + winding
                // the cap spans [xa, xa + winding]; the copy brings the part outside [-pi, pi] back in
                let s = if winding > 0.0f then -TwoPi else TwoPi
                // one strip zig-zagging between the pole row and the corners A, B, C, A+winding;
                // written out twice (unshifted, shifted) -- FShade does not generate a geometry
                // stage for a `for` loop around these yields ("input has conflicting types")
                yield { t.P0 with pos = clip xa poleY a.Z; llr = V3f(xa, poleY, a.Z) }
                yield { t.P0 with pos = clip xa a.Y a.Z; llr = V3f(xa, a.Y, a.Z) }
                yield { t.P1 with pos = clip xb poleY b.Z; llr = V3f(xb, poleY, b.Z) }
                yield { t.P1 with pos = clip xb b.Y b.Z; llr = V3f(xb, b.Y, b.Z) }
                yield { t.P2 with pos = clip xc poleY c.Z; llr = V3f(xc, poleY, c.Z) }
                yield { t.P2 with pos = clip xc c.Y c.Z; llr = V3f(xc, c.Y, c.Z) }
                yield { t.P0 with pos = clip xa2 poleY a.Z; llr = V3f(xa2, poleY, a.Z) }
                yield { t.P0 with pos = clip xa2 a.Y a.Z; llr = V3f(xa2, a.Y, a.Z) }
                restartStrip()
                yield { t.P0 with pos = clip (xa + s) poleY a.Z; llr = V3f(xa + s, poleY, a.Z) }
                yield { t.P0 with pos = clip (xa + s) a.Y a.Z; llr = V3f(xa + s, a.Y, a.Z) }
                yield { t.P1 with pos = clip (xb + s) poleY b.Z; llr = V3f(xb + s, poleY, b.Z) }
                yield { t.P1 with pos = clip (xb + s) b.Y b.Z; llr = V3f(xb + s, b.Y, b.Z) }
                yield { t.P2 with pos = clip (xc + s) poleY c.Z; llr = V3f(xc + s, poleY, c.Z) }
                yield { t.P2 with pos = clip (xc + s) c.Y c.Z; llr = V3f(xc + s, c.Y, c.Z) }
                yield { t.P0 with pos = clip (xa2 + s) poleY a.Z; llr = V3f(xa2 + s, poleY, a.Z) }
                yield { t.P0 with pos = clip (xa2 + s) a.Y a.Z; llr = V3f(xa2 + s, a.Y, a.Z) }
                restartStrip()
        }

    /// Polar stereographic (`Projection.polar`). Triangles entirely beyond the cutoff
    /// colatitude are dropped, and so is anything near the opposite pole, where rho runs to
    /// infinity. No seam: longitude only enters through sin/cos.
    let polarStereographic (t : Triangle<MapVertex>) =
        triangle {
            let sign = uniform.MapPolarSign
            let a = t.P0.llr
            let b = t.P1.llr
            let c = t.P2.llr
            let ca = HalfPi - sign * a.Y
            let cb = HalfPi - sign * b.Y
            let cc = HalfPi - sign * c.Y
            let nearest  = min ca (min cb cc)
            let farthest = max ca (max cb cc)

            if nearest <= uniform.MapMaxColatitude && farthest < 0.9f * Pi then
                // unwrapped longitudes keep the interpolated MapLonLatR continuous
                let xb = a.X + wrapPi (b.X - a.X)
                let xc = xb + wrapPi (c.X - b.X)
                let ra = 2.0f * tan (0.5f * ca)
                let rb = 2.0f * tan (0.5f * cb)
                let rc = 2.0f * tan (0.5f * cc)
                yield { t.P0 with pos = clip (ra * sin a.X) (-sign * ra * cos a.X) a.Z; llr = V3f(a.X, a.Y, a.Z) }
                yield { t.P1 with pos = clip (rb * sin b.X) (-sign * rb * cos b.X) b.Z; llr = V3f(xb, b.Y, b.Z) }
                yield { t.P2 with pos = clip (rc * sin c.X) (-sign * rc * cos c.X) c.Z; llr = V3f(xc, c.Y, c.Z) }
                restartStrip()
        }

    /// Test/diagnostic output: the interpolated body-centred position as the colour, for a
    /// float target. Deliberately not `llr`: the geometry stage derives the clip position from
    /// llr, so llr agrees with the pixel by construction; the position does not.
    let bodyPositionColor (v : MapVertex) =
        fragment {
            return V4f(v.bp, 1.0f)
        }

    /// Graticule and other map-space line work: positions are map-space already.
    let mapSpaceLine (v : Effects.Vertex) =
        vertex {
            return { v with pos = uniform.MapViewProj * V4f(v.pos.X, v.pos.Y, 0.0f, 1.0f) }
        }

    let private projectionStage (kind : MapProjectionKind) =
        match kind with
        | MapProjectionKind.Equirectangular -> toEffect equirectangular
        | _                                  -> toEffect polarStereographic

    /// Surface effect of the map: body lon/lat/radius -> map, then the OPC texture.
    let surfaceEffect (kind : MapProjectionKind) =
        Effect.compose [
            toEffect lonLatRadius
            projectionStage kind
            toEffect PRo3D.Base.OPCFilter.improvedDiffuseTexture
        ]

    /// Same geometry, but writes the body-centred position instead of the texture.
    let bodyPositionEffect (kind : MapProjectionKind) =
        Effect.compose [
            toEffect lonLatRadius
            projectionStage kind
            toEffect bodyPositionColor
        ]

    let graticuleEffect =
        Effect.compose [
            toEffect mapSpaceLine
            toEffect DefaultSurfaces.vertexColor
        ]
