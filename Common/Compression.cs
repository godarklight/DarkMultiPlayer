using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using ICSharpCode.SharpZipLib.GZip;

namespace DarkMultiPlayerCommon
{
    public class Compression
    {
        public const int COMPRESSION_THRESHOLD = 4096;
        public static bool compressionEnabled = false;
        public static bool sysIOCompressionWorks
        {
            get;
            private set;
        }

        public static long TestSysIOCompression()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            ManualResetEvent mre = new ManualResetEvent(false);
            Thread compressionThreadTester = new Thread(new ParameterizedThreadStart(CompressionTestWorker));
            compressionThreadTester.IsBackground = true;
            compressionThreadTester.Start(mre);
            bool result = mre.WaitOne(1000);
            if (!result)
            {
                compressionThreadTester.Abort();
            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private static void CompressionTestWorker(object mreObject)
        {
            ManualResetEvent mre = (ManualResetEvent)mreObject;
            bool compressionWorks = true;
            try
            {
                byte[] smallEmptyTest = new byte[COMPRESSION_THRESHOLD / 2];
                byte[] bigEmptyTest = new byte[COMPRESSION_THRESHOLD * 2];
                byte[] smallRandomTest = new byte[COMPRESSION_THRESHOLD / 2];
                byte[] bigRandomTest = new byte[COMPRESSION_THRESHOLD * 2];
                Random rand = new Random();
                rand.NextBytes(smallRandomTest);
                rand.NextBytes(bigRandomTest);
                byte[] t1 = SysIOCompress(smallEmptyTest);
                byte[] t2 = SysIOCompress(bigEmptyTest);
                byte[] t3 = SysIOCompress(smallRandomTest);
                byte[] t4 = SysIOCompress(bigRandomTest);
                byte[] t5 = SysIODecompress(t1);
                byte[] t6 = SysIODecompress(t2);
                byte[] t7 = SysIODecompress(t3);
                byte[] t8 = SysIODecompress(t4);
                //Fail the test if the byte array doesn't match
                if (!ByteCompare(smallEmptyTest, t5))
                {
                    compressionWorks = false;
                }
                if (!ByteCompare(bigEmptyTest, t6))
                {
                    compressionWorks = false;
                }
                if (!ByteCompare(smallRandomTest, t7))
                {
                    compressionWorks = false;
                }
                if (!ByteCompare(bigRandomTest, t8))
                {
                    compressionWorks = false;
                }
                sysIOCompressionWorks = compressionWorks;
            }
            catch
            {
                sysIOCompressionWorks = false;
            }
            mre.Set();
        }

        public static bool ByteCompare(byte[] lhs, byte[] rhs)
        {
            if (lhs.Length != rhs.Length)
            {
                return false;
            }
            for (int i = 0; i < lhs.Length; i++)
            {
                if (lhs[i] != rhs[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static byte[] CompressIfNeeded(byte[] inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            if (inputBytes.Length < COMPRESSION_THRESHOLD || !compressionEnabled)
            {
                return AddCompressionHeader(inputBytes, false);
            }
            return AddCompressionHeader(Compress(inputBytes), true);
        }



        public static byte[] DecompressIfNeeded(byte[] inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            if (!BytesAreCompressed(inputBytes))
            {
                return RemoveDecompressedHeader(inputBytes);
            }
            if (!compressionEnabled)
            {
                throw new Exception("Cannot decompress if compression is disabled!");
            }
            return Decompress(RemoveDecompressedHeader(inputBytes));
        }

        /// <summary>
        /// Tests if the byte[] is compressed.
        /// </summary>
        /// <returns><c>true</c>, if the byte[] was compressed, <c>false</c> otherwise.</returns>
        /// <param name="inputBytes">Input bytes.</param>
        public static bool BytesAreCompressed(byte[] inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            return BitConverter.ToBoolean(inputBytes, 0);
        }

        /// <summary>
        /// Appends the decompressed header.
        /// </summary>
        /// <returns>The message with the prepended header</returns>
        /// <param name="inputBytes">Input bytes.</param>
        public static byte[] AddCompressionHeader(byte[] inputBytes, bool value)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            byte[] returnBytes = new byte[inputBytes.Length + 1];
            BitConverter.GetBytes(value).CopyTo(returnBytes, 0);
            Array.Copy(inputBytes, 0, returnBytes, 1, inputBytes.Length);
            return returnBytes;
        }

        /// <summary>
        /// Removes the decompressed header.
        /// </summary>
        /// <returns>The message without the prepended header</returns>
        /// <param name="inputBytes">Input bytes.</param>
        public static byte[] RemoveDecompressedHeader(byte[] inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            byte[] returnBytes = new byte[inputBytes.Length - 1];
            Array.Copy(inputBytes, 1, returnBytes, 0, inputBytes.Length - 1);
            return returnBytes;
        }

        public static byte[] Compress(byte[] inputBytes)
        {
            if (sysIOCompressionWorks)
            {
                return SysIOCompress(inputBytes);
            }
            return ICSharpCompress(inputBytes);
        }

        public static byte[] Decompress(byte[] inputBytes)
        {
            if (sysIOCompressionWorks)
            {
                return SysIODecompress(inputBytes);
            }
            return ICSharpDecompress(inputBytes);
        }

        private static byte[] SysIOCompress(byte[] inputBytes)
        {
            byte[] returnBytes = null;
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gs = new GZipStream(ms, CompressionMode.Compress))
                {
                    gs.Write(inputBytes, 0, inputBytes.Length);
                }
                returnBytes = ms.ToArray();
            }
            return returnBytes;
        }

        private static byte[] SysIODecompress(byte[] inputBytes)
        {
            byte[] returnBytes = null;
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (MemoryStream ms = new MemoryStream(inputBytes))
                {
                    using (GZipStream gs = new GZipStream(ms, CompressionMode.Decompress))
                    {
                        //Stream.CopyTo is a .NET 4 feature?
                        byte[] buffer = new byte[4096];
                        int numRead;
                        while ((numRead = gs.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            outputStream.Write(buffer, 0, numRead);
                        }
                    }
                }
                returnBytes = outputStream.ToArray();
            }
            return returnBytes;
        }

        private static byte[] ICSharpCompress(byte[] inputBytes)
        {
            byte[] returnBytes = null;
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipOutputStream gs = new GZipOutputStream(ms))
                {
                    gs.Write(inputBytes, 0, inputBytes.Length);
                }
                returnBytes = ms.ToArray();
            }
            return returnBytes;
        }

        private static byte[] ICSharpDecompress(byte[] inputBytes)
        {
            byte[] returnBytes = null;
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (MemoryStream ms = new MemoryStream(inputBytes))
                {
                    using (GZipInputStream gs = new GZipInputStream(ms))
                    {
                        //Stream.CopyTo is a .NET 4 feature?
                        byte[] buffer = new byte[4096];
                        int numRead;
                        while ((numRead = gs.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            outputStream.Write(buffer, 0, numRead);
                        }
                    }
                }
                returnBytes = outputStream.ToArray();
            }
            return returnBytes;
        }
    }
}
