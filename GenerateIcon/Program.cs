using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Manta Ray — icon generator.
// Assembly icons (manta ray, teal): Manta_24.png, Manta_48.png
// Acoustic icons (amber on navy): MantaSource, MantaMesh, MantaNoise, MantaInterior, MantaContours, MantaLegend
// Environment icons (teal on navy): MantaWind, MantaSun, MantaPressure
class Program
{
    // ── Palettes ──────────────────────────────────────────────────────────────
    static readonly Color Navy     = Color.FromArgb(  8,  12,  28);
    static readonly Color Amber    = Color.FromArgb(245, 166,  35);
    static readonly Color Teal     = Color.FromArgb(  0, 210, 180);
    static readonly Color Cyan     = Color.FromArgb( 60, 220, 255);
    static readonly Color Wht      = Color.White;

    // Acoustic gradient (blue → cyan → yellow → orange → red)
    static readonly (double t, Color c)[] Stops = {
        (0.00, Color.FromArgb(  0,   0, 255)),
        (0.25, Color.FromArgb(  0, 220, 255)),
        (0.50, Color.FromArgb(255, 240,   0)),
        (0.75, Color.FromArgb(255, 110,   0)),
        (1.00, Color.FromArgb(255,   0,   0)),
    };

    static Color AcousticGrad(double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        for (int i = 0; i < Stops.Length - 1; i++)
        {
            if (t <= Stops[i + 1].t)
            {
                double s = (t - Stops[i].t) / (Stops[i + 1].t - Stops[i].t);
                var c0 = Stops[i].c; var c1 = Stops[i + 1].c;
                return Color.FromArgb(
                    Cl(c0.R + (int)(s * (c1.R - c0.R))),
                    Cl(c0.G + (int)(s * (c1.G - c0.G))),
                    Cl(c0.B + (int)(s * (c1.B - c0.B))));
            }
        }
        return Stops[Stops.Length - 1].c;
    }

