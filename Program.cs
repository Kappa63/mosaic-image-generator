using System.Drawing;
using System.Text.Json;
using System.IO;
using ColorMine;
using ColorMine.ColorSpaces;
using ColorMine.ColorSpaces.Comparisons;

namespace MosaicImageGeneration
{
	public class ThumbnailData
	{
		public required string path { get; init; }
		public required Rgb avgColor { get; init; }
	}

	static class Program
	{
		private const int _nChunks = 100; // _nChunks * _nChunks
		private const string _targetImage = "/Pictures/stp.png";
		private const string _thumbnailDir = "/Pictures/thumbnails";
		private const string _thumbnailDataCache = "/Pictures/cache.json";
		private const string _outputPath = "/Pictures/results/";
		
		private static void Main(string[] args)
		{
			var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var bmp = new Bitmap(userDir+_targetImage);
			var chunkWidth = bmp.Width / _nChunks;
			var chunkHeight = bmp.Height / _nChunks;

			if (!File.Exists(userDir+_thumbnailDataCache))
				populateThumbnailCache(userDir+_thumbnailDir, userDir+_thumbnailDataCache);
			
			var thumbnailImages = JsonSerializer.Deserialize<List<ThumbnailData>>(File.ReadAllText(userDir+_thumbnailDataCache))!;

			var chunks = bmp.GenerateChunks(chunkWidth, chunkHeight);

			var chunkColorAverages = chunks.Select(c => c.AverageColor()).ToList();
			
			var mosaicThumbnails = BestFitThumbnails(chunkColorAverages, thumbnailImages);
			
			var finalMosaicImage = StitchMosaic(mosaicThumbnails.Select(mt => new Bitmap(mt)).ToList(), chunkWidth, chunkHeight);
			
			finalMosaicImage.Save(userDir+_outputPath+"output.png");
		}
		
		private static Rgb AverageColor(this Bitmap img)
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
			
			return new Rgb{R = avgR, G = avgG, B = avgB};
		}

		private static void populateThumbnailCache(string thumbnailDir, string outputPath)
		{
			var result = new List<ThumbnailData>();
			foreach (var file in Directory.GetFileSystemEntries(thumbnailDir, "*", SearchOption.AllDirectories))
				if (file.EndsWith(".jpg") || file.EndsWith(".jpeg") || file.EndsWith(".png"))
					result.Add(new ThumbnailData{path = file, avgColor = new Bitmap(file).AverageColor()});	
			File.WriteAllText(outputPath, JsonSerializer.Serialize(result));
		}

		private static List<Bitmap> GenerateChunks(this Bitmap img, int chunkWidth, int chunkHeight)
		{
			var result = new List<Bitmap>();

			for (var widthChunkNum = 0; widthChunkNum < _nChunks; widthChunkNum++)
				for (var heightChunkNum = 0; heightChunkNum < _nChunks; heightChunkNum++)
				{
					var chunk = new Bitmap(chunkWidth, chunkHeight);
					using var graphics = Graphics.FromImage(chunk);
					graphics.DrawImage( img, new Rectangle(0, 0, chunkWidth, chunkHeight),
							new Rectangle(widthChunkNum*chunkWidth, heightChunkNum*chunkHeight, chunkWidth, chunkHeight), GraphicsUnit.Pixel);
					result.Add(chunk);
				}
			
			return result;
		}

		private static List<string> BestFitThumbnails(List<Rgb> chunkColorsAverages, List<ThumbnailData> thumbnailImages)
		{
			var result = new List<string>();
			
			foreach (var chunk in chunkColorsAverages)
			{
				string? closestImagePath = null;
				var closestDeltaE = double.MaxValue;
				foreach (var thumbnailImage in thumbnailImages)
				{
					var dE = chunk.Compare(thumbnailImage.avgColor, new Cie1976Comparison());
					if (closestDeltaE < dE)
						continue;
					closestDeltaE = dE;
					closestImagePath = thumbnailImage.path;
				}
				result.Add(closestImagePath!);
			}

			return result;
		}
	
		private static Bitmap StitchMosaic(List<Bitmap> chunks, int chunkWidth, int chunkHeight)
		{
			var result = new Bitmap(chunkWidth * _nChunks, chunkHeight * _nChunks);

			using var graphics = Graphics.FromImage(result);
			var i = 0;
			for (var widthChunkNum = 0; widthChunkNum < _nChunks; widthChunkNum++)
				for (var heightChunkNum = 0; heightChunkNum < _nChunks; heightChunkNum++)
				{
					graphics.DrawImage(chunks[i++], 
					new Rectangle(widthChunkNum * chunkWidth, heightChunkNum * chunkHeight, chunkWidth, chunkHeight),
					 new Rectangle(0, 0, chunkWidth, chunkHeight), GraphicsUnit.Pixel);
				}

			return result;
		}
	}
}