using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aardvark.Base;
using Aardvark.Runtime;

namespace Aardvark.Opc.PatchHierarchy
{
    /// <summary>
    /// Helper class to manage/create various paths in an OPC folder
    /// </summary>
    public class OpcPaths
    {
        public class Identifiers
        {
            // patches/
            public const string PatchesSubDir = "patches";
            public const string CacheFileName = "cache.bin";
            public const string PatchHierarchyFileName = "patchhierarchy.xml";
            public const string ProfilLutFileName = "profilelut.bin";

            // per patch
            // Patch.xml
            // .aara files: Normals, Offset, Positions, Positions2d.....

            // KdTrees in root-patch directory

            public const string KdTreeExt = "aakd";
            public const string KdTreeN = "." + KdTreeExt;
            public const string KdTreeN2d = "-2d." + KdTreeExt;
            public const string KdTreeZero = "-0." + KdTreeExt;
            public const string KdTreeZero2d = "-0-2d." + KdTreeExt;

            // images/
            public const string ImagesSubDir = "images";
            public const string ImagePyramidFileName = "imagepyramid.xml";
        }

        #region Properties
        public string BasePath { get; private set; }

        public string PatchesSubDir { get; private set; }
        public string ImagesSubDir { get; private set; }

        public string PatchHierarchyPath { get; private set; }
        public List<string> ImagePyramidPaths { get; private set; }
        public string CachedPatchHierarchyPath { get; private set; }
        public string ProfileLutPath { get; private set; }

        public string RootPatchName { get; private set; }
        public string RootPatchPath { get; private set; }

        public string ShortName { get; private set; }

        private string m_kdTreeNPath;
        private string m_kdTreeN2dPath;
        private string m_kdTreeZeroPath;
        private string m_kdTreeZero2dPath;

        public string GetKdTreeNPath(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return m_kdTreeNPath;
            else
                return m_kdTreeN2dPath;
        }

        public string GetKdTreeZeroPath(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return m_kdTreeZeroPath;
            else
                return m_kdTreeZero2dPath;
        }
        #endregion

        /// <summary>
        /// Creates path helper from a given OPC folder
        /// </summary>
        public OpcPaths(string basePath)
        {
            ImagePyramidPaths = new List<string>();

            BasePath = basePath;

            PatchesSubDir = Path.Combine(basePath, Identifiers.PatchesSubDir);
            ImagesSubDir = Path.Combine(basePath, Identifiers.ImagesSubDir);

            CachedPatchHierarchyPath = Path.Combine(PatchesSubDir, Identifiers.CacheFileName);
            PatchHierarchyPath = Path.Combine(PatchesSubDir, Identifiers.PatchHierarchyFileName);

            ProfileLutPath = Path.Combine(PatchesSubDir, Identifiers.ProfilLutFileName);            

            ShortName = Path.Combine(Directory.GetParent(BasePath).Name, Path.GetFileName(basePath));

            var subDirs = StorageConfig.GetDirectories(ImagesSubDir);
            foreach (var sd in subDirs)
            {
                var dir = Path.Combine(ImagesSubDir, sd);
                ImagePyramidPaths.Add(Path.Combine(dir, Identifiers.ImagePyramidFileName));
            }
        }

        /// <summary>
        /// Construction of specific paths (such as kdtree paths) is only possible
        /// with the name of an OPC' root patch
        /// </summary>
        public void SetRootPatchName(string rootPatchName)
        {
            RootPatchName = rootPatchName;
            RootPatchPath = Path.Combine(PatchesSubDir, RootPatchName);
            var kdTreeFileStub = Path.Combine(RootPatchPath, RootPatchName);

            m_kdTreeNPath = kdTreeFileStub + Identifiers.KdTreeN;
            m_kdTreeN2dPath = kdTreeFileStub + Identifiers.KdTreeN2d;

            m_kdTreeZeroPath = kdTreeFileStub + Identifiers.KdTreeZero;
            m_kdTreeZero2dPath = kdTreeFileStub + Identifiers.KdTreeZero2d;
        }

        /// <summary>
        /// Returns OpcPaths with with most paths created from a given OPC folder. 
        /// All remaining paths need a RootPatchName to be created.
        /// </summary>
        public static OpcPaths From(string baseDirectory)
        {
            return new OpcPaths(baseDirectory);
        }

        /// <summary>
        /// Returns OpcPaths with all paths created from a given OPC folder. 
        /// This involves Loading of a PatchHierarchyInfo cache from disk. If the cache does not 
        /// exist RootPatchName is read from xml. 
        /// </summary>
        public static OpcPaths FullPathsFrom(string baseDirectory)
        {
            var paths = new OpcPaths(baseDirectory);
            var rootPatchName = string.Empty;

            //get RootPatchName from cache file or read from xml
            if (StorageConfig.FileExists(paths.CachedPatchHierarchyPath))
            {
                var info = Load.As<PatchHierarchyInfo>(paths.CachedPatchHierarchyPath);
                rootPatchName = info.RootPatch;
            }
            else
                rootPatchName = PatchHierarchyXML.From(baseDirectory).RootPatchName;

            Requires.NotEmpty(rootPatchName, "RootPatchName could not be retrieved");

            paths.SetRootPatchName(rootPatchName);

            return paths;
        }



        #region KdTree methods

        public int SelectLevelK()
        {
            var kList = FindLevelKPaths();
            if (kList.Count > 0)
                return kList.Min();

            return -1;
        }

        public List<int> FindLevelKPaths()
        {
            var kList = new List<int>();
            for (int i = 1; i < 6; i++)
            {
                var kdTreePath = GetAggKdTreePath(i, PositionsType.V3dPositions);
                if (StorageConfig.FileExists(kdTreePath))
                    kList.Add(i);
            }

            return kList;
        }

        /// <summary>
        /// Gets the aggregate kdtree path according to the specified level and positions type. Level -1 indicates
        /// the path for a Level N kdtree
        /// </summary>
        public string GetAggKdTreePath(int level, PositionsType posType)
        {
            return GetKdTreePath(RootPatchName, level, posType);
        }

        /// <summary>
        /// Gets kdtree path for a certain patch according to name, level and postype.
        /// </summary>
        public string GetKdTreePath(string patchName, int level, PositionsType posType)
        {
            var fileName = GetKdTreeFileName(patchName, level, posType);
            var kdTreePath = Path.Combine(RootPatchPath, fileName);

            return kdTreePath;
        }

        public static string GetKdTreeFileName(string patchName, int level, PositionsType posType)
        {
            string levelSub = level > -1 ? "-" + level : string.Empty;
            string positionSub = posType == PositionsType.V3dPositions ? string.Empty : "-2d";

            var fileName = string.Format("{0}{1}{2}.{3}", patchName, levelSub, positionSub, Identifiers.KdTreeExt);

            return fileName;
        }

        #endregion
    }
}
