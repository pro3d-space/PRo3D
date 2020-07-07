using Aardvark.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aardvark.Opc
{
    public static class IndexArrayCache
    {
        static ConcurrentDictionary<V2i, int[]> s_indexArrayTable;
        static ConcurrentDictionary<V2i, int[]> s_triangleStripIndexArrayTable;

        public static int[] GetIndexArray(V2i size)
        {
            return GetIndexArray(size, null);
        }

        public static int[] GetIndexArray(V2i size, List<int> invalidPoints)
        {
            if(invalidPoints != null)
                return OpcIndices.ComputeIndexArray(size, invalidPoints);

            if (s_indexArrayTable == null)
            {
                s_indexArrayTable = new ConcurrentDictionary<V2i, int[]>();
            }

            lock (s_indexArrayTable)
            {
                int[] indices;
                s_indexArrayTable.TryGetValue(size, out indices);

                if (indices != null)
                    return indices;
                else
                {
                    Report.Begin("Computing Index Array Table for {0}", size);
                    indices = OpcIndices.ComputeIndexArray(size, invalidPoints);
                    if (!s_indexArrayTable.ContainsKey(size))
                    {
                        s_indexArrayTable.TryAdd(size, indices);
                    }
                    Report.End();

                    return indices;
                }
            }
        }

        public static int[] GetTriangleStripIndexArray(V2i size)
        {
            if (s_triangleStripIndexArrayTable == null)
            {
                s_triangleStripIndexArrayTable = new ConcurrentDictionary<V2i, int[]>();
            }

            int[] indices;
            s_triangleStripIndexArrayTable.TryGetValue(size, out indices);

            try
            {
                if (indices != null)
                    return indices;
                else
                {
                    indices = OpcIndices.ComputeTriangleStripIndexArray(size);
                    s_triangleStripIndexArrayTable.TryAdd(size, indices);

                    return indices;
                }
            }
            catch (ArgumentException)
            {
                return s_triangleStripIndexArrayTable[size];
            }

            //if (s_triangleStripIndexArrayTable.ContainsKey(size))
            //    return s_triangleStripIndexArrayTable[size];
            //else
            //{
            //    s_triangleStripIndexArrayTable.Add(size, OpcTile.ComputeTriangleStripIndexArray(size));
            //    return s_triangleStripIndexArrayTable[size];
            //}
        }
    }
}
