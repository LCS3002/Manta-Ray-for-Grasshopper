using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;

namespace Manta
{
    static class MantaColors
    {
        public static readonly Color Navy  = Color.FromArgb(  8,  12,  28);
        public static readonly Color Teal  = Color.FromArgb(  0, 210, 180);
        public static readonly Color Cyan  = Color.FromArgb( 60, 220, 255);
        public static readonly Color Amber = Color.FromArgb(245, 166,  35);
    }

    static class MantaIcons
    {
        static Bitmap Load(string name)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly()
                                       .GetManifestResourceStream("Manta." + name))
                    return s != null ? new Bitmap(s) : null;
            }
            catch { return null; }
        }

        static Bitmap _manta24, _manta48;
        static Bitmap _src24, _msh24, _nse24, _int24, _con24, _leg24;
        static Bitmap _mwnd24, _msun24, _mpre24;

        public static Bitmap Manta24      => _manta24 ?? (_manta24 = Load("Manta_24.png"));
        public static Bitmap Manta48      => _manta48 ?? (_manta48 = Load("Manta_48.png"));
        public static Bitmap Source24     => _src24   ?? (_src24   = Load("MantaSource_24.png"));
        public static Bitmap Mesh24       => _msh24   ?? (_msh24   = Load("MantaMesh_24.png"));
        public static Bitmap Noise24      => _nse24   ?? (_nse24   = Load("MantaNoise_24.png"));
        public static Bitmap Interior24   => _int24   ?? (_int24   = Load("MantaInterior_24.png"));
        public static Bitmap Contours24   => _con24   ?? (_con24   = Load("MantaContours_24.png"));
        public static Bitmap Legend24     => _leg24   ?? (_leg24   = Load("MantaLegend_24.png"));
        public static Bitmap MantaWind24  => _mwnd24  ?? (_mwnd24  = Load("MantaWind_24.png"));
        public static Bitmap MantaSun24   => _msun24  ?? (_msun24  = Load("MantaSun_24.png"));
        public static Bitmap MantaPre24   => _mpre24  ?? (_mpre24  = Load("MantaPressure_24.png"));
    }

    public class MantaAssemblyInfo : GH_AssemblyInfo
    {
        public override string Name          => "Manta Ray";
        public override string Description   => "Environmental analysis for Grasshopper — acoustic noise, animated wind, solar path, pressure waves. One plugin, all physics.";
        public override Guid   Id            => new Guid("A1B2C3D4-E5F6-4890-8BCD-EF1234567891");
        public override string AuthorName    => "LCS3002";
        public override string AuthorContact => "https://github.com/LCS3002/Manta-Ray-for-Grasshopper";
        public override Bitmap Icon          => MantaIcons.Manta48;
    }
}
