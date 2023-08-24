using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Compute.OpenCL;
using OpenTK.Graphics.OpenGL;

namespace CL_AviDemo_To_AVI
{
    class OpenTKHDRConvert
    {
        public static void exceptIfError(CLResultCode res, string errorMessage = null)
        {
            if (res != CLResultCode.Success)
            {
                throw new Exception($"Error with errorCode {res}. Details if available: {errorMessage}");
            }
        }

        static CLContext context;
        static CLProgram program;
        static CLKernel kernel;
        static CLDevice? deviceToUse = null;
        static CLBuffer buffer;
        static CLBuffer bufferOutput;
        static CLCommandQueue queue;

        public static void prepareTK(int Length, DeviceType deviceTypeToUse)
        {
            CLResultCode res;

            CLPlatform[] platforms;
            CL.GetPlatformIds(out platforms);


#if DEBUG
            Console.WriteLine("DEBUG BUILD");
#endif

            Console.WriteLine("Devices:");

            bool priorityPicked = false;

            foreach (CLPlatform platform in platforms)
            {


                byte[] platformName;
                CL.GetPlatformInfo(platform, PlatformInfo.Name, out platformName);
                string platformNameString = System.Text.Encoding.Default.GetString(platformName);

                CLDevice[] devices;
                CL.GetDeviceIds(platform, DeviceType.All, out devices);

                foreach (CLDevice device in devices)
                {
                    byte[] deviceName;
                    byte[] deviceType;
                    CL.GetDeviceInfo(device, DeviceInfo.Name, out deviceName);
                    CL.GetDeviceInfo(device, DeviceInfo.Type, out deviceType);
                    string deviceNameString = System.Text.Encoding.Default.GetString(deviceName);
                    UInt64 deviceTypeNum = BitConverter.ToUInt64(deviceType);//TODO Is this portable?
                    Console.Write(platformNameString);
                    Console.Write(": ");
                    Console.Write(deviceNameString);
                    Console.Write(" (");
                    Console.Write((DeviceType)deviceTypeNum);
                    Console.Write(")");

                    if ((DeviceType)deviceTypeNum == deviceTypeToUse && !priorityPicked)
                    {
                        deviceToUse = device;
                        if (deviceNameString.Contains("NVIDIA"))
                        {
                            Console.Write(" - priority pick (for real this time)!\n");

                            priorityPicked = true;
                        }
                    }
                    Console.Write("\n");
                }
            }

            if (deviceToUse == null)
            {
                throw new Exception("OpenCL device selection failure (no GPU found)");
            }

            context = CL.CreateContext(IntPtr.Zero, 1, new CLDevice[] { deviceToUse.Value }, IntPtr.Zero, IntPtr.Zero, out res);

            exceptIfError(res, "Error creating context");

            string kernelCode = @"
                    __kernel void ToHDR(__global float* input, __global unsigned char* output, int pixelOffset, int divideCount)
                    {
                        int gid = get_global_id(0);

                        const float m1 = 1305.0f / 8192.0f;
	                    const float m2 = 2523.0f / 32.0f;
	                    const float c1 = 107.0f / 128.0f;
	                    const float c2 = 2413.0f / 128.0f;
	                    const float c3 = 2392.0f / 128.0f;                        

                        float v1 = 0.01f*input[gid*pixelOffset]/(float)divideCount;
                        float v2 = 0.01f*input[gid*pixelOffset+1]/(float)divideCount;
                        float v3 = 0.01f*input[gid*pixelOffset+2]/(float)divideCount;

                        float t1 = v1 * 0.627441372057979+ v2 *0.329297459521910 + v3 * 0.043351458394495;
                        float t2 = v1 * 0.069027617147078+ v2 *0.919580666887028 + v3 * 0.011361422575401;
                        float t3 = v1 * 0.016364235071681+ v2 *0.088017162471727 + v3 * 0.895564972725983;

                        t1 = pow((c1 + c2 * pow(t1, m1)) / (1.0f + c3 * pow(t1, m1)), m2);
                        t2 = pow((c1 + c2 * pow(t2, m1)) / (1.0f + c3 * pow(t2, m1)), m2);
                        t3 = pow((c1 + c2 * pow(t3, m1)) / (1.0f + c3 * pow(t3, m1)), m2);

                        /*const mat3 srgbToHDR = mat3(0.627441372057979,  0.329297459521910,  0.043351458394495,0.069027617147078,  0.919580666887028 , 0.011361422575401,0.016364235071681 , 0.088017162471727,  0.895564972725983);
	
	                    const vec3 charMult = vec3(255.0f,255.0f,255.0f);
	                    const vec3 castAdd = vec3(0.5f,0.5f,0.5f);*/


                        //output[gid*pixelOffset] = 255.0f*input[gid*pixelOffset]/(float)divideCount;
                        //output[gid*pixelOffset+1] = 255.0f*input[gid*pixelOffset+1]/(float)divideCount;
                        //output[gid*pixelOffset+2] = 255.0f*input[gid*pixelOffset+2]/(float)divideCount;
                        output[gid*pixelOffset] = 255.0f*t1;
                        output[gid*pixelOffset+1] = 255.0f*t2;
                        output[gid*pixelOffset+2] = 255.0f*t3;
                    }
                ";

            program = CL.CreateProgramWithSource(context, kernelCode, out res);

            exceptIfError(res, "Error creating program");

            res = CL.BuildProgram(program, 1, new CLDevice[] { deviceToUse.Value }, "", (IntPtr)0, (IntPtr)0);

            //exceptIfError(res, "Error building program");

            if (res != CLResultCode.Success)
            {
                byte[] errorLog;
                CL.GetProgramBuildInfo(program, deviceToUse.Value, ProgramBuildInfo.Log, out errorLog);
                string errorLogString = System.Text.Encoding.Default.GetString(errorLog);
                Console.WriteLine(errorLogString);
                throw new Exception("OpenCL Kernel compilation failure");
            }

            kernel = CL.CreateKernel(program, "ToHDR", out res);

            exceptIfError(res, "Error creating kernel");

            //Console.WriteLine("Wtf it compiled?");

            buffer = CL.CreateBuffer(context, MemoryFlags.ReadWrite, (nuint)(sizeof(float)*Length), IntPtr.Zero, out res);

            exceptIfError(res, "Error creating buffer");

            bufferOutput = CL.CreateBuffer(context, MemoryFlags.ReadWrite, (nuint)(Length), IntPtr.Zero, out res);

            exceptIfError(res, "Error creating output buffer");


            queue = CL.CreateCommandQueueWithProperties(context, deviceToUse.Value, IntPtr.Zero, out res);


            exceptIfError(res, "Error creating command queue");
        }

