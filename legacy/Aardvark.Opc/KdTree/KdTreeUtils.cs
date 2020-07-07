using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Linq;
using Aardvark.Runtime;
using Aardvark.Opc.PatchHierarchy;
using Aardvark.VRVis;
using Aardvark.Base;
using System.Threading;

namespace Aardvark.Opc.KdTree
{
    public static class KdTreeUtils
    {
        /// <summary>
        /// Loads level N kdtree. If level N kdtree does not exist it is built from rootpatch.
        /// </summary>
        public static KdIntersectionTree BuildOrLoadKdTreeN(OpcPaths opcPaths, PositionsType posType, PatchFileInfo patchFileInfo)
        {
            KdIntersectionTree kdiTree = null;
            var kdTreeNPatch = opcPaths.GetKdTreeNPath(posType);
            if (!StorageConfig.FileExists(kdTreeNPatch))
            {
                kdiTree = KdTreeUtils.BuildKdTreeForPatch(opcPaths.RootPatchName, -1, patchFileInfo, opcPaths, posType, saveTriangles: true);
                kdiTree.Save(opcPaths.RootPatchName);
            }
            else
                kdiTree = Load.As<KdIntersectionTree>(opcPaths.GetKdTreeNPath(posType));

            return kdiTree;
        }

        public static void BuildKdtreeN(OpcPaths opcPaths, PositionsType posType, bool overrideExisting = false, float maxTriangleSize = float.PositiveInfinity)
        {
            var hierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);
            var patchInfo = hierarchyInfo.PatchTree.Info;
            opcPaths.SetRootPatchName(hierarchyInfo.RootPatch);

            if (posType == PositionsType.V2dPositions && !patchInfo.Has2dData)
            {
                Report.Warn("KdTreeUtils: 2d KdTreeN needs 2d data to be able to get built.");
                return;
            }

            if (overrideExisting || !StorageConfig.FileExists(opcPaths.GetKdTreeNPath(posType)))
                BuildKdTreeForPatch(opcPaths.RootPatchName, -1, patchInfo, opcPaths, posType, true, true, maxTriangleSize);
        }