    static int Cl(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    // ── Canvas helpers ────────────────────────────────────────────────────────
    static Bitmap Canvas(int sz, Action<Graphics, float> draw)
    {
        var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Navy);
            draw(g, sz);
        }
        return bmp;
    }

    static PointF[] Sc(float sc, PointF[] pts)
    {
        var r = new PointF[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            r[i] = new PointF(pts[i].X * sc, pts[i].Y * sc);
        return r;
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X,          r.Y,          d, d, 180, 90);
        p.AddArc(r.Right - d,  r.Y,          d, d, 270, 90);
        p.AddArc(r.Right - d,  r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.X,          r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }

    // ── Assembly icon: manta ray silhouette (teal) ────────────────────────────
    static Bitmap DrawManta(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;
            using (var b = new SolidBrush(Teal))
            {
                // Main wing shape — viewed from above
                g.FillPolygon(b, Sc(sc, new PointF[]
                {
                    new PointF(12,  3),   // front tip
                    new PointF(22, 10),   // right wingtip
                    new PointF(19, 14),   // right trailing edge
                    new PointF(14, 19),   // right tail base
                    new PointF(12, 22),   // tail tip
                    new PointF(10, 19),   // left tail base
                    new PointF( 5, 14),   // left trailing edge
                    new PointF( 2, 10),   // left wingtip
                }));
                // Left cephalic fin
                g.FillPolygon(b, Sc(sc, new PointF[]
                    { new PointF(10, 3), new PointF(9, 7), new PointF(11, 8) }));
                // Right cephalic fin
                g.FillPolygon(b, Sc(sc, new PointF[]
                    { new PointF(14, 3), new PointF(15, 7), new PointF(13, 8) }));
            }
            // Body oval — subtle darker overlay
            using (var b = new SolidBrush(Color.FromArgb(80, 0, 80, 70)))
                g.FillEllipse(b, 9f*sc, 8f*sc, 6f*sc, 8f*sc);
            // Bright eye dot
            float er = sc * 0.7f;
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                g.FillEllipse(b, 12f*sc - er, 10f*sc - er, er*2, er*2);
        });
    }

    // ── Wind: curl-noise streamlines + arrow ──────────────────────────────────
    static Bitmap DrawWind(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;

            // Three wavy streamlines
            float[] baseY  = { 7f, 12f, 17f };
            float[] phases = { 0f, 0.4f, 0.8f };
            using (var pen = new Pen(Teal, sc * 1.1f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int li = 0; li < 3; li++)
                {
                    var pts = new List<PointF>();
                    for (int i = 0; i <= 18; i++)
                    {
                        float t  = (float)i / 18f;
                        float x  = (2f + t * 15f) * sc;
                        float y  = baseY[li] * sc
                                 + (float)Math.Sin((t + phases[li]) * Math.PI * 2.2f) * sc * 0.9f;
                        pts.Add(new PointF(x, y));
                    }
                    g.DrawLines(pen, pts.ToArray());
                }
            }

            // Arrow head pointing right
            float aY = 12f * sc;
            using (var b = new SolidBrush(Cyan))
                g.FillPolygon(b, new PointF[]
                {
                    new PointF(22f * sc, aY),
                    new PointF(16f * sc, aY - 2.8f * sc),
                    new PointF(16f * sc, aY + 2.8f * sc),
                });
        });
    }

    // ── Sun: sun disc + radiating rays ────────────────────────────────────────
    static Bitmap DrawSun(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc    = s / 24f;
            float cx    = 12f * sc, cy = 12f * sc;
            float coreR = 3.4f * sc;
            float rayR  = 7.5f * sc;

            // Glow halo
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - coreR*2.2f, cy - coreR*2.2f, coreR*4.4f, coreR*4.4f);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor    = Color.FromArgb(70, Teal);
                    pgb.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(pgb, path);
                }
            }

            // 8 rays
            using (var pen = new Pen(Teal, sc * 1.1f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    g.DrawLine(pen,
                        cx + (float)Math.Cos(angle) * (coreR + sc * 0.8f),
                        cy + (float)Math.Sin(angle) * (coreR + sc * 0.8f),
                        cx + (float)Math.Cos(angle) * rayR,
                        cy + (float)Math.Sin(angle) * rayR);
                }
            }

            // Sun disc
            using (var b = new SolidBrush(Cyan))
                g.FillEllipse(b, cx - coreR, cy - coreR, coreR*2, coreR*2);
            // Bright core
            float cr2 = coreR * 0.45f;
            using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                g.FillEllipse(b, cx - cr2, cy - cr2, cr2*2, cr2*2);
        });
    }

    // ── Pressure: source dot + expanding arcs ─────────────────────────────────
    static Bitmap DrawPressure(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;
            float cx = 4.5f * sc, cy = 12f * sc;

            // Source glow
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - sc*3f, cy - sc*3f, sc*6f, sc*6f);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor    = Color.FromArgb(90, Cyan);
                    pgb.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(pgb, path);
                }
            }
            using (var b = new SolidBrush(Teal))
                g.FillEllipse(b, cx - sc, cy - sc, sc*2, sc*2);
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                g.FillEllipse(b, cx - sc*0.45f, cy - sc*0.45f, sc*0.9f, sc*0.9f);

            // Expanding arcs
            for (int i = 1; i <= 4; i++)
            {
                float r     = sc * (1.8f + i * 3.0f);
                int   alpha = 210 - i * 40;
                float thick = sc * (1.1f - i * 0.10f);
                if (cx + r > s * 1.1f) continue;
                using (var pen = new Pen(Color.FromArgb(alpha, Teal), Math.Max(thick, 0.5f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap   = LineCap.Round;
                    g.DrawArc(pen, cx - r, cy - r, r*2, r*2, -65, 130);
                }
            }
        });
    }

    // ── Source: speaker cone + sound waves ────────────────────────────────────
    static Bitmap DrawSource(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;
            using (var b = new SolidBrush(Amber))
            {
                g.FillRectangle(b, 3*sc, 9*sc, 4*sc, 6*sc);
                g.FillPolygon(b, Sc(sc, new PointF[]
                    { new PointF(7, 7), new PointF(12, 4), new PointF(12, 20), new PointF(7, 17) }));
            }
            for (int i = 1; i <= 3; i++)
            {
                int   alpha = 255 - i * 55;
                float r     = sc * (1.8f + i * 2.4f);
                using (var pen = new Pen(Color.FromArgb(alpha, Amber), sc * 1.1f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, 12*sc - r, 12*sc - r, r*2, r*2, -55, 110);
                }
            }
        });
    }

    // ── Mesh: vertex-grid icon ────────────────────────────────────────────────
    static Bitmap DrawMesh(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc  = s / 24f;
            float pad = 2.5f * sc;
            float w   = s - pad * 2;
            int   n   = 4;
            using (var pen = new Pen(Color.FromArgb(120, Amber), sc * 0.7f))
            {
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n;
                    g.DrawLine(pen, pad + t*w, pad,   pad + t*w, pad + w);
                    g.DrawLine(pen, pad,   pad + t*w, pad + w,   pad + t*w);
                }
            }
            using (var b = new SolidBrush(Amber))
            for (int row = 0; row <= n; row++)
            for (int col = 0; col <= n; col++)
            {
                float x  = pad + (float)col / n * w;
                float y  = pad + (float)row / n * w;
                float dr = sc * 0.85f;
                g.FillEllipse(b, x - dr, y - dr, dr*2, dr*2);
            }
        });
    }

    // ── Noise: gradient heat-map bars ─────────────────────────────────────────
    static Bitmap DrawNoise(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc   = s / 24f;
            int   bars = 6;
            float pad  = 2 * sc;
            float bw   = (s - pad * 2 - (bars - 1) * sc * 0.4f) / bars;
            float bh   = s - pad * 2;
            float y0   = pad;
            for (int i = 0; i < bars; i++)
            {
                double t  = (double)i / (bars - 1);
                float  x0 = pad + i * (bw + sc * 0.4f);
                using (var b = new SolidBrush(AcousticGrad(t)))
                    g.FillRectangle(b, x0, y0, bw, bh);
                using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                    g.FillRectangle(b, x0, y0, bw, bh * 0.25f);
            }
        });
    }

    // ── Interior: room + rays + interior point ────────────────────────────────
    static Bitmap DrawInterior(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc  = s / 24f;
            float pad = 3 * sc;
            float w   = s - pad * 2;
            using (var pen = new Pen(Amber, sc * 0.9f))
                g.DrawRectangle(pen, pad, pad, w, w);
            float cx = s * 0.5f, cy = s * 0.52f;
            using (var pen = new Pen(Color.FromArgb(140, Amber), sc * 0.6f))
            {
                g.DrawLine(pen, cx, cy, cx,     pad);
                g.DrawLine(pen, cx, cy, cx,     pad + w);
                g.DrawLine(pen, cx, cy, pad,     cy);
                g.DrawLine(pen, cx, cy, pad + w, cy);
                g.DrawLine(pen, cx, cy, pad,     pad);
                g.DrawLine(pen, cx, cy, pad + w, pad);
                g.DrawLine(pen, cx, cy, pad,     pad + w);
                g.DrawLine(pen, cx, cy, pad + w, pad + w);
            }
            float dr = sc * 1.3f;
            using (var b = new SolidBrush(Wht))
                g.FillEllipse(b, cx - dr, cy - dr, dr*2, dr*2);
            using (var b = new SolidBrush(Color.FromArgb(80, Wht)))
                g.FillEllipse(b, cx - dr*2, cy - dr*2, dr*4, dr*4);
        });
    }

    // ── Contours: concentric isodecibel lines ─────────────────────────────────
    static Bitmap DrawContours(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;
            float cx = s * 0.5f, cy = s * 0.55f;
            for (int i = 4; i >= 1; i--)
            {
                double t  = (double)(i - 1) / 3.0;
                float  rx = i * 2.6f * sc;
                float  ry = i * 1.9f * sc;
                using (var pen = new Pen(AcousticGrad(t), sc * 1.0f))
                    g.DrawEllipse(pen, cx - rx, cy - ry, rx*2, ry*2);
            }
            float dr = sc * 1.2f;
            using (var b = new SolidBrush(Amber))
                g.FillEllipse(b, cx - dr, cy - dr, dr*2, dr*2);
        });
    }

    // ── Legend: vertical gradient bar + ticks ─────────────────────────────────
    static Bitmap DrawLegend(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            float sc = s / 24f;
            float bx = 4*sc, by = 2*sc, bw = 5*sc, bh = s - 4*sc;
            int strips = sz * 2;
            for (int i = 0; i < strips; i++)
            {
                double t = 1.0 - (double)i / strips;
                float  y = by + (float)i / strips * bh;
                using (var b = new SolidBrush(AcousticGrad(t)))
                    g.FillRectangle(b, bx, y, bw, bh / strips + 1.5f);
            }
            using (var pen = new Pen(Color.FromArgb(80, Wht), sc * 0.5f))
                g.DrawRectangle(pen, bx, by, bw, bh);
            using (var pen = new Pen(Wht, sc * 0.7f))
            for (int i = 0; i < 5; i++)
            {
                float t = (float)i / 4;
                float y = by + t * bh;
                g.DrawLine(pen, bx + bw, y, bx + bw + 3*sc, y);
            }
        });
    }

    // ── Logo (512 px) — bold flat teal silhouette on navy (Grasshopper aesthetic)
    static Bitmap DrawLogo(int sz)
    {
        return Canvas(sz, (g, s) =>
        {
            g.Clear(Color.FromArgb(8, 12, 28));

            float mx = s * 0.500f;
            float my = s * 0.405f;   // body centre — ray sits in upper 60% of canvas

            // ── Teal manta silhouette — single continuous bezier path ─────────
            // Traced clockwise: left fin tip → left wing → tail → right wing → right fin
            // tip → V-notch → back to left fin tip.  Cephalic fins are part of the
            // outline (V-notch carved between them), not separate strokes.
            using (var body = new GraphicsPath())
            {
                float lfx = mx - s*0.108f, lfy = my - s*0.375f;  // left fin tip
                float rfx = mx + s*0.108f, rfy = my - s*0.375f;  // right fin tip
                float vnx = mx,            vny = my - s*0.278f;   // V-notch between fins
                float lwx = mx - s*0.476f, lwy = my - s*0.012f;  // left wingtip
                float rwx = mx + s*0.476f, rwy = my - s*0.012f;  // right wingtip
                float ttx = mx,            tty = my + s*0.400f;   // tail tip

                // Left fin tip → left wingtip  (outer leading edge)
                body.AddBezier(
                    lfx, lfy,
                    mx - s*0.195f, my - s*0.345f,
                    mx - s*0.342f, my - s*0.238f,
                    lwx, lwy);

                // Left wingtip → left tail base  (trailing sweep)
                body.AddBezier(
                    lwx, lwy,
                    mx - s*0.408f, my + s*0.118f,
                    mx - s*0.255f, my + s*0.252f,
                    mx - s*0.095f, my + s*0.322f);

                // Left tail base → tail tip
                body.AddBezier(
                    mx - s*0.095f, my + s*0.322f,
                    mx - s*0.035f, my + s*0.378f,
                    mx - s*0.008f, my + s*0.395f,
                    ttx, tty);

                // Tail tip → right tail base
                body.AddBezier(
                    ttx, tty,
                    mx + s*0.008f, my + s*0.395f,
                    mx + s*0.035f, my + s*0.378f,
                    mx + s*0.095f, my + s*0.322f);

                // Right tail base → right wingtip  (trailing sweep)
                body.AddBezier(
                    mx + s*0.095f, my + s*0.322f,
                    mx + s*0.255f, my + s*0.252f,
                    mx + s*0.408f, my + s*0.118f,
                    rwx, rwy);

                // Right wingtip → right fin tip  (outer leading edge)
                body.AddBezier(
                    rwx, rwy,
                    mx + s*0.342f, my - s*0.238f,
                    mx + s*0.195f, my - s*0.345f,
                    rfx, rfy);

                // Right fin tip → V-notch  (inner edge of right cephalic fin)
                body.AddBezier(
                    rfx, rfy,
                    mx + s*0.086f, my - s*0.362f,
                    mx + s*0.040f, my - s*0.306f,
                    vnx, vny);

                // V-notch → left fin tip  (inner edge of left cephalic fin)
                body.AddBezier(
                    vnx, vny,
                    mx - s*0.040f, my - s*0.306f,
                    mx - s*0.086f, my - s*0.362f,
                    lfx, lfy);

                body.CloseFigure();

                using (var b = new SolidBrush(Teal))
                    g.FillPath(b, body);
            }

            // ── Body centre vignette — dim ellipse to suggest depth ───────────
            using (var bpath = new GraphicsPath())
            {
                float bw = s*0.064f, bh = s*0.348f;
                bpath.AddEllipse(mx - bw*0.5f, my - bh*0.54f, bw, bh);
                using (var pgb = new PathGradientBrush(bpath))
                {
                    pgb.CenterColor    = Color.FromArgb(80, 0, 0, 0);
                    pgb.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(pgb, bpath);
                }
            }

            // Eye — small navy dot
            float er = s * 0.011f;
            using (var b = new SolidBrush(Color.FromArgb(170, 8, 12, 28)))
                g.FillEllipse(b, mx + s*0.020f - er, my - s*0.218f - er, er*2, er*2);

            // ── "MANTA" — bold white, centred ────────────────────────────────
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int   titlePx = (int)(s * 0.116f);
            float titleY  = s * 0.845f;
            using (var font = new Font("Segoe UI", titlePx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF tsz = g.MeasureString("MANTA", font);
                using (var b = new SolidBrush(Wht))
                    g.DrawString("MANTA", font, b, (s - tsz.Width) / 2f, titleY);
            }

        });
    }

    // ── Save helper ───────────────────────────────────────────────────────────
    static void Save(Bitmap bmp, string dir, string name)
    {
        using (bmp)
        {
            string path = Path.Combine(dir, name);
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine($"  wrote {name}");
        }
    }

    static void Main()
    {
        string outDir = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));

        Console.WriteLine($"\n  Manta Ray — icon generator");
        Console.WriteLine($"  output → {outDir}");
        Console.WriteLine();

        // Assembly icons — manta ray, teal
        Save(DrawManta(24),    outDir, "Manta_24.png");
        Save(DrawManta(48),    outDir, "Manta_48.png");

        // Acoustic component icons — amber
        Save(DrawSource(24),   outDir, "MantaSource_24.png");
        Save(DrawMesh(24),     outDir, "MantaMesh_24.png");
        Save(DrawNoise(24),    outDir, "MantaNoise_24.png");
        Save(DrawInterior(24), outDir, "MantaInterior_24.png");
        Save(DrawContours(24), outDir, "MantaContours_24.png");
        Save(DrawLegend(24),   outDir, "MantaLegend_24.png");

        // Environment component icons — teal
        Save(DrawWind(24),     outDir, "MantaWind_24.png");
        Save(DrawSun(24),      outDir, "MantaSun_24.png");
        Save(DrawPressure(24), outDir, "MantaPressure_24.png");

        Console.WriteLine("\n  Done.");
    }
}