        public unsafe static byte[] ConvertToHDR(float[] input, int pixelOffset, int divideCount)
        {

            CLResultCode res;

            var watch = new System.Diagnostics.Stopwatch();

            byte[] resultData = new byte[input.Length];


            CLEvent eventWhatever;

            watch.Start();
            //CL.EnqueueMapBuffer(queue, buffer, false, MapFlags.Read, 0, (nuint)input.Length, 0, null, out eventWhatever, out res);
            res = CL.EnqueueWriteBuffer(queue, buffer, true, (UIntPtr)0, input, null, out eventWhatever);
            watch.Stop();
            //Console.WriteLine($"TK write buffer: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error enqueueing buffer write.");

            watch.Restart();
            res = CL.SetKernelArg(kernel, 0, buffer);
            watch.Stop();
            //Console.WriteLine($"TK set kernel arg: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error setting kernel argument.");

            watch.Restart();
            res = CL.SetKernelArg(kernel, 1, bufferOutput);
            watch.Stop();
            //Console.WriteLine($"TK set kernel arg: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error setting kernel argument 2.");

            watch.Restart();
            int* pixelOffsetPtr = &pixelOffset;
            res = CL.SetKernelArg(kernel, 2, (UIntPtr)sizeof(int), (IntPtr)pixelOffsetPtr);
            
            watch.Stop();
            //Console.WriteLine($"TK set kernel arg: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error setting kernel argument 3.");

            watch.Restart();
            res = CL.SetKernelArg(kernel,3,(UIntPtr)sizeof(int),(IntPtr)(&divideCount));
            watch.Stop();
            //Console.WriteLine($"TK set kernel arg: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error setting kernel argument 4.");

            watch.Restart();
            res = CL.EnqueueNDRangeKernel(queue, kernel, 1, new nuint[] { 0 }, new nuint[] { (nuint)(input.Length / pixelOffset) }, new nuint[] { 32 }, 0, null, out eventWhatever);
            watch.Stop();
            //Console.WriteLine($"TK execute: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error kernel execution.");

            watch.Restart();
            CL.Finish(queue);
            watch.Stop();
            //Console.WriteLine($"TK finish: {watch.Elapsed.TotalMilliseconds}");

            watch.Restart();
            res = CL.EnqueueReadBuffer(queue, bufferOutput, true, (UIntPtr)0, resultData, null, out eventWhatever);
            watch.Stop();
            //Console.WriteLine($"TK read buffer: {watch.Elapsed.TotalMilliseconds}");

            exceptIfError(res, "Error enqueueing buffer read.");

            return resultData;
        }
    }
}
