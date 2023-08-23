using SharpAvi;
using SharpAvi.Output;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using ParallelAssemblyLineNET;
using System.Linq;
using NaturalSort.Extension;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using Pfim;
using System.Runtime.InteropServices;
using ImageFormat = Pfim.ImageFormat;

namespace CL_AviDemo_To_AVI
{
    class Program
    {

        struct WriterStreamPair
        {
            public AviWriter writer;
            public IAviVideoStream stream;
            public StreamWriter statsCSV;
            public StreamWriter timecodes;
            public StreamWriter deleteFilesScript;
        }

        struct ImageWrapper
        {
            public string filename;
            public string filenameWithoutPath;
            public bool IsValid;
            public int Width;
            public int Height;
            public byte[] imageData;
        }


        static ConcurrentDictionary<Int64, StackedFloatImage> outputimageData = new ConcurrentDictionary<Int64, StackedFloatImage>();
        //static ConcurrentDictionary<int, ByteImage> imageData = new ConcurrentDictionary<int, ByteImage>();
        static FileSystemWatcher fsw = null;
        static string extension = ".tga";
        static int inputFPS = 1000;
        static int outputFPS = 60;
        static double fpsRatio = (double)inputFPS / (double)outputFPS;
        static Int64 inputIndex = 0;
        static string outputFolder = ".";

        static AutoResetEvent videoWriterARE = new AutoResetEvent(true);
        static AutoResetEvent newFileARE = new AutoResetEvent(true);
        static bool forceFinish = false;

        static void Main(string[] args)
        {

            if(args.Length < 2) {

                Console.Write("Input FPS: ");
                inputFPS = int.Parse(Console.ReadLine());
            } else
            {
                inputFPS = int.Parse(args[1]);
                Console.WriteLine($"Input FPS: {inputFPS}");
            }
            if(args.Length < 3) {

                Console.Write("Output FPS: ");
                outputFPS = int.Parse(Console.ReadLine());
            } else
            {
                outputFPS = int.Parse(args[2]);
                Console.WriteLine($"Output FPS: {outputFPS}");
            }

            fpsRatio = (double)inputFPS / (double)outputFPS;

            if (args.Length < 4) {

                Console.Write("File extension (enter for .tga): ");
                string extensionTmp = "." + Console.ReadLine().Trim().Replace(".", "");
                if (extensionTmp != ".")
                {
                    extension = extensionTmp;
                }
            } else
            {
                string extensionTmp = "." + args[3].Trim().Replace(".", "");
                if (extensionTmp != ".")
                {
                    extension = extensionTmp;
                }
                Console.WriteLine($"File extension: {extension}");
            }
            if (args.Length < 5) {

                Console.Write("Output folder (enter for .): ");
                string tmp = Console.ReadLine().Trim();
                outputFolder = tmp != "" ? tmp : ".";
            } else
            {
                string tmp = args[4].Trim();
                outputFolder = tmp != "" ? tmp : ".";
                Console.WriteLine($"Output folder: {extension}");
            }

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;
            Task.Factory.StartNew(() => {
                videoWriter();
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) => {
                Console.WriteLine(t.Exception.ToString());
            }, TaskContinuationOptions.OnlyOnFaulted);

            tokenSource = new CancellationTokenSource();
            ct = tokenSource.Token;
            Task.Factory.StartNew(() => {
                newFileChecker();
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) => {
                Console.WriteLine(t.Exception.ToString());
            }, TaskContinuationOptions.OnlyOnFaulted);


            fsw = new FileSystemWatcher();

            fsw.Path = args.Length > 0 ? args[0] : ".";

            fsw.Created += Fsw_Created;
            //fsw.Changed += Fsw_Changed;
            fsw.Renamed += Fsw_Renamed;

            fsw.Error += Fsw_Error;

            fsw.InternalBufferSize = 32000;

            fsw.EnableRaisingEvents = true;



            Console.WriteLine("Press any key to stop.");
            Console.ReadKey();
            forceFinish = true;
            videoWriterARE.Set(); newFileARE.Set();
            Console.WriteLine("Finishing up.");
            Console.ReadKey();
        }

        private static void Fsw_Renamed(object sender, RenamedEventArgs e)
        {
            Console.Write(e.Name);
        }

        private static void Fsw_Changed(object sender, FileSystemEventArgs e)
        {
            //Console.Write(e.Name);
        }

        private static void Fsw_Error(object sender, ErrorEventArgs e)
        {
            Console.WriteLine("X");
            Console.WriteLine(e.ToString());
            Console.WriteLine(e.GetException().ToString());
        }

        static ConcurrentQueue<string> newFiles = new ConcurrentQueue<string>();  
        private static void Fsw_Created(object sender, FileSystemEventArgs e)
        {
            //Console.Write("^");
            string fullPath = e.FullPath;
            newFiles.Enqueue(fullPath);
            newFileARE.Set();
            /*string fullPath = e.FullPath;
            
            }*/
        }
        
