using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaNoiseComponent : GH_Component
    {
        private volatile Mesh _display;

        public MantaNoiseComponent()
            : base("Noise", "Noise",
                   "Core acoustic analysis — direct + optional first-order reflections.\n" +
                   "Paints blue→red heat-map. Enable compliance overlay with Limit dB.\n" +
                   "Formula: L = L_src − 20·log10(d) − 11 + 10·log10(cosθ + 0.01)",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("33445566-7788-4900-AABB-CCDDEEFF0012");
        protected override Bitmap Icon => MantaIcons.Noise24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter   ("Mesh",       "M",   "Analysis mesh from Manta Mesh",                                        GH_ParamAccess.item);
            p.AddPointParameter  ("Sources",    "S",   "Source points from Manta Source",                                      GH_ParamAccess.list);
            p.AddNumberParameter ("Levels",     "dB",  "dB levels from Manta Source",                                          GH_ParamAccess.list);
            p.AddNumberParameter ("Min dB",     "Min", "Pin lower bound of colour scale (auto if omitted)",                     GH_ParamAccess.item);
            p.AddNumberParameter ("Max dB",     "Max", "Pin upper bound of colour scale (auto if omitted)",                     GH_ParamAccess.item);
            p.AddBooleanParameter("Reflections","R",   "Enable first-order acoustic reflections",                              GH_ParamAccess.item, false);
            p.AddNumberParameter ("Absorption", "α",   "Reflection loss per bounce in dB (default 3)",                         GH_ParamAccess.item, 3.0);
            p.AddNumberParameter ("Materials",  "Mat", "Per-face absorption coefficient 0–1 — single value or list per face",  GH_ParamAccess.list);
            p.AddNumberParameter ("Limit dB",   "Lim", "Regulatory limit (e.g. 55 dB WHO day) — activates compliance overlay", GH_ParamAccess.item);
            p[3].Optional = true;
            p[4].Optional = true;
            p[5].Optional = true;
            p[6].Optional = true;
            p[7].Optional = true;
            p[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter  ("Mesh",         "M",   "Vertex-coloured facade mesh",                              GH_ParamAccess.item);
            p.AddNumberParameter("Face dB",      "dB",  "Per-face total dB → Manta Interior / Manta Contours",     GH_ParamAccess.list);
            p.AddNumberParameter("Min dB",       "Min", "Colour-scale minimum → Manta Legend",                        GH_ParamAccess.item);
            p.AddNumberParameter("Max dB",       "Max", "Colour-scale maximum → Manta Legend",                        GH_ParamAccess.item);
            p.AddNumberParameter("Exceeded m²",  "ExA", "Facade area exceeding Limit dB (m²)",                     GH_ParamAccess.item);
            p.AddNumberParameter("% Exceeded",   "Ex%", "Percentage of facade area exceeding Limit dB",            GH_ParamAccess.item);
            p.AddMeshParameter  ("Reflect Mesh", "RM",  "False-colour mesh of first-order reflection hotspots",    GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _display = null;

            Mesh    mesh       = null;
            var     sources    = new List<Point3d>();
            var     levels     = new List<double>();
            double  userMin    = double.NaN, userMax = double.NaN;
            bool    reflOn     = false;
            double  absorption = 3.0;
            var     matList    = new List<double>();
            double  limitDb    = double.NaN;

            if (!DA.GetData    (0, ref mesh)  || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh — connect Manta Mesh"); return; }
            if (!DA.GetDataList(1, sources)   || sources.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No sources — connect Manta Source"); return; }
            if (!DA.GetDataList(2, levels)    || levels.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No levels — connect Manta Source"); return; }

            bool hasMin   = DA.GetData    (3, ref userMin);
            bool hasMax   = DA.GetData    (4, ref userMax);
            DA.GetData    (5, ref reflOn);
            DA.GetData    (6, ref absorption);
            DA.GetDataList(7, matList);
            bool hasLimit = DA.GetData    (8, ref limitDb);

            while (levels.Count < sources.Count)
                levels.Add(levels[levels.Count - 1]);

            if (mesh.FaceNormals.Count != mesh.Faces.Count) mesh.FaceNormals.ComputeFaceNormals();
            if (mesh.Normals.Count     != mesh.Vertices.Count) mesh.Normals.ComputeNormals();

            if (mesh.Faces.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Mesh has no faces"); return; }

            double[] faceAlpha = null;
            if (matList.Count > 0)
            {
                faceAlpha = new double[mesh.Faces.Count];
                for (int fi = 0; fi < faceAlpha.Length; fi++)
                    faceAlpha[fi] = fi < matList.Count ? matList[fi] : matList[matList.Count - 1];
            }

            double[] faceDb = Acoustics.ComputeDirect(mesh, sources, levels);

            double[] reflDb = null;
            if (reflOn)
            {
                if (mesh.Faces.Count > 5000)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Reflections on {mesh.Faces.Count} faces — may take a moment.");
                reflDb = Acoustics.ComputeReflections(mesh, sources, levels, absorption, faceAlpha);
                faceDb = Acoustics.MergeDirectReflected(faceDb, reflDb);
            }

            double scaleMin = (hasMin && !double.IsNaN(userMin)) ? userMin : faceDb.Min();
            double scaleMax = (hasMax && !double.IsNaN(userMax)) ? userMax : faceDb.Max();
            if (Math.Abs(scaleMax - scaleMin) < 1e-6) scaleMax = scaleMin + 1.0;

            bool limitMode = hasLimit && !double.IsNaN(limitDb);
            if (limitMode) Acoustics.PaintVerticesLimit(mesh, faceDb, limitDb);
            else           Acoustics.PaintVertices     (mesh, faceDb, scaleMin, scaleMax);

            _display = Acoustics.BuildDisplayMesh(mesh);

            DA.SetData    (0, mesh);
            DA.SetDataList(1, faceDb);
            DA.SetData    (2, scaleMin);
            DA.SetData    (3, scaleMax);

            if (limitMode)
            {
                double totalArea = 0;
                for (int fi = 0; fi < mesh.Faces.Count; fi++)
                    totalArea += Acoustics.FaceArea(mesh, mesh.Faces[fi]);
                double exArea = Acoustics.ExceededArea(mesh, faceDb, limitDb);
                DA.SetData(4, exArea);
                DA.SetData(5, totalArea > 0 ? exArea / totalArea * 100.0 : 0.0);
            }

            if (reflDb != null)
            {
                var    reflMesh = mesh.DuplicateMesh();
                var    valid    = reflDb.Where(d => d > -100).ToArray();
                double rMin     = valid.Length > 0 ? valid.Min() : -20.0;
                double rMax     = valid.Length > 0 ? valid.Max() :   0.0;
                if (Math.Abs(rMax - rMin) < 1e-6) rMax = rMin + 1.0;
                Acoustics.PaintVertices(reflMesh, reflDb, rMin, rMax);
                DA.SetData(6, reflMesh);
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            var m = _display;
            if (m != null && m.VertexColors.Count > 0)
                args.Display.DrawMeshFalseColors(m);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            var m = _display;
            if (m != null) args.Display.DrawMeshWires(m, args.WireColour);
        }
    }
}
