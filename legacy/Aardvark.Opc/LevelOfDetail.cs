using Aardvark.Base;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;

namespace Aardvark.Opc
{
    public static class LevelOfDetail
    {
        public static Func<int, LevelOfDetailSettings, double> DistanceFunc;

        public class LevelOfDetailSettings : IFieldCodeable
        {
            public double Max;
            public double Quadratic;
            public double Linear;
            public double Constant;

            #region IFieldCodeable Members

            public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
            {
                return new[] 
                {
                    new FieldCoder(0, "Max", (c, o) => c.CodeDouble(ref ((LevelOfDetailSettings)o).Max)),
                    new FieldCoder(1, "Quadratic", (c, o) => c.CodeDouble(ref ((LevelOfDetailSettings)o).Quadratic)),
                    new FieldCoder(2, "Linear", (c, o) => c.CodeDouble(ref ((LevelOfDetailSettings)o).Linear)),
                    new FieldCoder(3, "Constant", (c, o) => c.CodeDouble(ref ((LevelOfDetailSettings)o).Constant)),
                };
            }

            #endregion
        }

        public class LevelOfDetailDistances : IFieldCodeable
        {
            public const string FileName = "LodDistances.xml";
            public string[] LodDistances;

            public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
            {
                return new[] 
                {
                    new FieldCoder(0, "LodDistances", (c, o) => c.CodeStringArray(ref ((LevelOfDetailDistances)o).LodDistances)),
                };
            }
        }
       
        /// <summary>
        /// Creates a distance metric value for a given hierarchy level depending on the polynomial equation
        /// specified through the coefficients in the supplied LevelOfDetailSettings structure. Assumes level 0
        /// to have the smallest distance
        /// </summary>
        public static double QuadraticDistance(int level, LevelOfDetailSettings settings)
        {
            return level * level * settings.Quadratic + level * settings.Linear + settings.Constant;
        }

        /// <summary>
        /// Creates a distance metric value for a given hierarchy level depending on the polynomial equation
        /// specified through the coefficients in the supplied LevelOfDetailSettings structure. Assumes Level 0
        /// to have the greatest distance.
        /// </summary>
        public static double QuadraticDistanceFalling(int level, LevelOfDetailSettings settings)
        {
            return (settings.Max / (level * level * settings.Quadratic + level * settings.Linear + 1.0)) + settings.Constant;
        }

        /// <summary>
        /// Returns an appropriate TileLayout value according to the 2d dimensions of a grid (z is ignored)
        /// </summary>
        public static TileLayout GetTileLayout(V3l dim)
        {
            if (dim.X >= 2 && dim.Y >= 2)
                return TileLayout.Full;
            else if (dim.X == 1 && dim.Y == 1)
                return TileLayout.Single;
            else if (dim.Y == 1)
                return TileLayout.Horizontal;
            else if (dim.X == 1)
                return TileLayout.Vertical;
            else
                throw new Exception("TileLayout not handled");
        }

        /// <summary>
        /// Computes the number of levels of a bottom-up hierarchy built on a grid of tiled elements. It is assumed
        /// that the root (top) of the hierarchy finally consists of one single tile. 
        /// </summary>
        public static int ComputeHierarchyDepth(V2i gridSize)
        {
            var depth = System.Math.Log(System.Math.Max(gridSize.X, gridSize.Y), 2);
            return System.Math.Ceiling(depth).ToInt();
        }

        public static ISg CreateBottomUpLodBoxHierarchy(Volume<ISg> vgsVolume, LevelOfDetailSettings settings)
        {
            var maxLevel = ComputeHierarchyDepth(vgsVolume.Size.XY.ToV2i());

            return LodBoxHierarchy(maxLevel-1, vgsVolume, settings, maxLevel-1);
        }

        public static ISg CreateBottomUpLodBoxHierarchyDataInLeaves(Volume<ISg> vgsVolume, LevelOfDetailSettings settings)
        {
            var maxLevel = ComputeHierarchyDepth(vgsVolume.Size.XY.ToV2i());

            return LodBoxHierarchy(maxLevel, vgsVolume, settings, maxLevel);
        }