        private static void newFileChecker()
        {
            while (true)
            {
                newFileARE.WaitOne();
                if (forceFinish)
                {
                    break;
                }
                string fullPathTmp = null;
                while (newFiles.TryDequeue(out fullPathTmp))
                {
                    //Console.Write("*");
                    string fullPath = fullPathTmp;
                    string filename = Path.GetFileNameWithoutExtension(fullPath);
                    string extensionHere = Path.GetExtension(fullPath);
                    int fileNumber = 0;
                    if (filename.StartsWith("shot") && extensionHere.Equals(extension, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(filename.Substring(4, 4), out fileNumber))
                    {
                        Int64 indexHere = Interlocked.Increment(ref inputIndex) - 1;
                        CancellationTokenSource tokenSource = new CancellationTokenSource();
                        CancellationToken ct = tokenSource.Token;
                        Task.Factory.StartNew(() =>
                        {
                            ReadFileIntoMemory(fullPath, indexHere);
                        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) =>
                        {
                            Console.WriteLine(t.Exception.ToString());
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
                System.Threading.Thread.Sleep(50);
            }
        }

        private static void ReadFileIntoMemory(string fullPath, Int64 index)
        {
            bool readSuccessful = false;
            bool fileDeleted = false;
            while (!readSuccessful || !fileDeleted)
            {
//#if !DEBUG
                try
                {
//#endif
                    if (!readSuccessful) { 

                        if (extension.Equals(".tga", StringComparison.InvariantCultureIgnoreCase))
                        {
                            using (IImage image = Pfimage.FromFile(fullPath))
                            {
                                PixelFormat format;

                                // Convert from Pfim's backend agnostic image format into GDI+'s image format
                                switch (image.Format)
                                {
                                    case ImageFormat.Rgba32:
                                        format = PixelFormat.Format32bppArgb;
                                        break;
                                    case ImageFormat.Rgb24:
                                        format = PixelFormat.Format24bppRgb;
                                        break;
                                    default:
                                        // see the sample for more details
                                        throw new NotImplementedException();
                                }

                                ByteImage img = new ByteImage(image.Data,image.Stride,image.Width,image.Height,format);

                                AddImage(img,index);
                                //imageData[(int)index] = img;
                                Console.Write(".");
                                readSuccessful = true;
                            }
                        } else
                        {

                            using (Image bmp = Image.FromFile(fullPath))
                            {
                                ByteImage img = ByteImage.FromBitmap((Bitmap)bmp);
                                AddImage(img, index);
                                //imageData[(int)index] = img;
                                //Console.Write(".");
                                readSuccessful = true;
                            }
                        }
                    }

                    if (readSuccessful && !fileDeleted)
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                        fileDeleted = true;
                    }
//#if !DEBUG
                } catch(Exception e)
                {
                    Console.Write("_");
                    Console.WriteLine(e.ToString());
                }
//#endif

                if (!readSuccessful || !fileDeleted)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        private unsafe static void AddImage(ByteImage img, Int64 inputImageIndex)
        {
            Int64 outputIndex = (Int64)((double)inputImageIndex / fpsRatio);
            StackedFloatImage outputImage = null;
            lock (outputimageData)
            {
                if (!outputimageData.ContainsKey(outputIndex))
                {
                    outputimageData[outputIndex] = new StackedFloatImage(img.stride, img.width, img.height,img.pixelFormat);
                }
                outputImage = outputimageData[outputIndex];
            }
            int pixelOffset = img.pixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;
            //lock (outputImage) { 
            fixed (float* imageDataPtr = outputImage.imageData) { 
                for (int y= 0; y < outputImage.height; y++)
                {
                    lock (outputImage.lineLocks[y]) // So we can do images in parallel, we just lock on to lines. Idk if this is actually performant tho...
                    {
                        for(int x = 0; x < outputImage.width; x++)
                        {
                            int reverseY = outputImage.height - y -1;
                            //outputImage.imageData[y * outputImage.stride + x * pixelOffset] += img.imageData[reverseY * outputImage.stride + x * pixelOffset];
                            //outputImage.imageData[y * outputImage.stride + x * pixelOffset+1] += img.imageData[reverseY * outputImage.stride + x * pixelOffset + 1];
                            //outputImage.imageData[y * outputImage.stride + x * pixelOffset+2] += img.imageData[reverseY * outputImage.stride + x * pixelOffset + 2];
                            imageDataPtr[y * outputImage.stride + x * pixelOffset] += img.imageData[reverseY * outputImage.stride + x * pixelOffset];
                            imageDataPtr[y * outputImage.stride + x * pixelOffset+1] += img.imageData[reverseY * outputImage.stride + x * pixelOffset + 1];
                            imageDataPtr[y * outputImage.stride + x * pixelOffset+2] += img.imageData[reverseY * outputImage.stride + x * pixelOffset + 2];
                        }
                    }
                }
            }
            //}
            lock (outputImage)
            {
                outputImage.appliedSourceImageCount++;
                Int64 lowestInputIndexThisOutputImage = (Int64)(0.5+Math.Ceiling((double)outputIndex * fpsRatio))-2;
                if (lowestInputIndexThisOutputImage < 0) lowestInputIndexThisOutputImage = 0;
                while (((Int64)((double)lowestInputIndexThisOutputImage / fpsRatio)) < outputIndex)
                {
                    lowestInputIndexThisOutputImage++;
                }
                Int64 highestInputIndexThisOutputImage = (Int64)(0.5+Math.Floor((double)(outputIndex+1L) * fpsRatio))+2;
                while (((Int64)((double)highestInputIndexThisOutputImage / fpsRatio)) > outputIndex)
                {
                    highestInputIndexThisOutputImage--;
                }
                Int64 countNeeded = highestInputIndexThisOutputImage - lowestInputIndexThisOutputImage + 1;
                //Console.WriteLine($"{inputIndex},{fpsRatio},{outputIndex},{lowestInputIndexThisOutputImage},{highestInputIndexThisOutputImage},{countNeeded},{outputImage.appliedSourceImageCount}");
                if(outputImage.appliedSourceImageCount >= countNeeded)
                {
                    // Save it.
                    outputImage.finished = true;
                }
            }
            videoWriterARE.Set();
        }

        static Int64 videoNextOutputIndex = 0;
        static AviWriter writer = null;
        static IAviVideoStream videoStream = null;
        public static void videoWriter()
        {
            while (true)
            {
                videoWriterARE.WaitOne();
                if (forceFinish)
                {
                    break;
                }
                while (true)
                {
                    StackedFloatImage outputImage = null;
                    lock (outputimageData)
                    {
                        if (outputimageData.ContainsKey(videoNextOutputIndex))
                        {
                            outputImage = outputimageData[videoNextOutputIndex];
                        }
                    }
                    if (outputImage != null)
                    {
                        bool doSave = false;
                        float divisionFactor = 1;
                        lock (outputImage)
                        {
                            if (outputImage.finished)
                            {
                                doSave = true;
                            }
                            else if ((DateTime.Now - outputImage.lastModified).TotalSeconds > 10) // Just a safety. Ideally it won't ever happen.
                            {
                                doSave = true;
                                Console.Write("wtf");
                            }
                        }
                        if (doSave)
                        {
                            divisionFactor = Math.Max(1, outputImage.appliedSourceImageCount);
                            byte[] outputImageByteData = new byte[outputImage.imageData.Length];
                            for (int i = 0; i < outputImage.imageData.Length; i++)
                            {
                                outputImageByteData[i] = (byte)Math.Clamp(outputImage.imageData[i]/divisionFactor,0.0f,255.0f);
                            }
                            ByteImage outputByteImg = new ByteImage(outputImageByteData,outputImage.stride,outputImage.width,outputImage.height,outputImage.pixelFormat);

                            if(writer == null)
                            {
                                writer = new AviWriter(GetUnusedFilename(Path.Combine(outputFolder,"cl_avidemo.avi")))
                                {
                                    FramesPerSecond = outputFPS,
                                    // Emitting AVI v1 index in addition to OpenDML index (AVI v2)
                                    // improves compatibility with some software, including 
                                    // standard Windows programs like Media Player and File Explorer
                                    EmitIndex1 = true,
                                    MaxSuperIndexEntries = 512
                                };
                                videoStream = writer.AddVideoStream();
                                videoStream.BitsPerPixel = outputImage.pixelFormat == PixelFormat.Format32bppArgb ? BitsPerPixel.Bpp32 : BitsPerPixel.Bpp24;
                                videoStream.Width = outputImage.width;
                                videoStream.Height = outputImage.height;
                                videoStream.Codec = KnownFourCCs.Codecs.Uncompressed;
                            }
                            videoStream.WriteFrame(true, // is key frame? (many codecs use concept of key frames, for others - all frames are keys)
                            outputByteImg.imageData, // array with frame data
                            0, // starting index in the array
                            outputByteImg.imageData.Length); // length of the data

                            Console.Write("|");

                            lock (outputimageData)
                            {
                                if (outputimageData.ContainsKey(videoNextOutputIndex))
                                {
                                    outputimageData.TryRemove(videoNextOutputIndex, out _);
                                }
                            }

                            videoNextOutputIndex++;

                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                
            }
            if(writer != null)
            {
                writer.Close();
            }

        }


        
        public static string GetUnusedFilename(string baseFilename)
        {
            if (!File.Exists(baseFilename))
            {
                return baseFilename;
            }
            string extension = Path.GetExtension(baseFilename);

            int index = 1;
            while (File.Exists(Path.ChangeExtension(baseFilename, "." + (++index) + extension))) ;

            return Path.ChangeExtension(baseFilename, "." + (index) + extension);
        }


        public struct AverageHelper
        {
            public Vector3 value;
            public long divider;
        }
    }

}