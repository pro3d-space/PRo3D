using Aardvark.Algodat;
using Aardvark.Base;
using Aardvark.Lod;
using Aardvark.Opc.KdTree;
using Aardvark.Runtime;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;

namespace Aardvark.Opc.PatchHierarchy
{
    public class OpcDescriptor
    {
        public string Id;
        public string Path;
        public string RootPatchName;
        public Box3d BoundingBox
        {
            get
            {
                return PosType == PositionsType.V3dPositions
                    ? BoundingBoxXYZ
                    : BoundingBoxSvBR;
            }
        }
        public Box3d BoundingBoxXYZ;
        public Box3d BoundingBoxSvBR;
        public PositionsType PosType;
        public bool IsZipped;

        public static OpcDescriptor From(string basePath, PositionsType posType = PositionsType.V3dPositions)
        {
            return OpcDescriptor.From(basePath, basePath, posType);
        }

        public static OpcDescriptor From(string id, string basePath, PositionsType posType, bool isZipped = false)
        {
            if (isZipped)
            {
                var fileStreamHandler = StorageConfig.FileStreamHandler as ZipContainerStreamHandler;
                if (fileStreamHandler == null)
                {
                    fileStreamHandler = new ZipContainerStreamHandler();
                    StorageConfig.FileStreamHandler = fileStreamHandler;
                }

                fileStreamHandler.RegisterFile(basePath);
                Report.Line("Found and registered OPC Zip files (" + basePath + ").");
            }

            // Load patchHierarchy.xml and Patch.xml(from root patch) for some basic infos
            var phiXML = PatchHierarchyXML.From(basePath);
            var info = PatchFileInfo.FromFile(phiXML.RootPatchPath);

            return new OpcDescriptor()
            {
                Id = id,
                Path = basePath,
                RootPatchName = phiXML.RootPatchName,

                PosType = posType,
                IsZipped = isZipped,
                BoundingBoxXYZ = info.GlobalBoundingBox,
                BoundingBoxSvBR = info.GlobalBoundingBox2d
            };
        }
    }

    public enum KdTreeLevel { Lvl_0, Lvl_K, Lvl_N };

    [RegisterTypeInfo]
    public class PatchHierarchy : Instance, IRayCastable
    {
        public const string Identifier = "PatchHierarchy";

        #region public properties

        public Symbol OpcId { get; private set; }

        public OpcPaths Paths { get; private set; }
        public string DirectoryPath
        {
            get
            {
                return Paths.BasePath;
            }
        }

        public Box3d BoundingBox
        {
            get
            {
                return PositionType == PositionsType.V3dPositions
                    ? m_globalBoundingBoxXYZ
                    : m_globalBoundingBoxSvBR;
            }
        }
        public PositionsType PositionType { get; private set; }

        public PatchHierarchyInfo PatchHierarchyInfo
        {
            get
            {
                if (!IsStreamingInProgress)
                    StartAsyncLoadingPatchHierarchyInfo();

                if (!m_loadPatchHierarchyInfoTask.IsCompleted)
                    Report.Warn("PatchHierarchy: Waiting for PatchHierarchyInfo to be loaded for OPC " + Paths.ShortName);

                return m_loadPatchHierarchyInfoTask.Result;
            }
        }

        public double MinSv
        {
            get
            {
                return m_globalBoundingBoxSvBR.Min.X;
            }
        }
        public double MaxSv
        {
            get
            {
                return m_globalBoundingBoxSvBR.Max.X;
            }
        }

        public KdTreeAttribute KdTreeN { get; private set; }
        
        public KdTreeAttribute KdTreeK
        {
            get
            {
                // start loading
                SetupAsyncLoadingKdTree_K();
                
                // loading finished
                if (m_loadKdTreeKTask.IsCompleted)
                    return m_loadKdTreeKTask.Result;

                // keep waiting
                return null;
            }
        }

        public KdTreeAttribute KdTree0 { get; private set; }

        public ProfileLookUpTable ProfileLookUp { get; private set; }

        public bool IsStreamingInProgress { get; private set; }
        public bool IsStreamingFinished { get; private set; }
        public IObservable<Symbol> StreamingFinishedEvent
        {
            get { return m_isStreamedEvent; }
        }

        /// <summary>
        /// Checks if there is a LoD distance metric. 
        /// </summary>
        public bool HasLoDMetric
        {
            get
            {
                return !LoDDistanceMetric.IsEmptyOrNull();
            }
        }

        #endregion

        #region public fields

        public IOpcSgBuilderStrategy SceneGraphBuilder;
        public bool IgnoreViewFrustumCulling;
        public List<float> LoDDistanceMetric;

        public bool LoadKdTreeLevel_0;
        public bool LoadKdTreeLevel_K = false;

        #endregion

        #region private fields

        private Box3d m_globalBoundingBoxXYZ;
        private Box3d m_globalBoundingBoxSvBR;

        private Subject<Symbol> m_isStreamedEvent = new Subject<Symbol>();
        private Task<PatchHierarchyInfo> m_loadPatchHierarchyInfoTask = null;
        private Task<KdTreeAttribute> m_loadKdTreeKTask = null;

        #endregion

        #region constructors

        public PatchHierarchy(OpcDescriptor desc)
            : base(Identifier)
        {
            Requires.That(StorageConfig.DirectoryExists(desc.Path), desc.Path + " cannot be found");

            OpcId = desc.Id;
            PositionType = desc.PosType;

            Paths = OpcPaths.From(desc.Path);
            Paths.SetRootPatchName(desc.RootPatchName);

            m_globalBoundingBoxXYZ = desc.BoundingBoxXYZ;
            m_globalBoundingBoxSvBR = desc.BoundingBoxSvBR;

            LoadKdTreeLevel_0 = true;
            IsStreamingFinished = false;
            IsStreamingInProgress = false;

            SetupAsyncLoadingPatchHierarchyInfo();
        }

