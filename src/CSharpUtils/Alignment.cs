//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Aardvark.Base;

//namespace Aardvark.VRVis.Approx
//{

//    public class Approx<T>
//    {
//        public T Value;
//        public ScalarError Error;
//        public int Iterations;
//        public int Resets;
//        public bool Converged;
//    }

//    public class CenteredTrafo3d
//    {
//        public Shift3d SourceShift;
//        public Trafo3d Trafo;
//        public Shift3d TargetShift;

//    }

//    public struct ScalarError
//    {
//        public double MaxRelative;
//        public double AvgRelative;
//        public double MaxAbsolute;
//        public double AvgAbsolute;

//        public static readonly ScalarError MaxValue = new ScalarError
//        {
//            MaxRelative = double.MaxValue,
//            AvgRelative = double.MaxValue,
//            MaxAbsolute = double.MaxValue,
//            AvgAbsolute = double.MaxValue,
//        };

//        public void Set(double size, int count, double[] errorArray)
//        {
//            MaxAbsolute = errorArray.FoldLeft(0.0, (max, d) => Fun.Max(max, d));
//            AvgAbsolute = errorArray.FoldLeft(0.0, (sum, d) => sum + d) / count;
//            MaxRelative = MaxAbsolute / size;
//            AvgRelative = AvgAbsolute / size;
//        }


//    }


//    public class Approximation<T>
//    {
//        public T Value;
//        public double MaxRelativeError;
//        public double AvgRelativeError;
//        public double MaxAbsoluteError;
//        public double AvgAbsoluteError;
//        public int Iterations;
//        public int Resets;
//        public bool Converged;
//    }


//    public static class PoseTrafoEstimation
//    {

//        public static M33d ComputeRotation(V3d[] source, V3d[] target)
//        {
//            int n = source.Length;
//            if (n != target.Length) throw new ArgumentException();

//            // covariance matrix
//            var A = M33d.Zero;
//            for (var j = 0; j < 3; j++)
//            {
//                for (var i = 0; i < 3; i++)
//                {
//                    var sum = 0.0;
//                    for (var k = 0; k < n; k++) sum += target[k][i] * source[k][j];
//                    A[i, j] = sum;
//                }
//            }

//            // optimal rotation matrix R
//            var svd = A.SVD();
//            var Ut = svd.U.Transposed;
//            var d = (svd.V * Ut).Det.Sign();
//            var R = svd.V * new M33d(1, 0, 0, 0, 1, 0, 0, 0, d) * Ut;

//            return R;
//        }


//        public class Config
//        {
//            public int Iterations = 1024;
//            public double MaxRelativeError = 1.0e-9;

//            public double MaxNoImprovementDelta = 1.0e-4;

//            public int NoImprovementCount = 4;
//            public double MaxNoImprovementError = 1.0e-2;

//            public int MaxRetryCount = 32;

//            public bool Report = false;

//            public static Config SimilarityTrafoDefault = new Config
//            {
//                Iterations = 1024,
//                MaxRelativeError = 1.0e-9,
//                MaxNoImprovementDelta = 1.0e-4,
//                NoImprovementCount = 4,
//                MaxNoImprovementError = 1.0e-2,
//                MaxRetryCount = 32,
//                Report = false,
//            };

//            public static Config CameraTrafoDefault = new Config
//            {
//                Iterations = 1024,
//                MaxRelativeError = 1.0e-9,
//                MaxNoImprovementDelta = 1.0e-4,
//                NoImprovementCount = 4,
//                MaxNoImprovementError = 1.0e-2,
//                MaxRetryCount = 32,
//                Report = false,
//            };

//        }


//        /// <summary>
//        /// Estimates the similarity transformation (scale * rotation * translation)
//        /// from the source to the target points.
//        /// </summary>
//        public static Approximation<Trafo3d> SimilarityTrafo(
//                V3d[] sourcePoints, V3d[] targetPoints, Config config = null)
//        {
//            if (config == null) config = Config.SimilarityTrafoDefault;

