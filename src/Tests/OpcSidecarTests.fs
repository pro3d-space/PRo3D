module OpcSidecarTests

open System
open System.IO

open Expecto

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Core
open PRo3D.Core.Surface

/// Tests for the two sidecar files an OPC carries next to its hierarchy:
///   * `*.opc.json` - product provenance, the DEM reference model and, for OPCs derived
///     from a SPICE DSK (`*.bds`) shape model, the DSKBRIEF summary of that shape.
///   * `*.opcx` - the attribute layer declarations, including the value range each
///     texture layer is normalised into.
module private Fixtures =

    /// Directory holding an OPC with a `*.opc.json` sidecar that includes a DskBrief
    /// block. Resolved under PRO3D_PRIVATE_TESTDATA; PRO3D_BDS_OPC names it directly.
    let bdsOpcDir =
        TestUtils.Roots.firstExisting [
            Environment.GetEnvironmentVariable "PRO3D_BDS_OPC"
            TestUtils.Roots.privateDir
                [ "HERA"; "OPCUpdate"; "BDS_Metadata"; "BDS_Metadata"; "Deimos" ]
            |> Option.defaultValue ""
        ]

    /// Directory holding an OPC whose `*.opcx` declares multi-channel attribute layers.
    /// Resolved under PRO3D_PRIVATE_TESTDATA; PRO3D_AARA_OPC names it directly.
    let aaraOpcDir =
        TestUtils.Roots.firstExisting [
            Environment.GetEnvironmentVariable "PRO3D_AARA_OPC"
            TestUtils.Roots.privateDir
                [ "HERA"; "OPCUpdate"; "AARA_Textures"; "AARA_Textures"; "Dimorphos" ]
            |> Option.defaultValue ""
        ]

    let tryOpcx (dir : string) =
        Directory.EnumerateFiles(dir, "*.opcx") |> Seq.tryHead

