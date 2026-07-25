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
		private static readonly string _baseDir = AppContext.BaseDirectory;
		
		private static int _nChunks = 100; // _nChunks * _nChunks
		
		private static string _targetImage = Path.Combine(_baseDir, "target.jpg");
		private static string _thumbnailDir = Path.Combine(_baseDir, "thumbnails");
		private static string _thumbnailDataCache = Path.Combine(_baseDir, "cache.json");
		private static string _outputDir = Path.Combine(_baseDir, "results");
		
		private static void Main(string[] args)
		{
			Console.WriteLine("Mosaic Image Generation");
			ParseArgs(args);
			
			Console.WriteLine("Checking for thumbnail cache.");
			if (!File.Exists(_thumbnailDataCache))
			{
				Console.WriteLine($"cache.json not available in {_thumbnailDataCache}.");
				Console.WriteLine($"Creating thumbnail cache using the thumbnails in {_thumbnailDir}. This will take a while.");
				populateThumbnailCache(_thumbnailDir, _thumbnailDataCache);
			}
			Console.WriteLine($"Loading thumbnail cache from {_thumbnailDataCache}");
			var thumbnailImages = JsonSerializer.Deserialize<List<ThumbnailData>>(File.ReadAllText(_thumbnailDataCache))!;
			
			Console.WriteLine($"Loading target image from {_targetImage}");
			var bmp = new Bitmap(_targetImage);
			var chunkWidth = bmp.Width / _nChunks;
			var chunkHeight = bmp.Height / _nChunks;

			Console.WriteLine($"Chunking image into {_nChunks} chunks...");
			var chunks = bmp.GenerateChunks(chunkWidth, chunkHeight);
			bmp.Dispose();

			Console.WriteLine("Finding chunk color averages.");
			var chunkColorAverages = chunks.Select(c =>
			{
				var avgColor = c.AverageColor();
				c.Dispose();
				return avgColor;
			}).ToList();
			
			
			Console.WriteLine("Finding the best thumbnails.");
			var mosaicThumbnails = BestFitThumbnails(chunkColorAverages, thumbnailImages);

			Console.WriteLine("Stitching the thumbnails.");
			var finalMosaicImage = StitchMosaic(mosaicThumbnails, chunkWidth, chunkHeight);

			var finalOutputPath = GetOutputPath(_outputDir);
			Console.WriteLine($"Saving the generated mosaic to {finalOutputPath}");
			finalMosaicImage.Save(finalOutputPath);
			finalMosaicImage.Dispose();
			Console.WriteLine("Done.");
		}
		
		private static void ParseArgs(string[] args)
		{
			foreach (var arg in args)
			{
				if (!arg.StartsWith("--") || !arg.Contains('='))
					continue;

				var split = arg.IndexOf('=');
				var key = arg[2..split].ToLowerInvariant();
				var value = arg[(split + 1)..].Trim('"');

				switch (key)
				{
					case "chunks":
						if (!int.TryParse(value, out var n) || n < 1)
						{
							Console.WriteLine($"Invalid value for --chunks: '{value}' (must be a positive integer)");
							Environment.Exit(1);
						}
						_nChunks = n;
						break;
					case "target":     _targetImage = value; break;
					case "thumbnails": _thumbnailDir = value; break;
					case "cache":      _thumbnailDataCache = value; break;
					case "output":     _outputDir = value; break;
					default:
						Console.WriteLine($"Unknown option: --{key}");
						Console.WriteLine("Options: --target= --thumbnails= --cache= --output=");
						Environment.Exit(1);
						break;
				}
			}
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
	
		private static Bitmap StitchMosaic(List<string> chunks, int chunkWidth, int chunkHeight)
		{
			var result = new Bitmap(chunkWidth * _nChunks, chunkHeight * _nChunks);

			using var graphics = Graphics.FromImage(result);
			var i = 0;
			for (var widthChunkNum = 0; widthChunkNum < _nChunks; widthChunkNum++)
				for (var heightChunkNum = 0; heightChunkNum < _nChunks; heightChunkNum++)
				{
					using var thumb = new Bitmap(chunks[i++]);
					
					graphics.DrawImage(thumb,
						new Rectangle(widthChunkNum * chunkWidth, heightChunkNum * chunkHeight, chunkWidth, chunkHeight),
						new Rectangle(0, 0, thumb.Width, thumb.Height),   // full thumbnail → scaled into the cell
						GraphicsUnit.Pixel);
				}

			return result;
		}
		
		private static string GetOutputPath(string baseDir)
		{
			var path = Path.Combine(baseDir, "output.png");
			
			var counter = 1;
			while (File.Exists(path))
				path = Path.Combine(baseDir, $"output_{counter++}.png");

			return path;
		}
	}
}