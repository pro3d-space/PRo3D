using Aardvark.Algodat;
using Aardvark.Base;
using Aardvark.Lod;
using Aardvark.Opc.KdTree;
using Aardvark.Rendering;
using Aardvark.Rendering.SlimDx;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;

namespace Aardvark.Opc.PatchHierarchy
{
    public enum PositionsType
    {
        V3dPositions,
        V2dPositions,
    }

    [RegisterTypeInfo]
    public class Patch : Instance
    {
        public const string Identifier = "Patch";

        public static class Properties
        {
            public const string PatchFilePath = "PatchFilePath";
            public const string ImageFilePath = "ImageFilePath";

            public const string PatchFileInfo = "PatchFileInfo";
            public const string Level = "Level";
            public const string MaxLevel = "MaxLevel";

            public const string PositionsType = "PositionsType";
        }

        public string PatchFilePath
        {
            get { return Get<string>(Properties.PatchFilePath); }
            set { this[Properties.PatchFilePath] = value; }
        }

        public PatchFileInfo PatchFileInfo
        {
            get { return Get<PatchFileInfo>(Properties.PatchFileInfo); }
            set { this[Properties.PatchFileInfo] = value; }
        }

        public int Level
        {
            get { return Get<int>(Properties.Level); }
            set { this[Properties.Level] = value; }
        }

        public int MaxLevel
        {
            get { return Get<int>(Properties.MaxLevel); }
            set { this[Properties.MaxLevel] = value; }
        }

        public PositionsType PositionsType
        {
            get { return Get<PositionsType>(Properties.PositionsType); }
            set { this[Properties.PositionsType] = value; }
        }

        public double AvgTexelSize { get; set; }

        public double DepthRanking { get; set; }

        public IPatchLoadingStrategy LoadingStrategy = new PatchLoadingStrategy();

        public bool InvertZ { get; set; }
        public float MaxTriangleSize { get; set; }

        //static volatile int patchCounter = 0;

        /// <summary>
        /// Represents a single patch of ordered positions including coords, textures, etc. 
        /// </summary>
        public Patch(PatchFileInfo info, string patchPath, int level, int maxLevel, PositionsType posType, bool invertZ = false, float maxTriangleSize = float.PositiveInfinity)
            : base(Identifier)
        {
            PatchFilePath = patchPath;
            Level = level;
            PatchFileInfo = info;
            MaxLevel = maxLevel;
            PositionsType = posType;
            InvertZ = invertZ;
            MaxTriangleSize = maxTriangleSize;
            //patchCounter++;
            //Report.Line("++" + patchCounter);
        }

        //~Patch()
        //{
        //    patchCounter--;
        //    Report.Line("--" + patchCounter);
        //}

        public static Patch FromFile(string patchXmlPath, int level, int maxLevel, PositionsType posType)
        {
            return new Patch(PatchFileInfo.FromFile(patchXmlPath), patchXmlPath, level, maxLevel, posType);
        }

        public static C4f GetLodColor(int level, int maxLevel)
        {
            if (maxLevel == 0)
                return C4f.Red;

            float hue = ((float)level / (float)maxLevel) * 2 / (float)3;

            return C3f.FromHSV(hue, 1.0f, 1.0f).ToC4f();
        }

        public static AardvarkFormat GetTextureFormatFromPixelFormat(PixelFormat pixelFormat)
        {
            var size = Image.GetPixelFormatSize(pixelFormat);
            var result = AardvarkFormat.Dxt5;

            switch (size)
            {
                case 8:
                    //8bit
                    result = AardvarkFormat.L8;
                    break;
                case 24:
                case 32:
                    result = AardvarkFormat.Dxt5;
                    break;
                default:
                    break;
            }

            return result;
        }
    }


    [Rule(typeof(Patch))]
    public class PatchRule : IRule, IGetAvgTexelSize, IHasFinished
    {

        private Patch m_instance;
        private ISg m_returnISg;
        private Dictionary<string, Texture> m_asyncTextures;
        private readonly Object asyncLock = new object();

        bool m_isInitialized = false;


        public PatchRule(Patch instance, AbstractTraversal traversal)
        {
            m_instance = instance;
        }

        public void InitForPath(AbstractTraversal t)
        {
            //Nop
        }

