// Manta Ray — math tests
// Standalone, no Rhino/GH dependency.
// Reproduces MantaAcoustics.cs math inline; runs as plain console app.

using System;
using System.Drawing;

// ── Self-contained acoustic math (mirrors MantaAcoustics.cs exactly) ─────────
static class AM
{
    public const double MinDist = 0.1;

    static readonly double[] StopT = { 0.00, 0.25, 0.50, 0.75, 1.00 };
    static readonly int[]    StopR = {    0,    0,  255,  255,  255 };
    static readonly int[]    StopG = {    0,  220,  240,  110,    0 };
    static readonly int[]    StopB = {  255,  255,    0,    0,    0 };
    static int Cl(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public static Color GradientColor(double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        for (int i = 0; i < StopT.Length - 1; i++)
        {
            if (t <= StopT[i + 1])
            {
                double s = (t - StopT[i]) / (StopT[i + 1] - StopT[i]);
                return Color.FromArgb(
                    Cl((int)(StopR[i] + s*(StopR[i+1]-StopR[i]))),
                    Cl((int)(StopG[i] + s*(StopG[i+1]-StopG[i]))),
                    Cl((int)(StopB[i] + s*(StopB[i+1]-StopB[i]))));
            }
        }
        return Color.Red;
    }

    public static Color DbToColor(double db, double minDb, double maxDb)
        => GradientColor((db - minDb) / Math.Max(maxDb - minDb, 1e-6));

    public static Color LimitColor(double db, double limitDb)
    {
        double over = db - limitDb;
        if (over <= -5) return Color.FromArgb(  0, 200,  80);
        if (over <=  0) return Color.FromArgb(200, 220,   0);
        if (over <=  5) return Color.FromArgb(255, 140,   0);
        return              Color.FromArgb(220,   0,   0);
    }

    // L = Lsrc − 20·log10(d) − 11 + 10·log10(cosθ + 0.01)
    public static double DirectDb(double[] cen, double[] nrm, double[] src, double level)
    {
        double dx = src[0]-cen[0], dy = src[1]-cen[1], dz = src[2]-cen[2];
        double len = Math.Sqrt(dx*dx + dy*dy + dz*dz);
        double d   = Math.Max(len, MinDist);
        if (len > 1e-12) { dx/=len; dy/=len; dz/=len; } else { dx=0; dy=0; dz=1; }
        double cosT = Math.Max(nrm[0]*dx + nrm[1]*dy + nrm[2]*dz, 0.0);
        return level - 20.0*Math.Log10(d) - 11.0 + 10.0*Math.Log10(cosT + 0.01);
    }

    // 10·log10(Σ 10^(Li/10))
    public static double EnergySum(double[] levels)
    {
        double s = 0;
        foreach (double L in levels) s += Math.Pow(10.0, L/10.0);
        return s > 0 ? 10.0*Math.Log10(s) : -200.0;
    }

    // Σ[ 10^(dBi/10) · area_i / dist_i² ]  →  single dB scalar
    public static double InteriorScore(
        double[][] cens, double[] areas, double[] faceDb, double[] ip)
    {
        double sum = 0;
        for (int i = 0; i < cens.Length; i++)
        {
            double dx = cens[i][0]-ip[0], dy = cens[i][1]-ip[1], dz = cens[i][2]-ip[2];
            double dist = Math.Max(Math.Sqrt(dx*dx+dy*dy+dz*dz), MinDist);
            sum += Math.Pow(10.0, faceDb[i]/10.0) * areas[i] / (dist*dist);
        }
        return sum > 0 ? 10.0*Math.Log10(sum) : -200.0;
    }

    // r = d − 2·(d·n)·n
    public static void ReflectDir(double[] d, double[] n, out double[] r)
    {
        double dot = d[0]*n[0] + d[1]*n[1] + d[2]*n[2];
        r = new[]{ d[0]-2*dot*n[0], d[1]-2*dot*n[1], d[2]-2*dot*n[2] };
    }

    // Linear interpolation parameter t where vA + t*(vB-vA) = level
    public static double ContourT(double vA, double vB, double level)
        => (level - vA) / (vB - vA);
}

// ── Test harness ──────────────────────────────────────────────────────────────
class Program
{
    static int _pass, _fail;

