namespace PRo3D.Linking

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open PRo3D.Minerva

open Adaptify

/// represents one product and the camera parametes from its selection
type LinkingFeature =
    {
        id: string
        hull: Hull3d
        position: V3d
        rotation: Rot3d
        trafo: Trafo3d
        trafoInv: Trafo3d
        camTrafo: Trafo3d
        camFrustum: Frustum
        instrument: Instrument
        imageDimensions: V2i
        imageOffset: V2i
    }
    
/// type used to enable switching between images (previous / next)
type LinkingFeatureDisplay =
    {
        before:   IndexList<LinkingFeature>
        f:        LinkingFeature
        after:    IndexList<LinkingFeature>
    }

type LinkingAction =
    | MinervaAction of MinervaAction
    | UpdatePickingPoint of Option<V3d> * HashMap<Instrument, bool>
    | ToggleView of Instrument
    | OpenFrustum of LinkingFeatureDisplay
    | ChangeFrustumOpacity of float
    | CloseFrustum

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LinkingFeature =
    let initial = {
        id = ""
        hull = Hull3d 0
        position = V3d.Zero
        rotation = Rot3d.Identity
        trafo = Trafo3d.Identity
        trafoInv = Trafo3d.Identity
        camTrafo = Trafo3d.Identity
        camFrustum = Frustum.perspective 60.0 0.01 1000.0 1.0
        instrument = Instrument.NotImplemented
        imageDimensions = V2i.Zero
        imageOffset = V2i.Zero
    }

type InstrumentParameter =
    {
        horizontalFoV:  float
        sensorSize:     V2i
    }

module InstrumentParameter =
    let initial = {
        horizontalFoV = 0.0
        sensorSize = V2i.Zero
    }

[<ModelType>]
type LinkingModel =
    {
        [<TreatAsValue>]
        frustums:               HashMap<string,LinkingFeature>
        instrumentParameter:    HashMap<Instrument, InstrumentParameter>
        trafo:                  Trafo3d
        pickingPos:             Option<V3d>
        filterProducts:         HashMap<Instrument, bool>
        overlayFeature:         Option<LinkingFeatureDisplay>
        frustumOpacity:         float
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LinkingModel = 
    let initial = {
        frustums            = HashMap.Empty
        instrumentParameter = HashMap.Empty
        trafo               = Trafo3d.Identity
        pickingPos          = None
        filterProducts      = HashMap.Empty
        overlayFeature      = None
        frustumOpacity      = 0.5
    }