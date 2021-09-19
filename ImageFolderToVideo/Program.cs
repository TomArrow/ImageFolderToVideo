using Accord.Video.FFMPEG;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ImageFolderToVideo
{
    class Program
    {

        //static int theTargetWidth = 1080, theTargetHeight = 1080;
        static int theTargetWidth = 256, theTargetHeight = 256;
        static bool doGifMultiFrame = false;

        static void Main(string[] args)
        {
            string folder = args.Length > 0 ? args[0] : "";
            folder = @"test";

            string[] files = Directory.GetFiles(folder);

            VideoFileWriter writer = new VideoFileWriter();
            writer.Open(GetUnusedFilename("test2.avi"), theTargetWidth, theTargetHeight, 30, VideoCodec.FFV1);

            Image image = null;
            Image scaledImage = null;
            bool readSuccessful = false;
            int index = 0;
            foreach(string file in files)
            {

                readSuccessful = true;
                try
                {
                    image = Image.FromFile(file);
                } catch (Exception e)
                {
                    readSuccessful = false;
                }
                if (!readSuccessful)
                {
                    Console.WriteLine("File " + file + " could not be read.");
                    continue;
                } else
                {
                    Console.WriteLine("File " + file + " read.");
                }

                //scaledImage = new Bitmap(image,new Size(1920,1080),);

                FrameDimension dim = new FrameDimension(image.FrameDimensionsList[0]);
                int frameCount = image.GetFrameCount(dim);

                if (!doGifMultiFrame)
                {
                    frameCount = 1;
                }
                for(int i = 0; i < frameCount; i++)
                {
                    image.SelectActiveFrame(dim, i);
                    //scaledImage = scaleImage(image, theTargetWidth, theTargetHeight);

                    //writer.WriteVideoFrame((Bitmap)scaledImage);
                    writer.WriteVideoFrame((Bitmap)image);
                }

                

                image.Dispose();
                //scaledImage.Dispose();

                index++;

                //if (index > 100) break;
            }
            writer.Close();
        }


        
        private static Image scaleImage(Image orig,int width,int height,bool cropBorders=true,bool preserveAspect= true)
        {
            // First convert to 24bit for unified addressing of pixels
            Bitmap in24bit = new Bitmap(orig.Width, orig.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (Graphics gr = Graphics.FromImage(in24bit))
            {
                gr.DrawImage(orig, new Rectangle(0, 0, in24bit.Width, in24bit.Height));
            }

            LinearAccessByteImageUnsignedNonVectorized img = LinearAccessByteImageUnsignedNonVectorized.FromBitmap(in24bit);

            int[] currentBorders = new int[] { 0, 0, orig.Width - 1, orig.Height - 1 };

            // Check if the image by any chance is just one flat color. In which case cropping makes no sense and we just leave it be.
            Vector3 topLeft, bottomRight;
            topLeft = img[0, 0];
            bool imageIsFlat = true;
            for (int y =0;y<orig.Height;y++)
            {
                for (int x = 0; x < orig.Width; x++)
                {
                    if(img[x,y] != topLeft)
                    {
                        imageIsFlat = false;
                        goto FlatDetectionFinished; // Not elegant but idc
                    }
                }
            }
            FlatDetectionFinished:

            if (cropBorders && !imageIsFlat)
            {
                int[] previousBorders = currentBorders;
                bool changeOcurred = true;
                bool tmpCleanBorderLine = false;
                bool thatsIllegal = false; // This basically triggers if the resulting cropped image ends up being zero pixels in one dimension, which obviously makes no sense. This would happen for flat image (already accounted for) or for images that contain nothing but perfect stripes. In such a case, we just avoid any cropping at all.
                while (changeOcurred)
                {
                    changeOcurred = false;


                    topLeft = img[currentBorders[0], currentBorders[1]];
                    bottomRight = img[currentBorders[2], currentBorders[3]];

                    // top
                    tmpCleanBorderLine = true;
                    for(int i = currentBorders[0]; i <= currentBorders[2]; i++)
                    {
                        if(topLeft != img[i, currentBorders[1]])
                        {
                            tmpCleanBorderLine = false;
                        }
                    }
                    if (tmpCleanBorderLine)
                    {
                        currentBorders[1]++; changeOcurred = true;
                    }
                    if(currentBorders[2] - currentBorders[0] < 1 || currentBorders[3]-currentBorders[1] < 1)
                    {
                        thatsIllegal = true;
                        currentBorders = previousBorders;
                        break;
                    }

                    topLeft = img[currentBorders[0], currentBorders[1]];
                    bottomRight = img[currentBorders[2], currentBorders[3]];
                    // bottom
                    tmpCleanBorderLine = true;
                    for (int i = currentBorders[0]; i <= currentBorders[2]; i++)
                    {
                        if (bottomRight != img[i, currentBorders[3]])
                        {
                            tmpCleanBorderLine = false;
                        }
                    }
                    if (tmpCleanBorderLine)
                    {
                        currentBorders[3]--; changeOcurred = true;
                    }
                    if (currentBorders[2] - currentBorders[0] < 1 || currentBorders[3] - currentBorders[1] < 1)
                    {
                        thatsIllegal = true;
                        currentBorders = previousBorders;
                        break;
                    }

                    topLeft = img[currentBorders[0], currentBorders[1]];
                    bottomRight = img[currentBorders[2], currentBorders[3]];
                    // left
                    tmpCleanBorderLine = true;
                    for (int i = currentBorders[1]; i <= currentBorders[3]; i++)
                    {
                        if (topLeft != img[currentBorders[0], i])
                        {
                            tmpCleanBorderLine = false;
                        }
                    }
                    if (tmpCleanBorderLine)
                    {
                        currentBorders[0]++; changeOcurred = true;
                    }
                    if (currentBorders[2] - currentBorders[0] < 1 || currentBorders[3] - currentBorders[1] < 1)
                    {
                        thatsIllegal = true;
                        currentBorders = previousBorders;
                        break;
                    }

                    topLeft = img[currentBorders[0], currentBorders[1]];
                    bottomRight = img[currentBorders[2], currentBorders[3]];
                    // right
                    tmpCleanBorderLine = true;
                    for (int i = currentBorders[1]; i <= currentBorders[3]; i++)
                    {
                        if (bottomRight != img[currentBorders[2], i])
                        {
                            tmpCleanBorderLine = false;
                        }
                    }
                    if (tmpCleanBorderLine)
                    {
                        currentBorders[2]--; changeOcurred = true;
                    }
                    if (currentBorders[2] - currentBorders[0] < 1 || currentBorders[3] - currentBorders[1] < 1)
                    {
                        thatsIllegal = true;
                        currentBorders = previousBorders;
                        break;
                    }
                }
                
            }


            // Now lets find out the average border color for filling the background
            AverageHelper averageBorderColorHelper = new AverageHelper();
            // top
            for (int i = currentBorders[0]+1; i <= currentBorders[2]-1; i++)
            {
                averageBorderColorHelper.value += img[i, currentBorders[1]];
                averageBorderColorHelper.divider++;
            }
            // bottom
            for (int i = currentBorders[0]+1; i <= currentBorders[2]-1; i++)
            {
                averageBorderColorHelper.value += img[i, currentBorders[3]];
                averageBorderColorHelper.divider++;
            }
            // left
            for (int i = currentBorders[1]; i <= currentBorders[3]; i++)
            {
                averageBorderColorHelper.value += img[currentBorders[0], i];
                averageBorderColorHelper.divider++;
            }
            // right
            for (int i = currentBorders[1]; i <= currentBorders[3]; i++)
            {
                averageBorderColorHelper.value += img[currentBorders[2], i];
                averageBorderColorHelper.divider++;
            }

            Vector3 averageBorderColor = averageBorderColorHelper.value / averageBorderColorHelper.divider;

            img = null;

            // preserve aspect ratio
            double aspectRatioTarget = (double)width/(double)height;
            double aspectRatio = ((double)currentBorders[2] - (double)currentBorders[0]) / (double)(currentBorders[3] - (double)currentBorders[1]);
            Rectangle target = target = new Rectangle(0, 0, width, height);
            if (aspectRatio == aspectRatioTarget)
            {
                // Already ok then.
            } else if (aspectRatio > aspectRatioTarget)
            {
                int targetHeight = (int)Math.Round((double)width / aspectRatio);
                // Letterboxing
                target = new Rectangle(0,(height-targetHeight)/2,width,targetHeight);
            } else if (aspectRatio < aspectRatioTarget)
            {
                // Pillarboxing

                int targetWidth = (int)Math.Round((double)height * aspectRatio);
                target = new Rectangle((width-targetWidth)/2,0, targetWidth, height);
            }


            Bitmap clone = new Bitmap(width, height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (Graphics gr = Graphics.FromImage(clone))
            {
                //gr.Clear(Color.FromArgb((int)Math.Round(averageBorderColor.Z),                    (int)Math.Round(averageBorderColor.Y),                    (int)Math.Round(averageBorderColor.X)));
                //gr.Clear(Color.FromArgb(128,128,128));
                gr.Clear(Color.White);
                gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gr.SmoothingMode = SmoothingMode.HighQuality;
                gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gr.CompositingQuality = CompositingQuality.HighQuality;
                gr.DrawImage(in24bit, target, new Rectangle(currentBorders[0],currentBorders[1],currentBorders[2]- currentBorders[0], currentBorders[3]- currentBorders[1]),GraphicsUnit.Pixel);
            
            }
            in24bit.Dispose();

            return clone;
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
    }
    public struct AverageHelper
    {
        public Vector3 value;
        public long divider;
    }
}