        public ISg SetParameters(AbstractTraversal t)
        {
            if (!m_isInitialized)
            {
                var info = m_instance.PatchFileInfo;
                var bb = m_instance.PositionsType == PositionsType.V3dPositions ?
                    info.LocalBoundingBox : info.LocalBoundingBox2d;

                var patch = new StreamingJob(
                    () =>
                    {
                        //TaskCombinators.Delay(100);
                        //do importer logic here

                        var patchVg = m_instance.LoadingStrategy.Load(m_instance.PatchFileInfo, m_instance.PatchFilePath, m_instance.PositionsType, 
                                       true, true, true, m_instance.MaxTriangleSize);

                        if (patchVg == null)
                            return EmptyLeaf.Singleton;

                        var lodColor = Patch.GetLodColor(m_instance.Level, m_instance.MaxLevel);
                        patchVg["LodColor"] = lodColor;
                        
                        for (int i = 0; i < patchVg.Textures.Count; i++)
                        {
                            var key = patchVg.Textures.Keys.ToList()[i];

                            var source = patchVg.Textures[key].Convertible;
                            Convertible target = null;

                            if (t.Renderer is SlimDx9Renderer)
                            {
                                target = SlimDx9TextureConvertible.Create(
                                    new SlimDx9TextureConvertible.SlimDx9TextureParameters()
                                    {
                                        //SlimDx9Format = SlimDX.Direct3D9.Format., // .Dxt5,
                                        Pool = SlimDX.Direct3D9.Pool.Default
                                    });
                                source.ConvertInto(target);
                            }
                            else
                            {
                                // nothing todo in slimdx10renderer (this just loads the texture on demand)
                                // fix this if you are fed up with framerate hick ups
                                target = source;
                            }

                            patchVg.Textures[key] = new Texture(target);
                        }
                        lock (asyncLock)
                        {
                            m_asyncTextures = patchVg.Textures;
                        }

                        return patchVg.ToVertexGeometrySet();
                    },
                   bb, m_instance.MaxLevel - m_instance.Level, true);

                patch.DestructSideEffects = DisposeSideEffects;

                var placeHolder = Primitives.WireBox(bb, C4b.Red).ToVertexGeometrySet();

                m_returnISg = new AsyncStreamingNode(patch, placeHolder)
                {
                    DebugName = Path.GetFileName(m_instance.PatchFilePath),
                };

                var local2Global = m_instance.PatchFileInfo.GetLocal2Global(m_instance.PositionsType);

                if (m_instance.InvertZ)
                    local2Global = M44d.Scale(1, 1, -1) * local2Global;

                var global2Local = local2Global.Inverse;
                m_returnISg = Rsg.Apply(Rsg.Attribute.PushTrafo3d(new Trafo3d(local2Global, global2Local)), m_returnISg);

                m_isInitialized = true;
            }

            return m_returnISg;
        }

        public void DisposeSideEffects()
        {
            lock (asyncLock)
            {
                if (m_asyncTextures != null)
                {
                    foreach (var tex in m_asyncTextures.Values)
                    {
                        var dxTex = SlimDx9TextureConvertible.OriginalTextureWithParameters(tex.Convertible);
                        if (dxTex != null) dxTex.Dispose();
                    }
                    m_asyncTextures = null;
                }
            }
        }

        public bool DisposeAndRemove(DisposeAndRemoveTraversal t)
        {
            //patchCounter--;
            //Report.Line("--" + patchCounter);

            DisposeSideEffects();

            if (m_returnISg != null)
                return t.TryDisposeAndRemoveRule(m_instance, t, m_returnISg);

            return true;
        }

        public double GetAvgTexelSize(GetAvgTexelSizeTraversal traversal)
        {
            return m_instance.AvgTexelSize;
        }

        public bool HasFinished(HasFinishedTraversal t)
        {
            if (m_returnISg == null)
                return false;

            return m_returnISg.HasFinished(t);
        }
    }

    public class CachedCvg : Instance
    {
        public const string Identifier = "CachedCvg";

        public CachedCvg(ConcreteVertexGeometryLeaf cvg)
            : base(Identifier)
        {
            Cvg = cvg;
        }

        public readonly ConcreteVertexGeometryLeaf Cvg;
    }

    [Rule(typeof(CachedCvg))]
    public class CachedCvgRule : IRule
    {
        private readonly CachedCvg m_instance;

        public CachedCvgRule(CachedCvg instance, AbstractTraversal t)
        {
            m_instance = instance;
        }

        public void InitForPath(AbstractTraversal t)
        {
            // nop
        }

        public ISg SetParameters(AbstractTraversal t)
        {
            return m_instance.Cvg;
        }

        public bool DisposeAndRemove(DisposeAndRemoveTraversal t)
        {
            m_instance.Cvg.Dispose(t);

            return t.TryDisposeAndRemoveRule(m_instance, t);
        }
    }
}