let opcJsonTests () =
    testList "opc.json" [

        test "DskBrief summary is parsed" {
            let text = """
DSKBRIEF Program; Ver. 3.0.0, 02-NOV-2021; Toolkit Ver. N0067

Summary for: 0_BDS/Deimos/deimos_k005_tho_v02.bds

Body:                               402 (DEIMOS)
  Surface:                          14021 (Name not available)
  Reference frame:                  IAU_DEIMOS
  Data type:                        2 (Shape model using triangular plates)
  Coordinate system:                Planetocentric Latitudinal
    Min, max longitude  (deg):       -180.000     180.000
    Min, max latitude   (deg):        -90.0000     90.0000
    Min, max radius      (km):          3.58220     8.70680

    Type 2 parameters
    -----------------
      Number of vertices:                 2522
      Number of plates:                   5040
"""
            match OpcMetadata.parseDskBrief text with
            | None -> failtest "DSKBRIEF text should be recognised"
            | Some dsk ->
                Expect.equal dsk.body (Some "402 (DEIMOS)") "body"
                Expect.equal dsk.referenceFrame (Some "IAU_DEIMOS") "reference frame"
                Expect.equal dsk.coordinateSystem (Some "Planetocentric Latitudinal") "coordinate system"
                Expect.equal dsk.vertexCount (Some 2522) "vertex count"
                Expect.equal dsk.plateCount (Some 5040) "plate count"

                match dsk.radiusRangeKm with
                | Some r ->
                    Expect.floatClose Accuracy.high r.Min 3.58220 "min radius"
                    Expect.floatClose Accuracy.high r.Max 8.70680 "max radius"
                | None -> failtest "radius range"

                match dsk.longitudeRange, dsk.latitudeRange with
                | Some lon, Some lat ->
                    Expect.floatClose Accuracy.high lon.Min -180.0 "min longitude"
                    Expect.floatClose Accuracy.high lat.Max 90.0 "max latitude"
                | _ -> failtest "longitude/latitude range"
        }

        test "non-DSKBRIEF text is rejected" {
            Expect.isNone (OpcMetadata.parseDskBrief "") "empty"
            Expect.isNone (OpcMetadata.parseDskBrief "some unrelated note") "unrelated"
        }

        test "DemEllipsoid carries a frame and radii instead of a single axis" {
            // The DemModel block's shape depends on ModelType. DemSphere (Deimos export)
            // declares Center + Axis; DemEllipsoid (Dimorphos export) declares Center +
            // AxisX/AxisY/AxisZ + Radii. Reading only Center/Axis silently drops the radii,
            // which are the reference body's semi-axes.
            let json = """
            {
                "product_information": { "product_type": "opc" },
                "DemModel": {
                    "ModelType": "DemEllipsoid",
                    "Center": [0.0, 0.0, 0.0],
                    "AxisX": [1.0, 0.0, 0.0],
                    "AxisY": [0.0, -1.0, 0.0],
                    "AxisZ": [0.0, 0.0, -1.0],
                    "Radii": [89.5, 84.5, 57.5]
                }
            }
            """

            let metadata = OpcMetadata.ofJsonString "inline" json
            match metadata.demModel with
            | None -> failtest "DemModel should be present"
            | Some dem ->
                Expect.equal dem.modelType "DemEllipsoid" "model type"
                Expect.equal dem.center (Some V3d.Zero) "centre"
                Expect.isNone dem.axis "DemEllipsoid declares no single Axis"
                Expect.equal dem.radii (Some (V3d(89.5, 84.5, 57.5))) "semi-axes"

                match dem.frame with
                | Some (x, y, z) ->
                    Expect.equal x V3d.IOO "frame x"
                    Expect.equal y -V3d.OIO "frame y"
                    Expect.equal z -V3d.OOI "frame z"
                | None -> failtest "frame"
        }

        test "DemSphere keeps its single axis and has no radii" {
            let json = """
            {
                "DemModel": {
                    "ModelType": "DemSphere",
                    "Center": [39.232420540642, -75.516908187272, 339.02059989334],
                    "Axis": [0.030056109818016003, 0.99816122607631, -0.05263836073096]
                }
            }
            """

            match (OpcMetadata.ofJsonString "inline" json).demModel with
            | None -> failtest "DemModel should be present"
            | Some dem ->
                Expect.equal dem.modelType "DemSphere" "model type"
                Expect.isSome dem.axis "axis"
                Expect.isNone dem.frame "DemSphere declares no AxisX/Y/Z"
                Expect.isNone dem.radii "DemSphere declares no Radii"
        }

        test "sidecar path is derived from the opcx path" {
            let sidecar = OpcMetadata.sidecarPath (Path.Combine("some", "dir", "deimos_k005_tho_v02.opcx"))
            Expect.equal (Path.GetFileName sidecar) "deimos_k005_tho_v02.opc.json" "sidecar file name"
            Expect.equal (Path.GetDirectoryName sidecar) (Path.Combine("some", "dir")) "sidecar directory"
        }

        test "reads the BDS sidecar of a real OPC" {
            match Fixtures.bdsOpcDir |> Option.bind Fixtures.tryOpcx with
            | None -> skiptest "no OPC with a *.opc.json sidecar available (set PRO3D_BDS_OPC)"
            | Some opcxPath ->

            match OpcMetadata.tryReadForOpcx opcxPath with
            | None -> failtest $"no sidecar found next to {opcxPath}"
            | Some metadata ->
                OpcMetadata.log "test" metadata

                Expect.equal metadata.productType (Some "opc") "product type"
                Expect.isSome metadata.creatorId "creator id"
                Expect.isNonEmpty metadata.inputProducts "input products"

                match metadata.demModel with
                | None -> failtest "DemModel should be present"
                | Some dem ->
                    Expect.equal dem.modelType "DemSphere" "DEM model type"
                    Expect.isSome dem.center "DEM centre"
                    match dem.axis with
                    | Some axis ->
                        // the DEM sphere's axis is a direction, so it must be normalised
                        Expect.floatClose Accuracy.medium axis.Length 1.0 "DEM axis should be unit length"
                    | None -> failtest "DEM axis"
                    Expect.isNone dem.radii "a DemSphere has no Radii"

                match metadata.dskSummary with
                | None -> failtest "DskBrief should be present and recognised"
                | Some dsk ->
                    Expect.isSome dsk.referenceFrame "reference frame"
                    match dsk.radiusRangeKm with
                    | Some r -> Expect.isGreaterThan r.Max r.Min "radius range should be non-degenerate"
                    | None -> failtest "radius range"
        }
    ]