        public static bool HasKdTreeForGeoResolution(OpcPaths opcPaths, double minGeometricResolution, PositionsType posType)
        {
            var hierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);
            var level = GetLevelFromResolution(hierarchyInfo.AvgGeometrySizes, minGeometricResolution);
            return HasKdTreeForLevel(opcPaths, level, posType, hierarchyInfo);
        }

        public static bool HasKdTreeForLevel(OpcPaths opcPaths, int level, PositionsType posType, PatchHierarchyInfo hierarchyInfo = null)
        {
            if (hierarchyInfo == null)
                hierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);

            opcPaths.SetRootPatchName(hierarchyInfo.RootPatch);
            var kdTreeSetPath = opcPaths.GetAggKdTreePath(level, posType);

            return StorageConfig.FileExists(kdTreeSetPath);
        }

        /// <param name="minGeometricResolution">Used to select an appropriate level in the hierarchy to meet the specified minimum resolution criterion.</param>
        /// <param name="progressObserver">Reports progress in increments which sum up to 1.</param>
        public static void BuildKdTreeForGeoResolution(
            OpcPaths opcPaths, double minGeometricResolution, PositionsType posType,
            bool lazy = true, bool overrideExisting = false, IObserver<float> progressObserver = null,
            CancellationToken cancelToken = default(CancellationToken))
        {
            var hierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);
            var level = GetLevelFromResolution(hierarchyInfo.AvgGeometrySizes, minGeometricResolution);
            BuildKdTreeForLevel(opcPaths, level, posType, lazy, overrideExisting, progressObserver, cancelToken, hierarchyInfo);
        }

        /// <param name="level">Selects hierarchy Level the kdtree is built of (-1 shouldn't be used, use BuildKdtreeN instead).</param>
        /// <param name="progressObserver">Reports progress in increments which sum up to 1.</param>
        public static void BuildKdTreeForLevel(
            OpcPaths opcPaths, int level, PositionsType posType,
            bool lazy = true, bool overrideExisting = false, IObserver<float> progressObserver = null,
            CancellationToken cancelToken = default(CancellationToken), PatchHierarchyInfo hierarchyInfo = null,
            float maxTriangleSize = float.PositiveInfinity)
        {
            #region Preconditions

            if (hierarchyInfo == null)
                hierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);

            if (posType == PositionsType.V2dPositions && !hierarchyInfo.PatchTree.Info.Has2dData)
            {
                Report.Warn("KdTreeUtils: 2d KdTree needs 2d data to be able to get built.");
                progressObserver.OnNext(1f);
                progressObserver.OnCompleted();
                return;
            }

            opcPaths.SetRootPatchName(hierarchyInfo.RootPatch);

            var kdTreeSetPath = opcPaths.GetAggKdTreePath(level, posType);
            if (!overrideExisting && StorageConfig.FileExists(kdTreeSetPath))
            {
                progressObserver.OnNext(1f);
                progressObserver.OnCompleted();
                return;
            }

            #endregion

            hierarchyInfo.PatchTree.CreatePatchPaths(opcPaths.PatchesSubDir);

            // Get geometry patchinformations for certain level with a valid boundingbox
            var patchTrees = hierarchyInfo.RetrievePatchTreesOfLevel(level)
                .Where(x => !x.Info.GlobalBoundingBox.IsInvalid);

            IIntersectableObjectSet kdTree;
            var buildFlags =
                KdIntersectionTree.BuildFlags.Hierarchical |
                KdIntersectionTree.BuildFlags.EmptySpaceOptimization;

            // Decide lazy or eager
            if (lazy)
            {
                kdTree = BuildLazyKdTreeSet(patchTrees, opcPaths, level, posType, overrideExisting, progressObserver, cancelToken, maxTriangleSize);
                buildFlags |= KdIntersectionTree.BuildFlags.OptimalRaytracing;
            }
            else
            {
                kdTree = BuildConcreteKdTreeSet(patchTrees, opcPaths, level, posType, overrideExisting, progressObserver, cancelToken, maxTriangleSize);
                buildFlags |= KdIntersectionTree.BuildFlags.MediumIntersection;
            }

            // Save Kd-Tree
            var aggrTree = new KdIntersectionTree(kdTree, buildFlags);
            aggrTree.Save(kdTreeSetPath, waitMode: WaitMode.WaitUntilFinished);
        }

        public static int GetLevelFromResolution(List<double> avgGeometrySizes, double resolution)
        {
            var level = -1;

            var sizes = avgGeometrySizes.ToArray();
            for (int i = 0; i < sizes.Length; i++)
            {
                if (resolution >= sizes[i])
                    level = i;
                else
                    break;
            }

            return level;
        }

        public static Subject<float> GetKdTreeProgressObserver(OpcPaths opcPaths, int level, PositionsType posType)
        {
            var opcName = Path.GetFileName(opcPaths.BasePath);
            float totalProgress = 0f;

            var progress = new Subject<float>();
            progress.Sample(TimeSpan.FromSeconds(1.0)).Subscribe(inc =>
            {
                totalProgress += inc;
                Report.Line("{0} - {1} - {2} - Progress {3:0.0}%",
                    opcName, level, posType.ToString(), totalProgress * 100.0);

                //Report.ProgressDelta(tup.E1);
            });

            return progress;
        }

        #region private methods

        private static LazyKdTreeSet BuildLazyKdTreeSet(IEnumerable<PatchTree> patches, OpcPaths opcPaths,
            int level, PositionsType posType, bool overrideExisting, IObserver<float> progressObserver, CancellationToken cancelToken = default(CancellationToken),
            float maxTriangleSize = float.PositiveInfinity)
        {
            var placeHolders = new List<LazyKdTreeSet.KdTreePlaceHolder>();
            var progressInc = 1f / patches.Count();
            
            foreach (var p in patches)
            {
                cancelToken.ThrowIfCancellationRequested();

                var kdtreePath = opcPaths.GetKdTreePath(p.Id, level, posType);
                if (overrideExisting || !StorageConfig.FileExists(kdtreePath))
                    BuildKdTreeForPatch(p.Id, level, p.Info, opcPaths, posType, saveTriangles: false, saveKdTree: true, maxTriangleSize: maxTriangleSize);

                // Create place holders
                placeHolders.Add(new LazyKdTreeSet.KdTreePlaceHolder()
                {
                    BoundingBox = p.Info.GetGlobalBoundingBox(posType),
                    Affine = p.Info.GetLocal2Global(posType),
                    Path = opcPaths.GetKdTreePath(p.Id, level, posType),
                    ObjectSetPath = p.GetPositionPath(posType)
                });

                if (progressObserver != null)
                    progressObserver.OnNext(progressInc);
            }

            if (progressObserver != null)
                progressObserver.OnCompleted();

            return new LazyKdTreeSet(placeHolders);
        }

        /// <summary>
        /// NOT TESTED
        /// </summary>
        private static KdTreeSet BuildConcreteKdTreeSet(IEnumerable<PatchTree> patches, OpcPaths opcPaths,
            int level, PositionsType posType, bool overrideExisting, IObserver<float> progressObserver, CancellationToken cancelToken = default(CancellationToken),
            float maxTriangleSize = float.PositiveInfinity)
        {
            var kdTrees = new List<ConcreteKdIntersectionTree>();
            var progressInc = 1f / patches.Count();

            foreach (var p in patches)
            {
                cancelToken.ThrowIfCancellationRequested();

                KdIntersectionTree tree;
                var kdtreePath = opcPaths.GetKdTreePath(p.Id, level, posType);
                if (overrideExisting || !StorageConfig.FileExists(kdtreePath))
                    tree = BuildKdTreeForPatch(p.Id, level, p.Info, opcPaths, posType, saveTriangles: true, saveKdTree: false, maxTriangleSize: maxTriangleSize);
                else tree = Load.As<KdIntersectionTree>(kdtreePath);

                kdTrees.Add(tree.ToConcreteKdIntersectionTree());

                if (progressObserver != null)
                    progressObserver.OnNext(progressInc);
            }

            if (progressObserver != null)
                progressObserver.OnCompleted();

            return new KdTreeSet(kdTrees);
        }

        /// <summary>
        /// Builds kdtree for specific patch. 
        /// Kdtree is built according to specified positions type and hierarchy level.
        /// </summary>        
        private static KdIntersectionTree BuildKdTreeForPatch(
            string patchName, int level, PatchFileInfo info, OpcPaths paths,
            PositionsType posType, bool saveTriangles = false, bool saveKdTree = true,
            float maxTriangleSize = float.PositiveInfinity)
        {
            var path = Path.Combine(paths.PatchesSubDir, patchName);

            var patchGeometry = new TriangleSet();
            //var patchGeometryTest = new TriangleSet();
            if ((maxTriangleSize < float.PositiveInfinity)&&(maxTriangleSize > 0.000001f))
            {
                patchGeometry = PatchLoadingStrategy.LoadPatchTriangleSetWithoutOversizedTriangles(info, path, posType, maxTriangleSize);
                //patchGeometryTest = PatchLoadingStrategy.LoadPatchTriangleSet(info, path, posType);
            }
            else
            {
                patchGeometry = PatchLoadingStrategy.LoadPatchTriangleSet(info, path, posType);
            }
            
            KdIntersectionTree kdIntTree =
                new KdIntersectionTree(patchGeometry,
                        KdIntersectionTree.BuildFlags.MediumIntersection |
                        KdIntersectionTree.BuildFlags.Hierarchical);

            if (!saveTriangles)
                kdIntTree.ObjectSet = null;

            if (saveKdTree)
            {
                var kdTreePath = paths.GetKdTreePath(patchName, level, posType);
                kdIntTree.Save(kdTreePath);
            }

            return kdIntTree;
        }

        #endregion
    }
}