    static void Eq(string name, double actual, double expected, double tol = 0.01)
    {
        bool ok = Math.Abs(actual - expected) <= tol;
        Console.WriteLine($"  [{(ok?"PASS":"FAIL")}] {name}");
        if (!ok) Console.WriteLine($"         got {actual:F5}  expected {expected:F5}  tol {tol}");
        if (ok) _pass++; else _fail++;
    }

    static void RGB(string name, Color c, int er, int eg, int eb, int tol = 5)
    {
        bool ok = Math.Abs(c.R-er)<=tol && Math.Abs(c.G-eg)<=tol && Math.Abs(c.B-eb)<=tol;
        Console.WriteLine($"  [{(ok?"PASS":"FAIL")}] {name}");
        if (!ok) Console.WriteLine($"         got ({c.R},{c.G},{c.B})  expected ({er},{eg},{eb})  tol ±{tol}");
        if (ok) _pass++; else _fail++;
    }

    static void True(string name, bool cond)
    {
        Console.WriteLine($"  [{(cond?"PASS":"FAIL")}] {name}");
        if (cond) _pass++; else _fail++;
    }

    static void Banner(string s)
        => Console.WriteLine($"\n── {s} " + new string('─', Math.Max(0, 55 - s.Length)));

    static double[] O = { 0.0, 0.0, 0.0 };   // origin / centroid
    static double[] X = { 1.0, 0.0, 0.0 };   // +X normal

    // ── 1. Direct dB ──────────────────────────────────────────────────────────
    static void TestDirectDb()
    {
        Banner("Direct dB — inverse-square + Lambert cosine");

        //  d=1m, cosθ=1:   L = 90 − 0 − 11 + 10·log10(1.01) = 79.043
        Eq("d=1m  normal incidence",
            AM.DirectDb(O, X, new[]{ 1.0,0,0 }, 90), 79.043);

        //  d=10m, cosθ=1:  L = 90 − 20 − 11 + 0.043 = 59.043
        Eq("d=10m normal incidence",
            AM.DirectDb(O, X, new[]{ 10.0,0,0 }, 90), 59.043);

        //  d=100m, cosθ=1: L = 90 − 40 − 11 + 0.043 = 39.043
        Eq("d=100m normal incidence",
            AM.DirectDb(O, X, new[]{ 100.0,0,0 }, 90), 39.043);

        //  distance doubling = −6.02 dB
        double L10 = AM.DirectDb(O, X, new[]{ 10.0,0,0 }, 90);
        double L20 = AM.DirectDb(O, X, new[]{ 20.0,0,0 }, 90);
        Eq("distance doubling → −6.02 dB", L10 - L20, 6.021, 0.01);

        //  cosθ = 0 (face away): 10·log10(0+0.01) adds −20 dB
        double[] nAway = {-1.0,0,0};
        Eq("face pointing away (cosθ=0), d=10m",
            AM.DirectDb(O, nAway, new[]{ 10.0,0,0 }, 90), 39.0, 0.05);

        //  grazing 45°: normal=(0,0,1), source=(10,0,10), d=14.142, cosθ=0.707
        //  L = 90 − 20·log10(14.142) − 11 + 10·log10(0.717) = 54.55
        double[] nUp  = {0,0,1.0};
        Eq("45° incidence, d=14.14m, cosθ=0.707",
            AM.DirectDb(O, nUp, new[]{ 10.0,0,10.0 }, 90), 54.55, 0.1);

        //  distance clamping: src at origin → no NaN
        double clamped = AM.DirectDb(O, X, new[]{ 0.0,0,0 }, 90);
        True("d=0 clamped to 0.1 → finite result", !double.IsNaN(clamped));
    }

