using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaMeshComponent : GH_Component
    {
        public MantaMeshComponent()
            : base("Mesh", "Mesh",
                   "Convert any geometry to a Manta analysis mesh.\n" +
                   "Supports Mesh, Surface, Brep, SubD and Extrusion.\n" +
                   "Outputs normals-ready mesh, face count and total area.",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("22334455-6677-4889-99AA-BBCCDDEEFF01");
        protected override Bitmap Icon => MantaIcons.Mesh24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGeometryParameter("Geometry","G","Facade geometry — Mesh, Surface, Brep, SubD or Extrusion", GH_ParamAccess.item);
            p.AddIntegerParameter ("Quality", "Q","Mesh quality: 0 fast · 1 default · 2 analysis · 3 fine",   GH_ParamAccess.item, 1);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter  ("Mesh",      "M",  "Analysis mesh → Manta Noise / Manta Contours",   GH_ParamAccess.item);
            p.AddIntegerParameter("Face Count","FC", "Number of mesh faces",                            GH_ParamAccess.item);
            p.AddNumberParameter ("Area",     "A",  "Total surface area (model units²)",               GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GeometryBase geo     = null;
            int          quality = 1;

            if (!DA.GetData(0, ref geo) || geo == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geometry"); return; }
            DA.GetData(1, ref quality);
            quality = Math.Max(0, Math.Min(3, quality));

            Mesh mesh = Acoustics.ConvertToMesh(geo, quality);
            if (mesh == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not convert geometry to mesh"); return; }
            if (mesh.Faces.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Resulting mesh has no faces"); return; }

            mesh.FaceNormals.ComputeFaceNormals();
            mesh.Normals.ComputeNormals();

            double totalArea = 0;
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
                totalArea += Acoustics.FaceArea(mesh, mesh.Faces[fi]);

            DA.SetData(0, mesh);
            DA.SetData(1, mesh.Faces.Count);
            DA.SetData(2, totalArea);
        }
    }
}
