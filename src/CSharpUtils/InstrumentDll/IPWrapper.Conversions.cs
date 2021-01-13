using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using IPWrappers;
using static JR.InstrumentPlatforms;

namespace IPWrappers
{
    public static class IPWrapperConversions
    {        
        public static List<V3d> ToV3ds(this SPoint3D[] self)
        {
            return self.Select(x => x.ToV3d()).ToList();
        }

        public static V3d ToV3d(this SPoint3D self)
        {
            return new V3d(self.m_dX, self.m_dY, self.m_dZ);
        }

        public static V3d ToV3d(this SVector3D self)
        {
            return new V3d(self.m_dX, self.m_dY, self.m_dZ);
        }

        public static Box3d ToBox3d(this SBoundingBox self)
        {
            var origin = self.m_oOriginBB.ToV3d();
            var edge1 = self.m_oEdge1.ToV3d();
            var edge2 = self.m_oEdge2.ToV3d();
            var edge3 = self.m_oEdge3.ToV3d();

            return new Box3d(origin,
                origin + edge1,
                origin + edge2,
                origin + edge3);
        }

        public static M44d ToM44d(this STransformationMatrix self)
        {
            return new M44d(self.m_adElement);
        }

        public static STransformationMatrix ToSTransfMatrix(this M44d self)
        {
            return new STransformationMatrix()
            {
                m_adElement = self.Elements.ToArray()
            };
        }
    }
}
