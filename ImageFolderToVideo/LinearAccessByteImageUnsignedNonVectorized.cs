using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ImageFolderToVideo
{
    public class LinearAccessByteImageUnsignedNonVectorized
    {
        public byte[] imageData;
        public int stride;
        public int width, height;
        public PixelFormat pixelFormat;
        public PixelFormat originalPixelFormat;
        public int originalStride;

        public LinearAccessByteImageUnsignedNonVectorized(byte[] linearData, LinearAccessByteImageUnsignedHusk husk)
        {
            stride = husk.stride;
            width = husk.width;
            height = husk.height;
            pixelFormat = husk.pixelFormat;
            originalPixelFormat = husk.originalPixelFormat;
            originalStride = husk.originalStride;
            imageData = linearData;
        }

        public LinearAccessByteImageUnsignedNonVectorized(byte[] imageDataA, int strideA, int widthA, int heightA, int channelMultiplier = 3)
        {
            originalStride = strideA;
            width = widthA;
            height = heightA;
            pixelFormat = PixelFormat.Format24bppRgb;

            if (channelMultiplier == 3)
            {

                originalPixelFormat = PixelFormat.Format24bppRgb;
            }
            else if (channelMultiplier == 4)
            {
                originalPixelFormat = PixelFormat.Format32bppArgb;
            }

            // bc thats what were gonna be using for the calculations. 2 bc we need to use widen to go from byte to short and that creates 2 vectors.
            // we can ignore the bit of empty bytes being compared because they will just be zeros being compared to zeros and adding extra ifs to check they're not being compared would likely just slow down things more.
            int vectorCountForMultiplication = Vector<short>.Count * 2;

            int pixelCount = width * height * 3;
            //int pixelCountDivisibleByVectorSize = (int)(vectorCountForMultiplication * Math.Ceiling((double)pixelCount / (double)vectorCountForMultiplication));

            imageData = new byte[pixelCount]; // We're not actually going to be using the extra pixels for anything useful, it's just to avoid memory overflow when reading from the array

            int strideHere, linearHere;
            for (int y = 0; y < height; y++)
            {
                strideHere = y * strideA;
                linearHere = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    imageData[linearHere + x * 3] =(imageDataA[strideHere + x * channelMultiplier] );
                    imageData[linearHere + x * 3 + 1] = (imageDataA[strideHere + x * channelMultiplier + 1] );
                    imageData[linearHere + x * 3 + 2] = (imageDataA[strideHere + x * channelMultiplier + 2] );
                }
            }

            stride = width;

            //imageData = imageDataA;
        }
        public LinearAccessByteImageUnsignedNonVectorized(byte[] imageDataA, int strideA, int widthA, int heightA, PixelFormat pixelFormatA)
        {
            originalStride = strideA;
            width = widthA;
            height = heightA;
            pixelFormat = PixelFormat.Format24bppRgb;
            originalPixelFormat = pixelFormatA;

            // bc thats what were gonna be using for the calculations. 2 bc we need to use widen to go from byte to short and that creates 2 vectors.
            // we can ignore the bit of empty bytes being compared because they will just be zeros being compared to zeros and adding extra ifs to check they're not being compared would likely just slow down things more.
            int vectorCountForMultiplication = Vector<short>.Count * 2;

            int pixelCount = width * height * 3;
            //int pixelCountDivisibleByVectorSize = (int)(vectorCountForMultiplication * Math.Ceiling((double)pixelCount / (double)vectorCountForMultiplication));

            imageData = new byte[pixelCount]; // We're not actually going to be using the extra pixels for anything useful, it's just to avoid memory overflow when reading from the array

            int channelMultiplier = 3;
            if (pixelFormatA == PixelFormat.Format32bppArgb)
            {
                channelMultiplier = 4;
            }

            int strideHere, linearHere;
            for (int y = 0; y < height; y++)
            {
                strideHere = y * strideA;
                linearHere = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    imageData[linearHere + x * 3] = (imageDataA[strideHere + x * channelMultiplier] );
                    imageData[linearHere + x * 3 + 1] = (imageDataA[strideHere + x * channelMultiplier + 1] );
                    imageData[linearHere + x * 3 + 2] = (imageDataA[strideHere + x * channelMultiplier + 2] );
                }
            }

            stride = width;

            //imageData = imageDataA;
        }

        public byte[] getOriginalDataReconstruction()
        {

            int pixelCount = width * height * 3;

            //int widthStrideDifference = stride - width;

            int channelMultiplier = 3;
            if (originalPixelFormat == PixelFormat.Format32bppArgb)
            {
                channelMultiplier = 4;
            }
            byte[] output = new byte[height * originalStride];

            int strideHere, linearHere;
            for (int y = 0; y < height; y++)
            {
                strideHere = y * originalStride;
                linearHere = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    output[strideHere + x * channelMultiplier] = (imageData[linearHere + x * 3] );
                    output[strideHere + x * channelMultiplier + 1] = (imageData[linearHere + x * 3 + 1] );
                    output[strideHere + x * channelMultiplier + 2] = (imageData[linearHere + x * 3 + 2] );
                    if (channelMultiplier == 4)
                    {
                        output[strideHere + x * channelMultiplier + 3] = (byte)255;
                    }
                }
            }
            return output;
        }


        public LinearAccessByteImageUnsignedHusk toHusk()
        {
            return new LinearAccessByteImageUnsignedHusk(this);
        }

        public int Length
        {
            get { return imageData.Length; }
        }

        public byte this[int index]
        {
            get
            {
                return imageData[index];
            }

            set
            {
                imageData[index] = value;
            }
        }

        public Vector3 this[int x,int y]
        {
            get
            {
                return new Vector3() { X = imageData[y * width*3 + x * 3] , Y = imageData[y * width * 3 + x * 3 + 1] , Z = imageData[y * width * 3 + x * 3 + 2] };
            }

        }
		
		
		public Bitmap ToBitmap()
        {
            Bitmap myBitmap = new Bitmap(this.width, this.height, this.originalPixelFormat);
            Rectangle rect = new Rectangle(0, 0, myBitmap.Width, myBitmap.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                myBitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                myBitmap.PixelFormat);

            bmpData.Stride = this.originalStride;

            IntPtr ptr = bmpData.Scan0;
            byte[] originalDataReconstruction = this.getOriginalDataReconstruction();
            System.Runtime.InteropServices.Marshal.Copy(originalDataReconstruction, 0, ptr, originalDataReconstruction.Length);

            myBitmap.UnlockBits(bmpData);
            return myBitmap;

        }
		
		public static LinearAccessByteImageUnsignedNonVectorized FromBitmap(Bitmap bmp)
        {

            // Lock the bitmap's bits.  
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                bmp.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int stride = Math.Abs(bmpData.Stride);
            int bytes = stride * bmp.Height;
            byte[] rgbValues = new byte[bytes];

            // Copy the RGB values into the array.
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            bmp.UnlockBits(bmpData);

            return new LinearAccessByteImageUnsignedNonVectorized(rgbValues, stride, bmp.Width, bmp.Height, bmp.PixelFormat);
        }
    }

    // Like an actual byteimage but without any actual content. For storing info about dimensions etc. of an image
    public class LinearAccessByteImageUnsignedHusk
    {

        public int stride;
        public int width, height;
        public PixelFormat pixelFormat;
        public PixelFormat originalPixelFormat;
        public int originalStride;
        public LinearAccessByteImageUnsignedHusk(LinearAccessByteImageUnsignedNonVectorized referenceImage)
        {
            stride = referenceImage.stride;
            width = referenceImage.width;
            height = referenceImage.height;
            pixelFormat = referenceImage.pixelFormat;
            originalPixelFormat = referenceImage.originalPixelFormat;
            originalStride = referenceImage.originalStride;
        }
    }
}