//            var rnd = new RandomNR2(130768);
//            var count = sourcePoints.Length;

//            var sourceCentroid = sourcePoints.ComputeCentroid();
//            var targetCentroid = targetPoints.ComputeCentroid();

//            var centeredSourcePoints = sourcePoints.Map(sp => sp - sourceCentroid);
//            var centeredTargetPoints = targetPoints.Map(tp => tp - targetCentroid);
//            var size = centeredTargetPoints.FoldLeft(0.0, (sum, tp) => sum + tp.Length) / count;

//            var converged = true;

//            Trafo3d trafo;

//            var relativeAvgErrorOld = double.MaxValue;
//            var totalIterations = 0;
//            var resets = 0;

//            var maxAbsoluteError = double.MaxValue;
//            var avgAbsoluteError = double.MaxValue;
//            var maxRelativeError = double.MaxValue;
//            var avgRelativeError = double.MaxValue;
//            var bestAvgRelativeError = double.MaxValue;
//            Trafo3d bestTrafo = Trafo3d.Identity;

//            var initialRotation = Trafo3d.Identity;

//            var distances = new double[count];

//            do
//            {
//                trafo = Trafo3d.Translation(-sourceCentroid) * initialRotation;

//                converged = true;
//                relativeAvgErrorOld = double.MaxValue;
//                int noImprovementCount = 0;
//                maxAbsoluteError = double.MaxValue;
//                avgAbsoluteError = double.MaxValue;
//                maxRelativeError = double.MaxValue;
//                avgRelativeError = double.MaxValue;

//                for (int iter = 0; iter < config.Iterations; iter++)
//                {
//                    centeredSourcePoints.SetMap(sourcePoints, p => trafo.Forward.TransformPos(p));

//                    var scale = centeredTargetPoints.FoldLeft2(centeredSourcePoints, 0.0,
//                            (sum, tp, sp) => sum + tp.Length / sp.Length)
//                            / count;

//                    // Report.Line("scale {0}", scale);
//                    trafo = trafo * Trafo3d.Scale(scale);

//                    centeredSourcePoints.SetMap(sourcePoints, p => trafo.Forward.TransformPos(p));

//                    distances.SetMap2(centeredSourcePoints, centeredTargetPoints,
//                                      (sp, tp) => V3d.Distance(tp, sp));

//                    maxAbsoluteError = distances.FoldLeft(0.0, (max, d) => Fun.Max(max, d));
//                    avgAbsoluteError = distances.FoldLeft(0.0, (sum, d) => sum + d) / count;

//                    maxRelativeError = maxAbsoluteError / size;
//                    avgRelativeError = avgAbsoluteError / size;

//                    var improvement = relativeAvgErrorOld / avgRelativeError - 1.0;

//                    if (config.Report)
//                        Report.Line("{0}: max={1:e4} avg={2:e4} delta={3:e4}",
//                                    iter, maxRelativeError, avgRelativeError, improvement);

//                    if (maxRelativeError < config.MaxRelativeError)
//                    {
//                        if (config.Report) Report.Line("FINISHED MAXRELATIVEERROR");
//                        totalIterations += iter;
//                        break;
//                    }

//                    if (improvement < config.MaxNoImprovementDelta)
//                        noImprovementCount++;
//                    else
//                        noImprovementCount = 0;


//                    if (noImprovementCount >= config.NoImprovementCount
//                            && avgRelativeError > config.MaxNoImprovementError)
//                    {
//                        if (config.Report) Report.Line("RESTART NO IMPROVEMENT");
//                        converged = false;
//                        var rot = (M44d)new Rot3d(rnd.UniformV3dDirection(), rnd.UniformDouble() * Constant.PiTimesTwo);
//                        initialRotation = new Trafo3d(rot, rot.Transposed);
//                        totalIterations += iter;
//                        resets++;

//                        if (avgRelativeError < bestAvgRelativeError)
//                        {
//                            bestAvgRelativeError = avgRelativeError;
//                            bestTrafo = trafo;
//                        }