let opcxAttributeLayerTests () =
    let readLayers (xml : string) =
        let doc = System.Xml.XmlDocument()
        doc.LoadXml xml
        SurfaceUtils.SurfaceAttributes.layers doc |> Seq.toList

    let opcx (layerBody : string) =
        $"""<?xml version='1.0' encoding='utf-8'?>
<Aardvark version="4">
  <SurfaceAttributes version="0" num="0">
    <AttributeLayers num="1" count="1">
      {layerBody}
    </AttributeLayers>
  </SurfaceAttributes>
</Aardvark>"""

    testList "opcx attribute layers" [

        test "single channel range" {
            let xml =
                opcx """<AttributeLayer version="0" num="15">
        <Type>Map</Type>
        <Label>Elevation</Label>
        <ChannelsDefinedRange>[0.004859322, 124.307319641]</ChannelsDefinedRange>
        <ChannelsActualRange>[0.004859322, 124.307319641]</ChannelsActualRange>
      </AttributeLayer>"""

            match readLayers xml with
            | [ ScalarLayer layer ] ->
                Expect.equal layer.label "Elevation" "label"
                Expect.floatClose Accuracy.high layer.definedRange.Min 0.004859322 "min"
                Expect.floatClose Accuracy.high layer.definedRange.Max 124.307319641 "max"
            | other -> failtest $"expected one scalar layer, got {other}"
        }

        test "multi channel range takes the first channel instead of failing" {
            // ExportGpc writes one range per channel for multi-channel maps (normals,
            // gravity vectors, lon/lat/radius). Range1d.Parse cannot read that, which used
            // to make such OPCs fail to import until the *.opcx was hand-patched.
            //
            // The layer keeps the *first* channel's range, because that is the channel the
            // attribute texture is read from. Unioning all three would widen this Gravity
            // range by 25% and skew every value de-normalised through it.
            let xml =
                opcx """<AttributeLayer version="0" num="12">
        <Type>Map</Type>
        <Label>Gravity</Label>
        <ChannelsDefinedRange>[[-0.000039896, 0.000040985], [-0.000046144, 0.000046183], [-0.000050788, 0.000050736]]</ChannelsDefinedRange>
        <ChannelsActualRange>[[-0.000039896, 0.000040985], [-0.000046144, 0.000046183], [-0.000050788, 0.000050736]]</ChannelsActualRange>
      </AttributeLayer>"""

            match readLayers xml with
            | [ ScalarLayer layer ] ->
                Expect.equal layer.label "Gravity" "label"
                Expect.floatClose Accuracy.high layer.definedRange.Min -0.000039896 "min of the first channel"
                Expect.floatClose Accuracy.high layer.definedRange.Max 0.000040985 "max of the first channel"
                Expect.equal layer.actualRange layer.definedRange "actual range parsed the same way"

                // all three channel ranges are still recoverable from the raw text
                let ranges =
                    SurfaceUtils.SurfaceAttributes.parseChannelRanges
                        "[[-0.000039896, 0.000040985], [-0.000046144, 0.000046183], [-0.000050788, 0.000050736]]"
                Expect.equal ranges.Length 3 "three channel ranges"
            | other -> failtest $"expected one scalar layer, got {other}"
        }

        test "unparsable range falls back instead of throwing" {
            let xml =
                opcx """<AttributeLayer version="0" num="13">
        <Type>Map</Type>
        <Label>Broken</Label>
        <ChannelsDefinedRange>not a range</ChannelsDefinedRange>
      </AttributeLayer>"""

            match readLayers xml with
            | [ ScalarLayer layer ] ->
                Expect.equal layer.label "Broken" "label"
                Expect.equal layer.definedRange (Range1d(0.0, 1.0)) "fallback range"
                Expect.equal layer.actualRange layer.definedRange "actual range falls back to defined range"
            | other -> failtest $"expected one scalar layer, got {other}"
        }

        test "the unpatched Dimorphos opcx loads" {
            match Fixtures.aaraOpcDir |> Option.bind Fixtures.tryOpcx with
            | None -> skiptest "no OPC with multi-channel attribute layers available (set PRO3D_AARA_OPC)"
            | Some opcxPath ->

            let layers = SurfaceUtils.SurfaceAttributes.read opcxPath |> Seq.toList
            let scalars = layers |> List.choose (function ScalarLayer l -> Some l | _ -> None)
            let textures = layers |> List.choose (function TextureLayer l -> Some l | _ -> None)

            Log.line "[Test] %s: %d scalar layers, %d texture layers"
                (Path.GetFileName opcxPath) scalars.Length textures.Length
            for l in scalars do
                Log.line "[Test]   %-12s %A" l.label l.definedRange

            Expect.isNonEmpty scalars "opcx should declare scalar layers"
            Expect.isNonEmpty textures "opcx should declare texture layers"
            for l in scalars do
                Expect.isGreaterThan l.definedRange.Max l.definedRange.Min $"{l.label}: range should be non-degenerate"
        }
    ]

let tests () =
    testList "OpcSidecar" [
        opcJsonTests()
        opcxAttributeLayerTests()
    ]
