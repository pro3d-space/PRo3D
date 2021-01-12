using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IPWrappers
{
    /// <summary>
    /// Contains functions and marshalling structs to call routines from the InstrumentPlatforms.dll 
    /// provided by Joanneum Research. 
    /// </summary>
    public static partial class ViewPlanner
    {        
        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern int Init(string configDir, string logDir);

        #region GetPlatformNames()
        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern uint GetNrOfAvailablePlatforms();

        [DllImport(@"InstrumentPlatforms.dll", CharSet = CharSet.Ansi)]
        public static extern void GetAvailablePlatforms([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] IntPtr[] platformNames, uint numberOfPlatforms);

        [DllImport(@"InstrumentPlatforms.dll", CharSet = CharSet.Ansi)]
        public static extern int InitPlatform(ref SPlatform poPlatform, uint nNrOfPlatformPointsOnGround, uint nNrOfPlatformInstruments, uint nNrOfPlatformAxes);
        #endregion

        #region GetPlatform(string platformId)     
        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern uint GetNrOfPlatformPointsOnGround(string pcPlatformId);
        
        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern uint GetNrOfPlatformInstruments(string pcPlatformId);

        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern uint GetNrOfPlatformAxes(string pcPlatformId);

        [DllImport(@"InstrumentPlatforms.dll")]
        public static extern int UpdatePlatform(ref SPlatform poPlatform);
        #endregion       

        public class ArrayBuffer
        {
            public IntPtr Pointer { get; private set; }
            public IntPtr[] ElementPointers { get; private set; }

            public int Count { get { return ElementPointers.Length; } }

            public ArrayBuffer(IntPtr pointer, IntPtr[] elementPointers)
            {
                Pointer = pointer;
                ElementPointers = elementPointers;
            }
        }
    }

    public static partial class CooTrafo
    {
        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern uint GetDllVersion();

        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern int Init(string configDir, string logDir);

        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern void DeInit();

        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern int Xyz2LatLonRad(double dX, double dY, double dZ, ref double pdLat, ref double pdLon, ref double pdRad);

        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern int Xyz2LatLonAlt(string pcPlanet, double dX, double dY, double dZ, ref double pdLat, ref double pdLon, ref double pdAlt );

        [DllImport(@".\bin\CooTransformation.dll")]
        public static extern int LatLonAlt2Xyz( string pcPlanet, double dLat, double dLon, double dAlt, ref double pdX, ref double pdY, ref double pdZ );
    }
}
