using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Aardvark.Base;
using Aardvark.VRVis;
using Aardvark.Algodat;
using Aardvark.Parser.Aara;
using Aardvark.Rendering;
using Aardvark.Runtime;

namespace Aardvark.Opc.PatchHierarchy
{
    public interface IPatchLoadingStrategy
    {
        /// <summary>
        /// Loads VertexGeometry from aara files. Beware: add Local2Global node for global space.
        /// </summary>
        /// <returns>Vertex Geometry in local OPC space.</returns>
        VertexGeometry Load(PatchFileInfo info, string basePath, PositionsType posType,
            bool loadTextures = true, bool loadNormals = true, bool loadTexCoords = true, float maxTriangleSize = float.PositiveInfinity);
    }

    public class PatchLoadingStrategy : IPatchLoadingStrategy
    {
        public bool LoadDiffTex = true;
        public bool LoadHueTex = true;

        #region implement IPatchLoadingStrategy

        public VertexGeometry Load(PatchFileInfo info, string basePath, PositionsType posType,
            bool loadTextures = true, bool loadNormals = true, bool loadTexCoords = true, float maxTriangleSize = float.PositiveInfinity)
        {
            //Array positions;
            //Symbol dataType;
            bool loadDiffTex = loadTextures ? LoadDiffTex : false;
            bool loadHueTex = loadTextures ? LoadHueTex : false;

            return LoadPatch(info, basePath, posType, //out positions, out dataType,
                loadNormals, loadTexCoords, loadDiffTex, loadHueTex, maxTriangleSize);
        }

        #endregion

        /// <summary>
        /// Gets Triangle Set loaded from AaraData and transformed to global space.
        /// </summary>
        public static TriangleSet LoadPatchTriangleSet(PatchFileInfo info, string basePath, PositionsType posType)
        {
            var vg = LoadPatch(info, basePath, posType, false, false, false, false, loadAsDoubles: false);
            if (vg == null) return new TriangleSet();

            var trafo =  posType == PositionsType.V3dPositions ? info.Local2Global : info.Local2Global2d;

            vg.TransformV3d(trafo);

            var triangles = vg.Triangles
               .Where(x => !x.Point0.Position.IsNaN &&
                   !x.Point1.Position.IsNaN &&
                   !x.Point2.Position.IsNaN)
               .Select(x => x.ToTriangle3d());

            return new TriangleSet(triangles);
        }

        /// <summary>
        /// Gets Triangle Set loaded from AaraData and transformed to global space.
        /// Triangles side length < maxTriiangleSize
        /// </summary>
        public static TriangleSet LoadPatchTriangleSetWithoutOversizedTriangles(PatchFileInfo info, string basePath, PositionsType posType, float maxTriangleSize = float.PositiveInfinity)
        {
            var vg = LoadPatch(info, basePath, posType, false, false, false, false);
            if (vg == null) return new TriangleSet();

            vg.Transform(
                posType == PositionsType.V3dPositions ?
                    info.Local2Global : info.Local2Global2d);

            var triangles = vg.Triangles
               .Where(x => !x.Point0.Position.IsNaN &&
                   !x.Point1.Position.IsNaN &&
                   !x.Point2.Position.IsNaN &&
                   (V3d.Distance(x.Point0.Position, x.Point1.Position) < maxTriangleSize) &&
                   (V3d.Distance(x.Point0.Position, x.Point2.Position) < maxTriangleSize) &&
                   (V3d.Distance(x.Point1.Position, x.Point2.Position) < maxTriangleSize))
               .Select(x => x.ToTriangle3d());

           return new TriangleSet(triangles);
        }

        /// <summary>
        /// Loads VertexGeometry from aara files. Beware: add Local2Global node for global space.
        /// </summary>
        /// <returns>Vertex Geometry in local OPC space.</returns>
        public static VertexGeometry LoadPatch(PatchFileInfo info, string basePath, PositionsType posType,
            bool loadNormals = true, bool loadTexCoords = true, bool loadDiffTex = true, bool loadHueTex = true, float maxTriangleSize = float.PositiveInfinity, bool loadAsDoubles = false)
        {
            Array positions;
            Symbol dataType;

            return LoadPatch(info, basePath, posType, out positions, out dataType,
                loadNormals, loadTexCoords, loadDiffTex, loadHueTex, maxTriangleSize, loadAsDoubles);
        }

