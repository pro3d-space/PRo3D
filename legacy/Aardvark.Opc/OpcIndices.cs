using Aardvark.Algodat;
using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using Aardvark.VRVis;

namespace Aardvark.Opc
{
    public class OpcIndices
    {
        #region Normals Computation
        /// <summary>
        /// Takes the input VertexGeometry and creates a Mesh from it to compute smooth normals. Creating a Mesh topology
        /// can be more time consuming than a straightforward Opc approach. Further the Mesh algorithm can not deal 
        /// with holes in the geometry.
        /// </summary>
        //private static V3d[] GenerateNormalsByMeshing(VertexGeometry vg)
        //{
        //    var triangleVertexIndexArray = vg.Indices;

        //    // create mesh ...
        //    var m = new Mesh();

        //    // ... set vertex positions
        //    m.VertexCount = vg.VertexCount;
        //    var vertexPositions = new PerVertexAttributeSet<V3f>(m, Interpolator.GetLinearV3f());

        //    for (int pi = 0; pi < vg.VertexCount; pi++) vertexPositions[pi] = (V3f)vg.Positions.GetValue(pi);
        //    m.VertexPositions = vertexPositions;

        //    // ... set triangles
        //    m.TriangleCount = triangleVertexIndexArray.Length / 3;
        //    m.SetTriangles(triangleVertexIndexArray);

        //    // ... create normals
        //    m.AddPerVertexIndexedNormals<V3f>(Conversion.RadiansFromDegrees(70));

        //    // create vertex geometry from mesh
        //    var resVg = m.GetVertexGeometry(Mesh.VertexGeometryMode.CompactLowPrecision);

        //    return resVg.NormalsAsV3ds.ToArray();
        //}

        /// <summary>
        /// Generates smooth vertex normals for the given position grid specified by an index- and a data-array.
        /// </summary>
        /// <returns>Vertex normals (size: positions.Count()).</returns>
        public static V3d[] GenerateVertexNormals(int[] indices, V3d[] positions)
        {
            var numOfVertices = positions.Count();
            var vertexNormals = Enumerable.Repeat(V3d.OOO, numOfVertices).ToArray();

            V3d faceNormal = V3d.NaN;
            for (int i = 0; i < indices.Count(); i++)
            {
                // compute faceNormal when reaching first index of face
                if (i % 3 == 0)
                    faceNormal = ComputeFaceNormal(
                        positions[indices[i]],
                        positions[indices[i + 1]],
                        positions[indices[i + 2]]);

                // accumulate faceNormals per vertex
                vertexNormals[indices[i]] += faceNormal;
            }

            for (int i = 0; i < numOfVertices; i++)
                vertexNormals[i].Normalize();

            return vertexNormals;
        }

        /// <summary>
        /// Generates hard face normals for the given position grid specified by an index- and a data-array.
        /// </summary>
        /// <returns>Face normals (size: indices.Count()/3).</returns>
        public static V3d[] GenerateFaceNormals(int[] indices, V3d[] positions)
        {
            var faceNormals = new V3d[indices.Count() / 3];

            for (int i = 0; i < indices.Count() / 3; i++)
            {
                faceNormals[i] = ComputeFaceNormal(
                    positions[indices[i * 3]],
                    positions[indices[i * 3 + 1]],
                    positions[indices[i * 3 + 2]]);
            }

            return faceNormals;
        }

        private static V3d ComputeFaceNormal(V3d p1, V3d p2, V3d p3)
        {
            if (p1.IsNaN || p2.IsNaN || p3.IsNaN)
                return V3d.NaN;

            var u = p2 - p1;
            var v = p3 - p1;

            return u.Cross(v).Normalized;
        }