//                        break;
//                    }

//                    if (noImprovementCount > config.NoImprovementCount)
//                    {
//                        if (config.Report) Report.Line("FINISHED NO IMPROVEMENT");
//                        totalIterations += iter;
//                        break;
//                    }

//                    relativeAvgErrorOld = avgRelativeError;

//                    var rotation = ComputeRotation(centeredSourcePoints, centeredTargetPoints);
//                    var rotationT = rotation.Transposed;

//                    trafo = trafo * new Trafo3d((M44d)rotationT, (M44d)(rotation));
//                }

//                if (resets >= config.MaxRetryCount)
//                {
//                    trafo = bestTrafo;
//                    avgRelativeError = bestAvgRelativeError;
//                    break;
//                }

//            }
//            while (!converged);

//            trafo = trafo * Trafo3d.Translation(targetCentroid);


//            return new Approximation<Trafo3d>
//            {
//                Value = trafo,
//                AvgRelativeError = avgRelativeError,
//                MaxRelativeError = maxRelativeError,
//                AvgAbsoluteError = avgAbsoluteError,
//                MaxAbsoluteError = maxAbsoluteError,
//                Iterations = totalIterations,
//                Resets = resets,
//                Converged = converged
//            };
//        }


//        /// <summary>
//        /// Estimates the camera transformation (rotation + translation)
//        /// based on two arrays of corresponding points in the global
//        /// coordinate system (3d) and camera coordinate system (2d,
//        /// projecton to z = 1, camera axis is the z-coordinate axis).
//        /// </summary>
//        public static Approximation<Trafo3d> CameraTrafo(
//                V3d[] globalPoints, V2d[] cameraPoints, Config config = null)
//        {
//            if (config == null) config = Config.CameraTrafoDefault;

//            var rnd = new RandomNR2(130768);
//            var count = globalPoints.Length;

//            var cameraVectors = cameraPoints.Map(p => p.XYI.Normalized);

//            var globalCentroid = globalPoints.ComputeCentroid();
//            var globalScale = globalPoints.Sum(p => (p - globalCentroid).Length) * 1.0 / count;

//            var cameraCentroid = VectorIEnumerableExtensions.ComputeCentroid(cameraPoints);
//            var cameraScale = cameraPoints.Sum(p => (p - cameraCentroid).Length) * 1.0 / count;

//            var initielShift = cameraCentroid.XYI * (globalScale / cameraScale);
//            var initialRotation = Trafo3d.Identity;

//            var converged = true;

//            Trafo3d trafo;

//            var avgRelativeErrorOld = double.MaxValue;
//            var totalIterations = 0;
//            var resets = 0;

//            var maxAbsoluteError = double.MaxValue;
//            var avgAbsoluteError = double.MaxValue;
//            var maxRelativeError = double.MaxValue;
//            var avgRelativeError = double.MaxValue;
//            var bestAvgRelativeError = double.MaxValue;
//            Trafo3d bestTrafo = Trafo3d.Identity;

//            var sourcePoints = new V3d[count];
//            var targetPoints = new V3d[count];
//            var distances = new double[count];

//            do
//            {
//                trafo = Trafo3d.Translation(-globalCentroid)
//                        * initialRotation * Trafo3d.Translation(initielShift);

//                V3d sourceCentroid;
//                V3d targetCentroid;

//                converged = true;
//                avgRelativeErrorOld = double.MaxValue;
//                int noImprovementCount = 0;

//                maxAbsoluteError = double.MaxValue;
//                avgAbsoluteError = double.MaxValue;
//                maxRelativeError = double.MaxValue;
//                avgRelativeError = double.MaxValue;

//                for (int iter = 0; iter < config.Iterations; iter++)
//                {
//                    sourcePoints.SetMap(globalPoints, p => trafo.Forward.TransformPos(p));
//                    targetPoints.SetMap2(sourcePoints, cameraVectors, (p, v) => v * V3d.Dot(v, p));

