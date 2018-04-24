using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using log4net;
using NetSerializer;
using SS13to64x.DMI;

namespace SS13to64x
{
    static class Program
    {
        private const string Datafile = "dmi_info.dat";

        private static bool _parrallelEnabled;

        private static ILog _log;

        private static bool use4X = false;

        static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            _log = LogManager.GetLogger("Main");




            if (args.Contains("-help") || args.Contains("-?"))
            {
                PrintHelp();
                return;
            }



            if (args.Length < 2)
            {
                Console.WriteLine("Not enough arguments, two required.. use -help");
                return;
            }

            var inputFolder = args[0];
            var outputFolder = args[1];

            if (args.Contains("-p"))
                _parrallelEnabled = true;

            if (args.Contains("-4x"))
                use4X = true;


            var files = Directory.GetFiles(inputFolder, "*.dmi", SearchOption.AllDirectories).ToList();

            files = Generate(files, inputFolder, outputFolder);


            if (_parrallelEnabled)
                Parallel.ForEach(files, file => Rebuild(file, outputFolder));
            else
            {
                foreach (var file in files)
                {
                    Rebuild(file, outputFolder);
                }
            }

            Console.ReadLine();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Mass DMI Rescaler");
            Console.WriteLine("Created by Head for Baystation ( Sebastian Broberg )");
            Console.WriteLine("########################");
            Console.WriteLine("Basic Usage: dmi_hqx.exe inputfolder outputfolder");
            Console.WriteLine("Addtional Arguments");
            Console.WriteLine("\t-p : Parallel processing enabled");
            Console.WriteLine("\t-4x : Set program to use HQx4 instead of x2");
            Console.ReadKey(true);
        }

        private static void Rebuild(string file, string outPath)
        {
            var ser =
                new Serializer(new List<Type>
                {
                    typeof (DmiImage),
                    typeof (DMIState),
                    typeof (DMIFrame),
                    typeof (DMIImageData)
                });

            var path = Path.GetDirectoryName(file);
            var relPath = path.Replace((outPath + "\\raw"), "");
            if (relPath.StartsWith("\\"))
                relPath = relPath.Substring(1);
            DmiImage dmi = null;

            try
            {
                using (var stream = File.OpenRead(file))
                {
                    dmi = (DmiImage)ser.Deserialize(stream);
                }
            }
            catch (Exception e)
            {
                _log.Error("Error during rebuild", e);
                throw;
            }

            dmi.StateHeight = 32;
            dmi.StateWidth = 32;

            var stateIndex = 0;
            foreach (var state in dmi.States)
            {
                var statePath = Path.Combine(path, stateIndex.ToString());
                var frameIndex = 0;
                foreach (var frame in state.GetFrames())
                {
                    var framePath = Path.Combine(statePath, frameIndex.ToString());
                    foreach (var image in frame.GetImages())
                    {
                        var imagePath = Path.Combine(framePath, image.Dir.ToString() + ".png");
                        if (File.Exists(imagePath))
                            image.Bitmap = new Bitmap(imagePath);
                        else
                        {
                            Console.WriteLine("File {0} not found!", imagePath);
                        }
                    }
                    frameIndex++;
                }
                stateIndex++;
            }
            DmiImage.Create(dmi, Path.Combine(outPath, "processed", relPath + ".png"), Path.Combine(outPath, "final", relPath + ".dmi"));
        }

        private static void AskContinue()
        {
            throw new NotImplementedException();
        }

