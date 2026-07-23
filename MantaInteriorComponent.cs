using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaInteriorComponent : GH_Component
    {
        public MantaInteriorComponent()
            : base("Interior", "Interior",
                   "Interior noise exposure score — each facade face acts as a secondary source.\n" +
                   "Score = 10·log10(Σ [10^(dBi/10) × area_i / dist_i²])\n" +
                   "Wire 'Interior dB' → Galapagos fitness and set to Minimise.",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("44556677-8899-4011-BBCC-DDEEFF001123");
        protected override Bitmap Icon => MantaIcons.Interior24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter  ("Mesh",        "M",  "Analysis mesh from Manta Noise",          GH_ParamAccess.item);
            p.AddNumberParameter("Face dB",     "dB", "Per-face dB values from Manta Noise",     GH_ParamAccess.list);
            p.AddPointParameter ("Interior Pt", "IP", "Point inside the building",             GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Interior dB",    "IntdB",
                "Interior noise exposure score (dB) — minimise with Galapagos",
                GH_ParamAccess.item);
            p.AddNumberParameter("Area-wtd mean",  "AvgdB",
                "Area-weighted mean facade dB (acoustic pressure)",
                GH_ParamAccess.item);
            p.AddNumberParameter("Peak face dB",   "PkdB",
                "Loudest single face on the facade",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh    mesh       = null;
            var     faceDbList = new List<double>();
            Point3d ip         = Point3d.Unset;

            if (!DA.GetData    (0, ref mesh)     || mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh"); return; }
            if (!DA.GetDataList(1, faceDbList)   || faceDbList.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Face dB — connect Manta Noise"); return; }
            if (!DA.GetData    (2, ref ip)        || ip == Point3d.Unset)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No interior point"); return; }

            if (faceDbList.Count != mesh.Faces.Count)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Face dB count does not match mesh face count"); return; }

            double[] faceDb = faceDbList.ToArray();

            double interiorScore = Acoustics.InteriorScore(mesh, faceDb, ip);

            double totalArea = 0, weightedSum = 0, peakDb = double.MinValue;
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                double a = Acoustics.FaceArea(mesh, mesh.Faces[fi]);
                totalArea   += a;
                weightedSum += faceDb[fi] * a;
                if (faceDb[fi] > peakDb) peakDb = faceDb[fi];
            }

            DA.SetData(0, interiorScore);
            DA.SetData(1, totalArea > 0 ? weightedSum / totalArea : 0);
            DA.SetData(2, peakDb > double.MinValue ? peakDb : 0);
        }
    }
}
