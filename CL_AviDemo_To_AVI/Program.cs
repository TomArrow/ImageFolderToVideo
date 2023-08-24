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
using OpenTK.Compute.OpenCL;

namespace CL_AviDemo_To_AVI
{
    class Program
    {

        enum ProcessingType
        {
            RAW,
            LINEARIZE,
            LINEARIZE_OVERBRIGHTDARK // Recording made with overbrightbits 1 but without R_GammaCorrect
        }
        
        enum OutputTransfer
        {
            RAW,
            HDR,
            HDR_HW
        }
        

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

        static ProcessingType processingType = ProcessingType.LINEARIZE;
        static OutputTransfer outputTransfer = OutputTransfer.RAW;

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
            if (args.Length < 6)
            {

                Console.Write("Processing type (r=raw,l=linearize,lod=linearize_overbrightdark) (default linearize): ");
                string tmp = Console.ReadLine().Trim();
                switch (tmp)
                {
                    default:
                    case "l":
                    case "linearize":
                        processingType = ProcessingType.LINEARIZE;
                        break;
                    case "lod":
                    case "linearize_overbrightdark":
                        processingType = ProcessingType.LINEARIZE_OVERBRIGHTDARK;
                        break;
                    case "raw":
                    case "r":
                        processingType = ProcessingType.RAW;
                        break;
                }
            }
            else
            {
                string tmp = args[5].Trim();
                switch (tmp)
                {
                    default:
                    case "l":
                    case "linearize":
                        processingType = ProcessingType.LINEARIZE;
                        break;
                    case "lod":
                    case "linearize_overbrightdark":
                        processingType = ProcessingType.LINEARIZE_OVERBRIGHTDARK;
                        break;
                    case "raw":
                    case "r":
                        processingType = ProcessingType.RAW;
                        break;
                }
                Console.WriteLine($"Processing type: {processingType}");
            }
            if (args.Length < 7)
            {

                Console.Write("Output transfer (r=raw,h=hdr,hhw=hdr_hw) (default raw): ");
                string tmp = Console.ReadLine().Trim();
                switch (tmp)
                {
                    default:
                    case "r":
                    case "raw":
                        outputTransfer = OutputTransfer.RAW;
                        break;
                    case "h":
                    case "hdr":
                        outputTransfer = OutputTransfer.HDR;
                        break;
                    case "hhw":
                    case "hdr_hw":
                        outputTransfer = OutputTransfer.HDR_HW;
                        break;
                }
            }
            else
            {
                string tmp = args[6].Trim();
                switch (tmp)
                {
                    default:
                    case "r":
                    case "raw":
                        outputTransfer = OutputTransfer.RAW;
                        break;
                    case "h":
                    case "hdr":
                        outputTransfer = OutputTransfer.HDR;
                        break;
                    case "hhw":
                    case "hdr_hw":
                        outputTransfer = OutputTransfer.HDR_HW;
                        break;
                }
                Console.WriteLine($"Output transfer: {outputTransfer}");
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

            
            fixed (byte* srcDataPtr = img.imageData)
            fixed (float* imageDataPtr = outputImage.imageData) {
                switch (processingType)
                {
                    case ProcessingType.RAW:
                        for (int y = 0; y < outputImage.height; y++)
                        {
                            lock (outputImage.lineLocks[y]) // So we can do images in parallel, we just lock on to lines. Idk if this is actually performant tho...
                            {
                                for (int x = 0; x < outputImage.width; x++)
                                {
                                    int reverseY = outputImage.height - y - 1;
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset] += srcDataPtr[reverseY * outputImage.stride + x * pixelOffset];
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 1] += srcDataPtr[reverseY * outputImage.stride + x * pixelOffset + 1];
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 2] += srcDataPtr[reverseY * outputImage.stride + x * pixelOffset + 2];
                                }
                            }
                        }
                        break;
                    default:
                    case ProcessingType.LINEARIZE:
                        for (int y = 0; y < outputImage.height; y++)
                        {
                            lock (outputImage.lineLocks[y]) // So we can do images in parallel, we just lock on to lines. Idk if this is actually performant tho...
                            {
                                for (int x = 0; x < outputImage.width; x++)
                                {
                                    int reverseY = outputImage.height - y - 1;
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset] += linearizeSRGB(srcDataPtr[reverseY * outputImage.stride + x * pixelOffset]/255f);
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 1] += linearizeSRGB(srcDataPtr[reverseY * outputImage.stride + x * pixelOffset +1] / 255f);
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 2] += linearizeSRGB(srcDataPtr[reverseY * outputImage.stride + x * pixelOffset +2] / 255f);
                                }
                            }
                        }
                        break;
                    case ProcessingType.LINEARIZE_OVERBRIGHTDARK:
                        float finalMultiplier = 2f / 255f;
                        Vector3 color = new Vector3();
                        for (int y = 0; y < outputImage.height; y++)
                        {
                            lock (outputImage.lineLocks[y]) // So we can do images in parallel, we just lock on to lines. Idk if this is actually performant tho...
                            {
                                for (int x = 0; x < outputImage.width; x++)
                                {
                                    int reverseY = outputImage.height - y - 1;
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset] += linearizeSRGB(img.imageData[reverseY * outputImage.stride + x * pixelOffset]* finalMultiplier);
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset + 1] += linearizeSRGB(img.imageData[reverseY * outputImage.stride + x * pixelOffset +1] * finalMultiplier);
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset + 2] += linearizeSRGB(img.imageData[reverseY * outputImage.stride + x * pixelOffset +2] * finalMultiplier);
                                    color.X = srcDataPtr[reverseY * outputImage.stride + x * pixelOffset];
                                    color.Y = srcDataPtr[reverseY * outputImage.stride + x * pixelOffset +1 ];
                                    color.Z = srcDataPtr[reverseY * outputImage.stride + x * pixelOffset +2 ];
                                    color *= finalMultiplier;
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset] += (color.X > 0.04045f ? (float)Math.Pow((color.X + 0.055) / 1.055, 2.4) : color.X / 12.92f);
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset + 1] += (color.Y > 0.04045f ? (float)Math.Pow((color.Y + 0.055) / 1.055, 2.4) : color.Y / 12.92f);
                                    //imageDataPtr[y * outputImage.stride + x * pixelOffset + 2] += (color.Z > 0.04045f ? (float)Math.Pow((color.Z + 0.055) / 1.055, 2.4) : color.Z / 12.92f);
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset] += (color.X > 0.04045f ? (float)Math.Pow((color.X + 0.055) / 1.055, 2.4) : color.X / 12.92f);
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 1] += (color.Y > 0.04045f ? (float)Math.Pow((color.Y + 0.055) / 1.055, 2.4) : color.Y / 12.92f);
                                    imageDataPtr[y * outputImage.stride + x * pixelOffset + 2] += (color.Z > 0.04045f ? (float)Math.Pow((color.Z + 0.055) / 1.055, 2.4) : color.Z / 12.92f);
                                }
                            }
                        }
                        break;
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


        // HDR Conversion
        const float m1 = 1305.0f / 8192.0f;
        const float m2 = 2523.0f / 32.0f;
        const float c1 = 107.0f / 128.0f;
        const float c2 = 2413.0f / 128.0f;
        const float c3 = 2392.0f / 128.0f;
        static float pq(float input)
        {
            return (float)Math.Pow((c1 + c2 * Math.Pow(input, m1)) / (1 + c3 * Math.Pow(input, m1)), m2);
        }
        static private Matrix4x4 XYZtoRec2020Matrix = new Matrix4x4(1.7167f, -0.6667f, 0.0176f, 0, -0.3557f, 1.6165f, -0.0428f, 0, -0.2534f, 0.0158f, 0.9421f, 0, 0, 0, 0, 0);
        static private Matrix4x4 RGBtoXYZMatrix = new Matrix4x4(0.4124f, 0.2126f, 0.0193f, 0, 0.3576f, 0.7152f, 0.1192f, 0, 0.1805f, 0.0722f, 0.9505f, 0, 0, 0, 0, 0);
        static private Matrix4x4 RGBtoRec2020Matrix = RGBtoXYZMatrix * XYZtoRec2020Matrix;
        static private Matrix4x4 RGBtoRec2020MatrixCent = RGBtoRec2020Matrix * 0.01f;


        
        private static float linearizeSRGB(float n)
        {
            return (n > 0.04045f ? (float)Math.Pow((n + 0.055) / 1.055, 2.4) : n / 12.92f);
        }
        private static float delinearizeSRGB(float n)
        {
            return n > 0.0031308f ? 1.055f * (float)Math.Pow(n, 1 / 2.4) - 0.055f : 12.92f * n;
        }

        static bool TKIsInitialized = false;
        static Int64 videoNextOutputIndex = 0;
        static AviWriter writer = null;
        static IAviVideoStream videoStream = null;
        public unsafe static void videoWriter()
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
                            if(outputTransfer == OutputTransfer.HDR)
                            {
                                int pixelOffset = outputImage.pixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;

                                // HDR
                                if (processingType == ProcessingType.RAW)
                                {
                                    /*float inverse255 = 1f / 255f;
                                    float inverseDivisionFactor = 1f / divisionFactor;
                                    Matrix4x4 finalMatrix = RGBtoRec2020MatrixCent * inverseDivisionFactor;
                                    Vector3 color = new Vector3();
                                    for (int i = 0; i < outputImage.imageData.Length; i += pixelOffset)
                                    {
                                        color.X = outputImage.imageData[i];
                                        color.Y = outputImage.imageData[i + 1];
                                        color.Z = outputImage.imageData[i + 2];
                                        color *= inverseDivisionFactor;
                                        color.X = linearizeSRGB(color.X);
                                        color.Y = linearizeSRGB(color.Y);
                                        color.Z = linearizeSRGB(color.Z);
                                        color = Vector3.Transform(color, finalMatrix);

                                        outputImageByteData[i] = (byte)Math.Clamp(pq(color.X) * 255f, 0.0f, 255.0f);
                                        outputImageByteData[i + 1] = (byte)Math.Clamp(pq(color.Y) * 255f, 0.0f, 255.0f);
                                        outputImageByteData[i + 2] = (byte)Math.Clamp(pq(color.Z) * 255f, 0.0f, 255.0f);
                                    }*/
                                    throw new Exception("HDR output transfer does not work with raw processing type.");
                                }
                                else // Linearized
                                {

                                    fixed (float* outputImageFloatDataPtr = outputImage.imageData)
                                    fixed (byte* outputImageByteDataPtr = outputImageByteData)
                                    {
                                        float inverseDivisionFactor = 1f / divisionFactor;
                                        Matrix4x4 finalMatrix = RGBtoRec2020MatrixCent * inverseDivisionFactor;
                                        Vector3 color = new Vector3();

                                        for (int i = 0; i < outputImage.imageData.Length; i += pixelOffset)
                                        {
                                            color.X = outputImageFloatDataPtr[i];
                                            color.Y = outputImageFloatDataPtr[i + 1];
                                            color.Z = outputImageFloatDataPtr[i + 2];
                                            //color *= 0.01f/divisionFactor;
                                            //color *= inverseDivisionFactor;
                                            //color = Vector3.Transform(color, RGBtoXYZMatrix);
                                            //color = Vector3.Transform(color, XYZtoRec2020Matrix);
                                            color = Vector3.Transform(color, finalMatrix);

                                            outputImageByteDataPtr[i] = (byte)Math.Clamp(pq(color.X) * 255f, 0.0f, 255.0f);
                                            outputImageByteDataPtr[i + 1] = (byte)Math.Clamp(pq(color.Y) * 255f, 0.0f, 255.0f);
                                            outputImageByteDataPtr[i + 2] = (byte)Math.Clamp(pq(color.Z) * 255f, 0.0f, 255.0f);
                                        }
                                    }
                                }
                            } else if(outputTransfer == OutputTransfer.HDR_HW)
                            {
                                int pixelOffset = outputImage.pixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;

                                // HDR
                                if (processingType == ProcessingType.RAW)
                                {
                                    /*float inverse255 = 1f / 255f;
                                    float inverseDivisionFactor = 1f / divisionFactor;
                                    Matrix4x4 finalMatrix = RGBtoRec2020MatrixCent * inverseDivisionFactor;
                                    Vector3 color = new Vector3();
                                    for (int i = 0; i < outputImage.imageData.Length; i += pixelOffset)
                                    {
                                        color.X = outputImage.imageData[i];
                                        color.Y = outputImage.imageData[i + 1];
                                        color.Z = outputImage.imageData[i + 2];
                                        color *= inverseDivisionFactor;
                                        color.X = linearizeSRGB(color.X);
                                        color.Y = linearizeSRGB(color.Y);
                                        color.Z = linearizeSRGB(color.Z);
                                        color = Vector3.Transform(color, finalMatrix);

                                        outputImageByteData[i] = (byte)Math.Clamp(pq(color.X) * 255f, 0.0f, 255.0f);
                                        outputImageByteData[i + 1] = (byte)Math.Clamp(pq(color.Y) * 255f, 0.0f, 255.0f);
                                        outputImageByteData[i + 2] = (byte)Math.Clamp(pq(color.Z) * 255f, 0.0f, 255.0f);
                                    }*/
                                    throw new Exception("HDR output transfer does not work with raw processing type.");
                                }
                                else // Linearized
                                {
                                    if (!TKIsInitialized)
                                    {
                                        OpenTKHDRConvert.prepareTK(outputImage.imageData.Length, DeviceType.Gpu);
                                        TKIsInitialized = true;
                                    }
                                    outputImageByteData = OpenTKHDRConvert.ConvertToHDR(outputImage.imageData, pixelOffset, (int)Math.Max(1, outputImage.appliedSourceImageCount));

                                    /*
                                    float inverseDivisionFactor = 1f / divisionFactor;
                                    Matrix4x4 finalMatrix = RGBtoRec2020MatrixCent * inverseDivisionFactor;
                                    Vector3 color = new Vector3();

                                    for (int i = 0; i < outputImage.imageData.Length; i+= pixelOffset)
                                    {
                                        color.X = outputImageFloatDataPtr[i];
                                        color.Y = outputImageFloatDataPtr[i+1];
                                        color.Z = outputImageFloatDataPtr[i+2];
                                        //color *= 0.01f/divisionFactor;
                                        //color *= inverseDivisionFactor;
                                        //color = Vector3.Transform(color, RGBtoXYZMatrix);
                                        //color = Vector3.Transform(color, XYZtoRec2020Matrix);
                                        color = Vector3.Transform(color, finalMatrix);

                                        outputImageByteDataPtr[i] = (byte)Math.Clamp(pq(color.X)*255f, 0.0f, 255.0f);
                                        outputImageByteDataPtr[i+1] = (byte)Math.Clamp(pq(color.Y) * 255f, 0.0f, 255.0f);
                                        outputImageByteDataPtr[i+2] = (byte)Math.Clamp(pq(color.Z) * 255f, 0.0f, 255.0f);
                                    }*/
                                }
                            } else
                            {
                                // RAW
                                if (processingType == ProcessingType.RAW)
                                {
                                    for (int i = 0; i < outputImage.imageData.Length; i++)
                                    {
                                        outputImageByteData[i] = (byte)Math.Clamp(outputImage.imageData[i] / divisionFactor, 0.0f, 255.0f);
                                    }
                                }
                                else
                                {
                                    float extraMult = processingType == ProcessingType.LINEARIZE_OVERBRIGHTDARK ? 0.5f : 1.0f;
                                    for (int i = 0; i < outputImage.imageData.Length; i++)
                                    {
                                        outputImageByteData[i] = (byte)Math.Clamp(delinearizeSRGB(outputImage.imageData[i] / divisionFactor) * 255f * extraMult, 0.0f, 255.0f);
                                    }
                                }
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