        static bool IsPowerOfTwo(int x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
        private static List<String> Generate(IEnumerable<string> files, string inputFolder, string outputFolder)
        {
            outputFolder = Path.Combine(outputFolder, "raw");
            var processedFiles = new List<String>();
            if (_parrallelEnabled)
            {
                var bag = new ConcurrentBag<string>();
                Parallel.ForEach(files, file =>
                {
                    var f = ExtractDMI(inputFolder, outputFolder, file);
                    if (!String.IsNullOrEmpty(f))
                        bag.Add(f);
                });
                processedFiles = bag.ToList();
            }
            else
            {
                processedFiles.AddRange(files.Select(file => ExtractDMI(inputFolder, outputFolder, file)).Where(f => !String.IsNullOrEmpty(f)));
            }
            return processedFiles;
        }

        private static string ExtractDMI(string input, string outPath, string file)
        {
            var ser =
                new Serializer(new List<Type> { typeof(DmiImage), typeof(DMIState), typeof(DMIFrame), typeof(DMIImageData) });
            DmiImage dmi = null;
            dmi = new DmiImage(file);
/*            try
            {
                dmi = new DmiImage(file);
            }
            catch (Exception e)
            {
                _log.Error("Error during extraction", e);
                return null;
            }
            */
            var oPath = Path.Combine(outPath, Path.GetDirectoryName(file.Replace(input + "\\", "")), dmi.DmiName);
            if (!Directory.Exists(oPath))
                Directory.CreateDirectory(oPath);


            using (var stream = File.Create(Path.Combine(oPath, Datafile)))
            {
                ser.Serialize(stream, dmi);
            }


            var stateIndex = 0;
            for (int i = 0; i < dmi.States.Count(); i++)
            {
                DMIState dmiState = dmi.States[i];
                var statePath = Path.Combine(oPath, stateIndex.ToString());
                if (!Directory.Exists(statePath))
                    Directory.CreateDirectory(statePath);
                int frameIndex = 0;
                foreach (var frame in dmiState.GetFrames())
                {
                    var framePath = Path.Combine(statePath, frameIndex.ToString());
                    if (!Directory.Exists(framePath))
                        Directory.CreateDirectory(framePath);
                    foreach (var image in frame.GetImages())
                    {
                        var imgPath = Path.Combine(framePath, image.Dir + ".png");
      //                  MakeImageTransform(image);
                        image.Bitmap.Save(imgPath);
                    }
                    frameIndex++;
                }
                stateIndex++;
            }
            _log.InfoFormat("Extracted {0}", file);

            return Path.Combine(oPath, Datafile);
        }

        /// <summary>
        /// Transform a given image using a direction specific template
        /// </summary>
        /// <param name="image"></param>
        /// <returns>Uses pass by reference, no return</returns>
        private static void MakeImageTransform(DMIImageData image)
        {
            FreeImageAPI.FreeImageBitmap inMap = new FreeImageAPI.FreeImageBitmap(image.Bitmap);
            FreeImageAPI.FreeImageBitmap transMap = new FreeImageAPI.FreeImageBitmap("./in/Templates/UnathiUnder" + image.Dir + ".png");
            //        FreeImageAPI.FreeImageBitmap transMap = new FreeImageAPI.FreeImageBitmap("./in/Templates/UnathiHatGlass.png");
            //      FreeImageAPI.FreeImageBitmap transMap = new FreeImageAPI.FreeImageBitmap("./in/Templates/TallGreySquare.png");
            inMap.ConvertColorDepth(FreeImageAPI.FREE_IMAGE_COLOR_DEPTH.FICD_32_BPP);
            transMap.ConvertColorDepth(FreeImageAPI.FREE_IMAGE_COLOR_DEPTH.FICD_32_BPP);
            Color inCol = new Color();
            Color transCol = new Color();
            Color clearRead = Color.FromArgb(0, 192, 192, 192);
            Color clearWrite = inMap.GetPixel(0, 0);
            int[,][] transStore = new int[transMap.Width, transMap.Height][];
            if (image.Dir == 4)
                inMap.RotateFlip(RotateFlipType.RotateNoneFlipX);
            // Get the original tranformation map's colours and store in 2D jagged array for manipulation
            for (int i = 0; i < transMap.Height; i++)
            {
                for (int j = 0; j < transMap.Width; j++)
                {
                    transCol = transMap.GetPixel(j, i);
                    transStore[j, i] = new int[4] { transCol.A, transCol.R, transCol.G, transCol.B };
                }
            }

            // Iterate through the image pixel by pixel, taking the colour from the original and applying it to
            // all members of the transform array with a colour that matches the location based on i and j.
            // Ex: if i is 10 and j is 20, all places in the array with the ARGB colour (255, 10, 20, 0)
            // will be replaced with the new colour.
            for (int i = 0; i < inMap.Height; i++)
            {
                for (int j = 0; j < inMap.Width; j++)
                {
                    inCol = inMap.GetPixel(j, i);
                    transCol = Color.FromArgb(255, j, i, 0);
                    // 
                    transStore = StoreColReplace(transStore, transCol, inCol, transMap.Width, transMap.Height);
                }
            }
            // Takes the array and writes the new pixels onto the template
            for (int i = 0; i < transMap.Height; i++)
            {
                for (int j = 0; j < transMap.Width; j++)
                {
                    Color tempCol = Color.FromArgb(transStore[j, i][0], transStore[j, i][1], transStore[j, i][2], transStore[j, i][3]);

                    if (tempCol.Equals(clearWrite))
                        tempCol = Color.FromArgb(transStore[31, 15][0], transStore[31, 15][1], transStore[31, 15][2], transStore[31, 15][3]);
                    transMap.SetPixel(j, i, tempCol);
                }
            }
            transMap.PreMultiplyWithAlpha();
            if (image.Dir == 4)
                transMap.RotateFlip(RotateFlipType.RotateNoneFlipX);

            image.Bitmap = transMap.ToBitmap();
        }

        private static int[,][] StoreColReplace(int[,][] colMatrix, Color transCol, Color newCol, int width, int height)
        {
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    if (colMatrix[j, i][0] == transCol.A && colMatrix[j, i][1] == transCol.R && colMatrix[j, i][2] == transCol.G && colMatrix[j, i][3] == transCol.B)
                    {
                        colMatrix[j, i] = new int[4] { newCol.A, newCol.R, newCol.G, newCol.B };
                    }
                }
            }


            return colMatrix;
        }

        private static DMIImageData MakeGreyscaleSquare(DMIImageData image)
        {
            FreeImageAPI.FreeImageBitmap bit = new FreeImageAPI.FreeImageBitmap(image.Bitmap);
            bit.ConvertColorDepth(FreeImageAPI.FREE_IMAGE_COLOR_DEPTH.FICD_32_BPP);

            Color grey = new Color();
            grey = bit.GetPixel(2, 2);
            for (int i = 0; i < bit.Height; i++)
            {
                for (int j = 0; j < bit.Width; j++)
                {
                    grey = Color.FromArgb(255, j, i, 0);
                    bit.SetPixel(j, i, grey);
                }
            }
            image.Bitmap = bit.ToBitmap();

            return image;
        }
    }
}