//                    sourceCentroid = sourcePoints.ComputeCentroid();
//                    targetCentroid = targetPoints.ComputeCentroid();

//                    var scale = targetPoints.FoldLeft2(sourcePoints, 0.0,
//                            (sum, tp, sp) => sum + (sp - sourceCentroid).Length / (tp - targetCentroid).Length)
//                            / count;

//                    var zshift = sourceCentroid.Length * (scale - 1.0);

//                    // Report.Line("scale {0}", scale);
//                    trafo = trafo * Trafo3d.Translation(0.0, 0.0, zshift);

//                    sourcePoints.SetMap(globalPoints, p => trafo.Forward.TransformPos(p));
//                    targetPoints.SetMap2(sourcePoints, cameraVectors, (p, v) => v * V3d.Dot(v, p));

//                    var size = sourcePoints.FoldLeft(0.0, (sum, sp) => sum + (sp - sourceCentroid).Length)
//                                                    / count;

//                    distances.SetMap2(sourcePoints, targetPoints, (sp, tp) => V3d.Distance(tp, sp));

//                    maxAbsoluteError = distances.FoldLeft(0.0, (max, d) => Fun.Max(max, d));
//                    avgAbsoluteError = distances.FoldLeft(0.0, (sum, d) => sum + d) / count;


//                    maxRelativeError = maxAbsoluteError / size;
//                    avgRelativeError = avgAbsoluteError / size;

//                    var improvement = avgRelativeErrorOld / avgRelativeError - 1.0;

//                    if (config.Report)
//                        Report.Line("{0}: max={1:e4} avg={2:e4} delta={3:e4}",
//                                    iter, maxRelativeError, avgRelativeError, improvement);

//                    if (maxRelativeError < config.MaxRelativeError)
//                    {
//                        if (config.Report) Report.Line("FINISHED MAXRELATIVEERROR");
//                        totalIterations += iter;
//                        break;
//                    }


//                    if (improvement < config.MaxNoImprovementDelta)
//                        noImprovementCount++;
//                    else
//                        noImprovementCount = 0;


//                    if (noImprovementCount >= config.NoImprovementCount
//                            && avgRelativeError > config.MaxNoImprovementError)
//                    {
//                        if (config.Report) Report.Line("RESTART NO IMPROVEMENT");
//                        converged = false;
//                        var rot = (M44d)new Rot3d(rnd.UniformV3dDirection(), rnd.UniformDouble() * Constant.PiTimesTwo);
//                        initialRotation = new Trafo3d(rot, rot.Transposed);
//                        totalIterations += iter;
//                        resets++;
//                        if (avgRelativeError < bestAvgRelativeError)
//                        {
//                            bestAvgRelativeError = avgRelativeError;
//                            bestTrafo = trafo;
//                        }
//                        break;
//                    }

//                    if (noImprovementCount > config.NoImprovementCount)
//                    {
//                        if (config.Report) Report.Line("FINISHED NO IMPROVEMENT");
//                        totalIterations += iter;
//                        break;
//                    }

//                    avgRelativeErrorOld = avgRelativeError;

//                    sourceCentroid = sourcePoints.ComputeCentroid();
//                    targetCentroid = targetPoints.ComputeCentroid();

//                    sourcePoints.Apply(sp => sp - sourceCentroid);
//                    targetPoints.Apply(tp => tp - targetCentroid);

//                    var rotation = ComputeRotation(sourcePoints, targetPoints);
//                    var rotationT = rotation.Transposed;

//                    trafo = trafo // * Trafo3d.Translation(-sourceCentroid)
//                            * new Trafo3d((M44d)rotationT, (M44d)(rotation))
//                            * Trafo3d.Translation((rotationT * -sourceCentroid) + targetCentroid);
//                }

//                if (resets >= config.MaxRetryCount)
//                {
//                    trafo = bestTrafo;
//                    avgRelativeError = bestAvgRelativeError;
//                    break;
//                }

//            }
//            while (!converged);


