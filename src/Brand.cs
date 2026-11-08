using System;
using System.Drawing;
using System.Reflection;

[assembly: AssemblyTitle("Magic Select")]
[assembly: AssemblyDescription("Selecione qualquer coisa na tela para traduzir, pesquisar ou perguntar à IA.")]
[assembly: AssemblyProduct("Magic Select")]
[assembly: AssemblyCompany("Magic Select")]
[assembly: AssemblyCopyright("© 2026 Magic Select")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace MagicSelect
{
    static class Brand
    {
        public const string Name = "Magic Select";
        public const string Version = "1.1.0";
        public const string Tagline = "Selecione qualquer coisa na tela. Traduza, pesquise, pergunte.";

        /// <summary>The app icon (embedded resource "app.ico") at the requested size.</summary>
        public static Icon GetIcon(int size)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s, size, size);
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Bitmap GetLogo(int size)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
            using (var big = s != null ? new Bitmap(s) : SystemIcons.Application.ToBitmap())
            {
                var bmp = new Bitmap(size, size);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(big, 0, 0, size, size);
                }
                return bmp;
            }
        }
    }
}