        /// <summary>
        /// Computes the neighbour indices in grid for a given position. 
        /// </summary>
        private static List<int> GetGridNeighbourIndices(int x, int y, V2i tileSize)
        {
            var resultList = new List<int>();

            //North
            if (y > 0)
                resultList.Add((y - 1) * tileSize.X + x);

            //East
            if (x < tileSize.X - 1)
                resultList.Add(y * tileSize.X + x + 1);

            //South
            if (y < tileSize.Y - 1)
                resultList.Add((y + 1) * tileSize.X + x);

            //West
            if (x > 0)
                resultList.Add(y * tileSize.X + x - 1);

            if (x == tileSize.X - 1)
                resultList.Add(y * tileSize.X + x - 1);

            //if (resultList.Count == 0)
            //{
            //    resultList.Add((y - 1) * tileSize.X + x);
            //    resultList.Add(y * tileSize.X + x - 1);
            //}

            return resultList;
        }
        #endregion  

        public static IEnumerable<int> GetInvalidPositions(V3d[] inputArray)
        {
            for (int i = 0; i < inputArray.Length; i++)
            {
                if (inputArray[i].IsNaN)
                    yield return i;
            }
        }

        public static IEnumerable<int> GetInvalidPositions(V3f[] inputArray)
        {
            var invPoints = new List<int>();
            for (int i = 0; i < inputArray.Length; i++)
            {
                //if (!OpcIndices.IsValidPosition(inputArray[i]))
                if (inputArray[i].IsNaN)
                {
                    invPoints.Add(i);
                }
            }

            return invPoints.ToArray();
        }

        public static int[] GetInvalidPositionsFromOversizedTriangles(V3f[] inputArray)
        {
            var invPoints = new List<int>();
            for (int i = 0; i < inputArray.Length; i++)
            {

                //if (!OpcIndices.IsValidPosition(inputArray[i]))
                if (inputArray[i].IsNaN)
                {
                    invPoints.Add(i);
                }
            }

            return invPoints.ToArray();
        }

        // searches for oversized triangles and removes and adds all 3 vertex indices to invalid point list
        // (if one triangle side > maxTriangleSize => skip whole triangle)
        public static int[] GetAllPositionsFromOversizedTriangles(int[] invalidpositions, VertexGeometry vg, float maxTriangleSize)
        {
            var invalidPoints = new List<int>(invalidpositions);

            foreach (var t in vg.Triangles)
            {
                V3d p0, p1, p2;
                t.GetVertexPositions(out p0, out p1, out p2);

                if (p0.IsNaN || p1.IsNaN || p2.IsNaN) continue;

                double dist_p0p1 = V3d.Distance(p0, p1);
                double dist_p0p2 = V3d.Distance(p0, p2);
                double dist_p1p2 = V3d.Distance(p1, p2);
               
                if ((dist_p0p1 > maxTriangleSize) || (dist_p0p2 > maxTriangleSize) || (dist_p1p2 > maxTriangleSize))
                {
                    foreach (var index in t.VertexIndices)
                    {
                        if (!Array.Exists(invalidPoints.ToArray(), x => x == index))
                            invalidPoints.Add(index);
                    }
                }
            }

            return invalidPoints.ToArray();
        }

        // searches for oversized triangles and adds only invalid indices to invalid point list
        // (keep vertex indices for triangle side < maxTriangleSize) 
        public static int[] GetInvalidPositionsFromOversizedTriangles(int[] invalidpositions, VertexGeometry vg, float maxTriangleSize)
        {
            var invalidPoints = new List<int>(invalidpositions);

            foreach (var t in vg.Triangles)
            {
                V3d p0, p1, p2;
                t.GetVertexPositions(out p0, out p1, out p2);

                if (p0.IsNaN || p1.IsNaN || p2.IsNaN) continue;

                List<V3d> vertexlist = new List<V3d>();
                int p0_counter = 0;
                int p1_counter = 0;
                int p2_counter = 0;
                if (V3d.Distance(p0, p1) > maxTriangleSize) { vertexlist.Add(p0); vertexlist.Add(p1); }
                if (V3d.Distance(p0, p2) > maxTriangleSize) { vertexlist.Add(p0); vertexlist.Add(p2); }
                if (V3d.Distance(p1, p2) > maxTriangleSize) { vertexlist.Add(p1); vertexlist.Add(p2); }
                foreach (var p in vertexlist)
                {
                    if (p == p0) p0_counter++;
                    if (p == p1) p1_counter++;
                    if (p == p2) p2_counter++;
                }

                if ((p0_counter > 1) && (!Array.Exists(invalidPoints.ToArray(), x => x == t.VertexIndex0))) { invalidPoints.Add(t.VertexIndex0); }
                if ((p1_counter > 1) && (!Array.Exists(invalidPoints.ToArray(), x => x == t.VertexIndex1))) { invalidPoints.Add(t.VertexIndex1); }
                if ((p2_counter > 1) && (!Array.Exists(invalidPoints.ToArray(), x => x == t.VertexIndex2))) { invalidPoints.Add(t.VertexIndex2); }

            }

            return invalidPoints.ToArray();
        }

