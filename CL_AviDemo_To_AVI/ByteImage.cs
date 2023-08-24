using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CL_AviDemo_To_AVI
{
    class StackedFloatImage
    {
        public float[] imageData;
        public int stride;
        public int width, height;
        public PixelFormat pixelFormat;
        public object[] lineLocks = null;
        public Int64 appliedSourceImageCount = 0;
        public bool finished = false;
        public DateTime lastModified = DateTime.Now;

        public StackedFloatImage(int strideA, int widthA, int heightA, PixelFormat pixelFormatA)
        {
            imageData = new float[strideA * heightA];
            stride = strideA;
            width = widthA;
            height = heightA;
            lineLocks = new object[height];
            pixelFormat = pixelFormatA;
            for (int i=0;i < height; i++)
            {
                lineLocks[i] = new object();
            }
        }

        public int Length
        {
            get { return imageData.Length; }
        }

        public float this[int index]
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
    }
    class ByteImage
    {
        public byte[] imageData;
        public int stride;
        public int width, height;
        public PixelFormat pixelFormat;

        public ByteImage(byte[] imageDataA, int strideA, int widthA, int heightA, PixelFormat pixelFormatA)
        {
            imageData = imageDataA;
            stride = strideA;
            width = widthA;
            height = heightA;
            pixelFormat = pixelFormatA;
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

        public static ByteImage FromBitmap(Bitmap bmp)
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

            return new ByteImage(rgbValues, stride, bmp.Width, bmp.Height, bmp.PixelFormat);
        }

        public Bitmap ToBitmap()
        {
            ByteImage byteImage = this;
            Bitmap myBitmap = new Bitmap(byteImage.width, byteImage.height, byteImage.pixelFormat);
            Rectangle rect = new Rectangle(0, 0, myBitmap.Width, myBitmap.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                myBitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                myBitmap.PixelFormat);

            bmpData.Stride = byteImage.stride;

            IntPtr ptr = bmpData.Scan0;
            System.Runtime.InteropServices.Marshal.Copy(byteImage.imageData, 0, ptr, byteImage.imageData.Length);

            myBitmap.UnlockBits(bmpData);
            return myBitmap;

        }
    }
}
