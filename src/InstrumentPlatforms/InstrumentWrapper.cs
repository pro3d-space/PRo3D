using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace JR
{
    /// <summary>
    /// Contains functions and marshalling structs to call routines from the InstrumentPlatforms.dll 
    /// provided by Joanneum Research. 
    /// </summary>
    public static partial class InstrumentPlatforms
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
}