        // checks if an triangle has an oversized side length
        public static bool IsOversizedTriangle(V3f[] points, int index0, int index1, int index2, float maxTriangleSize)
        {
            V3f point0 = points[index0];
            V3f point1 = points[index1];
            V3f point2 = points[index2];

            double dist_p0p1 = V3f.Distance(point0, point1);
            double dist_p0p2 = V3f.Distance(point0, point2);
            double dist_p1p2 = V3f.Distance(point1, point2);

            if ((dist_p0p1 > maxTriangleSize) || (dist_p0p2 > maxTriangleSize) || (dist_p1p2 > maxTriangleSize))
            {
                return true;
            }

            return false;
        }

        // checks if the side length of an triangle has an oversized side length
        public static bool IsOversizedTriangleSideRatio(V3f[] points, int index0, int index1, int index2, float maxTriangleSize)
        {
            V3f point0 = points[index0];
            V3f point1 = points[index1];
            V3f point2 = points[index2];

            double dist_p0p1 = V3f.Distance(point0, point1);
            double dist_p0p2 = V3f.Distance(point0, point2);
            double dist_p1p2 = V3f.Distance(point1, point2);

            double sideRatio01 = Math.Abs(dist_p1p2 - dist_p0p2);
            double sideRatio02 = Math.Abs(dist_p0p1 - dist_p1p2);
            double sideRatio12 = Math.Abs(dist_p0p1 - dist_p0p2);


            if ((sideRatio01 > maxTriangleSize) || (sideRatio02 > maxTriangleSize) || (sideRatio12 > maxTriangleSize))
            {
                return true;
            }

            return false;
        }
        /// <summary>
        /// Check if point is valid according to Dibit magic numbers - all coords are -10000000.
        /// </summary>
        public static bool ValidPoint(V3d p)
        {
            return p.X != -10000000 && p.Y != -10000000 && p.Z != -10000000;
        }

        #region Index Computations
        /// <summary>
        /// Compute index array to triangulate an ordered point set with 
        /// a resolution of size.x X size.y
        /// </summary>
        /// <param name="size">V2i specifying number of points
        /// in x and y direction respectively</param>
        /// <returns>index array</returns>
        public static int[] ComputeIndexArray(V2i size)
        {
            return ComputeIndexArray(size, null);
        }

