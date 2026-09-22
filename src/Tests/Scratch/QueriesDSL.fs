module QueriesDSL



type SpatialQuery =
    | WithinAltitude of minimum : float * maximum : float
    | WithinPolygon of id : string
    | WithinBox of lat : float * lon : float
    | All of list<SpatialQuery>
    | AttributeThreshold of mineral : string * lower : float * upper : float



type Query = Query of list<SpatialQuery>

type Result = 
    | Map
    | Rendering

type Planet = ProvexData of string

type ColorMapping = Grayscale

type Output =
    | ExportedMesh
    | PRo3DVisualization
    | Image of ColorMapping

type Analyis = { planet : Planet; query : Query; output : Output }

let query =
    { 
        planet = ProvexData "Didymos"
        query = 
            Query [
                All [
                    WithinAltitude(-100.0,-50.0)
                    WithinBox(lat = -2.0, lon = 50.0)

                    AttributeThreshold("fe", 0.02, 0.05)
                ]
            ]
        output = Image ColorMapping.Grayscale
    }