//            return new Approximation<Trafo3d>
//            {
//                Value = trafo,
//                AvgRelativeError = avgRelativeError,
//                MaxRelativeError = maxRelativeError,
//                AvgAbsoluteError = avgAbsoluteError,
//                MaxAbsoluteError = maxAbsoluteError,
//                Iterations = totalIterations,
//                Resets = resets,
//                Converged = converged
//            };
//        }



//        /// <summary>
//        /// Estimates the similarity transformation (scale * rotation * translation)
//        /// between the source and target points.
//        /// </summary>
//        public static Approx<CenteredTrafo3d> Similarity(
//                V3d[] sourcePoints, V3d[] targetPoints, Config config = null)
//        {
//            if (config == null) config = Config.SimilarityTrafoDefault;

//            var rnd = new RandomNR2(130768);
//            var count = sourcePoints.Length;

//            var sourceCentroid = sourcePoints.ComputeCentroid();
//            var targetCentroid = targetPoints.ComputeCentroid();

//            var centeredSourcePoints = sourcePoints.Map(sp => sp - sourceCentroid);
//            var centeredTargetPoints = targetPoints.Map(tp => tp - targetCentroid);
//            var size = centeredTargetPoints.FoldLeft(0.0, (sum, tp) => sum + tp.Length) / count;

//            var transformedSourcePoints = centeredSourcePoints.Copy();

//            var converged = true;

//            Trafo3d trafo;

//            var relativeAvgErrorOld = double.MaxValue;
//            var totalIterations = 0;
//            var resets = 0;

//            var bestError = ScalarError.MaxValue;

//            var error = ScalarError.MaxValue;

//            Trafo3d bestTrafo = Trafo3d.Identity;

//            var initialRotation = Trafo3d.Identity;

//            var distances = new double[count];

//            do
//            {
//                trafo = initialRotation;
//                converged = true;
//                relativeAvgErrorOld = double.MaxValue;
//                int noImprovementCount = 0;
//                error = ScalarError.MaxValue;

//                for (int iter = 0; iter < config.Iterations; iter++)
//                {
//                    transformedSourcePoints.SetMap(centeredSourcePoints, p => trafo.Forward.TransformPos(p));

//                    var scale = centeredTargetPoints.FoldLeft2(transformedSourcePoints, 0.0,
//                            (sum, tp, sp) => sum + tp.Length / sp.Length)
//                            / count;

//                    // Report.Line("scale {0}", scale);
//                    trafo = trafo * Trafo3d.Scale(scale);

//                    transformedSourcePoints.SetMap(centeredSourcePoints, p => trafo.Forward.TransformPos(p));

//                    distances.SetMap2(transformedSourcePoints, centeredTargetPoints,
//                                      (sp, tp) => V3d.Distance(tp, sp));

//                    error.Set(size, count, distances);

//                    var improvement = relativeAvgErrorOld / error.AvgRelative - 1.0;

//                    if (config.Report)
//                        Report.Line("{0}: max={1:e4} avg={2:e4} delta={3:e4}",
//                                    iter, error.MaxRelative, error.AvgRelative, improvement);

//                    if (error.MaxRelative < config.MaxRelativeError)
//                    {
//                        if (config.Report) Report.Line("FINISHED MAXRELATIVEERROR");
//                        totalIterations += iter;
//                        break;
//                    }

//                    if (improvement < config.MaxNoImprovementDelta)
//                        noImprovementCount++;
//                    else
//                        noImprovementCount = 0;

//                    if (error.AvgRelative < bestError.AvgRelative)
//                    {
//                        bestError = error;
//                        bestTrafo = trafo;
//                    }

//                    if (noImprovementCount >= config.NoImprovementCount
//                            && error.AvgRelative > config.MaxNoImprovementError)
//                    {
//                        if (config.Report) Report.Line("RESTART NO IMPROVEMENT");
//                        converged = false;
//                        var rot = (M44d)new Rot3d(rnd.UniformV3dDirection(), rnd.UniformDouble() * Constant.PiTimesTwo);
//                        initialRotation = new Trafo3d(rot, rot.Transposed);
//                        totalIterations += iter;
//                        resets++;