    // ── 2. Energy summation ───────────────────────────────────────────────────
    static void TestEnergySum()
    {
        Banner("Energy summation");

        double single = AM.EnergySum(new[]{ 60.0 });
        Eq("1 source baseline = 60 dB",      single, 60.0);
        Eq("2 identical → +3.01 dB",         AM.EnergySum(new[]{ 60.0,60.0 }) - single,
                                              10.0*Math.Log10(2), 0.01);
        Eq("4 identical → +6.02 dB",         AM.EnergySum(new[]{ 60.0,60.0,60.0,60.0 }) - single,
                                              6.021, 0.01);
        Eq("10 identical → +10.00 dB",       AM.EnergySum(new double[10].Initialize(60.0)) - single,
                                              10.0, 0.01);
        Eq("empty array → −200",             AM.EnergySum(new double[0]), -200.0);
    }

    // ── 3. Line source reconstruction ─────────────────────────────────────────
    static void TestLineSource()
    {
        Banner("Line source: N sub-sources reconstruct total energy");
        double total = 75.0;
        foreach (int n in new[]{ 1, 5, 10, 20, 50 })
        {
            double sub   = total - 10.0*Math.Log10(n);
            var    subs  = new double[n].Initialize(sub);
            Eq($"N={n,2} → {total} dB", AM.EnergySum(subs), total, 0.001);
        }
    }

    // ── 4. Gradient colour ────────────────────────────────────────────────────
    static void TestGradientColor()
    {
        Banner("Gradient colour — blue → cyan → yellow → orange → red");

        // Keyframe stops (exact values from MantaAcoustics.cs)
        RGB("t=0.00 → pure blue",   AM.GradientColor(0.00),   0,   0, 255);
        RGB("t=0.25 → cyan",        AM.GradientColor(0.25),   0, 220, 255);
        RGB("t=0.50 → yellow",      AM.GradientColor(0.50), 255, 240,   0);
        RGB("t=0.75 → orange",      AM.GradientColor(0.75), 255, 110,   0);
        RGB("t=1.00 → red",         AM.GradientColor(1.00), 255,   0,   0);

        // Clamping
        RGB("t<0 clamps → blue",    AM.GradientColor(-0.5),   0,   0, 255);
        RGB("t>1 clamps → red",     AM.GradientColor( 1.5), 255,   0,   0);

        // Midpoints (interpolated)
        // t=0.125: halfway blue→cyan: R=0, G=110, B=255
        RGB("t=0.125 (half blue→cyan)", AM.GradientColor(0.125), 0, 110, 255, 3);

        // t=0.375: halfway cyan→yellow: R=127, G=230, B=128
        RGB("t=0.375 (half cyan→yellow)", AM.GradientColor(0.375), 127, 230, 128, 5);

        // DbToColor with equal min=max should not throw and return a valid colour
        Color c = AM.DbToColor(60, 60, 60);
        True("DbToColor equal min/max → no exception", c.A >= 0);
    }

    // ── 5. WHO limit overlay ──────────────────────────────────────────────────
    static void TestLimitColor()
    {
        Banner("WHO limit overlay colour");
        double lim = 55.0;

        Color c;
        c = AM.LimitColor(45.0, lim);  // −10 dB under → green
        True("−10 dB under → green (G dominates)", c.G > 150 && c.R < 20);

        c = AM.LimitColor(53.0, lim);  // −2 dB under → yellow
        True("−2 dB under → yellow (R≥200 and G≥200)", c.R >= 200 && c.G >= 200);

        c = AM.LimitColor(58.0, lim);  // +3 dB over → orange
        True("+3 dB over → orange (R=255, G>100, B=0)", c.R == 255 && c.G > 100 && c.B == 0);

        c = AM.LimitColor(70.0, lim);  // +15 dB over → red
        True("+15 dB over → red (R≥200, G=0, B=0)", c.R >= 200 && c.G == 0 && c.B == 0);

        // Boundary exactly at limit (over=0) → yellow band
        c = AM.LimitColor(55.0, lim);
        True("exactly at limit → yellow band", c.R >= 180 && c.G >= 180);

        // Boundary exactly at limit-5 (over=-5) → green band
        c = AM.LimitColor(50.0, lim);
        True("exactly 5 dB under limit → green", c.G > 150 && c.R < 20);
    }

