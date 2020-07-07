using Aardvark.Base;
using Aardvark.Runtime;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aardvark.Opc.PatchHierarchy
{
    public class PatchHierarchyInfo : IFieldCodeable
    {
        public List<string> TagList;
        public string AcquisitionDate;
        public List<double> AvgGeometrySizes;
        public List<double> AvgPixelSizes;
        public string RootPatch;
        public PatchTree PatchTree;

        private int m_depth = -1;
        public int Depth
        {
            get 
            {
                if (m_depth == -1)
                    m_depth = PatchTree.ComputeDepth();

                return m_depth;
            }
        }

        public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
        {
            yield return new FieldCoder(0, "TagList", (c, o) => c.CodeList_of_String_(ref ((PatchHierarchyInfo)o).TagList));
            yield return new FieldCoder(1, "AcquisitionDate", (c, o) => c.CodeString(ref ((PatchHierarchyInfo)o).AcquisitionDate));

            yield return new FieldCoder(2, "AvgGeometrySizes", (c, o) => c.CodeList_of_Double_(ref ((PatchHierarchyInfo)o).AvgGeometrySizes));
            yield return new FieldCoder(3, "AvgPixelSizes", (c, o) => c.CodeList_of_Double_(ref ((PatchHierarchyInfo)o).AvgPixelSizes));
            yield return new FieldCoder(4, "RootPatch", (c, o) => c.CodeString(ref ((PatchHierarchyInfo)o).RootPatch));

            yield return new FieldCoder(5, "PatchTree", (c, o) => c.CodeT(ref ((PatchHierarchyInfo)o).PatchTree));
        }

        public static PatchHierarchyInfo FromCacheFile(string filePath)
        {
            var patchHierarchyInfo = Load.As<PatchHierarchyInfo>(filePath);

            if (patchHierarchyInfo == null)
                Report.Warn("PatchHierarchyInfo: Wrong format cache file at " + filePath);

            return patchHierarchyInfo;
        }

        public static PatchHierarchyInfo FromXmlFile(OpcPaths opcPaths)
        {
            return FromXmlFile(new PatchHierarchyXML(opcPaths));
        }

        public static PatchHierarchyInfo FromXmlFile(PatchHierarchyXML ph)
        {
            var patchHierarchyInfo = new PatchHierarchyInfo();

            patchHierarchyInfo.AvgGeometrySizes = ph.AverageGeometrySizes;

            patchHierarchyInfo.AcquisitionDate = ph.AcquisitionDate;
            patchHierarchyInfo.TagList = ph.TagList;
            patchHierarchyInfo.RootPatch = ph.RootPatchName;

            patchHierarchyInfo.AvgPixelSizes = (new ImagePyramidXML(ph.OpcPaths)).AveragePixelSizes;

            patchHierarchyInfo.PatchTree = ph.PatchTree;

            return patchHierarchyInfo;
        }

        /// <summary>
        /// Builds PatchHierarchyInfo from the xml file (if it doesn't exist yet) and saves it as cache.bin
        /// </summary>
        /// <param name="overrideExisting">Creates and saves it, even if a cache.bin allready exists.</param>
        public static void BuildAndSaveCache(OpcPaths paths, bool overrideExisting = true)
        {
            if (overrideExisting || !StorageConfig.FileExists(paths.CachedPatchHierarchyPath))
            {
                var patchHierarchyInfo = FromXmlFile(paths);

                if (patchHierarchyInfo == null)
                {
                    Report.Error("PatchHierarchyInfo: Building cache failed.");
                    return;
                }

                patchHierarchyInfo.Save(
                    paths.CachedPatchHierarchyPath, waitMode: WaitMode.WaitUntilFinished);
            }
        }

        /// <summary>
        /// Loads PatchHierarchyInfo from cache file or xml, if cache doesn't exist.
        /// </summary>
        public static PatchHierarchyInfo BuildOrLoadCache(OpcPaths paths)
        {
            PatchHierarchyInfo patchHierarchyInfo = null;

            if (StorageConfig.FileExists(paths.CachedPatchHierarchyPath))
                patchHierarchyInfo = FromCacheFile(paths.CachedPatchHierarchyPath);

            if (patchHierarchyInfo == null)
            {
                Report.BeginTimed("PatchHierarchyInfo: Loading XML for " + paths.ShortName);
                patchHierarchyInfo = FromXmlFile(paths);
                Report.End();
            }

            if (patchHierarchyInfo == null)
            {
                Report.Error("PatchHierarchyInfo: Loading cache and XML failed for OPC " + paths.ShortName);
                return null;
            }

            return patchHierarchyInfo;
        }

        public List<PatchTree> RetrievePatchTreesOfLevel(int level)
        {
            return RetrievePatchTreesOfLevel(level, x => true);
        }

        public List<PatchTree> RetrievePatchTreesOfLevel(int level, Func<PatchTree, bool> filterFunc)
        {
            var collector = new List<PatchTree>();
            RetrievePatchesRecurse(PatchTree, Depth, level, collector, filterFunc);
            return collector;
        }

        private static void RetrievePatchesRecurse(PatchTree patchTree, int currentLevel,
           int desiredLevel, List<PatchTree> collector /* schirching around!! */, Func<PatchTree, bool> filterFunc)
        {
            var filteredSubNodes = patchTree.SubNodes.Where(filterFunc);

            if (patchTree.PatchPath == null)
                Report.Warn("patch path null");

            if (currentLevel == desiredLevel)
                collector.Add(patchTree);
            else
            {
                filteredSubNodes
                    .ForEach(x => RetrievePatchesRecurse(x, currentLevel - 1, desiredLevel, collector, filterFunc));
            }
        }
    }
}