//                        break;
//                    }

//                    if (noImprovementCount > config.NoImprovementCount)
//                    {
//                        if (config.Report) Report.Line("FINISHED NO IMPROVEMENT");
//                        totalIterations += iter;
//                        break;
//                    }

//                    relativeAvgErrorOld = error.AvgRelative;

//                    var rotation = ComputeRotation(transformedSourcePoints, centeredTargetPoints);
//                    var rotationT = rotation.Transposed;

//                    trafo = trafo * new Trafo3d((M44d)rotationT, (M44d)(rotation));
//                }

//                if (resets >= config.MaxRetryCount)
//                {
//                    trafo = bestTrafo;
//                    error = bestError;
//                    break;
//                }

//            }
//            while (!converged);

//            return new Approx<CenteredTrafo3d>
//            {
//                Value = new CenteredTrafo3d
//                {
//                    SourceShift = new Shift3d(-sourceCentroid),
//                    Trafo = trafo,
//                    TargetShift = new Shift3d(targetCentroid),
//                },
//                Error = error,
//                Iterations = totalIterations,
//                Resets = resets,
//                Converged = converged
//            };
//        }


//        /// <summary>
//        /// Esitmates the camera transformation (rotation + translation)
//        /// based on two arrays of corresponding points in the global
//        /// coordinate system (3d) and camera coordinate system (2d,
//        /// projecton to z = 1, camera axis is the z-coordinate axis).
//        /// </summary>
//        public static Approx<Trafo3d> Camera(
//                V3d[] globalPoints, V2d[] cameraPoints, Config config = null)
//        {
//            if (config == null) config = Config.CameraTrafoDefault;

//            var rnd = new RandomNR2(130768);
//            var count = globalPoints.Length;

//            var cameraVectors = cameraPoints.Map(p => p.XYI.Normalized);

//            var globalCentroid = globalPoints.ComputeCentroid();
//            var globalScale = globalPoints.Sum(p => (p - globalCentroid).Length) * 1.0 / count;

//            var cameraCentroid = VectorIEnumerableExtensions.ComputeCentroid(cameraPoints);
//            var cameraScale = cameraPoints.Sum(p => (p - cameraCentroid).Length) * 1.0 / count;

//            var initielShift = cameraCentroid.XYI * (globalScale / cameraScale);
//            var initialRotation = Trafo3d.Identity;

//            var converged = true;

//            Trafo3d trafo;

//            var avgRelativeErrorOld = double.MaxValue;
//            var totalIterations = 0;
//            var resets = 0;

//            var error = ScalarError.MaxValue;
//            var bestError = ScalarError.MaxValue;
//            Trafo3d bestTrafo = Trafo3d.Identity;

//            var sourcePoints = new V3d[count];
//            var targetPoints = new V3d[count];
//            var distances = new double[count];

//            do
//            {
//                trafo = Trafo3d.Translation(-globalCentroid)
//                        * initialRotation * Trafo3d.Translation(initielShift);

//                V3d sourceCentroid;
//                V3d targetCentroid;

//                converged = true;
//                avgRelativeErrorOld = double.MaxValue;
//                int noImprovementCount = 0;

//                error = ScalarError.MaxValue;

//                for (int iter = 0; iter < config.Iterations; iter++)
//                {
//                    sourcePoints.SetMap(globalPoints, p => trafo.Forward.TransformPos(p));
//                    targetPoints.SetMap2(sourcePoints, cameraVectors, (p, v) => v * V3d.Dot(v, p));

//                    sourceCentroid = sourcePoints.ComputeCentroid();
//                    targetCentroid = targetPoints.ComputeCentroid();

//                    var scale = targetPoints.FoldLeft2(sourcePoints, 0.0,
//                            (sum, tp, sp) => sum + (sp - sourceCentroid).Length / (tp - targetCentroid).Length)
//                            / count;

//                    var zshift = sourceCentroid.Length * (scale - 1.0);

