using System.Collections.Generic;
using Rhino.Geometry;

namespace Manta
{
    // Marching-triangles isodecibel contour extraction.
    static class ContourAlgo
    {
        public static List<Polyline> March(Mesh mesh, double[] vertexDb, double level)
        {
            var segs = new List<(Point3d A, Point3d B)>();

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var f = mesh.Faces[fi];
                AddSegs(mesh, vertexDb, f.A, f.B, f.C, level, segs);
                if (f.IsQuad)
                    AddSegs(mesh, vertexDb, f.A, f.C, f.D, level, segs);
            }

            double tol = mesh.GetBoundingBox(false).Diagonal.Length * 1e-5;
            if (tol < 1e-9) tol = 1e-4;
            return Chain(segs, tol);
        }

        static void AddSegs(Mesh mesh, double[] vDb, int iA, int iB, int iC,
                            double level, List<(Point3d, Point3d)> segs)
        {
            Point3d A = (Point3d)mesh.Vertices[iA];
            Point3d B = (Point3d)mesh.Vertices[iB];
            Point3d C = (Point3d)mesh.Vertices[iC];
            double dA = vDb[iA], dB = vDb[iB], dC = vDb[iC];

            var cross = new List<Point3d>(3);
            Interpolate(A, dA, B, dB, level, cross);
            Interpolate(B, dB, C, dC, level, cross);
            Interpolate(C, dC, A, dA, level, cross);

            if      (cross.Count == 2) segs.Add((cross[0], cross[1]));
            else if (cross.Count >= 3) segs.Add((cross[0], cross[2]));
        }

        static void Interpolate(Point3d A, double dA, Point3d B, double dB,
                                double level, List<Point3d> result)
        {
            if ((dA >= level) == (dB >= level)) return;
            double t = (level - dA) / (dB - dA);
            result.Add(A + (B - A) * t);
        }

        static List<Polyline> Chain(List<(Point3d A, Point3d B)> segs, double tol)
        {
            var result = new List<Polyline>();
            var used   = new bool[segs.Count];

            for (int start = 0; start < segs.Count; start++)
            {
                if (used[start]) continue;

                var chain = new List<Point3d> { segs[start].A, segs[start].B };
                used[start] = true;

                bool grew = true;
                while (grew)
                {
                    grew = false;
                    var tail = chain[chain.Count - 1];
                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (used[j]) continue;
                        if (tail.DistanceTo(segs[j].A) < tol)
                        { chain.Add(segs[j].B); used[j] = true; grew = true; break; }
                        if (tail.DistanceTo(segs[j].B) < tol)
                        { chain.Add(segs[j].A); used[j] = true; grew = true; break; }
                    }
                }

                grew = true;
                while (grew)
                {
                    grew = false;
                    var head = chain[0];
                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (used[j]) continue;
                        if (head.DistanceTo(segs[j].B) < tol)
                        { chain.Insert(0, segs[j].A); used[j] = true; grew = true; break; }
                        if (head.DistanceTo(segs[j].A) < tol)
                        { chain.Insert(0, segs[j].B); used[j] = true; grew = true; break; }
                    }
                }

                if (chain.Count >= 2)
                    result.Add(new Polyline(chain));
            }
            return result;
        }
    }
}
