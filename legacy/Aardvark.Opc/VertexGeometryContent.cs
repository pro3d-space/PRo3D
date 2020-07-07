using System.Collections.Generic;
using Aardvark.Runtime;
using Aardvark.Algodat;
using Aardvark.VRVis;

namespace Aardvark.Opc
{
    public class VertexGeometryContent : IFieldCodeable
    {
        public List<VertexGeometry> VertexGeometries;

        public VertexGeometryContent()
        {
            VertexGeometries = new List<VertexGeometry>();
        }

        public VertexGeometryContent(List<VertexGeometry> vgs)
        {
            VertexGeometries = vgs;
        }

        public VertexGeometryContent(VertexGeometry vg)
        {
            VertexGeometries = new List<VertexGeometry>() { vg };
        }

        #region IFieldCodeable Members

        public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
        {
            return new[] 
                {
                    new FieldCoder(0, "VertexGeometries", (c, o) => c.CodeList_of_T_(ref ((VertexGeometryContent)o).VertexGeometries)),
                };
        }

        #endregion
    }
}
