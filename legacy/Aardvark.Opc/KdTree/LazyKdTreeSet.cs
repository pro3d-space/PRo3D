using Aardvark.Algodat;
using Aardvark.Base;
using Aardvark.Rendering;
using Aardvark.Runtime;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aardvark.Opc.KdTree
{
    public static class ObjectSetBuilder
    {
        public static TriangleSet GetTriangleSetFromPath(LazyKdTreeSet.KdTreePlaceHolder ph)
        {
            return GetTriangleSetFromPath(ph.ObjectSetPath, ph.Affine);
        }

        /// <summary>
        /// Loads positions from aara file and returns a TriangleSet (a regular grid for triangulation is assumed).
        /// Further a translation offset can be applied to the position data
        /// </summary>
        public static TriangleSet GetTriangleSetFromPath(string posPath, M44d affine)
        {

            //create empty vg
            var vg = new VertexGeometry(GeometryMode.TriangleList);

            //read grid from aara file
            var grid = AaraHelpers.PointSampleGrid2dFromAaraFile(posPath);

            //apply affine with double prec
            //var transPos = grid.Positions
            //  .Select(x => (V3f)(affine.TransformPos(x)))
            //  .ToArray();

            var positions = grid.Positions.ToArray();

            //get indices while handling invalid positions
            var invPoints = OpcIndices.GetInvalidPositions(positions);
            var indices = IndexArrayCache.GetIndexArray(grid.Resolution, invPoints.ToList());

            if (indices.Length == 0)
                indices = new int[] { 0, 0, 0 };
          
            vg.Positions = positions;
            vg.TransformV3d(affine);
            vg.Indices = indices;

            //build up triangle set
            var triangles = vg.Triangles
                .Where(x => !x.Point0.Position.IsNaN &&
                    !x.Point1.Position.IsNaN &&
                    !x.Point2.Position.IsNaN)
                .Select(x => x.ToTriangle3d());

            return new TriangleSet(triangles);
        }
    }

    [RegisterTypeInfo]
    public class LazyKdTreeSet : Map, IIntersectableObjectSet
    {

        [RegisterTypeInfo]
        public class KdTreePlaceHolder : IFieldCodeable
        {
            public string Path;
            public Box3d BoundingBox;
            public string ObjectSetPath;
            public M44d Affine;
            public KdIntersectionTree KdTree;

            public KdTreePlaceHolder() { }


            #region IFieldCodeable Members

            public IEnumerable<FieldCoder> GetFieldCoders(int version)
            {
                yield return new FieldCoder(0, "Path", (c, o) => c.CodeString(ref ((KdTreePlaceHolder)o).Path));
                yield return new FieldCoder(1, "BoundingBox", (c, o) => c.CodeBox3d(ref ((KdTreePlaceHolder)o).BoundingBox));
                yield return new FieldCoder(2, "ObjectSetPath", (c, o) => c.CodeString(ref ((KdTreePlaceHolder)o).ObjectSetPath));
                yield return new FieldCoder(3, "Affine", (c, o) => c.CodeM44d(ref ((KdTreePlaceHolder)o).Affine));
            }

            #endregion
        }

        private Box3d m_cachedBoundingBox = Box3d.Invalid;

        public const string Identifier = "LazyKdTreeSet";

        public static class Property
        {
            public const string KdTreePlaceholders = "KdTreePlaceholders";
            public const string Level = "Level";
        }

        public LazyKdTreeSet()
        {
            m_typeName = Identifier;
            this[Property.KdTreePlaceholders] = new List<KdTreePlaceHolder>();
        }

        public LazyKdTreeSet(IEnumerable<KdTreePlaceHolder> pathBox3dPairs)
        {
            m_typeName = Identifier;
            this[Property.KdTreePlaceholders] = pathBox3dPairs.ToList();
        }

        public List<KdTreePlaceHolder> KdTreePlaceholders
        {
            get { return Get<List<KdTreePlaceHolder>>(Property.KdTreePlaceholders); }
        }

        public int Level
        {
            get { return Get<int>(Property.Level); }
            set { m_ht[Property.Level] = value; }
        }

        public int KdTreePlaceholdersCount
        {
            get { return KdTreePlaceholders.Count; }
        }

        public Box3d AggregateBoundingBox
        {
            get
            {
                if (m_cachedBoundingBox.IsInvalid)
                {
                    var box = Box3d.Invalid;
                    foreach (var ph in KdTreePlaceholders)
                    {
                        if (box.IsInvalid) box = ph.BoundingBox;

                        box.ExtendBy(ph.BoundingBox);
                    }
                    return box;
                }

                return m_cachedBoundingBox;
            }
        }

        /// <summary>
        /// The number of objects for the triangle set to act as an
        /// IIntersectableObjectSet.
        /// </summary>
        public int ObjectCount
        {
            get { return KdTreePlaceholders.Count; }
        }

        public Box3d ObjectBoundingBox(
                int objectIndex
                )
        {
            if (objectIndex >= 0)
            {
                var x = KdTreePlaceholders[objectIndex];
                return x.BoundingBox; //.KdIntersectionTree.BoundingBox3d.Transformed(x.Trafo);
            }

            return AggregateBoundingBox;

        }

        public void UpdatePlaceHolderPaths(string newDirectory)
        {
            foreach (var ph in KdTreePlaceholders)
            {
                var newPath = UpdatePathDirectory(ph.Path, newDirectory);
                var newObjectPath = UpdatePathDirectory(ph.ObjectSetPath, newDirectory);

                ph.Path = newPath;
                ph.ObjectSetPath = newObjectPath;
            }
        }

        private static string UpdatePathDirectory(string path, string newDirectory)
        {
            var relPath = path.ToLower();
            var index = relPath.LastIndexOf("patches");
            relPath = relPath.Substring(index, relPath.Length - index);
            var newPath = Path.Combine(newDirectory, relPath);

            return newPath;
        }


        #region IIntersectableObjectset

        public class KdTreeCacheElement
        {
            public readonly string Path;
            public readonly KdIntersectionTree Tree;

            public KdTreeCacheElement(string path, KdIntersectionTree tree)
            {
                Path = path;
                Tree = tree;
            }
        }

        /// <summary>
        /// Full associative Cache
        /// </summary>
        private static KdTreeCacheElement[] s_cache;
        private static int s_cachePointer = 0;

        static LazyKdTreeSet()
        {
            s_cache = new KdTreeCacheElement[20];
        }

        public static void ResetCache(int size)
        {
            s_cache = new KdTreeCacheElement[size];
            s_cachePointer = 0;
        }

        /// <summary>
        /// Performs cache lookup and returns the element in O(n) or loads the instance 
        /// into the cache.
        /// </summary>
        public static KdIntersectionTree LookupCache(KdTreePlaceHolder ph)
        {
            for (int i = 0; i < s_cache.Length; i++)
            {
                if (s_cache[i] != null && s_cache[i].Path == ph.Path)
                {
                    return s_cache[i].Tree;
                }
            }

            // otherwise load and put into cache
            return LoadAndPutIntoCache(ph);
        }

        public static KdIntersectionTree LoadAndPutIntoCache(KdTreePlaceHolder ph)
        {
            var element = Load.As<KdIntersectionTree>(ph.Path);

            s_cache[s_cachePointer] = new KdTreeCacheElement(ph.Path, element);
            s_cachePointer = (s_cachePointer + 1) % s_cache.Length;

            return element;
        }

        public bool ObjectsIntersectRay(
            int[] objectIndexArray, int firstIndex, int indexCount,
            FastRay3d fastRay,
            Func<IIntersectableObjectSet, int, bool> objectFilter,
            Func<IIntersectableObjectSet, int, int, RayHit3d, bool> hitFilter,
            double tmin, double tmax,
            ref ObjectRayHit hit
            )
        {
            int kdTreeIndex = -1;
            for (int i = firstIndex, e = firstIndex + indexCount; i < e; i++)
            {
                var index = objectIndexArray[i];
                var placeHolder = KdTreePlaceholders[index];

                //if bounding box is hit, stream Kd-intersection tree
                double t; // this t is not needed, only for the bb intersection call!
                if (placeHolder.BoundingBox.Intersects(fastRay.Ray, out t))
                {
                    var kdTree = LookupCache(placeHolder);
                    
                    if (kdTree.ObjectSet == null)
                    {
                        kdTree.ObjectSet = ObjectSetBuilder.GetTriangleSetFromPath(placeHolder);
                    }

                    if (kdTree.Intersect(fastRay, objectFilter, hitFilter, tmin, tmax, ref hit))
                        kdTreeIndex = index;
                    //}
                    //catch (NullReferenceException)
                    //{
                    //    if (kdTree.ObjectSet == null)
                    //    {
                    //        kdTree.ObjectSet = ObjectSetBuilder.GetTriangleSetFromPath(placeHolder);

                    //        if (kdTree.Intersect(fastRay, objectFilter, hitFilter, tmin, tmax, ref hit))
                    //            kdTreeIndex = index;
                    //    }
                    //}
                }
            }

            if (kdTreeIndex < 0) return false;

            if (hit.ObjectStack == null)
                hit.ObjectStack = new List<SetObject>();
            hit.ObjectStack.Add(new SetObject(this, kdTreeIndex));

            return true;
        }

        public void ObjectHitInfo(
                ObjectRayHit hit,
                ref ObjectHitInfo hitInfo
                )
        {
            // this never needs to be called
            hit.SetObject.Set.ObjectHitInfo(hit, ref hitInfo);
        }

        public bool ClosestPoint(
                int[] objectIndexArray, int firstIndex, int indexCount,
                V3d query,
                Func<IIntersectableObjectSet, int, bool> ios_index_objectFilter,
                Func<IIntersectableObjectSet, int, int, ObjectClosestPoint, bool> ios_index_part_ocp_pointFilter,
                ref ObjectClosestPoint closest
                )
        {
            //throw new NotImplementedException("ClosestPoint not implement for lazykdtree set");

            int kdTreeIndex = -1;
            for (int i = firstIndex, e = firstIndex + indexCount; i < e; i++)
            {
                int index = objectIndexArray[i];
                var ph = KdTreePlaceholders[index];
                var kdTree = Load.As<KdIntersectionTree>(ph.Path);
                //  var trafo = x.Trafo;
                //kdTree.KdIntersectionTree.ClosestPoint(trafo.Backward.TransformPos(query), ref closest)

                if (kdTree.ClosestPoint(query, ref closest))
                {
                    kdTreeIndex = index;
                    //closest.Point = trafo.Forward.TransformPos(closest.Point);
                }
            }
            if (kdTreeIndex < 0) return false;

            if (closest.ObjectStack == null)
                closest.ObjectStack = new List<SetObject>();

            closest.ObjectStack.Add(new SetObject(this, kdTreeIndex));
            return true;
        }

        public bool ObjectIntersectsBox(
                int objectIndex,
                Box3d box
                )
        {
            //throw new NotImplementedException("ObjectIntersectsBox not implement for lazykdtree set");

            //var x = ConcreteKdTreeList[objectIndex];
            //return x.KdIntersectionTree.IntersectsBox(box.Transformed(x.Trafo.Backward));

            var x = KdTreePlaceholders[objectIndex];
            return x.BoundingBox.Intersects(box);
        }

        public bool ObjectIsInsideBox(
                int objectIndex,
                Box3d box
                )
        {
            var ph = KdTreePlaceholders[objectIndex];
            var kdTree = Load.As<KdIntersectionTree>(ph.Path);

            return kdTree.IsInsideBox(box);

            //var x = ConcreteKdTreeList[objectIndex];
            //return x.KdIntersectionTree.IsInsideBox(box.Transformed(x.Trafo.Backward));
        }

        #endregion
    }
}
