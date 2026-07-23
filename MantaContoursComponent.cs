using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Manta
{
    public class MantaContoursComponent : GH_Component
    {
        public MantaContoursComponent()
            : base("Contours", "Contours",
                   "Extract isodecibel contour polylines — one branch per level in the output tree.\n" +
                   "Use for acoustic maps, regulatory overlays and documentation.",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("55667788-99AA-4122-CCDD-EEFF00112234");
        protected override Bitmap Icon => MantaIcons.Contours24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter  ("Mesh",   "M",  "Analysis mesh from Manta Noise",                       GH_ParamAccess.item);
            p.AddNumberParameter("Face dB","dB", "Per-face dB values from Manta Noise",                  GH_ParamAccess.list);
            p.AddNumberParameter("Levels", "L",  "dB levels to contour — e.g. {50,55,60,65,70,75}",  GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter ("Contours","C",
                "Isodecibel contour polylines — one branch per level",
                GH_ParamAccess.tree);
            p.AddNumberParameter("Levels","L",
                "dB level for each output branch",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Count","N",
                "Total number of contour segments generated",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh    mesh      = null;
            var     faceDbL   = new List<double>();
            var     targetLvl = new List<double>();

            if (!DA.GetData    (0, ref mesh)    || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh"); return; }
            if (!DA.GetDataList(1, faceDbL)     || faceDbL.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Face dB — connect Manta Noise"); return; }
            if (!DA.GetDataList(2, targetLvl)   || targetLvl.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No contour levels — e.g. {55,60,65,70}"); return; }

            if (faceDbL.Count != mesh.Faces.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Face dB count does not match mesh face count"); return; }

            if (mesh.Normals.Count    != mesh.Vertices.Count) mesh.Normals.ComputeNormals();
            if (mesh.FaceNormals.Count!= mesh.Faces.Count)    mesh.FaceNormals.ComputeFaceNormals();

            double[]   faceDb   = faceDbL.ToArray();
            double[]   vertexDb = Acoustics.FaceDbToVertexDb(mesh, faceDb);
            var        tree     = new GH_Structure<GH_Curve>();
            var        outLvls  = new List<double>();
            int        total    = 0;

            for (int li = 0; li < targetLvl.Count; li++)
            {
                double level    = targetLvl[li];
                var    contours = ContourAlgo.March(mesh, vertexDb, level);
                var    path     = new GH_Path(li);

                foreach (var poly in contours)
                {
                    if (poly.Count < 2) continue;
                    var nc = poly.ToNurbsCurve();
                    if (nc != null) { tree.Append(new GH_Curve(nc), path); total++; }
                }
                outLvls.Add(level);
            }

            if (total == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No contours generated — levels may be outside the dB range of the mesh.");

            DA.SetDataTree(0, tree);
            DA.SetDataList(1, outLvls);
            DA.SetData    (2, total);
        }
    }
}
