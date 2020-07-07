using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Aardvark.Base;
using System.Text;
using System.Threading.Tasks;

namespace IPWrappers
{
    public static partial class ViewPlanner
    {
        public const string NotSet = "NotSet";

        public static IntPtr ToPtr(this string self)
        {
            return Marshal.StringToHGlobalAnsi(self);
        }

        public static string ToStrAnsi(this IntPtr self)
        {
            return Marshal.PtrToStringAnsi(self);
        }

        #region Primitives
        [StructLayout(LayoutKind.Sequential)]
        public struct SPoint3D
        {
            public double m_dX;
            public double m_dY;
            public double m_dZ;

            public static SPoint3D Default()
            {
                return new SPoint3D()
                {
                    m_dX = 1.0,
                    m_dY = 2.0,
                    m_dZ = 3.0
                };
            }
        };      

        [StructLayout(LayoutKind.Sequential)]
        public struct SVector3D
        {
            public double m_dX;
            public double m_dY;
            public double m_dZ;

            public static SVector3D Default()
            {
                return new SVector3D()
                {
                    m_dX = double.NaN,
                    m_dY = double.NaN,
                    m_dZ = double.NaN
                };
            }
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct SAxis
        {
            public IntPtr m_pcAxisId;
            public IntPtr m_pcAxisDescription;

            public SPoint3D m_oStartPoint;
            public SPoint3D m_oEndPoint;
            public double m_fMinAngle; /* unit: gon */
            public double m_fMaxAngle; /* unit: gon */
            public double m_fCurrentAngle; /* unit: gon */

            public static SAxis Default()
            {
                return new SAxis()
                {
                    m_pcAxisId = NotSet.ToPtr(),
                    m_pcAxisDescription = NotSet.ToPtr(),
                    m_oStartPoint = SPoint3D.Default(),
                    m_oEndPoint = SPoint3D.Default(),
                    m_fMinAngle = 1.0,
                    m_fMaxAngle = 2.0,
                    m_fCurrentAngle = 3.0
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SBoundingBox
        {
            public SPoint3D m_oOriginBB;
            /* indicating direction and length of a bounding box's edge */
            public SVector3D m_oEdge1;
            public SVector3D m_oEdge2;
            public SVector3D m_oEdge3;

            public static SBoundingBox Default()
            {
                return new SBoundingBox()
                {
                    m_oOriginBB = SPoint3D.Default(),
                    m_oEdge1 = SVector3D.Default(),
                    m_oEdge2 = SVector3D.Default(),
                    m_oEdge3 = SVector3D.Default()
                };
            }
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct STransformationMatrix
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public double[] m_adElement;

            public static STransformationMatrix Default()
            {
                var a = new double[16];
                for (int i = 0; i < 16; i++)
                    a[i] = i * 0.1;

                return new STransformationMatrix()
                {
                    m_adElement = a
                };
            }
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct STransformation
        {
            public IntPtr m_pcTransfName;
            public IntPtr m_pcSourceFrame;
            public IntPtr m_pcTargetFrame;
            public STransformationMatrix m_oHelmertTransfMatrix;

            public static STransformation Default()
            {
                return new STransformation()
                {
                    m_pcTransfName = "TransName".ToPtr(),
                    m_pcSourceFrame = "SourceName".ToPtr(),
                    m_pcTargetFrame = "TargeName".ToPtr(),
                    m_oHelmertTransfMatrix = STransformationMatrix.Default()
                };
            }
        }
        #endregion

        [StructLayout(LayoutKind.Sequential)]
        public struct SInstrumentIntrinsics
        {
            public uint m_nResolutionH;
            public uint m_nResolutionV;

            public double m_dFieldOfViewH;
            public double m_dFieldOfViewV;
          
            public double m_dPrinciplePointH;
            public double m_dPrinciplePointV;
            public double m_dFocalLengthPerPxH;
            public double m_dFocalLengthPerPxV;

            //public IntPtr m_pcDistortionMapH; /* file pointer to horizontal distortions */
            //                                  /* (2D matrix containing col offset per pixel) */
            //public IntPtr m_pcDistortionMapV; /* file pointer to vertical distortions */
            //                                  /* (2D matrix containing row offset per pixel) */
            //public IntPtr m_pcvignettingMap;  /* file pointer to vignetting information */
            //                                  /* (2D matrix containing grey value scale factor per pixel) */

            public static SInstrumentIntrinsics Default()
            {
                return new SInstrumentIntrinsics()
                {
                    m_nResolutionH = 0,
                    m_nResolutionV = 0,

                    m_dFieldOfViewH = 1.0,
                    m_dFieldOfViewV = 2.0,
                    m_dPrinciplePointH = 3.0,
                    m_dPrinciplePointV = 4.0,

                    m_dFocalLengthPerPxH = 5.0,
                    m_dFocalLengthPerPxV = 6.0,

                    //m_pcDistortionMapH = "DistortH".ToPtr(),
                    //m_pcDistortionMapV = "DistortV".ToPtr(),
                    //m_pcvignettingMap = "Vignette".ToPtr()
                };
            }
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct SInstrumentExtrinsics
        {
            public IntPtr m_pcReferenceFrame;
            public SPoint3D m_oPosition;
            public SVector3D m_oLookAt;
            public SVector3D m_oUp;
            public SBoundingBox m_oBoundingBox;

            public static SInstrumentExtrinsics Default()
            {
                return new SInstrumentExtrinsics()
                {
                    m_pcReferenceFrame = NotSet.ToPtr(),
                    m_oPosition = SPoint3D.Default(),
                    m_oLookAt = SVector3D.Default(),
                    m_oUp = SVector3D.Default(),
                    m_oBoundingBox = SBoundingBox.Default()
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SInstrument
        {
            public IntPtr m_pcInstrumentName;
            public uint m_nNrOfCalibratedFocalLengths;
            
            public IntPtr m_pdCalibratedFocalLengths; //double
            public double m_dCurrentFocalLengthInMm;

            public SInstrumentIntrinsics m_oInstrumentIntrinsics;
            public SInstrumentExtrinsics m_oInstrumentExtrinsics;

            public uint m_nNrOfInstrumentAxes;

            public IntPtr m_poInstrumentAxes; /* list of references to SPlatform member variable */

            public static SInstrument Default(IntPtr[] axesRefs)
            {
                var axesRefs_m = MarshalArray(axesRefs);


                return new SInstrument()
                {
                    m_pdCalibratedFocalLengths = IntPtr.Zero,
                    m_dCurrentFocalLengthInMm = 0,
                    m_pcInstrumentName = "DefaultInstrumentName".ToPtr(),
                    m_oInstrumentIntrinsics = SInstrumentIntrinsics.Default(),
                    m_oInstrumentExtrinsics = SInstrumentExtrinsics.Default(),

                    m_nNrOfInstrumentAxes = (uint)axesRefs.Length,
                    m_poInstrumentAxes = axesRefs_m
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SPlatform
        {
            public IntPtr m_pcPlatformId;

            public STransformation m_oPlatform2Ground;

            public SBoundingBox m_oBoundingBox;

            public uint m_nNrOfPlatformPointsOnGround;
            public IntPtr m_poPointsOnGround; //array SPoint3D

            public uint m_nNrOfPlatformInstruments;
            public IntPtr m_poPlatformInstruments; //array SInstrument

            public uint m_nNrOfPlatformAxes;
            public IntPtr m_poPlatformAxes; //array SAxis

            public static SPlatform CreateEmpty()
            {
                //var pointsArray = new SPoint3D[numberPointsOnGround].Set(SPoint3D.Default());
                //var axisArray = new SAxis[numberOfAxes].Set(SAxis.Default());
                //var axisArray_m = MarshalArray(axisArray);

                //var childPointers = s_childPointerLut[axisArray_m];
                //var instrumentsArray = new SInstrument[numberOfInstruments].Set(SInstrument.Default(childPointers));

                return new SPlatform()
                {
                    m_pcPlatformId = NotSet.ToPtr(),
                    m_oPlatform2Ground = STransformation.Default(),
                    m_oBoundingBox = SBoundingBox.Default(),

                  //  m_nNrOfPlatformPointsOnGround = numberPointsOnGround,
                  //  m_poPointsOnGround = MarshalArray(pointsArray),

                 //   m_nNrOfPlatformInstruments = numberOfInstruments,
                  //  m_poPlatformInstruments = MarshalArray(instrumentsArray),

                  //  m_nNrOfPlatformAxes = numberOfAxes,
                  //  m_poPlatformAxes = axisArray_m
                };
            }

            public static SPlatform CreateEmpty(uint numberPointsOnGround, uint numberOfInstruments, uint numberOfAxes)
            {
                var pointsArray = new SPoint3D[numberPointsOnGround].Set(SPoint3D.Default());
                var axisArray = new SAxis[numberOfAxes].Set(SAxis.Default());
                var axisArray_m = MarshalArray(axisArray);

                var childPointers = s_childPointerLut[axisArray_m];
                var instrumentsArray = new SInstrument[numberOfInstruments].Set(SInstrument.Default(childPointers));
              
                return new SPlatform()
                {
                    m_pcPlatformId = NotSet.ToPtr(),
                    m_oPlatform2Ground = STransformation.Default(),
                    m_oBoundingBox = SBoundingBox.Default(),

                    m_nNrOfPlatformPointsOnGround = numberPointsOnGround,
                    m_poPointsOnGround = MarshalArray(pointsArray),

                    m_nNrOfPlatformInstruments = numberOfInstruments,
                    m_poPlatformInstruments = MarshalArray(instrumentsArray),

                    m_nNrOfPlatformAxes = numberOfAxes,
                    m_poPlatformAxes = axisArray_m
                };
            }
        };

        public static volatile Dictionary<IntPtr, IntPtr[]> s_childPointerLut = new Dictionary<IntPtr, IntPtr[]>();

        /// <summary>
        /// Marshals array of structs to a pointer. In this process global memory is allocated 
        /// and the resulting pointer is returned.
        /// </summary>
        public static IntPtr MarshalArray<T>(T[] input) where T : struct
        {
            // compute sizes
            var count = input.Length;
            var sizeOfOne = Marshal.SizeOf(typeof(T));
            var arraySize = sizeOfOne * count;

            // allocate mem and get buffer pointer
            var arrayPtr = Marshal.AllocHGlobal(arraySize);
            var intPtrs = new IntPtr[count];

            //first ptr is array ptr
            var runningPtr = arrayPtr;
            for (int i = 0; i < count; i++)
            {
                intPtrs[i] = runningPtr;
                Marshal.StructureToPtr(input[i], runningPtr, false);
                runningPtr = new IntPtr((long)runningPtr + sizeOfOne);
            }

            s_childPointerLut.Add(arrayPtr, intPtrs);

            return arrayPtr;
        }

        /// <summary>
        /// Marshals an array of structs to an already existing array pointer. 
        /// The array must be of the same TYPE and LENGTH as when it was allocated.
        /// </summary>
        public static void MarshalArray<T>(T[] input, IntPtr existingPtr) where T : struct
        {
            // compute sizes
            var count = input.Length;
            var sizeOfOne = Marshal.SizeOf(typeof(T));
            var arraySize = sizeOfOne * count;

            var intPtrs = new IntPtr[count];

            //first ptr is array ptr
            var runningPtr = existingPtr;
            for (int i = 0; i < count; i++)
            {
                intPtrs[i] = runningPtr;
                Marshal.StructureToPtr(input[i], runningPtr, false);
                runningPtr = new IntPtr((long)runningPtr + sizeOfOne);
            }        
        }

        //public static T[] UnMarshalArray<T>(ArrayBuffer input) where T : struct
        //{
        //    return UnMarshalArray<T>(input.Pointer, input.ElementPointers);
        //}

        public static T[] UnMarshalArray<T>(IntPtr ptr) where T : struct
        {
            var elements = s_childPointerLut[ptr];
            return UnMarshalArray<T>(ptr, elements);
        }

        public static T[] UnMarshalArray<T>(IntPtr ptr, IntPtr[] elements) where T : struct
        {
            var count = elements.Length;
            var outputArray = new T[count];

            for (int i = 0; i < count; i++)
            {
                outputArray[i] = (T)Marshal.PtrToStructure(elements[i], typeof(T));                
            }

            return outputArray;
        }

        public static T[] UnMarshalArray<T>(IntPtr ptr, int count) where T : struct
        {
            var outputArray = new T[count];
            var sizeOfOne = Marshal.SizeOf(typeof(T));
            var runningPtr = ptr;

            for (int i = 0; i < count; i++)
            {
                outputArray[i] = (T)Marshal.PtrToStructure(runningPtr, typeof(T));
                runningPtr = new IntPtr((long)runningPtr + sizeOfOne);
            }

            return outputArray;
        }

        public static void DisposePointers()
        {
            foreach (var kvp in s_childPointerLut)
            {
                Marshal.FreeHGlobal(kvp.Key);
            }

            s_childPointerLut.Clear();
        }
    }
}
