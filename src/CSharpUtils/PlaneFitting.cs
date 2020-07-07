using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Uncodium;

namespace CSharpUtils
{
    public static class PlaneFitting
    {
        public static Plane3d Fit(this V3d[] points)
        {

            var kk = points.Length;

            // compute covariance matrix of k closest points relative to centroid
            var c = V3d.Zero;
            for (var j = 0; j < kk; j++) c += points[j];
            c /= kk;

            var cvm = M33d.Zero;
            for (var j = 0; j < kk; j++)
                CovarianceMatrixExtensions.AddOuterProduct(ref cvm, points[j] - c);
            cvm /= kk;

            // solve eigensystem -> eigenvector for smallest eigenvalue gives normal
            Eigensystems.Dsyevh3(cvm, out M33d q, out V3d w);
            var n = ((w.X < w.Y) ? ((w.X < w.Z) ? q.C0 : q.C2) : ((w.Y < w.Z) ? q.C1 : q.C2));

            return new Plane3d(n, c);
        }

        public static Line3d Line(this V3d[] points)
        {
            // compute covariance matrix of k closest points relative to centroid
            var kk = points.Length;

            // compute covariance matrix of k closest points relative to centroid
            var c = V3d.Zero;
            for (var j = 0; j < kk; j++) c += points[j];
            c /= kk;

            var cvm = M33d.Zero;
            for (var j = 0; j < kk; j++)
                CovarianceMatrixExtensions.AddOuterProduct(ref cvm, points[j] - c);
            cvm /= kk;

            // solve eigensystem -> eigenvector with largest eigenvalue is best fit line
            Eigensystems.Dsyevh3(cvm, out M33d q, out V3d w);
            var n = ((w.X > w.Y) ? ((w.X > w.Z) ? q.C0 : q.C2) : ((w.Y > w.Z) ? q.C1 : q.C2));
           
            return new Line3d(c - n, c + n);
        }
    }
}