//                    // Report.Line("scale {0}", scale);
//                    trafo = trafo * Trafo3d.Translation(0.0, 0.0, zshift);

//                    sourcePoints.SetMap(globalPoints, p => trafo.Forward.TransformPos(p));
//                    targetPoints.SetMap2(sourcePoints, cameraVectors, (p, v) => v * V3d.Dot(v, p));

//                    var size = sourcePoints.FoldLeft(0.0, (sum, sp) => sum + (sp - sourceCentroid).Length)
//                                                    / count;

//                    distances.SetMap2(sourcePoints, targetPoints, (sp, tp) => V3d.Distance(tp, sp));

//                    error.Set(size, count, distances);

//                    var improvement = avgRelativeErrorOld / error.AvgRelative - 1.0;

//                    if (config.Report)
//                        Report.Line("{0}: max={1:e4} avg={2:e4} delta={3:e4}",
//                                    iter, error.MaxRelative, error.AvgRelative, improvement);

//                    if (error.MaxRelative < config.MaxRelativeError)
//                    {
//                        if (config.Report) Report.Line("FINISHED MAXRELATIVEERROR");
//                        totalIterations += iter;
//                        break;
//                    }


//                    if (improvement < config.MaxNoImprovementDelta)
//                        noImprovementCount++;
//                    else
//                        noImprovementCount = 0;


//                    if (error.AvgRelative < bestError.AvgRelative)
//                    {
//                        bestError = error;
//                        bestTrafo = trafo;
//                    }

//                    if (noImprovementCount >= config.NoImprovementCount
//                            && error.AvgRelative > config.MaxNoImprovementError)
//                    {
//                        if (config.Report) Report.Line("RESTART NO IMPROVEMENT");
//                        converged = false;
//                        var rot = (M44d)new Rot3d(rnd.UniformV3dDirection(), rnd.UniformDouble() * Constant.PiTimesTwo);
//                        initialRotation = new Trafo3d(rot, rot.Transposed);
//                        totalIterations += iter;
//                        resets++;
//                        break;
//                    }

//                    if (noImprovementCount > config.NoImprovementCount)
//                    {
//                        if (config.Report) Report.Line("FINISHED NO IMPROVEMENT");
//                        totalIterations += iter;
//                        break;
//                    }

//                    avgRelativeErrorOld = error.AvgRelative;

//                    sourceCentroid = sourcePoints.ComputeCentroid();
//                    targetCentroid = targetPoints.ComputeCentroid();

//                    sourcePoints.Apply(sp => sp - sourceCentroid);
//                    targetPoints.Apply(tp => tp - targetCentroid);

//                    var rotation = ComputeRotation(sourcePoints, targetPoints);
//                    var rotationT = rotation.Transposed;

//                    trafo = trafo // * Trafo3d.Translation(-sourceCentroid)
//                            * new Trafo3d((M44d)rotationT, (M44d)(rotation))
//                            * Trafo3d.Translation((rotationT * -sourceCentroid) + targetCentroid);
//                }

//                if (resets >= config.MaxRetryCount)
//                {
//                    trafo = bestTrafo;
//                    error = bestError;
//                    break;
//                }

//            }
//            while (!converged);

//            return new Approx<Trafo3d>
//            {
//                Value = trafo,
//                Error = error,
//                Iterations = totalIterations,
//                Resets = resets,
//                Converged = converged
//            };
//        }
//    }

//    public static class Tools
//    {

//        public static void ToText(this M44d m)
//        {
//            Report.Line("[ {0:0.00000}, {1:0.00000}, {2:0.00000}, {3:0.00000} ]", m.M00, m.M01, m.M02, m.M03);
//            Report.Line("[ {0:0.00000}, {1:0.00000}, {2:0.00000}, {3:0.00000} ]", m.M10, m.M11, m.M12, m.M13);
//            Report.Line("[ {0:0.00000}, {1:0.00000}, {2:0.00000}, {3:0.00000} ]", m.M20, m.M21, m.M22, m.M23);
//        }

//    }
//}
