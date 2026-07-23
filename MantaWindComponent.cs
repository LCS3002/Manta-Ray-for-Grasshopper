using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaWindComponent : GH_Component
    {
        // ── Volatile animation state ──────────────────────────────────────────
        volatile bool      _alive;
        volatile bool      _animate = true;
        Thread             _thread;
        DateTime           _start = DateTime.Now;

        // Set once per SolveInstance — read-only by animation thread
        volatile Point3d[] _seeds;
        double    _windSpeed;
        int       _trailSteps;

        // ── Baked streamline output (for curve output) ────────────────────────
        volatile Polyline[] _streamlines;

        public MantaWindComponent()
            : base("Wind", "Wind",
                   "Animated wind streamlines — particles advect through a curl-noise field and deflect around the mesh.\n" +
                   "Set Rhino viewport to Perspective for best effect.\n" +
                   "Formula: v = V_wind + curl(N(x/scale + t·0.1, y/scale, z/scale)) × turbulence",
                   "Manta", "Environment")
        { }

        public override Guid ComponentGuid => new Guid("C3D4E5F6-A7B8-4012-9CDE-123456789012");
        protected override Bitmap Icon => MantaIcons.MantaWind24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter   ("Mesh",       "M",  "Analysis mesh (e.g. from Manta Mesh)",        GH_ParamAccess.item);
            p.AddVectorParameter ("Wind Dir",   "V",  "Wind direction vector (will be normalised)",   GH_ParamAccess.item, new Vector3d(1, 0, 0));
            p.AddNumberParameter ("Speed",      "Sp", "Wind speed — controls animation rate",         GH_ParamAccess.item, 5.0);
            p.AddNumberParameter ("Turbulence", "Tu", "Curl-noise turbulence intensity (0 = laminar)", GH_ParamAccess.item, 1.5);
            p.AddNumberParameter ("Scale",      "Sc", "Noise scale relative to geometry",             GH_ParamAccess.item, 10.0);
            p.AddIntegerParameter("Particles",  "N",  "Number of streamline particles",               GH_ParamAccess.item, 80);
            p.AddIntegerParameter("Trail",      "Tr", "Trail length (steps)",                         GH_ParamAccess.item, 20);
            p.AddIntegerParameter("Seed",       "S",  "Random seed for particle placement",           GH_ParamAccess.item, 0);
            p.AddBooleanParameter("On",         "On", "Animate live in the viewport (off = compute outputs only, no 60 fps redraw loop)", GH_ParamAccess.item, true);
            for (int i = 0; i < 9; i++) p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter ("Streamlines", "SL", "Baked streamline polylines — connect to see static field", GH_ParamAccess.list);
            p.AddVectorParameter("Field Pts",   "FP", "Sampled wind vectors at mesh face centres",               GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh    mesh   = null;
            var     dir    = new Vector3d(1, 0, 0);
            double  speed  = 5, turb = 1.5, scale = 10;
            int     nPart  = 80, trail = 20, seed = 0;
            bool    on     = true;

            if (!DA.GetData(0, ref mesh) || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh"); return; }

            DA.GetData(1, ref dir);   DA.GetData(2, ref speed);
            DA.GetData(3, ref turb);  DA.GetData(4, ref scale);
            DA.GetData(5, ref nPart); DA.GetData(6, ref trail);
            DA.GetData(7, ref seed);   DA.GetData(8, ref on);
            _animate = on;

            nPart = Math.Max(1, Math.Min(500, nPart));
            trail = Math.Max(2, Math.Min(100, trail));
            speed = Math.Max(0.1, speed);
            scale = Math.Max(0.1, scale);

            dir.Unitize();

            // Store for animation thread
            _windSpeed  = speed;
            _trailSteps = trail;

            BoundingBox box       = mesh.GetBoundingBox(true);
            double      diag      = box.Diagonal.Length;
            if (diag < 1e-9) diag = 1.0;
            double      influence = diag * 0.12;               // deflection reach around the mesh
            _seeds                = MantaMath.SeedParticles(box, dir, nPart, seed);

            // Normals are needed for mesh-aware deflection
            if (mesh.Normals.Count != mesh.Vertices.Count) mesh.Normals.ComputeNormals();
            bool meshClosed = mesh.IsClosed;

            // Bake deflected streamlines — particles seed upstream, flow through the
            // scene and deflect around the geometry. Fixed step count so every path is
            // full length (Speed only drives playback, not the shape).
            const int steps = 220;
            double    dt    = diag / 200.0;
            var       sls   = new Polyline[nPart];
            for (int i = 0; i < nPart; i++)
            {
                var poly = new Polyline();
                var pt   = _seeds[i];
                for (int s2 = 0; s2 < steps; s2++)
                {
                    poly.Add(pt);
                    pt = MantaMath.Advect(mesh, meshClosed, pt, dir, turb, scale, 0, dt, influence);
                }
                sls[i] = poly;
            }
            _streamlines = sls;

            // Wind vectors at face centres
            var fieldVecs = new List<Vector3d>();
            if (mesh.FaceNormals.Count != mesh.Faces.Count)
                mesh.FaceNormals.ComputeFaceNormals();
            foreach (var f in mesh.Faces)
            {
                var c = (Point3d)(((Vector3d)(Point3d)mesh.Vertices[f.A]
                                 + (Vector3d)(Point3d)mesh.Vertices[f.B]
                                 + (Vector3d)(Point3d)mesh.Vertices[f.C]) * (1.0/3.0));
                fieldVecs.Add(MantaMath.WindVelocity(c, dir, turb, scale, 0));
            }

            DA.SetDataList(0, new List<Curve>(Array.ConvertAll(sls, sl => (Curve)sl.ToNurbsCurve())));
            DA.SetDataList(1, fieldVecs);

            if (_animate) StartThread();
            else Rhino.RhinoDoc.ActiveDoc?.Views.Redraw(); // clear last frame when switched off
        }

        // ── Animation thread ──────────────────────────────────────────────────
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
                    Thread.Sleep(16); // ~60 fps
                }
            }) { IsBackground = true, Name = "MantaWind" };
            _thread.Start();
        }

        public override void RemovedFromDocument(GH_Document doc)
        {
            _alive = false;
            base.RemovedFromDocument(doc);
        }

        // ── Viewport drawing ──────────────────────────────────────────────────
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!_animate) return;
            var    streams = _streamlines;
            if (streams == null) return;

            double t     = (DateTime.Now - _start).TotalSeconds;
            double speed = _windSpeed;
            int    trail = _trailSteps;

            for (int i = 0; i < streams.Length; i++)
            {
                var sl = streams[i];
                if (sl == null || sl.Count < 2) continue;
                int n = sl.Count;

                // Faint full streamline — shows the flow wrapping the geometry
                args.Display.DrawPolyline(sl, Color.FromArgb(26, 80, 200, 210), 1);

                // A soft bright streak glides along the streamline (no head dot,
                // fades at both ends so it reads as flowing air, not a comet).
                double phase = (i * 0.618033988) % 1.0;            // golden-ratio spacing
                double u     = (t * speed * 0.03 + phase) % 1.0;    // 0..1 along the path
                int    hi    = (int)(u * (n - 1));

                for (int s = 0; s < trail; s++)
                {
                    int a = hi - s, b = a - 1;
                    if (b < 0) break;
                    double f      = (double)s / trail;              // 0 at lead → 1 at tail
                    double bright = Math.Sin((1.0 - f) * Math.PI);  // peaks mid-streak, ~0 at both ends
                    int    alpha  = (int)(235 * bright);
                    if (alpha < 6) continue;
                    var    col    = Color.FromArgb(alpha, 130, (int)(205 + 45 * bright), 255);
                    args.Display.DrawLine(sl[a], sl[b], col, bright > 0.55 ? 3 : 2);
                }
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args) { }
    }
}