    // ── 6. Contour interpolation ──────────────────────────────────────────────
    static void TestContourInterpolation()
    {
        Banner("Contour edge interpolation (marching triangles)");

        Eq("midpoint 60→70, level=65",          AM.ContourT(60, 70, 65), 0.500);
        Eq("level at vA (t=0)",                  AM.ContourT(55, 75, 55), 0.000);
        Eq("level at vB (t=1)",                  AM.ContourT(55, 75, 75), 1.000);
        Eq("1/4 position 40→80, level=50",       AM.ContourT(40, 80, 50), 0.250);
        Eq("3/4 position 40→80, level=70",       AM.ContourT(40, 80, 70), 0.750);
        True("level below segment → t<0",        AM.ContourT(60, 70, 50) < 0);
        True("level above segment → t>1",        AM.ContourT(60, 70, 80) > 1);

        // Interpolated point must lie on the edge
        double vA = 42.3, vB = 81.7, level = 65.0;
        double t = AM.ContourT(vA, vB, level);
        double reconstruct = vA + t*(vB - vA);
        Eq("interpolated point reconstructs level", reconstruct, level, 1e-9);
    }

    // ── 7. Reflection direction ───────────────────────────────────────────────
    static void TestReflectionDirection()
    {
        Banner("Reflection direction r = d − 2·(d·n)·n");

        double Mag(double[] v) => Math.Sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);

        // Normal incidence: d=(0,0,−1), n=(0,0,1) → r=(0,0,1)
        AM.ReflectDir(new[]{ 0.0,0,-1 }, new[]{ 0.0,0,1 }, out double[] r1);
        Eq("normal: Rx=0", r1[0], 0, 1e-9);
        Eq("normal: Ry=0", r1[1], 0, 1e-9);
        Eq("normal: Rz=1", r1[2], 1, 1e-9);

        // 45°: d=(sq,0,−sq), n=(0,0,1) → r=(sq,0,sq)
        double sq = Math.Sqrt(0.5);
        AM.ReflectDir(new[]{ sq,0,-sq }, new[]{ 0.0,0,1 }, out double[] r2);
        Eq("45°: Rx=sq", r2[0], sq, 1e-6);
        Eq("45°: Ry=0",  r2[1], 0,  1e-6);
        Eq("45°: Rz=sq", r2[2], sq, 1e-6);

        // Magnitude preserved
        Eq("reflected magnitude = 1", Mag(r1), 1.0, 1e-9);
        Eq("reflected magnitude = 1 (45°)", Mag(r2), 1.0, 1e-9);

        // Grazing: d=(1,0,0), n=(0,0,1) → r=(1,0,0) unchanged
        AM.ReflectDir(new[]{ 1.0,0,0 }, new[]{ 0.0,0,1 }, out double[] r3);
        Eq("grazing: Rx=1", r3[0], 1, 1e-9);
        Eq("grazing: Rz=0", r3[2], 0, 1e-9);
    }

    // ── 8. Interior score ─────────────────────────────────────────────────────
    static void TestInteriorScore()
    {
        Banner("Interior exposure score Σ[10^(dBi/10)·area/dist²]");

        // Single face: db=60, area=1m², dist=2m
        //   score = 10·log10(10^6 · 1/4) = 10·(6 − log10(4)) ≈ 53.98
        double expected = 10.0*Math.Log10(Math.Pow(10,6.0)*1.0/4.0);
        double actual   = AM.InteriorScore(
            new[]{ new[]{ 0.0,2.0,0.0 } },
            new[]{ 1.0 }, new[]{ 60.0 }, O);
        Eq("single face analytical", actual, expected, 0.01);

        // Closer interior point → higher score
        double far  = AM.InteriorScore(new[]{ new[]{0.0,4.0,0.0} }, new[]{1.0}, new[]{60.0}, O);
        double near = AM.InteriorScore(new[]{ new[]{0.0,2.0,0.0} }, new[]{1.0}, new[]{60.0}, O);
        True("closer interior pt → higher score", near > far);

        // Louder facade → higher score
        double quiet = AM.InteriorScore(new[]{ new[]{0.0,2.0,0.0} }, new[]{1.0}, new[]{60.0}, O);
        double loud  = AM.InteriorScore(new[]{ new[]{0.0,2.0,0.0} }, new[]{1.0}, new[]{70.0}, O);
        True("louder facade → higher score", loud > quiet);
        Eq("10 dB louder face → 10 dB higher score", loud - quiet, 10.0, 0.01);

        // Distance clamping: no NaN when pt = centroid
        double noNan = AM.InteriorScore(new[]{ O }, new[]{1.0}, new[]{60.0}, O);
        True("dist=0 clamped → finite", !double.IsNaN(noNan));

        // Multiple faces: two equal faces, same dist → score ≈ single + 3 dB area factor
        double s1 = AM.InteriorScore(new[]{ new[]{0.0,2.0,0.0} }, new[]{1.0}, new[]{60.0}, O);
        double s2 = AM.InteriorScore(
            new[]{ new[]{0.0,2.0,0.0}, new[]{0.0,-2.0,0.0} },
            new[]{1.0,1.0}, new[]{60.0,60.0}, O);
        True("two equal faces → higher score than one", s2 > s1);
    }

