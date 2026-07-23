using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Manta
{
    public class MantaSourceComponent : GH_Component
    {
        public MantaSourceComponent()
            : base("Source", "Source",
                   "Define acoustic noise sources — point sources and/or line sources (rail track, road).\n" +
                   "Line sources are subdivided into N equal-power sub-points: L_sub = L_total − 10·log10(N).",
                   "Manta", "Acoustic")
        { }

        public override Guid ComponentGuid => new Guid("11223344-5566-4778-8899-AABBCCDDEEF0");
        protected override Bitmap Icon => MantaIcons.Source24;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter ("Point Sources","P",  "Individual noise source points",                         GH_ParamAccess.list);
            p.AddNumberParameter("Point dB",    "dBP", "Sound power level for each point source (dB SPL)",       GH_ParamAccess.list);
            p.AddCurveParameter ("Rail / Road", "T",   "Line-source curve — rail track or road centreline",      GH_ParamAccess.list);
            p.AddNumberParameter("Line dB",     "dBT", "Sound power level for each line source (dB SPL)",        GH_ParamAccess.list);
            p.AddIntegerParameter("Subdivisions","N",  "Sub-sources per line source (default 20)",               GH_ParamAccess.item, 20);
            p[0].Optional = true;
            p[1].Optional = true;
            p[2].Optional = true;
            p[3].Optional = true;
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter ("Sources","S",  "All source positions (flat list) → Manta Noise",   GH_ParamAccess.list);
            p.AddNumberParameter("Levels", "dB", "Matched dB levels → Manta Noise",                  GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N",  "Total number of source sub-points",              GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var ptSrcs    = new List<Point3d>();
            var ptLevels  = new List<double>();
            var tracks    = new List<Curve>();
            var trkLevels = new List<double>();
            int subdiv    = 20;

            DA.GetDataList(0, ptSrcs);
            DA.GetDataList(1, ptLevels);
            DA.GetDataList(2, tracks);
            DA.GetDataList(3, trkLevels);
            DA.GetData    (4, ref subdiv);
            subdiv = Math.Max(1, Math.Min(500, subdiv));

            if (ptSrcs.Count == 0 && tracks.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect at least one point (P) or line source (T)"); return; }

            var outSrc = new List<Point3d>();
            var outLvl = new List<double>();

            for (int i = 0; i < ptSrcs.Count; i++)
            {
                outSrc.Add(ptSrcs[i]);
                outLvl.Add(i < ptLevels.Count ? ptLevels[i]
                           : ptLevels.Count > 0 ? ptLevels[ptLevels.Count - 1] : 80.0);
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] == null) continue;
                double lvl = i < trkLevels.Count ? trkLevels[i]
                             : trkLevels.Count > 0 ? trkLevels[trkLevels.Count - 1] : 85.0;
                Acoustics.SubdivideLineSource(tracks[i], lvl, subdiv, outSrc, outLvl);
            }

            if (outSrc.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid sources produced"); return; }

            DA.SetDataList(0, outSrc);
            DA.SetDataList(1, outLvl);
            DA.SetData    (2, outSrc.Count);
        }
    }
}