        /// <summary>
        /// Compute index array to triangulate an ordered point set with 
        /// a resolution of size.x X size.y and specify a list of indices 
        /// which shall be excluded from triangulation (i.e. invalid scan points). 
        /// Affected faces will result in degenerated triangles.
        /// </summary>
        /// <param name="size">V2i specifying number of points
        /// in x and y direction respectively</param>
        /// <param name="invalidPoints">List of indices excluded from trianguation</param>
        /// <returns>index array</returns>
        public static int[] ComputeIndexArray(V2i size, IEnumerable<int> invalidPoints)
        {
            var indexArray = new int[(size.X - 1) * (size.Y - 1) * 6];

            int k = 0;

            bool hasInvalids = invalidPoints != null;

            #region no invalids
            if (!hasInvalids)
            {
                for (int y = 0; y < size.Y - 1; y++)
                {
                    for (int x = 0; x < size.X - 1; x++)
                    {
                        indexArray[k] = y * size.X + x;
                        indexArray[k + 1] = (y + 1) * size.X + x;
                        indexArray[k + 2] = y * size.X + x + 1;

                        indexArray[k + 3] = y * size.X + x + 1;
                        indexArray[k + 4] = (y + 1) * size.X + x;
                        indexArray[k + 5] = (y + 1) * size.X + x + 1;

                        k += 6;
                    }
                }

                return indexArray.ToArray();
            }
            #endregion
            #region has invalids
            else
            {
                var invalidDict = invalidPoints.ToDictionary(n => n);
                int a1, b1, c1;
                int a2, b2, c2;
                var indices = new List<int>();
                bool invalidFace = false;
                int counter = 0;

                for (int y = 0; y < size.Y - 1; y++)
                {
                    for (int x = 0; x < size.X - 1; x++)
                    {
                        a1 = y * size.X + x;
                        b1 = (y + 1) * size.X + x;
                        c1 = y * size.X + x + 1;

                        a2 = y * size.X + x + 1;
                        b2 = (y + 1) * size.X + x;
                        c2 = (y + 1) * size.X + x + 1;

                        indices.Clear();
                        indices.Add(a1); indices.Add(b1); indices.Add(c1);
                        indices.Add(a2); indices.Add(b2); indices.Add(c2);

                        invalidFace = indices.Select(n => n).Where(m => invalidDict.ContainsKey(m)).ToList().Count() > 0;

                        if (invalidFace)
                        {
                            //Console.WriteLine("Invalid Face Found");
                            counter++;
                            k += 6;
                            continue;
                        }

                        indexArray[k] = a1;
                        indexArray[k + 1] = b1;
                        indexArray[k + 2] = c1;

                        indexArray[k + 3] = a2;
                        indexArray[k + 4] = b2;
                        indexArray[k + 5] = c2;

                        k += 6;
                    }
                }

                if (counter > 0)
                    Report.Line(5, "Invalid faces found: " + counter);
                return indexArray.ToArray();
            }
            #endregion
        }

        /// <summary>
        /// Compute index array to triangulate an ordered point set with 
        /// a resolution of size.x X size.y and specify a list of indices 
        /// which shall be excluded from triangulation (i.e. invalid scan points). 
        /// Affected faces will result in degenerated triangles.
        /// Triangles with min. one side > maxTriangle size shall be excluded too.
        /// </summary>
        /// <param name="size">V2i specifying number of points
        /// in x and y direction respectively</param>
        /// <param name="invalidPoints">List of indices excluded from trianguation</param>
        /// <param name="points">List of all 3d points</param>
        /// <param name="maxTriangleSize">Maximum side length of valid triangle</param>
        /// <returns>index array</returns>
        public static int[] ComputeIndexArray(V2i size, IEnumerable<int> invalidPoints, V3f[] points, float maxTriangleSize)
        {
            var indexArray = new int[(size.X - 1) * (size.Y - 1) * 6];

            int k = 0;

            var invalidDict = invalidPoints.ToDictionary(n => n);
            int a1, b1, c1;
            int a2, b2, c2;
            var indices = new List<int>();
            bool invalidFace = false;
            int counter = 0;
            int oversized_counter = 0;

            for (int y = 0; y < size.Y - 1; y++)
            {
                for (int x = 0; x < size.X - 1; x++)
                {
                    a1 = y * size.X + x;
                    b1 = (y + 1) * size.X + x;
                    c1 = y * size.X + x + 1;

                    a2 = y * size.X + x + 1;
                    b2 = (y + 1) * size.X + x;
                    c2 = (y + 1) * size.X + x + 1;

                    indices.Clear();
                    indices.Add(a1); indices.Add(b1); indices.Add(c1);
                    indices.Add(a2); indices.Add(b2); indices.Add(c2);

                    invalidFace = indices.Select(n => n).Where(m => invalidDict.ContainsKey(m)).ToList().Count() > 0;

                    if (invalidFace)
                    {
                        //Console.WriteLine("Invalid Face Found");
                        counter++;
                        k += 6;
                        continue;
                    }

                    if (!IsOversizedTriangle(points, a1, b1, c1, maxTriangleSize))
                    //if (!IsOversizedTriangleSideRatio(points, a1, b1, c1, maxTriangleSize))
                    {
                        indexArray[k] = a1;
                        indexArray[k + 1] = b1;
                        indexArray[k + 2] = c1;
                    }
                    else
                        oversized_counter++;

                    if (!IsOversizedTriangle(points, a2, b2, c2, maxTriangleSize))
                    //if (!IsOversizedTriangleSideRatio(points, a2, b2, c2, maxTriangleSize))
                    {
                        indexArray[k + 3] = a2;
                        indexArray[k + 4] = b2;
                        indexArray[k + 5] = c2;
                    }
                    else
                        oversized_counter++;
                    

                    k += 6;
                }
            }
                
            if (counter > 0)
                Report.Line(5, "Invalid faces found: " + counter);
            return indexArray.ToArray();   
        }