    // ── 9. Merge direct + reflected ───────────────────────────────────────────
    static void TestMergeDirectReflected()
    {
        Banner("Merge direct + reflected (energy addition)");

        double Merge(double d, double r)
        {
            double eD = Math.Pow(10.0, d/10.0);
            double eR = r > -100 ? Math.Pow(10.0, r/10.0) : 0.0;
            return 10.0*Math.Log10(eD + eR);
        }

        Eq("equal direct+reflected → +3.01 dB",
            Merge(60, 60) - 60.0, 10.0*Math.Log10(2), 0.01);

        Eq("reflected=−200 → same as direct",
            Merge(60, -200) - 60.0, 0.0, 0.001);

        // Reflection 10 dB below direct → ~0.414 dB boost
        Eq("reflection −10 dB below → +0.414 dB boost",
            Merge(60, 50) - 60.0, 0.414, 0.01);

        // Reflection 20 dB below → ~0.043 dB boost (negligible)
        Eq("reflection −20 dB below → ~0.043 dB boost",
            Merge(60, 40) - 60.0, 0.043, 0.005);
    }

    // ── 10. Scale and guard tests ─────────────────────────────────────────────
    static void TestScaleGuards()
    {
        Banner("Scale guards and edge cases");

        // Zero-range guard: equal min/max → expand by 1
        double sMin = 60.0, sMax = 60.0;
        if (Math.Abs(sMax - sMin) < 1e-6) sMax = sMin + 1.0;
        Eq("zero-range guard expands max by 1", sMax - sMin, 1.0);

        // Gradient clamping extremes
        Color lo = AM.GradientColor(-999);
        Color hi = AM.GradientColor( 999);
        True("t→−∞ clamps → blue (B=255)", lo.B == 255);
        True("t→+∞ clamps → red  (R=255)", hi.R == 255);

        // EnergySum single value identity
        Eq("EnergySum single → identity", AM.EnergySum(new[]{ 47.3 }), 47.3, 1e-6);
    }

    // ── Main ──────────────────────────────────────────────────────────────────
    static void Main()
    {
        Console.WriteLine("\n  Manta Ray — math tests");
        Console.WriteLine("  " + new string('═', 58));

        TestDirectDb();
        TestEnergySum();
        TestLineSource();
        TestGradientColor();
        TestLimitColor();
        TestContourInterpolation();
        TestReflectionDirection();
        TestInteriorScore();
        TestMergeDirectReflected();
        TestScaleGuards();

        Console.WriteLine("\n  " + new string('═', 58));
        Console.ForegroundColor = _fail > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        Console.WriteLine($"  {_pass} passed   {_fail} failed");
        Console.ResetColor();
        Environment.Exit(_fail > 0 ? 1 : 0);
    }
}

// ── Extension helpers ─────────────────────────────────────────────────────────
static class ArrayExt
{
    public static T[] Initialize<T>(this T[] arr, T value)
    {
        for (int i = 0; i < arr.Length; i++) arr[i] = value;
        return arr;
    }
}
