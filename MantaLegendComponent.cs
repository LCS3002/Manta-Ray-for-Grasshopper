using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Manta
{
    public class MantaLegendComponent : GH_Component
    {
        private Mesh          _barMesh;
        private List<Point3d> _tickPts    = new List<Point3d>();
        private List<string>  _tickLabels = new List<string>();
        private double        _textSize   = 0.5;

        public MantaLegendComponent()
            : base("Legend", "Legend",
                   "Draw a dB colour-scale legend in the Rhino viewport.\n" +
                   "Position with Origin; connect Min/Max from Manta Noise.",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("66778899-AABB-4233-DDEE-FF0011223345");
        protected override Bitmap Icon => MantaIcons.Legend24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter ("Origin", "O",   "Base-left corner of the legend in world space", GH_ParamAccess.item, new Point3d(0,0,0));
            p.AddNumberParameter("Min dB", "Min", "Scale minimum from Manta Noise",                   GH_ParamAccess.item, 40.0);
            p.AddNumberParameter("Max dB", "Max", "Scale maximum from Manta Noise",                   GH_ParamAccess.item, 80.0);
            p.AddNumberParameter("Height", "H",   "Bar height in model units (default 5)",         GH_ParamAccess.item, 5.0);
            p.AddNumberParameter("Width",  "W",   "Bar width in model units (default 1)",          GH_ParamAccess.item, 1.0);
            p.AddIntegerParameter("Ticks", "T",   "Number of tick labels (default 5)",             GH_ParamAccess.item, 5);
            p.AddNumberParameter("Limit dB","Lim","Draw limit line on legend (optional)",          GH_ParamAccess.item);
            for (int i = 0; i <= 6; i++) p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Legend Mesh","LM","Gradient bar mesh — bake to make permanent", GH_ParamAccess.item);
            p.AddTextParameter ("Labels",   "Lb","Tick label strings",                          GH_ParamAccess.list);
            p.AddPointParameter("Label Pts","LP","World positions of tick labels",               GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _barMesh    = null;
            _tickPts    = new List<Point3d>();
            _tickLabels = new List<string>();

            Point3d origin = new Point3d(0,0,0);
            double  minDb = 40, maxDb = 80, height = 5, width = 1, limitDb = double.NaN;
            int     ticks = 5;

            DA.GetData(0, ref origin);
            DA.GetData(1, ref minDb);
            DA.GetData(2, ref maxDb);
            DA.GetData(3, ref height);
            DA.GetData(4, ref width);
            DA.GetData(5, ref ticks);
            DA.GetData(6, ref limitDb);

            ticks  = Math.Max(2, Math.Min(20, ticks));
            height = Math.Max(0.01, height);
            width  = Math.Max(0.001, width);
            if (maxDb <= minDb) maxDb = minDb + 1;

            // Build gradient strip mesh
            int   strips = 64;
            var   m      = new Mesh();
            float bW = (float)width, bH = (float)height;
            float ox = (float)origin.X, oy = (float)origin.Y, oz = (float)origin.Z;

            for (int i = 0; i <= strips; i++)
            {
                float t = (float)i / strips;
                float y = oy + t * bH;
                m.Vertices.Add(new Point3f(ox,      y, oz));
                m.Vertices.Add(new Point3f(ox + bW, y, oz));
            }
            var colors = new Color[(strips + 1) * 2];
            for (int i = 0; i <= strips; i++)
            {
                double db = minDb + (double)i / strips * (maxDb - minDb);
                Color  c  = Acoustics.DbToColor(db, minDb, maxDb);
                colors[i * 2] = colors[i * 2 + 1] = c;
            }
            m.VertexColors.SetColors(colors);
            for (int i = 0; i < strips; i++)
            {
                int b = i * 2;
                m.Faces.AddFace(b, b+1, b+3, b+2);
            }
            m.Normals.ComputeNormals();
            _barMesh = m;

            // Tick labels
            _textSize = height / ticks * 0.60;
            for (int i = 0; i < ticks; i++)
            {
                double t  = (double)i / (ticks - 1);
                double db = minDb + t * (maxDb - minDb);
                float  y  = oy + (float)(t * height);
                _tickPts.Add(new Point3d(ox + bW * 1.15, y, oz));
                _tickLabels.Add($"{db:F0} dB");
            }

            if (!double.IsNaN(limitDb) && limitDb >= minDb && limitDb <= maxDb)
            {
                double t = (limitDb - minDb) / (maxDb - minDb);
                float  y = oy + (float)(t * height);
                _tickPts.Add(new Point3d(ox - bW * 0.6, y, oz));
                _tickLabels.Add($"▶ {limitDb:F0} dB limit");
            }

            DA.SetData    (0, _barMesh);
            DA.SetDataList(1, _tickLabels);
            DA.SetDataList(2, _tickPts);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            var bm = _barMesh;
            if (bm != null && bm.VertexColors.Count > 0)
                args.Display.DrawMeshFalseColors(bm);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            var pts    = _tickPts;
            var labels = _tickLabels;
            if (pts == null || labels == null) return;
            double sz = _textSize > 0 ? _textSize : 0.5;
            for (int i = 0; i < Math.Min(pts.Count, labels.Count); i++)
            {
                var t3d = new Text3d(labels[i], new Plane(pts[i], Vector3d.ZAxis), sz);
                args.Display.Draw3dText(t3d, Color.White);
                t3d.Dispose();
            }
        }
    }
}