        public PatchHierarchy(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
            : this(desc)
        {
            SceneGraphBuilder = sceneGraphBuilder;
        }

        #endregion

        #region public methods

        public static PatchHierarchy From(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
        {
            return new PatchHierarchy(desc, sceneGraphBuilder);
        }

        public ConcreteKdIntersectionTree GetKdTree(KdTreeLevel level)
        {
            ConcreteKdIntersectionTree kdTreeList = null;

            switch (level)
            {
                case KdTreeLevel.Lvl_0:
                    if (KdTree0 != null)
                        kdTreeList = KdTree0.KdTree;
                    break;
                case KdTreeLevel.Lvl_K:
                    SetupAsyncLoadingKdTree_K();
                    if (!m_loadKdTreeKTask.IsCompleted)
                        Report.Warn("PatchHierarchy: Waiting for KdTree Lvl-K to be loaded for OPC " + Paths.ShortName);

                    var kdTreeK = m_loadKdTreeKTask.Result;
                    if (kdTreeK != null)
                    {
                        kdTreeList = kdTreeK.KdTree;
                        break;
                    }

                    // if there is no Lvl-K, fallback to Lvl-N
                    goto case KdTreeLevel.Lvl_N;
                default: // default is Lvl-N
                case KdTreeLevel.Lvl_N:
                    if (KdTreeN != null)
                        kdTreeList = KdTreeN.KdTree;
                    break;
            }

            return kdTreeList;
        }

        #region EqualityComparer

        public static ByIdComparer ById = new ByIdComparer();

        public class ByIdComparer : IEqualityComparer<PatchHierarchy>
        {
            public bool Equals(PatchHierarchy ph1, PatchHierarchy ph2)
            {
                return ph1.OpcId == ph2.OpcId;
            }
            public int GetHashCode(PatchHierarchy ph)
            {
                return ph.OpcId.GetHashCode();
            }
        }

        #endregion

        #region IRayCastable

        public string GetKey()
        {
            return DirectoryPath;
        }

        #endregion

        #endregion

        #region private methods

        private void OnStreamingFinished()
        {
            IsStreamingInProgress = false;
            IsStreamingFinished = true;
            m_isStreamedEvent.OnNext(OpcId);
            OpcEvents.StreamingFinished.Fire(OpcId.ToString());
        }

        private void SetupAsyncLoadingPatchHierarchyInfo()
        {
            if (m_loadPatchHierarchyInfoTask == null)
            {
                m_loadPatchHierarchyInfoTask = new Task<PatchHierarchyInfo>(() =>
                    PatchHierarchyInfo.BuildOrLoadCache(Paths));
            }
        }

        private void StartAsyncLoadingPatchHierarchyInfo()
        {
            if (m_loadPatchHierarchyInfoTask.Status == TaskStatus.Created)
            {
                try { m_loadPatchHierarchyInfoTask.Start(); }
                catch (InvalidOperationException)
                {
                    Report.Warn("PatchHierarchy: PatchHierarchyInfoTask seems to have been started more than once.");
                }
            }
        }

        private static KdIntersectionTree LoadKdTree0(OpcPaths paths, PositionsType posType)
        {
            var kdIntTree = Load.As<KdIntersectionTree>(paths.GetKdTreeZeroPath(posType));

            if (kdIntTree == null)
            {
                Report.Error("PatchHierarchy: Loading " +
                    (posType == PositionsType.V3dPositions ? "3d" : "2d") +
                    " KdTree-0 failed for " + paths.ShortName);
                return null;
            }

            // update place-holder-paths of LazyKdTreeSets
            if (kdIntTree.ObjectSet is LazyKdTreeSet)
            {
                ((LazyKdTreeSet)kdIntTree.ObjectSet)
                    .UpdatePlaceHolderPaths(paths.BasePath);
            }
            else Report.Error("PatchHierarchy: ObjectSet of KdIntersectionTree is not a LazyKdTreeSet.");

            return kdIntTree;
        }

        private void SetupAsyncLoadingKdTree_K(bool createHotTask = true)
        {
            if (m_loadKdTreeKTask == null)
            {
                m_loadKdTreeKTask = new Task<KdTreeAttribute>(() =>
                {
                    var kdIntTree = LoadKdTreeK(Paths);
                    if (kdIntTree == null)
                        return null;
                    return new KdTreeAttribute(this, kdIntTree);
                });

                if (createHotTask)
                    StartAsyncLoadingKdTree_K();
            }
            else
            {
                if (createHotTask && !IsStreamingInProgress)
                    StartAsyncLoadingKdTree_K();
            }
        }

        private void StartAsyncLoadingKdTree_K()
        {
            if (m_loadKdTreeKTask.Status == TaskStatus.Created)
            {
                try { m_loadKdTreeKTask.Start(); }
                catch (InvalidOperationException)
                {
                    Report.Warn("PatchHierarchy: LoadKdTreeKTask seems to have been started more than once.");
                }
            }
        }

        private static KdIntersectionTree LoadKdTreeK(OpcPaths paths)
        {
            var lvl_K = paths.SelectLevelK();

            if (lvl_K == -1)
            {
                Report.Warn("PatchHierarchy: No Level-k Kd-Tree available for OPC " + paths.ShortName);
                return null;
            }

            var pathKdTreeK = paths.GetAggKdTreePath(lvl_K, PositionsType.V3dPositions);
            var kdIntTree = Load.As<KdIntersectionTree>(pathKdTreeK);

            if (kdIntTree == null)
                Report.Warn("PatchHierarchy: Couldn't load Kd-Tree K for OPC " + paths.ShortName);

            return kdIntTree;
        }

        #endregion

        [Rule(typeof(PatchHierarchy))]
        public class PatchHierarchyRule : IRule, IPickable, IGetKdTreeList
        {
            #region private fields

            private PatchHierarchy m_instance;
            private ISg m_asyncHierarchy;
            private PickingInfo m_pickingInfo;
            private CancellationTokenSource m_cancelStreaming = new CancellationTokenSource();

            private static Telemetry.CpuTime s_cpu_LoadKdTree0 = new Telemetry.CpuTime().Register("PatchHierarchyRule: Load KdTree0", true, true);
            private static Telemetry.CpuTime s_cpu_LoadOpc = new Telemetry.CpuTime().Register("PatchHierarchyRule: Load Opc", true, true);

            #endregion

            public string Name { get; private set; }

            public PatchHierarchyRule(PatchHierarchy instance, AbstractTraversal traversal)
            {
                m_instance = instance;

                Name = Path.GetFileName(Path.GetFullPath(Path.Combine(instance.DirectoryPath, @"..")));

                if (m_instance.LoadKdTreeLevel_K)
                    m_instance.SetupAsyncLoadingKdTree_K(false);

                m_asyncHierarchy = SetupRenderNode(m_instance, this);
            }

            #region IRule

            public void InitForPath(AbstractTraversal t) { } //Nop

            public ISg SetParameters(AbstractTraversal t)
            {
                return m_asyncHierarchy;
            }

            public bool DisposeAndRemove(DisposeAndRemoveTraversal t)
            {
                m_cancelStreaming.Cancel();

                var childNodes = m_asyncHierarchy;
                m_asyncHierarchy = EmptyLeaf.Singleton;
                return t.TryDisposeAndRemoveRule(m_instance, t, childNodes);
            }

            #endregion

            #region IGetKdTreeList

            public List<ConcreteKdIntersectionTree> GetKdTreeList(GetKdTreeTraversal t)
            {
                var filter =
                    t.Filter == "Level0" ? KdTreeLevel.Lvl_0 :
                    t.Filter == "LevelK" ? KdTreeLevel.Lvl_K :
                    KdTreeLevel.Lvl_N;

                var kdTree = m_instance.GetKdTree(filter);

                if (kdTree == null)
                    return new List<ConcreteKdIntersectionTree> { };
                else return new List<ConcreteKdIntersectionTree> { kdTree };
            }

            #endregion

            #region IPickable

            public PickingInfo PickingInfo
            {
                get
                {
                    return m_pickingInfo;
                }
            }

            #endregion

            #region private methods

            private static ISg SetupRenderNode(PatchHierarchy patchHier, PatchHierarchyRule patchHierRule)
            {
                var hierarchy = SetupStreamingJob(patchHier, patchHierRule);

                // center BB to avoid float conversion problem on transfer to GPU
                var bbCenter = patchHier.BoundingBox.Center;
                var placeHolder = Rsg.Apply(
                    Rsg.Attribute.PushTrafo3d(Trafo3d.Translation(bbCenter)),
                    Primitives.WireBox(patchHier.BoundingBox - bbCenter, C4b.Gray).ToVertexGeometrySet()
                    );

                return new BoundingBoxAttribute(
                                        new GCHandle(
                                            new LoD(placeHolder, //simple
                                                    new AsyncStreamingNode(hierarchy, placeHolder), //detail
                                                    new TrivialNotSufficientDecider()
                                            ) { IsPinned = patchHier.IgnoreViewFrustumCulling })
                                        {
                                            DebugName = "PatchHierarchyLoDs " + patchHier.OpcId,
                                            IgnoreFrustumCulling = patchHier.IgnoreViewFrustumCulling,
                                            IsPinned = false
                                        }
                                    );
            }

            private static StreamingJob SetupStreamingJob(PatchHierarchy patchHier, PatchHierarchyRule patchHierRule)
            {
                var cancelToken = patchHierRule.m_cancelStreaming.Token;

                var hierarchy = new StreamingJob(
                    () =>
                    {
                        Report.BeginTimed("PatchHierarchyRule: Streaming OPC {0}", patchHier.OpcId);
                        Kernel.CQ.Enqueue(() => patchHier.IsStreamingInProgress = true);

                        #region preload

                        // Load PatchHierarchyInfo
                        PatchHierarchyInfo phi = null;
                        if (!cancelToken.IsCancellationRequested)
                        {
                            patchHier.StartAsyncLoadingPatchHierarchyInfo();
                            phi = patchHier.m_loadPatchHierarchyInfoTask.Result;
                        }

                        // Load KdTreeN
                        if (patchHier.KdTreeN == null && !cancelToken.IsCancellationRequested)
                        {
                            var kdIntTree =
                                KdTreeUtils.BuildOrLoadKdTreeN(patchHier.Paths, patchHier.PositionType, phi.PatchTree.Info);
                            Kernel.CQ.Enqueue(() =>
                                patchHier.KdTreeN = new KdTreeAttribute(patchHier, kdIntTree));
                        }

                        // Load ProfileLut
                        if (patchHier.ProfileLookUp == null && StorageConfig.FileExists(patchHier.Paths.ProfileLutPath) && !cancelToken.IsCancellationRequested)
                        {
                            var lut = Load.As<ProfileLookUpTable>(patchHier.Paths.ProfileLutPath);
                            if (lut == null)
                                Report.Warn("PatchHierarchy: Loading ProfileLookUpTable failed for OPC " + patchHier.OpcId);
                            else
                                lut.UpdateFilePaths(patchHier.Paths);
                            Kernel.CQ.Enqueue(() => patchHier.ProfileLookUp = lut);
                        }

                        // Load KdTreeK
                        if (patchHier.LoadKdTreeLevel_K &&
                            patchHier.m_loadKdTreeKTask != null &&
                            !cancelToken.IsCancellationRequested)
                        {
                            patchHier.StartAsyncLoadingKdTree_K();
                            patchHier.m_loadKdTreeKTask.Wait();
                        }

                        // Load KdTree0
                        if (patchHier.LoadKdTreeLevel_0 && patchHier.KdTree0 == null && !cancelToken.IsCancellationRequested)
                        {
                            using (s_cpu_LoadKdTree0.Timer)
                            {
                                //var kdTree0 = LoadKdTree0(patchHier);
                                var kdTree0 = LoadKdTree0(patchHier.Paths, patchHier.PositionType);

                                Kernel.CQ.Enqueue(() =>
                                {
                                    patchHier.KdTree0 = new KdTreeAttribute(patchHier, kdTree0);
                                    // set pickingInfo
                                    patchHierRule.m_pickingInfo = new PickingInfo(new KdTreeSet(patchHier.KdTree0.KdTree));
                                });
                            }
                        }

                        #endregion

                        if (cancelToken.IsCancellationRequested)
                        {
                            Report.End();
                            return EmptyLeaf.Singleton;
                        }

                        //execute opc building strategy
                        ISg isg;
                        using (s_cpu_LoadOpc.Timer)
                        {
                            isg = patchHier.SceneGraphBuilder.CreateSceneGraph(patchHier.Paths, patchHier);
                        }

                        Kernel.CQ.Enqueue(() => patchHier.OnStreamingFinished());
                        Report.End();
                        return isg;
                    }, patchHier.BoundingBox, 0);

                return hierarchy;
            }

            #endregion
        }
    }

    #region PatchHierarchy w/TaskCompletionSource

    //[RegisterTypeInfo]
    //public class PatchHierarchy : Instance, IRayCastable, IDisposable
    //{
    //    #region public properties and fields

    //    public const string Identifier = "PatchHierarchy";

    //    public Symbol OpcId { get; private set; }

    //    public OpcPaths Paths
    //    {
    //        get
    //        {
    //            if (m_paths.RootPatchName == null)
    //                m_paths.SetRootPatchName(PatchHierarchyInfo.RootPatch);
    //            return m_paths;
    //        }
    //    }
    //    public string DirectoryPath
    //    {
    //        get
    //        {
    //            return m_paths.BasePath;
    //        }
    //    }

    //    public Box3d BoundingBox { get; private set; }
    //    public PositionsType PositionType { get; private set; }
    //    public bool IgnoreViewFrustumCulling { get; set; }

    //    public PatchHierarchyInfo PatchHierarchyInfo
    //    {
    //        get
    //        {
    //            if (m_loadPatchHierarchyInfoTask == null || !m_loadPatchHierarchyInfoTask.IsCompleted)
    //            {   // load, if not yet loaded asynchrounas
    //                var tcs = new TaskCompletionSource<PatchHierarchyInfo>();
    //                m_loadPatchHierarchyInfoTask = tcs.Task;
    //                var phi = PatchHierarchyInfo.BuildOrLoadCache(m_paths);
    //                tcs.SetResult(phi);
    //            }

    //            return m_loadPatchHierarchyInfoTask.Result;
    //        }
    //    }
    //    public double MinSv
    //    {
    //        get
    //        {
    //            return PatchHierarchyInfo.PatchTree.Info.GlobalBoundingBox2d.Min.X;
    //        }
    //    }
    //    public double MaxSv
    //    {
    //        get
    //        {
    //            return PatchHierarchyInfo.PatchTree.Info.GlobalBoundingBox2d.Max.X;
    //        }
    //    }

    //    public KdTreeAttribute KdTreeN
    //    {
    //        get
    //        {
    //            if (m_loadKdTreeNTask == null || !m_loadKdTreeNTask.IsCompleted)
    //                return null;
    //            return m_loadKdTreeNTask.Result;
    //        }
    //    }
    //    public LazyKdTreeAttribute KdTreeK { get; set; }
    //    public KdTreeAttribute KdTree0
    //    {
    //        get
    //        {
    //            if (m_loadKdTree0Task == null || !m_loadKdTree0Task.IsCompleted)
    //                return null;
    //            return m_loadKdTree0Task.Result;
    //        }
    //    }

    //    public ProfileLookUpTable ProfileLookUp
    //    {
    //        get
    //        {
    //            if (m_loadLutTask == null || !m_loadLutTask.IsCompleted)
    //                return null;
    //            return m_loadLutTask.Result;
    //        }
    //    }

    //    public bool IsStreamed = false;
    //    public IObservable<Symbol> IsStreamedEvent
    //    {
    //        get { return m_isStreamedEvent; }
    //    }

    //    public IOpcSgBuilderStrategy SceneGraphBuilder { get; set; }

    //    public bool MaxLevelToShow { get; set; }

    //    public bool LoadLevel0KdTree { get; set; }

    //    public List<float> LoDDistanceMetric { get; set; }

    //    /// <summary>
    //    /// Checks if there is a LoD distance metric. 
    //    /// </summary>
    //    public bool HasLoDMetric
    //    {
    //        get
    //        {
    //            return !LoDDistanceMetric.IsEmptyOrNull();
    //        }
    //    }

    //    #endregion

    //    #region private fields

    //    private static Symbol s_Level0 = "Level0";
    //    private static Symbol s_LevelK = "LevelK";

    //    private Subject<Symbol> m_isStreamedEvent = new Subject<Symbol>();
    //    private Task<PatchHierarchyInfo> m_loadPatchHierarchyInfoTask = null;
    //    private Task<KdTreeAttribute> m_loadKdTreeNTask = null;
    //    private Task<ProfileLookUpTable> m_loadLutTask = null;
    //    private Task<KdTreeAttribute> m_loadKdTree0Task = null;
    //    private OpcPaths m_paths = null;

    //    #endregion

    //    #region constructors

    //    public PatchHierarchy() { }

    //    public PatchHierarchy(OpcDescriptor desc)
    //        : base(Identifier)
    //    {
    //        Requires.That(StorageConfig.DirectoryExists(desc.Path), desc.Path + " cannot be found");

    //        LoadLevel0KdTree = true;

    //        OpcId = desc.Id;
    //        BoundingBox = desc.BoundingBox;
    //        PositionType = desc.PosType;
    //        m_paths = OpcPaths.From(desc.Path);
    //    }

    //    public PatchHierarchy(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
    //        : this(desc)
    //    {
    //        SceneGraphBuilder = sceneGraphBuilder;
    //    }

    //    #endregion

    //    #region public methods

    //    public static PatchHierarchy From(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
    //    {
    //        return new PatchHierarchy(desc, sceneGraphBuilder);
    //    }

    //    public ConcreteKdIntersectionTree GetKdTree(Symbol filter)
    //    {
    //        #region Level 0 KdTree
    //        if (filter != null && filter == s_Level0)
    //        {
    //            // KdTree0 not loaded or not finished loading
    //            if (KdTree0 == null)
    //                return null;

    //            ConcreteKdIntersectionTree kdTreeList = null;
    //            try
    //            {
    //                kdTreeList = KdTree0.KdTree;
    //                if (kdTreeList == null)
    //                {
    //                    Report.Warn("PatchHierarchy: No Level-0 kdTree list associated with opc {0}", DirectoryPath);
    //                    return kdTreeList;
    //                }
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                Report.Warn("PatchHierarchy: kdtree format fail");
    //            }

    //            var kdIntTree = kdTreeList.KdIntersectionTree;
    //            if (kdIntTree != null)
    //            {
    //                var lazySet = kdIntTree.ObjectSet as LazyKdTreeSet;
    //                if (lazySet != null)
    //                    lazySet.UpdatePlaceHolderPaths(DirectoryPath);
    //            }

    //            return kdTreeList;
    //        }
    //        #endregion

    //        #region Level K KdTree
    //        if (filter != null && filter == s_LevelK)
    //        {
    //            //kdTreek attribute not set create one
    //            if (KdTreeK == null)
    //            {
    //                if (KdTreeN != null)
    //                {
    //                    Report.Warn("PatchHierarchy: No KdTree-K found, falling back to KdTree-N in Opc " + OpcId);
    //                    return KdTreeN.KdTree;
    //                }
    //                else
    //                    Report.Warn("PatchHierarchy: No KdTree-K or KdTree-N found in Opc " + OpcId);
    //                return null;
    //            }

    //            ConcreteKdIntersectionTree kdTreeList = null;
    //            try
    //            {
    //                KdTreeK.Preload();
    //                kdTreeList = KdTreeK.KdTree;
    //                if (kdTreeList == null)
    //                {
    //                    Report.Warn("PatchHierarchy: No Level-k kdTree list associated with opc {0}", DirectoryPath);
    //                    return kdTreeList;
    //                }
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                Report.Warn("PatchHierarchy: kdtree format fail");
    //            }

    //            var kdIntTree = kdTreeList.KdIntersectionTree;
    //            if (kdIntTree != null)
    //            {
    //                var lazySet = kdIntTree.ObjectSet as LazyKdTreeSet;
    //                if (lazySet != null)
    //                    lazySet.UpdatePlaceHolderPaths(DirectoryPath);
    //            }

    //            return kdTreeList;
    //        }

    //        #endregion

    //        // Level N KdTree
    //        if (KdTreeN == null)
    //            return null;
    //        return KdTreeN.KdTree;
    //    }

    //    #region EqualityComparer

    //    public static ByIdComparer ById = new ByIdComparer();

    //    public class ByIdComparer : IEqualityComparer<PatchHierarchy>
    //    {
    //        public bool Equals(PatchHierarchy ph1, PatchHierarchy ph2)
    //        {
    //            return ph1.OpcId == ph2.OpcId;
    //        }
    //        public int GetHashCode(PatchHierarchy ph)
    //        {
    //            return ph.OpcId.GetHashCode();
    //        }
    //    }

    //    #endregion

    //    #region IRayCastable

    //    // TODO: should be combination of OpcId and ViewerId or OpcId and PosType?!?!
    //    public string GetKey()
    //    {
    //        return DirectoryPath;
    //    }

    //    #endregion

    //    #region IDisposable

    //    public void Dispose()
    //    {
    //        m_isStreamedEvent.OnCompleted();
    //        m_isStreamedEvent.Dispose();
    //    }

    //    #endregion

    //    #endregion

    //    #region private methods

    //    //private void LoadKdTree0()
    //    //{
    //    //    if (m_loadKdTree0Task != null && !m_loadKdTree0Task.IsCompleted)
    //    //        return;

    //    //    var completionSource = new TaskCompletionSource<KdTreeAttribute>();
    //    //    m_loadKdTree0Task = completionSource.Task;

    //    //    var kdTree0 = LoadKdTree0(this);

    //    //    completionSource.SetResult(kdTree0);
    //    //}

    //    private static KdTreeAttribute LoadKdTree0(PatchHierarchy ph)
    //    {
    //        if (ph.Paths.RootPatchName == null)
    //            return null;

    //        var kdIntTree = Load.As<KdIntersectionTree>(ph.Paths.GetKdTreeZeroPath(ph.PositionType));
    //        if (kdIntTree == null)
    //            Report.Error("PatchHierarchy: Loading KdTree-0 failed for OPC " + ph.OpcId);
    //        var kdTree0 = new KdTreeAttribute(ph, kdIntTree);

    //        if (kdTree0.KdTree.KdIntersectionTree == null)
    //            Report.Error("PatchHierarchy: KdIternsectionTree not available.");
    //        else
    //        {
    //            var lazySet = kdTree0.KdTree.KdIntersectionTree.ObjectSet as LazyKdTreeSet;
    //            if (lazySet == null)
    //                Report.Error("PatchHierarchy: ObjectSet of KdIntersectionTree is not a LazyKdTreeSet.");
    //            else
    //            {
    //                lazySet.KdTreePlaceholders.ForEach(x =>
    //                {
    //                    var kdtreeName = Path.GetFileName(x.Path);
    //                    x.Path = Path.Combine(ph.Paths.RootPatchPath, kdtreeName);

    //                    string patchName, posName;
    //                    if (ph.PositionType == PositionsType.V3dPositions)
    //                    {
    //                        patchName = kdtreeName.Substring(0, kdtreeName.Length - 7);
    //                        posName = "positions.aara";
    //                    }
    //                    else
    //                    {
    //                        patchName = kdtreeName.Substring(0, kdtreeName.Length - 10);
    //                        posName = "positions2d.aara";
    //                    }

    //                    x.ObjectSetPath = Path.Combine(Path.Combine(ph.Paths.PatchesSubDir, patchName), posName);
    //                    Requires.That(StorageConfig.FileExists(x.Path), "level 0 patch " + patchName + " is missing. kdtree invalid");
    //                });
    //            }
    //        }

    //        return kdTree0;
    //    }

    //    #endregion

    //    [Rule(typeof(PatchHierarchy))]
    //    public class PatchHierarchyRule : IRule, IPickable, IGetKdTreeList
    //    {
    //        #region private fields

    //        private PatchHierarchy m_instance;
    //        private ISg m_asyncHierarchy;
    //        private PickingInfo m_pickingInfo;
    //        private bool m_initialized = false;

    //        private TaskCompletionSource<PatchHierarchyInfo> m_patchHierarchyInfoCS = null;
    //        private TaskCompletionSource<KdTreeAttribute> m_kdTreeNCS = null;
    //        private TaskCompletionSource<ProfileLookUpTable> m_lutCS = null;
    //        private TaskCompletionSource<KdTreeAttribute> m_kdTree0CS = null;
    //        private EventDescription m_streamingFinished;

    //        private Stack<IDisposable> m_disposables = new Stack<IDisposable>();

    //        private static Telemetry.CpuTime s_cpu_LoadKdTree0 = new Telemetry.CpuTime().Register("PatchHierarchyRule: Load KdTree0", true, true);
    //        private static Telemetry.CpuTime s_cpu_LoadOpc = new Telemetry.CpuTime().Register("PatchHierarchyRule: Load Opc", true, true);

    //        #endregion

    //        public PatchHierarchyRule(PatchHierarchy instance, AbstractTraversal traversal)
    //        {
    //            m_instance = instance;
    //            m_streamingFinished = new EventDescription("PatchHierarchyRule:StreamingFinished " + m_instance.OpcId);
    //            m_disposables.Push
    //                (Kernel.EQ.Where(m_streamingFinished).Subscribe((_) => StreamingFinishedCallback()));
    //        }

    //        private void StreamingFinishedCallback()
    //        {
    //            m_instance.IsStreamed = true;
    //            m_instance.m_isStreamedEvent.OnNext(m_instance.OpcId);
    //            OpcEvents.StreamingFinished.Fire(m_instance.OpcId.ToString());
    //        }

    //        #region IRule

    //        public void InitForPath(AbstractTraversal t) { } //Nop

    //        public ISg SetParameters(AbstractTraversal t)
    //        {
    //            if (!m_initialized)
    //            {
    //                #region Prepare TaskCompletionSources for threadsafe Feedback

    //                if (m_instance.m_loadPatchHierarchyInfoTask == null)
    //                {   // PatchHierarchyInfo
    //                    m_patchHierarchyInfoCS = new TaskCompletionSource<PatchHierarchyInfo>();
    //                    m_instance.m_loadPatchHierarchyInfoTask = m_patchHierarchyInfoCS.Task;
    //                }
    //                if (m_instance.m_loadKdTreeNTask == null)
    //                {   // KdTree N
    //                    m_kdTreeNCS = new TaskCompletionSource<KdTreeAttribute>();
    //                    m_instance.m_loadKdTreeNTask = m_kdTreeNCS.Task;
    //                }
    //                if (m_instance.m_loadLutTask == null && StorageConfig.FileExists(m_instance.Paths.ProfileLutPath))
    //                {   // Lookup Table
    //                    m_lutCS = new TaskCompletionSource<ProfileLookUpTable>();
    //                    m_instance.m_loadLutTask = m_lutCS.Task;
    //                }
    //                if (m_instance.LoadLevel0KdTree && m_instance.m_loadKdTree0Task == null)
    //                {   // KdTree 0
    //                    m_kdTree0CS = new TaskCompletionSource<KdTreeAttribute>();
    //                    m_instance.m_loadKdTree0Task = m_kdTree0CS.Task;
    //                }

    //                #endregion

    //                var paths = new OpcPaths(m_instance.m_paths.BasePath);

    //                // Setup OPC loading
    //                var hierarchy = new StreamingJob(
    //                    () =>
    //                    {
    //                        Report.BeginTimed("PatchHierarchyRule: Streaming OPC {0}", m_instance.OpcId);

    //                        #region preload

    //                        if (!m_instance.IsStreamed)
    //                        {
    //                            // Load PatchHierarchyInfo
    //                            if (m_patchHierarchyInfoCS != null)
    //                            {
    //                                var phi = PatchHierarchyInfo.BuildOrLoadCache(paths);
    //                                m_patchHierarchyInfoCS.SetResult(phi);
    //                            }
    //                            paths.SetRootPatchName(m_instance.PatchHierarchyInfo.RootPatch);

    //                            // Load KdTreeN
    //                            if (m_kdTreeNCS != null)
    //                            {
    //                                var kdIntTree =
    //                                    KdTreeUtils.BuildOrLoadKdTreeN(paths, m_instance.PositionType, m_instance.PatchHierarchyInfo.PatchTree.Info);
    //                                m_kdTreeNCS.SetResult(new KdTreeAttribute(m_instance, kdIntTree));
    //                            }
    //                            // Load ProfileLut
    //                            if (m_lutCS != null)
    //                            {
    //                                var lut = Load.As<ProfileLookUpTable>(paths.ProfileLutPath);
    //                                if (lut == null)
    //                                    Report.Warn("PatchHierarchy: Loading ProfileLookUpTable failed for OPC " + m_instance.OpcId);
    //                                else
    //                                    lut.UpdateFilePaths(paths);
    //                                m_lutCS.SetResult(lut);
    //                            }
    //                            // Load KdTree0
    //                            if (m_kdTree0CS != null)
    //                            {
    //                                using (s_cpu_LoadKdTree0.Timer)
    //                                {
    //                                    var kdTree0 = LoadKdTree0(m_instance);
    //                                    m_kdTree0CS.SetResult(kdTree0);
    //                                }
    //                            }
    //                        }

    //                        #endregion

    //                        //execute opc building strategy
    //                        ISg isg;
    //                        using (s_cpu_LoadOpc.Timer)
    //                        {
    //                            isg = m_instance.SceneGraphBuilder.CreateSceneGraph(paths, m_instance);
    //                        }

    //                        m_streamingFinished.Fire();
    //                        Report.End();
    //                        return isg;
    //                    }, m_instance.BoundingBox, 0);

    //                // center BB to avoid float conversion problem on transfer to GPU
    //                var bbCenter = m_instance.BoundingBox.Center;
    //                var placeHolder = Rsg.Apply(
    //                    Rsg.Attribute.PushTrafo3d(Trafo3d.Translation(bbCenter)),
    //                    Primitives.WireBox(m_instance.BoundingBox - bbCenter, C4b.Gray).ToVertexGeometrySet()
    //                    );

    //                m_asyncHierarchy = new BoundingBoxAttribute(
    //                                        new GCHandle(
    //                                            new LoD(placeHolder, //simple
    //                                                    new AsyncStreamingNode(hierarchy, placeHolder), //detail
    //                                                    new TrivialNotSufficientDecider()
    //                                            ) { IsPinned = m_instance.IgnoreViewFrustumCulling })
    //                                        {
    //                                            DebugName = "PatchHierarchyLoDs " + m_instance.OpcId,
    //                                            IgnoreFrustumCulling = m_instance.IgnoreViewFrustumCulling,
    //                                            IsPinned = false
    //                                        }
    //                                    );

    //                m_initialized = true;
    //            }

    //            // set pickingInfo
    //            if (m_pickingInfo.Geometry == null && m_instance.KdTree0 != null && m_instance.IsStreamed)
    //            {
    //                var kdTree = m_instance.KdTree0.GetKdTreeList(t.State);
    //                m_pickingInfo = new PickingInfo(new KdTreeSet(kdTree));
    //            }

    //            return m_asyncHierarchy;
    //        }

    //        public bool DisposeAndRemove(DisposeAndRemoveTraversal t)
    //        {
    //            m_disposables.DisposeAll();
    //            m_instance.Dispose();
    //            var childNodes = m_asyncHierarchy;
    //            m_asyncHierarchy = EmptyLeaf.Singleton;

    //            return t.TryDisposeAndRemoveRule(m_instance, t, childNodes);
    //        }

    //        #endregion

    //        #region IGetKdTreeList

    //        public List<ConcreteKdIntersectionTree> GetKdTreeList(GetKdTreeTraversal t)
    //        {
    //            var kdTree = m_instance.GetKdTree(t.Filter);

    //            if (kdTree == null)
    //                return new List<ConcreteKdIntersectionTree> { };
    //            else return new List<ConcreteKdIntersectionTree> { kdTree };
    //        }

    //        #endregion

    //        #region IPickable

    //        public PickingInfo PickingInfo
    //        {
    //            get
    //            {
    //                return m_pickingInfo;
    //            }
    //        }

    //        #endregion
    //    }
    //}

    #endregion

    #region PatchHierarchy Old

    //[RegisterTypeInfo]
    //public class PatchHierarchy : Instance, IRayCastable //, IDisposable
    //{
    //    public const string Identifier = "PatchHierarchy";

    //    public Symbol OpcId { get; private set; }

    //    #region public properties

    //    public string DirectoryPath { get; private set; }
    //    public Box3d BoundingBox { get; private set; }
    //    public PositionsType PositionType { get; private set; }
    //    public bool IgnoreViewFrustumCulling { get; set; }
    //    public PatchHierarchyInfo PatchHierarchyInfo { get; set; }
    //    public bool Visible { get; set; }
    //    public KdTreeAttribute KdTreeN
    //    {
    //        get { return Get<KdTreeAttribute>(Property.KdTreeN.ToString()); }
    //        set { this[Property.KdTreeN.ToString()] = value; }
    //    }
    //    public LazyKdTreeAttribute KdTreeK
    //    {
    //        get { return Get<LazyKdTreeAttribute>(Property.KdTreeK.ToString()); }
    //        set { this[Property.KdTreeK.ToString()] = value; }
    //    }
    //    public LazyKdTreeAttribute KdTree0
    //    {
    //        get { return Get<LazyKdTreeAttribute>(Property.KdTree0.ToString()); }
    //        set { this[Property.KdTree0.ToString()] = value; }
    //    }

    //    public ProfileLookUpTable ProfileLookUp { get; set; }

    //    public static class Property
    //    {
    //        public static readonly Symbol KdTree0 = "KdTree0";
    //        public static readonly Symbol KdTreeK = "KdTreeK";
    //        public static readonly Symbol KdTreeN = "KdTreeN";
    //    }

    //    public double GetMinSv()
    //    {
    //        if (PatchHierarchyInfo == null)
    //            return double.NaN;

    //        return PatchHierarchyInfo.PatchTree.Info.GlobalBoundingBox2d.Min.X;
    //    }

    //    public double GetMaxSv()
    //    {
    //        if (PatchHierarchyInfo == null)
    //            return double.NaN;

    //        return PatchHierarchyInfo.PatchTree.Info.GlobalBoundingBox2d.Max.X;
    //    }

    //    public EventSource<bool> IsStreamed;

    //    public IOpcSgBuilderStrategy SceneGraphBuilder { get; set; }

    //    public OpcPaths Paths { get; set; }
    //    public bool MaxLevelToShow { get; set; }

    //    public bool LoadLevel0KdTree { get; set; }

    //    public List<float> LoDDistanceMetric { get; set; }

    //    #endregion

    //    public PatchHierarchy() { }

    //    public PatchHierarchy(OpcDescriptor desc)
    //        : base(Identifier)
    //    {
    //        Requires.That(StorageConfig.DirectoryExists(desc.Path), desc.Path + " cannot be found");

    //        //if (!LoadLevel0KdTree)
    //        //{
    //        //    Report.Warn("Loading of Level0 kdtree is deactivated for {0}", desc.Path);
    //        //    Report.Warn("Picking is not possible");
    //        //}

    //        IsStreamed = new EventSource<bool>(false);
    //        LoadLevel0KdTree = true;

    //        OpcId = desc.Id;
    //        DirectoryPath = desc.Path;
    //        BoundingBox = desc.BoundingBox;
    //        PositionType = desc.PosType;
    //    }

    //    public PatchHierarchy(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
    //        : this(desc)
    //    {
    //        SceneGraphBuilder = sceneGraphBuilder;
    //    }

    //    /// <summary>
    //    /// Checks if there is a LoD distance metric. 
    //    /// </summary>
    //    public bool HasLoDMetric
    //    {
    //        get
    //        {
    //            return !LoDDistanceMetric.IsEmptyOrNull();
    //        }
    //    }
    //    public string GetKey()
    //    {
    //        return DirectoryPath; // TODO: should be combination of OpcId and ViewerId or OpcId and PosType?!?!
    //    }

    //    public static PatchHierarchy From(OpcDescriptor desc, IOpcSgBuilderStrategy sceneGraphBuilder)
    //    {
    //        return new PatchHierarchy(desc, sceneGraphBuilder);
    //    }

    //    private static Symbol s_Level0 = "Level0";
    //    private static Symbol s_LevelK = "LevelK";

    //    public ConcreteKdIntersectionTree GetKdTree(Symbol filter)
    //    {
    //        if (!IsStreamed.Latest)
    //            return null;

    //        #region Level 0 KdTree
    //        if (filter != null && filter == s_Level0)
    //        {
    //            //kdTree0 attribute not set create one
    //            if (KdTree0 == null)
    //            {
    //                if (Paths == null)
    //                    return null;

    //                KdTree0 =
    //                    new LazyKdTreeAttribute(this, Paths.GetKdTreeZeroPath(PositionType));
    //            }

    //            Requires.NotNull(KdTree0);

    //            ConcreteKdIntersectionTree kdTreeList = null;
    //            try
    //            {
    //                KdTree0.Preload();
    //                kdTreeList = KdTree0.KdTree;
    //                if (kdTreeList == null)
    //                {
    //                    Report.Warn("No Level-0 kdTree list associated with opc {0}", DirectoryPath);
    //                    return kdTreeList;
    //                }
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                Report.Warn("kdtree format fail");
    //            }

    //            var kdIntTree = kdTreeList.KdIntersectionTree;
    //            if (kdIntTree != null)
    //            {
    //                var lazySet = kdIntTree.ObjectSet as LazyKdTreeSet;
    //                if (lazySet != null)
    //                    lazySet.UpdatePlaceHolderPaths(DirectoryPath);
    //            }

    //            return kdTreeList;
    //        }
    //        #endregion

    //        #region Level K KdTree
    //        if (filter != null && filter == s_LevelK)
    //        {
    //            //kdTreek attribute not set create one
    //            if (KdTreeK == null)
    //            {
    //                return KdTreeN != null ? KdTreeN.KdTree : null;
    //            }

    //            ConcreteKdIntersectionTree kdTreeList = null;
    //            try
    //            {
    //                KdTreeK.Preload();
    //                kdTreeList = KdTreeK.KdTree;
    //                if (kdTreeList == null)
    //                {
    //                    Report.Warn("No Level-k kdTree list associated with opc {0}", DirectoryPath);
    //                    return kdTreeList;
    //                }
    //            }
    //            catch (KeyNotFoundException)
    //            {
    //                Report.Warn("kdtree format fail");
    //            }

    //            var kdIntTree = kdTreeList.KdIntersectionTree;
    //            if (kdIntTree != null)
    //            {
    //                var lazySet = kdIntTree.ObjectSet as LazyKdTreeSet;
    //                if (lazySet != null)
    //                    lazySet.UpdatePlaceHolderPaths(DirectoryPath);
    //            }

    //            return kdTreeList;
    //        }

    //        #endregion

    //        if (KdTreeN == null)
    //        {
    //            //Report.Warn("PatchHierarchy: m_instance.KdTree == null");
    //            //Report.Warn("No kdtree N could be found");
    //            return null;
    //        }

    //        //return kdTree N
    //        return KdTreeN.KdTree;
    //    }

    //    #region EqualityComparer

    //    public static ByIdComparer ById = new ByIdComparer();

    //    public class ByIdComparer : IEqualityComparer<PatchHierarchy>
    //    {
    //        public bool Equals(PatchHierarchy ph1, PatchHierarchy ph2)
    //        {
    //            return ph1.OpcId == ph2.OpcId;
    //        }
    //        public int GetHashCode(PatchHierarchy ph)
    //        {
    //            return ph.OpcId.GetHashCode();
    //        }
    //    }

    //    #endregion

    //    // TODO: this is not used yet :(
    //    public void Dispose()
    //    {
    //        IsStreamed.Emit(false);
    //        IsStreamed.Values.TryDispose();
    //    }
    //}
    //[Rule(typeof(PatchHierarchy))]
    //public class PatchHierarchyRule : IRule, IDisposeAndRemove, IPickable, IGetKdTreeList
    //{
    //    #region private fields

    //    private ISg m_asyncHierarchy;
    //    private PatchHierarchy m_instance;
    //    private PickingInfo m_pickingInfo;
    //    private bool m_initialized = false;

    //    #endregion

    //    public PatchHierarchyRule(PatchHierarchy instance, AbstractTraversal traversal)
    //    {
    //        m_instance = instance;
    //    }

    //    public void InitForPath(AbstractTraversal t) { } //Nop

    //    Group kdGroup = new Group();
    //    public ISg SetParameters(AbstractTraversal t)
    //    {
    //        if (!m_initialized)
    //        {
    //            var hierarchy = new StreamingJob(
    //                () =>
    //                {
    //                    Report.BeginTimed("Streaming OPC {0}", m_instance.OpcId);
    //                    var paths = OpcPaths.From(m_instance.DirectoryPath);

    //                    m_instance.PatchHierarchyInfo = PatchHierarchyInfo.BuildOrLoadCache(paths);

    //                    paths.SetRootPatchName(m_instance.PatchHierarchyInfo.RootPatch);

    //                    m_instance.Paths = paths;

    //                    PatchHierarchyUtils.HandleKdTrees(m_instance);

    //                    if (StorageConfig.FileExists(paths.ProfileLutPath))
    //                    {
    //                        var lut = Load.As<ProfileLookUpTable>(paths.ProfileLutPath);
    //                        lut.UpdateFilePaths(paths);
    //                        m_instance.ProfileLookUp = lut;
    //                    }

    //                    //execute opc building strategy
    //                    var isg = m_instance.SceneGraphBuilder.CreateSceneGraph(paths, m_instance);

    //                    Requires.NotNull(isg);
    //                    m_instance.IsStreamed.Emit(true);
    //                    OpcEvents.StreamingFinished.Fire(m_instance.OpcId.ToString());

    //                    Report.End();
    //                    return isg;

    //                }, m_instance.BoundingBox, 0);

    //            // center BB to avoid float conversion problem on transfer to GPU
    //            var bbCenter = m_instance.BoundingBox.Center;
    //            var placeHolder = Rsg.Apply(
    //                Rsg.Attribute.PushTrafo3d(Trafo3d.Translation(bbCenter)),
    //                Primitives.WireBox(m_instance.BoundingBox - bbCenter, C4b.Gray).ToVertexGeometrySet()
    //                );

    //            m_asyncHierarchy = new BoundingBoxAttribute(
    //                                    new GCHandle(
    //                                        new LoD(placeHolder, //simple
    //                                                new AsyncStreamingNode(hierarchy, placeHolder), //detail
    //                                                new TrivialNotSufficientDecider()
    //                                        ) { IsPinned = m_instance.IgnoreViewFrustumCulling })
    //                                    {
    //                                        DebugName = "patch_hier",
    //                                        IgnoreFrustumCulling = m_instance.IgnoreViewFrustumCulling,
    //                                        IsPinned = false
    //                                    }
    //                                );

    //            m_initialized = true;
    //        }


    //        if (m_instance.IsStreamed.Latest && m_instance.KdTree0 != null && m_pickingInfo.Geometry == null)
    //        {
    //            var kdTree = m_instance.KdTree0.GetKdTreeList(t.State);

    //            #region debug
    //            //var k = kdTree.First().KdIntersectionTree.ObjectSet as LazyKdTreeSet;
    //            //var firstPoint = V3d.NaN;

    //            //Report.Line(k.ToString());

    //            //var vgs = k.ConcreteKdTreeList.Select(k1 =>
    //            //{
    //            //    var triangles = k1.KdIntersectionTree.ObjectSet as TriangleSet;
    //            //    if (firstPoint.IsNaN)
    //            //        firstPoint = triangles.Position3dList.First();

    //            //    return triangles.Position3dList.Select(x => (x - firstPoint).ToV3f()).ToArray();
    //            //}).Where(x => x.Length > 0)
    //            //.Select(x => new VertexGeometry(GeometryMode.TriangleList) { Positions = x }).ToVertexGeometrySet();

    //            //kdGroup.Add(vgs.Trafo(Trafo3d.Translation(firstPoint)));
    //            #endregion

    //            m_pickingInfo = new PickingInfo(new KdTreeSet(kdTree));
    //        }

    //        return new Group(m_asyncHierarchy, kdGroup);
    //    }

    //    public bool DisposeAndRemove(DisposeAndRemoveTraversal t)
    //    {
    //        var kdTreeNotNull = m_instance.KdTree0 != null && m_instance.KdTree0.KdTree != null &&
    //            m_instance.KdTree0.KdTree.KdIntersectionTree != null && m_instance.KdTree0.KdTree.KdIntersectionTree.ObjectSet != null;

    //        if (kdTreeNotNull)
    //        {
    //            Report.Line("Disposing KdTree");

    //            var lazyKdTreeSet = m_instance.KdTree0.KdTree.KdIntersectionTree.ObjectSet as LazyKdTreeSet;
    //            if (lazyKdTreeSet != null)
    //            {
    //                foreach (var ph in lazyKdTreeSet.KdTreePlaceholders)
    //                {
    //                    ph.KdTree = null;
    //                }
    //            }

    //            m_instance.KdTree0 = null; //.DisposeAndRemove(t.State);
    //            //result = t.TryDisposeAndRemoveRule(m_instance, t, m_instance.KdTree0);
    //        }

    //        return t.TryDisposeAndRemoveRule(m_instance, t, m_asyncHierarchy);
    //    }

    //    public List<ConcreteKdIntersectionTree> GetKdTreeList(GetKdTreeTraversal t)
    //    {
    //        var kdTree = m_instance.GetKdTree(t.Filter);

    //        if (kdTree == null)
    //            return new List<ConcreteKdIntersectionTree> { };
    //        else return new List<ConcreteKdIntersectionTree> { kdTree };
    //    }

    //    public PickingInfo PickingInfo
    //    {
    //        get { return m_pickingInfo; }
    //    }
    //}

    #endregion
}