using System.Drawing;
using System.IO;

namespace MosaicImageGeneration
{
    static class Program
    {
        private const string _targetImage = "/Pictures/stp.png";
		
        private static void Main(string[] args)
        {
            var bmp = new Bitmap(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+_targetImage);
            Console.WriteLine(bmp.AverageColor());
        }

        private static Color AverageColor(this Bitmap img)
        {
            ulong rSum = 0;
            ulong gSum = 0;
            ulong bSum = 0;
			
            for (var w = 0; w < img.Width; w++)
            {
                for (var h = 0; h < img.Height; h++)
                {
                    var pixel = img.GetPixel(w, h);
                    rSum += pixel.R;
                    gSum += pixel.G;
                    bSum += pixel.B;
                }
            }
            var avgR = (int) (rSum / (ulong)(img.Width*img.Height));
            var avgG = (int) (gSum / (ulong)(img.Width*img.Height));
            var avgB = (int) (bSum / (ulong)(img.Width*img.Height));
			
            return Color.FromArgb(avgR, avgG, avgB);
        }
    }
}