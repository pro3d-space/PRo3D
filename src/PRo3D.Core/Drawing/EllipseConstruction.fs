namespace PRo3D.Core.Drawing

open Aardvark.Base

module EllipseConstruction =
 
    /// Given
    ///   • p0,p1 – the two ends of the major axis
    ///   • p2    – a point whose projection onto the minor‐axis direction
    ///             sets the signed semi‐minor length
    /// Returns (center, semiMajorVec, semiMinorVec),
    /// where semiMajorVec·semiMinorVec = 0.

    let constructEllipseOrtho2d
        (p0 : V2d)   // major axis end
        (p1 : V2d)   // major axis end
        (p2 : V2d)   // point controlling minor axis length 
        : Ellipse2d =

        // 1) center & raw semi-major
        let center   = (p0 + p1) * 0.5
        let majorRaw = p1 - center

        // 2) normalize for unit direction
        let majorLen = majorRaw.Length
        if majorLen = 0.0 then
            invalidArg "p0,p1" "Major axis length must be non-zero."
        let majorDir = majorRaw / majorLen

        // 3) perpendicular for minor direction
        let minorDir = V2d(-majorDir.Y, majorDir.X)

        // 4) project third point onto minorDir
        let rawMinor = p2 - center
        let minorLen = Vec.dot rawMinor minorDir

        // 5) semi-axes vectors
        let majorVec = majorDir * majorLen
        let minorVec = minorDir * minorLen

        Ellipse2d(center, majorVec, minorVec)


    /// Compute ellipse points by sampling the returned Ellipse2d.
    let computeEllipsePoints
        (ellipse : Ellipse2d) (samples : int)
        : V2d[] =

        Array.init (samples + 1) (fun i ->
            let t = 2.0 * Constant.Pi * (float i / float samples)
            let c = cos t
            let s = sin t
            ellipse.Center + ellipse.Axis0 * c + ellipse.Axis1 * s
        )