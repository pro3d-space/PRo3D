# KdTree Picking & Triangle Index Analysis

## Goal
Retrieve the triangle index from a KdTree ray intersection hit.

## Key Data Structures

### ObjectRayHit (Aardvark.Geometry.Intersection)
```
ObjectRayHit
├── SetObject.Index        ← triangle index in the TriangleSet
├── SetObject.Set          ← IIntersectableObjectSet (cast to TriangleSet)
├── RayHit.T               ← distance along ray
├── RayHit.Point           ← 3D intersection point
├── RayHit.Coord           ← barycentric/UV coords (V2d)
├── RayHit.Part            ← part index (0 for simple triangle sets)
└── RayHit.BackSide        ← whether backface was hit
```

### How triangle index gets stored
In `IntersectableTriangleSet.ObjectsIntersectRay()` (aardvark.algodat):
```csharp
hit = new ObjectRayHit()
{
    SetObject = new SetObject(this, index),  // 'index' = triangle index
    RayHit = new RayHit3d()
    {
        Part = 0,
        Point = ray.Ray.GetPointOnRay(tmax),
        T = tmax,
        Coord = V2d.Zero,
        BackSide = false
    }
};
```

## How to extract triangle index after intersection

```fsharp
let mutable hit = ObjectRayHit.MaxRange
if kdi.Intersect(ray, 0.0, System.Double.MaxValue, &hit) then
    let triangleIndex = hit.SetObject.Index              // triangle index
    let triangleSet = hit.SetObject.Set :?> TriangleSet  // the geometry
    let hitPoint = hit.RayHit.Point                      // world-space point
    let hitT = hit.RayHit.T                              // ray parameter
```

## KdTree types in pro3d

Defined in `src\PRo3D.Base\KdTrees.fs`:
- **LazyKdTree**: Lazily loaded from disk. Fields: `kdTree`, `kdtreePath`, `objectSetPath`, `affine: Trafo3d`, `boundingBox`
- **InCoreKdTree**: Pre-loaded in memory. Fields: `kdTree: ConcreteKdIntersectionTree`, `boundingBox`
- **Level0KdTree**: Union of LazyKdTree | InCoreKdTree
- **ConcreteKdIntersectionTree**: Wraps `KdIntersectionTree` + `Trafo3d` for coordinate transforms

## Intersection call chain in pro3d

```
SurfaceIntersection.doKdTreeIntersection()           (src\PRo3D.Core\Surface.fs)
  ├─ Filters surfaces by visibility/activity
  ├─ Gets KdTree HashMap for each surface
  ├─ Tests bounding boxes with ray
  ├─ For hit bounding boxes:
  │  └─ DebugKdTreesX.intersectKdTrees()
  │     └─ kdi.Intersect(ray, null, hitFilter, 0.0, Double.MaxValue, &hit)
  ├─ Returns (ObjectRayHit, Surface) option
  └─ Sorts hits by priority and distance (hit.RayHit.T)
```

The existing code only uses `hit.RayHit.T` and `hit.RayHit.Point`. The triangle index (`hit.SetObject.Index`) is available but currently not extracted.

The hit filter callback receives the index as parameter `b` but only uses it for filtering:
```fsharp
fun (a:IIntersectableObjectSet) (b:int) _ _ ->
    let triangles = a :?> TriangleSet
    false  // don't omit
```

## Key source files

- **Core KdTree library**: `C:\Users\haral\Desktop\aardvark\aardvark.algodat\src\Aardvark.Geometry.Intersection\`
  - `KdIntersectionTree.cs` - tree build + intersect
  - `IntersectableTriangleSet.cs` - triangle intersection, stores index in SetObject
  - `ObjectRayHit.cs` - hit result struct
- **OPC KdTree loading**: `C:\Users\haral\Desktop\aardvark\OpcViewer\src\OPCViewer.Base\KdTrees.fs`
- **pro3d KdTree types**: `src\PRo3D.Base\KdTrees.fs`
- **pro3d ray intersection**: `src\PRo3D.Core\Surface.fs` (SurfaceIntersection, DebugKdTreesX modules)
- **pro3d picking**: `src\PRo3D.Viewer\Viewer\Picking.fs`

## KdTree build flags
- `BuildFlags.Picking` - optimized for picking queries
- `BuildFlags.MediumIntersection | Hierarchical` - used in OPC viewer
- Split threshold controls leaf size (7/36/108 triangles per leaf)
