using System;
using System.Collections.Generic;
using System.Linq;
using Aardvark.Runtime;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using Aardvark.Base;

namespace Aardvark.Opc
{
    public static class SgResourceManager
    {
        private static int s_maximumLifeTime = 2;
        private static int s_maxTotalMemory = 600000000;

        /// <summary>
        /// describes the desired maximum life time of an object before it can be disposed in seconds
        /// </summary>
        public static int MaximumLifeTime
        {
            get { return SgResourceManager.s_maximumLifeTime; }
            set { SgResourceManager.s_maximumLifeTime = value; }
        }

        /// <summary>
        /// Memory threshold in bytes. When reached FreeMemory() is called automatically
        /// </summary>
        public static int MaxTotalMemory
        {
            get { return SgResourceManager.s_maxTotalMemory; }
            set { SgResourceManager.s_maxTotalMemory = value; }
        }

        static DateTime s_startTime = DateTime.Now;
        static SgResourceManager()
        {

        }

        private static Dictionary<ISg, long> s_tileUsage
            = new Dictionary<ISg, long>();

        //public static void Register(ISg sg, TraversalState state)
        //{
        //    var replace = s_tileUsage.ContainsKey(sg) ? s_tileUsage[sg] : DateTime.MinValue;
        //    s_tileUsage[sg] = replace;
        //}

       // private static bool m_forceFreeMemory = false;
        public static void Use(ISg sg, TraversalState state)
        {
            //if (s_tileUsage.ContainsKey(sg))
            //    s_tileUsage.Remove(sg);

            s_tileUsage[sg] = (long)(DateTime.Now - s_startTime).TotalSeconds;

            //if (GC.GetTotalMemory(false) > 600000000)
            //    m_forceFreeMemory = true;
        }

        public static void FreeMemory(TraversalState state)
        {
            Report.Line(1, "Free");
            var x = (long)((DateTime.Now - s_startTime).TotalSeconds);

            var tilesDescriptionsWhichCouldBeRemoved =
                from tileKvp in s_tileUsage
                where x - tileKvp.Value > MaximumLifeTime
                select tileKvp;

            var tdcbrArray = tilesDescriptionsWhichCouldBeRemoved.ToArray();

            Report.Line(1, "USED TILES BEF: " + s_tileUsage.Count);
            Report.Line(1, "REMOVING " + tdcbrArray.Length + "TILES");
            foreach (var tileKvp in tdcbrArray)
            {
                // Report.Line(0, "Tile disposed " + (currentCycle - tileKvp.Value));
                var tile = tileKvp.Key;
                s_tileUsage.Remove(tile);
                tile.DisposeAndRemove(state);
            }
            Report.Line(1, "USED TILES AFT: " + s_tileUsage.Count);
        }

        public static void DisposeAll(TraversalState state)
        {
            foreach (var tileKvp in s_tileUsage)
            {
                var tile = tileKvp.Key;
                s_tileUsage.Remove(tile);
                tile.DisposeAndRemove(state);
            }
        }
    }
}
