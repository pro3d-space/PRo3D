using Aardvark.Algodat.Points;
using Aardvark.Base;
using Aardvark.Parser.Aara;
using System;
using System.Collections.Generic;

namespace Aardvark.Opc
{

    public static class PointSampleExtension
    {
        /// <summary>
        /// Returns a subregion of an IEnumerable, which is expected to be a grid in the given resolution. 
        /// </summary>
        /// <param name="origin">upper left corner in the subpatch</param>
        /// <param name="size">dimensions in number of entries</param>
        /// <param name="resolution">Resolution of the expected grid</param>
        public static IEnumerable<T> GetSubPatch<T>(this IEnumerable<T> self, V2i origin, V2i size, V2i resolution)
        {
            //Exception for now - think of good handling of the different cases
            //return empty grid
            if (origin.X >= resolution.X || origin.Y >= resolution.Y)
                throw new NotImplementedException("requested origin outside of source grid");

            //return full grid
            if (size.X >= resolution.X || size.Y >= resolution.Y)
                throw new NotImplementedException("requested size bigger than source grid");

            //return grid as big as possible
            if (origin.X + size.X >= resolution.X || origin.Y + size.Y >= resolution.Y)
                throw new NotImplementedException("requested tile exceeds source grid boundaries");

            int i = -1;
            int firstIndex = origin.Y * resolution.X + origin.X;
            var last = origin + size;
            int lastIndex = (last.Y - 1) * resolution.X + last.X;

            int startRow = firstIndex;
            int endRow = firstIndex + size.X;

            Report.Line(1, "Skipping {0} entries", firstIndex);
            foreach (var x in self)
            {
                i++;

                if (i < firstIndex) continue;

                if (i >= startRow && i < endRow)
                {
                    if (i == endRow - 1)
                    {
                        //Console.WriteLine();
                        //Console.WriteLine("old start: {0},end: {1}", startRow, endRow);
                        startRow += resolution.X;
                        endRow += resolution.X;
                        // Console.WriteLine("new start: {0},end: {1}", startRow, endRow);
                    }

                    yield return x;
                }

                if (i == lastIndex) yield break;
            }
        }

        public static IPointSampleGrid2d GetSubPatch(this IPointSampleGrid2d self, V2i origin, V2i size)
        {
            var result = self.Positions.GetSubPatch(origin, size, self.Resolution);

            return new PointSampleGrid2d(new PointSampleSet(result), size);

            //var posList = new List<V3d>();

            //var xOffset = origin.X;
            //var yOffset = origin.Y;

            //var stream = self.Positions;
            //var rowOrigin = yOffset * self.Resolution.X + xOffset;

            //var enumerator = stream.GetEnumerator();

            //Report.BeginTimed("construction subset of size {0}", size.ToString());
            //Report.BeginTimed("skipping {0} objects", rowOrigin);
            //for (int i = 0; i < rowOrigin; i++)
            //{
            //    enumerator.MoveNext();
            //}
            //Report.End();


            //for (int j = 0; j < size.Y; j++)
            //{
            //    //take a row and add it to poslist
            //    //move iterator to end of row
            //    for (int i = 0; i < size.X; i++)
            //    {
            //        posList.Add(enumerator.Current);
            //        enumerator.MoveNext();
            //    }

            //    //move iterator to begin of next row
            //    for (int i = 0; i < size.X; i++) enumerator.MoveNext();

            //    Report.Progress(posList.Count / (double)(size.X * size.Y));
            //    Report.Line();
            //}
            //Report.End("done");

        }
    }

    public static class AaraHelpers
    {
        public static IPointSampleGrid2d PointSampleGrid2dFromAaraFile(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException();

            var aara = AaraData.FromFile(fileName);

            // extract grid resolution
            var res = aara.Size;
            if (res.Length != 2) throw new ArgumentException("encountered aara file of unexpected dimensions - 2 expected");

            // extract points
            var elements = aara.LoadElements();
            V3d[] points = elements as V3d[];

            if (points == null)
            {
                if (elements is V3f[])
                    points = elements.CopyAndConvert(V3d.FromV3f);
                else
                    throw new ArgumentException(".aara contains unexpected type - V3d or V3f expected");
            }

            return new PointSampleGrid2d(new PointSampleSet(points), new V2i(res[0], res[1]));
        }

    }

}
