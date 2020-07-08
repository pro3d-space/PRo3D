using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpUtils
{
    public static class IndexComputation
    {
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

        public static bool IsOversizedTriangle(V3f[] points, int index0, int index1, int index2, float maxTriangleSize)
        {
            V3f point0 = points[index0];
            V3f point1 = points[index1];
            V3f point2 = points[index2];

            double dist_p0p1 = Vec.Distance(point0, point1);
            double dist_p0p2 = Vec.Distance(point0, point2);
            double dist_p1p2 = Vec.Distance(point1, point2);

            if ((dist_p0p1 > maxTriangleSize) || (dist_p0p2 > maxTriangleSize) || (dist_p1p2 > maxTriangleSize))
            {
                return true;
            }

            return false;
        }

        public static int[] ComputeIndices(V2i size, IEnumerable<int> invalidPoints, V3f[] points, float maxTriangleSize)
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
    }
}
