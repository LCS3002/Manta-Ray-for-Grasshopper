using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaPressureComponent : GH_Component
    {
        volatile bool   _alive;
        volatile bool   _animate = true;
        Thread          _thread;
        DateTime        _start = DateTime.Now;

        volatile Point3d[] _sources;
        volatile double[]  _levels;
        double    _maxRadius;
        double    _ringRate;
        int       _rings;
        BoundingBox _bbox;

        public MantaPressureComponent()
            : base("Pressure", "Pressure",
                   "Animated acoustic pressure wavefronts radiating from noise sources.\n" +
                   "Visualises how sound energy propagates outward in time.\n" +
                   "Pairs with Manta Noise — use the same sources for integrated analysis.",
                   "Manta", "Environment")
        { }

        public override Guid ComponentGuid => new Guid("E5F6A7B8-C9D0-4234-BCEF-345678901234");
        protected override Bitmap Icon => MantaIcons.MantaPre24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter  ("Sources",    "S",  "Noise source points (from Manta Source)",       GH_ParamAccess.list);
            p.AddNumberParameter ("Levels",     "dB", "dB levels per source (from Manta Source)",       GH_ParamAccess.list);
            p.AddNumberParameter ("Wave Speed", "c",  "Speed of sound in m/s (default 343)",         GH_ParamAccess.item, 343.0);
            p.AddNumberParameter ("Scale",      "Sc", "Ring reach as a fraction of the source spread (auto-sizes to your model)", GH_ParamAccess.item, 0.75);
            p.AddIntegerParameter("Rings",      "R",  "Wavefront rings per source",                  GH_ParamAccess.item, 5);
            p.AddBooleanParameter("On",         "On", "Animate live in the viewport (off = compute outputs only, no 60 fps redraw loop)", GH_ParamAccess.item, true);
            for (int i = 0; i < 6; i++) p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Peak dB",    "Pk", "Peak pressure level at each source (dB)",        GH_ParamAccess.list);
            p.AddPointParameter ("Source Pts", "SP", "Source locations (pass-through)",                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var   srcList  = new List<Point3d>();
            var   lvlList  = new List<double>();
            double speed   = 343, sc = 0.75;
            int    rings   = 5;
            bool   on      = true;

            if (!DA.GetDataList(0, srcList) || srcList.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No sources — connect Manta Source"); return; }

            DA.GetDataList(1, lvlList);
            DA.GetData(2, ref speed); DA.GetData(3, ref sc); DA.GetData(4, ref rings);
            DA.GetData(5, ref on);
            _animate = on;

            while (lvlList.Count < srcList.Count) lvlList.Add(lvlList.Count > 0 ? lvlList[lvlList.Count-1] : 80);

            _sources   = srcList.ToArray();
            _levels    = lvlList.ToArray();
            _rings     = Math.Max(1, Math.Min(20, rings));

            // Size the wavefronts relative to the source cloud so they stay visible
            // at any model scale/units. Sc = ring reach as a fraction of the spread.
            var bb = new BoundingBox(_sources);
            double span = bb.Diagonal.Length;
            bb.Inflate(bb.Diagonal.Length * 0.5 + 1);
            _bbox = bb;
            if (span < 1e-6) span = bb.Diagonal.Length;       // single-source fallback
            _maxRadius = span * Math.Max(0.01, sc);
            _ringRate  = 0.7 * Math.Max(0.1, speed) / 343.0;  // cycles/sec, gently scaled by c

            DA.SetDataList(0, lvlList);
            DA.SetDataList(1, srcList);

            if (_animate) StartThread();
            else Rhino.RhinoDoc.ActiveDoc?.Views.Redraw(); // clear last frame when switched off
        }

        void StartThread()
        {
            if (_alive) return;
            _alive = true;
            _start = DateTime.Now;
            _thread = new Thread(() =>
            {
                while (_alive)
                {
                    if (_animate) Rhino.RhinoDoc.ActiveDoc?.Views.Redraw();
                    Thread.Sleep(16);
                }
            }) { IsBackground = true, Name = "MantaPressure" };
            _thread.Start();
        }

        public override void RemovedFromDocument(GH_Document doc)
        {
            _alive = false;
            base.RemovedFromDocument(doc);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!_animate) return;
            var srcs   = _sources;
            var lvls   = _levels;
            if (srcs == null || srcs.Length == 0) return;

            double t      = (DateTime.Now - _start).TotalSeconds;
            double maxR   = _maxRadius;
            double rate   = _ringRate;
            int    nRings = _rings;

            for (int si = 0; si < srcs.Length; si++)
            {
                var   src   = srcs[si];
                double level = si < lvls.Length ? lvls[si] : 80;

                // Normalise level to visual intensity, with a floor so quiet
                // sources stay visible.
                double intensity = Math.Max(0.3, Math.Min(1, (level - 40) / 50.0));

                for (int ri = 0; ri < nRings; ri++)
                {
                    // Each ring is offset in time so they appear to march outward
                    double ringT   = (t * rate + (double)ri / nRings) % 1.0;
                    double radius  = ringT * maxR;

                    // Fade out as ring expands and as it ages
                    double fade = (1.0 - ringT) * intensity;
                    int    alpha = (int)(255 * fade);
                    if (alpha < 5) continue;

                    // Colour: high level = warm (amber-red), low = cool (blue-cyan)
                    int r = (int)(255 * intensity + 60  * (1 - intensity));
                    int g = (int)(140 * intensity + 180 * (1 - intensity));
                    int b = (int)( 20 * intensity + 255 * (1 - intensity));
                    var col = Color.FromArgb(alpha, r, g, b);

                    // Draw 3 great-circle rings at different orientations for spherical feel
                    var planeXY = new Plane(src, Vector3d.ZAxis);
                    var planeXZ = new Plane(src, Vector3d.YAxis);
                    var planeYZ = new Plane(src, Vector3d.XAxis);
                    int thick   = ri == 0 ? 2 : 1;

                    args.Display.DrawCircle(new Circle(planeXY, radius), col, thick);
                    args.Display.DrawCircle(new Circle(planeXZ, radius), Color.FromArgb(alpha/2, r, g, b), 1);
                    args.Display.DrawCircle(new Circle(planeYZ, radius), Color.FromArgb(alpha/2, r, g, b), 1);
                }

                // Source pulse: bright core that beats with the rings
                double pulse   = 0.5 + 0.5 * Math.Sin(t * Math.PI * 2.0);
                int    pAlpha  = (int)(180 + 75 * pulse);
                double coreR   = maxR * 0.03 * (1 + 0.3 * pulse);
                var    corePl  = new Plane(src, Vector3d.ZAxis);
                args.Display.DrawCircle(new Circle(corePl, coreR),
                    Color.FromArgb(pAlpha, 255, 220, 80), 3);
                args.Display.DrawPoint(src, Rhino.Display.PointStyle.Circle, 5,
                    Color.FromArgb(255, 255, 240, 100));
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args) { }
    }
}
