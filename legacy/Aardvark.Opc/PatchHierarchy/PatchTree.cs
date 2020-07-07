using Aardvark.Base;
using Aardvark.VRVis;
using System.Collections.Generic;
using System.IO;

namespace Aardvark.Opc.PatchHierarchy
{
    public interface IHierarchyNode<T> : INode where T : IHierarchyNode<T>
    {
        new IEnumerable<T> SubNodes { get; }
    }

    [RegisterTypeInfo]
    public class PatchTree : IHierarchyNode<PatchTree>, IFieldCodeable
    {
        /// <summary>
        /// For serialization only.
        /// </summary>
        public PatchTree() { }

        public PatchTree(string id, PatchFileInfo info, List<PatchTree> subNodes = null)
        {
            if (subNodes == null) subNodes = new List<PatchTree>();

            m_id = id;
            m_subNodes = subNodes;
            m_info = info;
        }

        private string m_id;
        private PatchFileInfo m_info;
        private List<PatchTree> m_subNodes;

        public string Id
        {
            get { return m_id; }
        }

        public PatchFileInfo Info
        {
            get { return m_info; }
        }

        public IEnumerable<PatchTree> SubNodes
        {
            get { return m_subNodes; }
        }

        /// <summary>
        /// Enables usage of INode routines on PatchTrees
        /// </summary>
        IEnumerable<INode> INode.SubNodes
        {
            get { return m_subNodes; }
        }

        public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
        {
            return new[]
            {
                new FieldCoder(0, "Id", (c, o) => c.CodeString(ref ((PatchTree)o).m_id)),
                new FieldCoder(1, "Info", (c, o) => c.CodeT(ref ((PatchTree)o).m_info)),
                new FieldCoder(2, "SubNodes", (c, o) => c.CodeList_of_T_(ref ((PatchTree)o).m_subNodes)),
            };
        }

        public string PatchPath { get; set; }

        public string GetPositionPath(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return Path.Combine(PatchPath, Info.Positions);
            else
                return Path.Combine(PatchPath, Info.Positions2d);
        }

        /// <summary>
        /// Constructs patchpaths in a PatchTree hierarchy so that no absolute paths
        /// have to be saved within a PatchTree.
        /// </summary>
        public void CreatePatchPaths(string basePath)
        {
            var patchPath = Path.Combine(basePath, Id);
            PatchPath = patchPath;
            SubNodes.ForEach(x => x.CreatePatchPaths(basePath));
        }
    }
}
