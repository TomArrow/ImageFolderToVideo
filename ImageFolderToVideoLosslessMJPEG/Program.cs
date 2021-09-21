using SharpAvi;
using SharpAvi.Output;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using ParallelAssemblyLineNET;

namespace ImageFolderToVideoLosslessMJPEG
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
            public bool IsValid;
            public int Width;
            public int Height;
            public byte[] imageData;
        }

        static void Main(string[] args)
        {
            //string folder = args.Length > 0 ? args[0] : "";

            foreach(string arg in args)
            {
                doFolder(arg);
            }

            string[] foldersToDo = File.ReadAllLines("foldersToProcess.txt");
            foreach(string folderToDo in foldersToDo)
            {
                string[] tmp = folderToDo.Split(";");
                doFolder(tmp[1], tmp[0]);
            }

            Console.ReadKey();
        }

        public static void doFolder(string folder,string prefix="output")
        {
            string[] files = Directory.GetFiles(folder);

            Dictionary<string, WriterStreamPair> writers = new Dictionary<string, WriterStreamPair>();

            /*AviWriter writer = new AviWriter(GetUnusedFilename("test.avi"))
            {
                FramesPerSecond = 30,
                // Emitting AVI v1 index in addition to OpenDML index (AVI v2)
                // improves compatibility with some software, including 
                // standard Windows programs like Media Player and File Explorer
                EmitIndex1 = true
            };
            IAviVideoStream stream = writer.AddVideoStream();
            stream.Width = 845;
            stream.Height = 615;
            stream.Codec = KnownFourCCs.Codecs.MotionJpeg;
            */


            //bool readSuccessful = false;
            //int index = 0;



            ParallelAssemblyLine.Run<ImageWrapper, ImageWrapper>(

                // Image reader
                (long index) => {

                    if (index < files.Length)
                    {
                        string file = files[index];
                        string ext = Path.GetExtension(file).ToLower();
                        if (ext != ".jpg" && ext != ".jpeg")
                        {
                            return new ImageWrapper { imageData = null, filename = null };
                        }
                        return new ImageWrapper { imageData = File.ReadAllBytes(files[index]), filename = files[index] };
                    }
                    else
                    {
                        return null;
                    }
                },

            // Image integrity checker (multithreaded)
            (ImageWrapper input, long index) => {
                bool readSuccessful = false;
                if (input.imageData != null)
                {
                    readSuccessful = true;
                    try
                    {
                        Image image = null;
                        using (MemoryStream ms = new MemoryStream(input.imageData))
                        {

                            image = Image.FromStream(ms);
                            input.Width = image.Width;
                            input.Height = image.Height;
                        }
                        image.Dispose();
                    }
                    catch (Exception e)
                    {
                        readSuccessful = false;
                    }

                    if (!readSuccessful)
                    {
                        Console.WriteLine("File " + input.filename + " could not be read.");
                        input.IsValid = false;
                    }
                    else
                    {
                        Console.WriteLine("File " + input.filename + " read.");
                        input.IsValid = true;
                    }

                }

                return input;
            },


            // Image writer.
            (ImageWrapper image, long index) => {

                if (!image.IsValid || image.imageData == null)
                {
                    return;
                }

                string resolutionKey = image.Width + "x" + image.Height;

                if (!writers.ContainsKey(resolutionKey))
                {
                    WriterStreamPair wsp = new WriterStreamPair();
                    wsp.writer = new AviWriter(GetUnusedFilename(prefix+"-" + resolutionKey + ".avi"))
                    {
                        FramesPerSecond = 30,
                        // Emitting AVI v1 index in addition to OpenDML index (AVI v2)
                        // improves compatibility with some software, including 
                        // standard Windows programs like Media Player and File Explorer
                        EmitIndex1 = true
                    };
                    wsp.stream = wsp.writer.AddVideoStream();
                    wsp.stream.Width = image.Width;
                    wsp.stream.Height = image.Height;
                    wsp.stream.Codec = KnownFourCCs.Codecs.MotionJpeg;

                    wsp.statsCSV = new StreamWriter(GetUnusedFilename(prefix + "-" + resolutionKey + ".avi.stats.csv"));
                    wsp.timecodes = new StreamWriter(GetUnusedFilename(prefix + "-" + resolutionKey + ".avi.timecodes.txt"));
                    wsp.deleteFilesScript = new StreamWriter(GetUnusedFilename(prefix + "-" + resolutionKey + ".avi.deleteOriginals.sh"));

                    wsp.statsCSV.WriteLine("frameNumber;originalFilename;timestamp;absoluteFilename");
                    wsp.timecodes.WriteLine("# timecode format v2");
                    wsp.deleteFilesScript.WriteLine("#!/bin/bash");
                    wsp.deleteFilesScript.WriteLine("echo \"Delete original files? Press any key, twice, to continue.\"");
                    wsp.deleteFilesScript.WriteLine("read -n1 -r");
                    wsp.deleteFilesScript.WriteLine("read -n1 -r");

                    writers.Add(resolutionKey, wsp);
                }

                WriterStreamPair wasp = writers[resolutionKey];
                wasp.stream.WriteFrame(true, // is key frame? (many codecs use concept of key frames, for others - all frames are keys)
                    image.imageData, // array with frame data
                    0, // starting index in the array
                    image.imageData.Length); // length of the data

                long? timestampParseTry = null;
                string possibleTimeStamp = Path.GetFileNameWithoutExtension(image.filename);
                long tmp;
                if(long.TryParse(possibleTimeStamp,out tmp))
                {
                    timestampParseTry = tmp;
                }

                wasp.deleteFilesScript.WriteLine("rm \""+ image.filename + "\"");
                wasp.statsCSV.WriteLine((wasp.stream.FramesWritten-1).ToString()+";"+ Path.GetFileName(image.filename) + ";" + timestampParseTry.ToString()+";"+ image.filename);
                wasp.timecodes.WriteLine(timestampParseTry.ToString()+".00");

            });

            foreach (WriterStreamPair writer in writers.Values)
            {
                writer.writer.Close();
                writer.statsCSV.Dispose();
                writer.timecodes.Dispose();
                writer.deleteFilesScript.WriteLine("read -n1 -r");
                writer.deleteFilesScript.Dispose();
            }

            /*
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLower();
                if(ext != ".jpg" && ext != ".jpeg")
                {
                    continue;
                }

                readSuccessful = true;
                try
                {
                    image = Image.FromFile(file);
                }
                catch (Exception e)
                {
                    readSuccessful = false;
                }
                if (!readSuccessful)
                {
                    Console.WriteLine("File " + file + " could not be read.");
                    continue;
                }
                else
                {
                    Console.WriteLine("File " + file + " read.");
                }

                //scaledImage = new Bitmap(image,new Size(1920,1080),);
                byte[] imageData = File.ReadAllBytes(file);


                stream.WriteFrame(true, // is key frame? (many codecs use concept of key frames, for others - all frames are keys)
                    imageData, // array with frame data
                    0, // starting index in the array
                    imageData.Length); // length of the data
             



                image.Dispose();
                //scaledImage.Dispose();

                index++;

                //if (index > 100) break;
            }*/
            //writer.Close();
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