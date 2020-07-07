using System.IO;
using System.Linq;
using Aardvark.Lod;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using Aardvark.Base;

namespace Aardvark.Opc.PatchHierarchy
{
    public interface IOpcSgBuilderStrategy
    {
        ISg CreateSceneGraph(OpcPaths paths, PatchHierarchy hier);
    }

    // probably make standard opc builder
}