        public static int[] ComputeLineStripIndexArray(V2i size)
        {
            var indexArray = new int[(size.X * size.Y)];

            for (int j = 0; j < size.Y; j++)
            {
                for (int i = 0; i < size.X; i++)
                {
                    int index = j * size.X + i;
                    indexArray[index] = index;
                }
            }

            return indexArray;
        }

        public static int[] ComputeTriangleStripIndexArray(V2i size, List<int> invalidPoints)
        {
            return null;
        }

        public static int[] ComputeTriangleStripIndexArray(V2i size)
        {
            var numOfStrips = size.Y - 1;
            var numOfSwapIndices = (numOfStrips) * 2;
            var numOfStripIndices = numOfStrips * size.X * 2;
            var numOfIndices = numOfSwapIndices + numOfStripIndices;

            var indexArray = new int[numOfIndices];

            int linItr = 0;
            for (int y = 0; y < size.Y - 1; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    var indexUp = size.X * y + x;
                    var indexDown = indexUp + size.X; //(size.X * (y + 1)) + x;

                    if (x == 0)
                    {
                        indexArray[linItr] = indexUp;
                        linItr++;
                    }

                    indexArray[linItr] = indexUp;
                    linItr++;
                    indexArray[linItr] = indexDown;
                    linItr++;

                    if (x == size.X - 1)
                    {
                        indexArray[linItr] = indexDown;
                        linItr++;
                    }
                }
            }

            return indexArray;
        }

        public static C4b[] ComputeColorMap(V2i size)
        {
            var length = size.X * size.Y;
            var indexArray = new C4b[length];

            var stepX = 255 / (double)size.X;
            var stepY = 255 / (double)size.Y;

            //var currColor = C4b.Black;
            for (int j = 0; j < size.Y; j++)
            {
                for (int i = 0; i < size.X; i++)
                {
                    int index = j * size.X + i;
                    var newR = (byte)(stepX * i);
                    var newG = (byte)(stepY * j);

                    indexArray[index] = new C4b(newR, newG, (byte)128);
                    indexArray[index].Opacity = 255;
                }
            }

            indexArray[0] = C4b.Red;
            indexArray[length - 1] = C4b.Blue;

            return indexArray;
        }

        public static C4b[] DefaultColor(V2i size)
        {
            var length = size.X * size.Y;
            var indexArray = new C4b[length].Set(C4b.White);

            return indexArray;
        }

        #region Row/Colum Accessors
        public static int[] GetColumnAsIndices(V2i size, int colNumber)
        {
            var colInds = new List<int>();

            for (int i = 0; i < size.Y; i++)
                colInds.Add(size.X * i + colNumber);

            return colInds.ToArray();
        }

        public static int[] GetRowAsIndices(V2i size, int rowNumber)
        {
            var colInds = new List<int>();

            for (int i = 0; i < size.X; i++)
                colInds.Add(size.X * rowNumber + i);

            return colInds.ToArray();
        }

        //First
        public static int[] GetFirstColumnAsIndices(V2i size)
        {
            return GetColumnAsIndices(size, 0);
        }

        public static int[] GetFirstRowAsIndices(V2i size)
        {
            return GetRowAsIndices(size, 0);
        }

        //Last
        public static int[] GetLastColumnAsIndices(V2i size)
        {
            return GetColumnAsIndices(size, size.X - 1);
        }

        public static int[] GetLastRowAsIndices(V2i size)
        {
            return GetRowAsIndices(size, size.Y - 1);
        }
        #endregion

        #endregion

    }
}