        /// <summary>
        /// Loads VertexGeometry from aara files. Beware: add Local2Global node for global space.
        /// </summary>
        /// <param name="positions">Raw positions, read from aara files for possible further processing.</param>
        /// <param name="dataType">DataType of positions.</param>
        /// <returns>Vertex Geometry in local OPC space.</returns>
        public static VertexGeometry LoadPatch(PatchFileInfo info, string basePath, PositionsType posType, out Array positions, out Symbol dataType,
            bool loadNormals = true, bool loadTexCoords = true, bool loadDiffTex = true, bool loadHueTex = true, float maxTriangleSize = float.PositiveInfinity, bool loadAsDoubles = false)
        {
            var vg = new VertexGeometry(GeometryMode.TriangleList);
            positions = null;

            // load metadata
            var aara3dPos = AaraData.FromFile(
                Path.Combine(basePath, posType == PositionsType.V3dPositions
                ? info.Positions : info.Positions2d));
            dataType = aara3dPos.DataTypeAsSymbol;

            var resolution = new V2i(aara3dPos.Size);
            if (resolution.AnySmaller(2))
            {
                Report.Warn("ignoring patch {0} due to invalid gridresolution {1}", basePath, resolution);
                return null;
            }

            // load positions
            positions = aara3dPos.LoadElements();
            var positions3d = loadAsDoubles ? positions : AaraData.ConvertArrayToV3fs[aara3dPos.DataTypeAsSymbol](positions);

            //var positionsV3 = loadAsDoubles ? 
            //    (Array)AaraData.ConvertArrayToV3ds[aara3dPos.DataTypeAsSymbol](positions) :
            //    (Array);

            vg.Positions = positions3d;

            var p = AaraData.ConvertArrayToV3fs[aara3dPos.DataTypeAsSymbol](positions);

            // calculate indices
            var invalidPoints = OpcIndices.GetInvalidPositions(p);
            // limit triangle size
            if ((maxTriangleSize < float.PositiveInfinity)&&(maxTriangleSize > 0.000001f))
                vg.Indices = OpcIndices.ComputeIndexArray(resolution, invalidPoints.ToList(), p, maxTriangleSize);
            else
                vg.Indices = OpcIndices.ComputeIndexArray(resolution, invalidPoints.ToList());

            // load normals
            if (loadNormals)
            {
                var normalPath = Path.Combine(basePath, "Normals.aara");
                if (StorageConfig.FileExists(normalPath))
                {
                    var normals = AaraData.FromFile(normalPath);
                    var normals3d = AaraData.ConvertArrayToV3fs[normals.DataTypeAsSymbol](normals.LoadElements());
                    vg.Normals = normals3d;
                }
            }

            // load coordinates
            vg.Coordinates = new CoordinatesMap();
            if (loadTexCoords)
            {
                var coordPath = Path.Combine(basePath, info.Coordinates.First());
                var coordinates = AaraData.FromFile(coordPath).LoadElements() as V2f[];
                vg.Coordinates[VertexGeometry.Property.DiffuseColorCoordinates] = coordinates;
            }

            // load textures
            vg.Textures = new TexturesMap();
            if (loadDiffTex)
            {
                var texFile = Path.ChangeExtension(info.Textures.First(), ".dds");
                var texPath = Path.GetFullPath(Path.Combine(basePath, @"..\..\images", texFile));

                if (StorageConfig.FileExists(texPath))
                {
                    var img = Convertible.FromFile(texPath);

                    vg.Textures[VertexGeometry.Property.DiffuseColorTexture] =
                        new Aardvark.Rendering.Texture(img)
                        {
                            ForceImmediateUpload = false
                        };
                }
            }
            if (loadHueTex)
            {
                vg.Textures[VertexGeometry.Property.LightMapTexture] =
                    new Aardvark.Rendering.Texture(Resources.HueColorMap.Convertible());
            }

            return vg;
        }
    }
}