        private static ISg LodBoxHierarchy(
            int level, Volume<ISg> vgsVolume, LevelOfDetailSettings settings, int maxLevel)
        {
            int detailCount = 1;
            if (vgsVolume.SX > 1) detailCount *= 2;
            if (vgsVolume.SY > 1) detailCount *= 2;
            if (vgsVolume.SZ > 1) detailCount *= 2;

            //detail count is 1 when space is not dividable
            #region detailCount == 1
            if (detailCount == 1)
            {
                //if level > 0 keep subsampling without subdivision
                if (level > 0) //|| ( level == 0 && (vgsVolume[0, 0, 0].TileSize.X > 256 || vgsVolume[0, 0, 0].TileSize.Y > 256)))
                {
                    var detailNodeList = LodBoxHierarchy(level - 1, vgsVolume, settings, maxLevel).IntoList();

                    var dist = DistanceFunc(level-1,settings);

                    Report.Line("LOD.cs Level " + level + " " + dist);

                    var box = new Sg.LodBox()
                    {
                        Name = "level" + level,
                        DetailNodes = detailNodeList,
                        Settings = new LodBoxSettings
                        {
                            Distance = dist,
                            DeciderCombination = "Distance, ScreenPixToTriSize, ScreenPixToTexPix, VisualImpact => Distance",
                        },
                    };

                    box["TileLayout"] = GetTileLayout(vgsVolume.Dim);
                    return box;            
                }

                return vgsVolume[0, 0, 0];
            }
            #endregion

            var detailNodes = new List<ISg>(detailCount);
            for (int i = 0; i < 8; i++)
            {
                bool valid = true;
                V3l origin = V3l.Zero;
                V3l length = vgsVolume.Size;

                //compute splitting dimensions for each axis
                for (int dim = 0; dim < 3; dim++)
                {
                    if ((i & (1 << dim)) == 0)
                    {
                        if (length[dim] > 1) length[dim] /= 2;
                    }
                    else
                    {
                        if (length[dim] > 1)
                        {
                            origin[dim] = length[dim] / 2;
                            length[dim] = length[dim] - origin[dim];
                        }
                        else
                            valid = false;
                    }
                }

                if (valid)
                {
                    ISg detailNode = 
                        LodBoxHierarchy(level - 1, vgsVolume.SubVolume(origin, length),settings, maxLevel);
                    detailNodes.Add(detailNode);
                }
            }

            {
                var dist = LevelOfDetail.DistanceFunc(level-1, settings);

                //Report.Line("LOD.cs Level " + level + " " + dist);

                bool isMax = false; // level == maxLevel;

                var box = new Sg.LodBox()
                {
                    Name = "level" + level,
                    DetailNodes = detailNodes,
                    Pinned = Sg.LodBox.PinnedOptions.Default,
                    Settings = new LodBoxSettings
                    {
                        Distance = dist,
                        DeciderCombination = !isMax ? "Distance, ScreenPixToTriSize, ScreenPixToTexPix, VisualImpact => Distance"
                        : "Distance, ScreenPixToTriSize, ScreenPixToTexPix, VisualImpact => false",
                    },
                };

               // s_numOfBoxes++;

                box["TileLayout"] = GetTileLayout(vgsVolume.Dim);

                //Report.Line("DIM: " + vgsVolume.Dim.ToString() + " "
                //    + box["TileLayout"].ToString() + " __lvl: "
                //    + level + " Desc.: " + box.DetailNodes.Count());

                var sg = Rsg.Apply(
                    new ResourcePinningEnabledValue()
                    {
                        Value = level == maxLevel
                    }, box);

                return box;
            }
        }

        public static void UpdateLodBoxAllow(Sg.LodBox node, bool allowTraversal)
        {
            //node.Settings.DeciderCombination = deciderCombination;

            node.AllowTraversal = allowTraversal;

            foreach (var subNode in node.DetailNodes)
            {
                var lodBox = subNode as Sg.LodBox;
                if(lodBox != null)
                {
                    UpdateLodBoxAllow(lodBox, allowTraversal);   
                }
            }
        }
    }
}
