using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Manta
{
    static class Acoustics
    {
        public const double MinDist = 0.1; // metres — prevents log(0) singularity

        // ── Colour gradient: blue → cyan → yellow → orange → red ─────────────
        static readonly double[] StopT = { 0.00, 0.25, 0.50, 0.75, 1.00 };
        static readonly int[]    StopR = {    0,    0,  255,  255,  255 };
        static readonly int[]    StopG = {    0,  220,  240,  110,    0 };
        static readonly int[]    StopB = {  255,  255,    0,    0,    0 };

        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        public static Color GradientColor(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            for (int i = 0; i < StopT.Length - 1; i++)
            {
                if (t <= StopT[i + 1])
                {
                    double s = (t - StopT[i]) / (StopT[i + 1] - StopT[i]);
                    return Color.FromArgb(
                        Clamp((int)(StopR[i] + s * (StopR[i+1] - StopR[i]))),
                        Clamp((int)(StopG[i] + s * (StopG[i+1] - StopG[i]))),
                        Clamp((int)(StopB[i] + s * (StopB[i+1] - StopB[i]))));
                }
            }
            return Color.Red;
        }

        public static Color DbToColor(double db, double minDb, double maxDb)
            => GradientColor((db - minDb) / Math.Max(maxDb - minDb, 1e-6));

        // Regulatory limit overlay: green(safe) → yellow → orange → red(over)
        public static Color LimitColor(double db, double limitDb)
        {
            double over = db - limitDb;
            if (over <= -5) return Color.FromArgb(  0, 200,  80);
            if (over <=  0) return Color.FromArgb(200, 220,   0);
            if (over <=  5) return Color.FromArgb(255, 140,   0);
            return                  Color.FromArgb(220,   0,   0);
        }

        // ── Geometry helpers ─────────────────────────────────────────────────
        public static Point3d FaceCentroid(Mesh mesh, MeshFace f)
        {
            var A = (Point3d)mesh.Vertices[f.A];
            var B = (Point3d)mesh.Vertices[f.B];
            var C = (Point3d)mesh.Vertices[f.C];
            if (f.IsQuad)
            {
                var D = (Point3d)mesh.Vertices[f.D];
                return new Point3d((A.X+B.X+C.X+D.X)*0.25,
                                   (A.Y+B.Y+C.Y+D.Y)*0.25,
                                   (A.Z+B.Z+C.Z+D.Z)*0.25);
            }
            return new Point3d((A.X+B.X+C.X)/3.0, (A.Y+B.Y+C.Y)/3.0, (A.Z+B.Z+C.Z)/3.0);
        }

        public static Vector3d FaceNormal(Mesh mesh, int fi)
        {
            var nf = mesh.FaceNormals[fi];
            var n  = new Vector3d(nf.X, nf.Y, nf.Z);
            n.Unitize();
            return n;
        }

        public static double FaceArea(Mesh mesh, MeshFace f)
        {
            var A = (Point3d)mesh.Vertices[f.A];
            var B = (Point3d)mesh.Vertices[f.B];
            var C = (Point3d)mesh.Vertices[f.C];
            double area = Vector3d.CrossProduct(B - A, C - A).Length * 0.5;
            if (f.IsQuad)
            {
                var D = (Point3d)mesh.Vertices[f.D];
                area += Vector3d.CrossProduct(C - A, D - A).Length * 0.5;
            }
            return area;
        }

        // ── Direct sound: L = L_src − 20·log10(d) − 11 + 10·log10(cosθ + 0.01) ──
        public static double DirectDb(Point3d centroid, Vector3d normal,
                                      Point3d source, double level)
        {
            Vector3d dir = source - centroid;
            double   d   = Math.Max(dir.Length, MinDist);
            dir.Unitize();
            double cosT = Math.Max(Vector3d.Multiply(normal, dir), 0.0);
            return level - 20.0 * Math.Log10(d) - 11.0 + 10.0 * Math.Log10(cosT + 0.01);
        }

        // Energy summation: L_total = 10·log10(Σ 10^(Li/10))
        public static double EnergySum(IList<double> levels)
        {
            double sum = 0;
            for (int i = 0; i < levels.Count; i++) sum += Math.Pow(10.0, levels[i] / 10.0);
            return sum > 0 ? 10.0 * Math.Log10(sum) : -200.0;
        }

        // Per-face direct dB
        public static double[] ComputeDirect(Mesh mesh, List<Point3d> sources, List<double> levels)
        {
            int    fc  = mesh.Faces.Count;
            var    res = new double[fc];
            var    buf = new double[sources.Count];
            for (int fi = 0; fi < fc; fi++)
            {
                Point3d  c = FaceCentroid(mesh, mesh.Faces[fi]);
                Vector3d n = FaceNormal(mesh, fi);
                for (int si = 0; si < sources.Count; si++)
                    buf[si] = DirectDb(c, n, sources[si], levels[si]);
                res[fi] = EnergySum(buf);
            }
            return res;
        }

        // ── First-order reflections ───────────────────────────────────────────
        // L_ref = L_src − 20·log10(d1+d2) − 11 + 10·log10(cosθ+0.01) − α_dB − mat_loss
        public static double[] ComputeReflections(Mesh mesh,
                                                  List<Point3d> sources,
                                                  List<double>  levels,
                                                  double        absorptionDb,
                                                  double[]      faceAlpha)
        {
            int fc         = mesh.Faces.Count;
            var reflEnergy = new double[fc];

            for (int si = 0; si < sources.Count; si++)
            {
                for (int fi = 0; fi < fc; fi++)
                {
                    Point3d  centroid = FaceCentroid(mesh, mesh.Faces[fi]);
                    Vector3d normal   = FaceNormal(mesh, fi);

                    Vector3d toFace = centroid - sources[si];
                    double   d1     = Math.Max(toFace.Length, MinDist);
                    toFace.Unitize();

                    double cosTheta = Math.Max(-Vector3d.Multiply(toFace, normal), 0.0);
                    if (cosTheta < 0.01) continue;

                    double   dot     = Vector3d.Multiply(toFace, normal);
                    Vector3d reflDir = toFace - 2.0 * dot * normal;
                    reflDir.Unitize();

                    double  eps    = Math.Max(d1 * 1e-4, 0.005);
                    Point3d origin = centroid + normal * eps;

                    double tHit = Intersection.MeshRay(mesh, new Ray3d(origin, reflDir));
                    if (double.IsNaN(tHit) || double.IsInfinity(tHit) || tHit < 1e-3) continue;

                    double d2   = Math.Max(tHit, 0.01);
                    double dist = Math.Max(d1 + d2, MinDist);

                    double alpha     = (faceAlpha != null && fi < faceAlpha.Length)
                                       ? Math.Max(0, Math.Min(0.99, faceAlpha[fi])) : 0.0;
                    double matLossDb = alpha > 0 ? -10.0 * Math.Log10(1.0 - alpha) : 0.0;

                    double L_ref = levels[si]
                        - 20.0 * Math.Log10(dist)
                        - 11.0
                        + 10.0 * Math.Log10(cosTheta + 0.01)
                        - absorptionDb
                        - matLossDb;

                    Point3d hitPt = origin + reflDir * tHit;
                    var     mp    = mesh.ClosestMeshPoint(hitPt, 1e10);
                    int     hitFi = mp != null ? mp.FaceIndex : -1;
                    if (hitFi >= 0 && hitFi != fi)
                        reflEnergy[hitFi] += Math.Pow(10.0, L_ref / 10.0);
                }
            }

            var result = new double[fc];
            for (int fi = 0; fi < fc; fi++)
                result[fi] = reflEnergy[fi] > 0 ? 10.0 * Math.Log10(reflEnergy[fi]) : -200.0;
            return result;
        }

        public static double[] MergeDirectReflected(double[] direct, double[] reflected)
        {
            var res = new double[direct.Length];
            for (int i = 0; i < direct.Length; i++)
            {
                double eD = Math.Pow(10.0, direct[i] / 10.0);
                double eR = reflected[i] > -100 ? Math.Pow(10.0, reflected[i] / 10.0) : 0.0;
                res[i] = 10.0 * Math.Log10(eD + eR);
            }
            return res;
        }

        // ── Interior exposure score ───────────────────────────────────────────
        // Σ [ 10^(dBi/10) × area_i / dist_i² ]  →  single dB fitness scalar
        public static double InteriorScore(Mesh mesh, double[] faceDb, Point3d interiorPt)
        {
            double sum = 0;
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                double area = FaceArea(mesh, mesh.Faces[fi]);
                double dist = Math.Max(FaceCentroid(mesh, mesh.Faces[fi])
                                           .DistanceTo(interiorPt), MinDist);
                sum += Math.Pow(10.0, faceDb[fi] / 10.0) * area / (dist * dist);
            }
            return sum > 0 ? 10.0 * Math.Log10(sum) : -200.0;
        }

        // ── Regulatory compliance ─────────────────────────────────────────────
        public static double ExceededArea(Mesh mesh, double[] faceDb, double limitDb)
        {
            double area = 0;
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
                if (faceDb[fi] > limitDb)
                    area += FaceArea(mesh, mesh.Faces[fi]);
            return area;
        }

        // ── Vertex colours ────────────────────────────────────────────────────
        public static double[] FaceDbToVertexDb(Mesh mesh, double[] faceDb)
        {
            int vc  = mesh.Vertices.Count;
            var sum = new double[vc];
            var cnt = new int[vc];
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var    f  = mesh.Faces[fi];
                double db = faceDb[fi];
                sum[f.A] += db; cnt[f.A]++;
                sum[f.B] += db; cnt[f.B]++;
                sum[f.C] += db; cnt[f.C]++;
                if (f.IsQuad) { sum[f.D] += db; cnt[f.D]++; }
            }
            double fallback = faceDb.Length > 0 ? faceDb.Average() : 0;
            var result = new double[vc];
            for (int vi = 0; vi < vc; vi++)
                result[vi] = cnt[vi] > 0 ? sum[vi] / cnt[vi] : fallback;
            return result;
        }

        public static void PaintVertices(Mesh mesh, double[] faceDb,
                                         double scaleMin, double scaleMax)
        {
            double[] vDb    = FaceDbToVertexDb(mesh, faceDb);
            var      colors = new Color[mesh.Vertices.Count];
            for (int vi = 0; vi < colors.Length; vi++)
                colors[vi] = DbToColor(vDb[vi], scaleMin, scaleMax);
            mesh.VertexColors.SetColors(colors);
        }

        public static void PaintVerticesLimit(Mesh mesh, double[] faceDb, double limitDb)
        {
            double[] vDb    = FaceDbToVertexDb(mesh, faceDb);
            var      colors = new Color[mesh.Vertices.Count];
            for (int vi = 0; vi < colors.Length; vi++)
                colors[vi] = LimitColor(vDb[vi], limitDb);
            mesh.VertexColors.SetColors(colors);
        }

        // Offset mesh by 0.1 % of bbox diagonal to prevent z-fighting
        public static Mesh BuildDisplayMesh(Mesh src)
        {
            var    m   = src.DuplicateMesh();
            double off = src.GetBoundingBox(false).Diagonal.Length * 0.001;
            if (off < 1e-6) off = 1e-6;
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var n = m.Normals[i];
                var v = (Point3d)m.Vertices[i];
                v += new Vector3d(n.X, n.Y, n.Z) * off;
                m.Vertices[i] = new Point3f((float)v.X, (float)v.Y, (float)v.Z);
            }
            return m;
        }

        // ── Geometry conversion ───────────────────────────────────────────────
        public static Mesh ConvertToMesh(GeometryBase geo, int quality)
        {
            var mp = quality == 0 ? MeshingParameters.FastRenderMesh
                   : quality == 1 ? MeshingParameters.Default
                   : quality == 2 ? MeshingParameters.DefaultAnalysisMesh
                                  : MeshingParameters.QualityRenderMesh;

            if (geo is Mesh direct) return direct.DuplicateMesh();

            Brep brep = null;
            if      (geo is Surface  srf) brep = srf.ToBrep();
            else if (geo is Brep       b) brep = b;
            else if (geo is Extrusion  e) brep = e.ToBrep(false);
            else if (geo is SubD    subd)
            {
                try { brep = subd.ToBrep(new SubDToBrepOptions()); }
                catch
                {
                    var sm = Mesh.CreateFromSubD(subd, 3);
                    if (sm != null && sm.Faces.Count > 0) return sm;
                }
            }

            if (brep != null)
            {
                var arr = Mesh.CreateFromBrep(brep, mp);
                if (arr != null && arr.Length > 0)
                {
                    var combined = new Mesh();
                    foreach (var m in arr) combined.Append(m);
                    return combined;
                }
            }
            return null;
        }

        // ── Line-source subdivision ───────────────────────────────────────────
        // Divide curve into N segment midpoints; each gets L_sub = L_total − 10·log10(N)
        public static void SubdivideLineSource(Curve curve, double totalLevel, int n,
                                               List<Point3d> sources, List<double> levels)
        {
            if (n < 1) n = 1;
            double subLevel = totalLevel - 10.0 * Math.Log10(n);
            double[] tParams = curve.DivideByCount(n, true);
            if (tParams == null || tParams.Length < 2)
            {
                sources.Add(curve.PointAtNormalizedLength(0.5));
                levels.Add(totalLevel);
                return;
            }
            for (int i = 0; i < tParams.Length - 1; i++)
            {
                sources.Add(curve.PointAt((tParams[i] + tParams[i + 1]) * 0.5));
                levels.Add(subLevel);
            }
        }
    }
}
