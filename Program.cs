using System.Drawing;

namespace MosaicImageGeneration
{
	static class Program
	{
		private const int _nChunks = 8; // _nChunks * _nChunks
		private const string _targetImage = "/Pictures/stp.png";
		private const string _outputPath = "/Pictures/test/";
		
		private static void Main(string[] args)
		{
			var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var bmp = new Bitmap(userDir+_targetImage);

			var chunks = bmp.GenerateChunks();

			var x = 0;
			foreach (var chunk in chunks)
			{
				chunk.Save(userDir+_outputPath + x++ + ".png");
			}
		}

		private static List<Bitmap> GenerateChunks(this Bitmap img)
		{
			var result = new List<Bitmap>();
			var chunkWidth = img.Width / _nChunks;
			var chunkHeight = img.Height / _nChunks;

			for (var widthChunkNum = 0; widthChunkNum < _nChunks; widthChunkNum++)
				for (var heightChunkNum = 0; heightChunkNum < _nChunks; heightChunkNum++)
				{
					var chunk = new Bitmap(chunkWidth, chunkHeight);
					var graphics = Graphics.FromImage(chunk);
					graphics.DrawImage( img, new Rectangle(0, 0, chunkWidth, chunkHeight),
						new Rectangle(widthChunkNum*chunkWidth, heightChunkNum*chunkHeight, chunkWidth, chunkHeight), GraphicsUnit.Pixel);
					graphics.Dispose();
					result.Add(chunk);
				}
			
			return result;
		}
		
		private static Color AverageColor(this Bitmap img)
		{
			ulong rSum = 0;
			ulong gSum = 0;
			ulong bSum = 0;
			
			for (var w = 0; w < img.Width; w++)
				for (var h = 0; h < img.Height; h++)
				{
					var pixel = img.GetPixel(w, h);
					rSum += pixel.R;
					gSum += pixel.G;
					bSum += pixel.B;
				}
			var avgR = (int) (rSum / (ulong)(img.Width*img.Height));
			var avgG = (int) (gSum / (ulong)(img.Width*img.Height));
			var avgB = (int) (bSum / (ulong)(img.Width*img.Height));
			
			return Color.FromArgb(avgR, avgG, avgB);
		}
	}
}