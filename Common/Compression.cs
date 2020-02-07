using System;
using System.IO;
using System.IO.Compression;

namespace DarkMultiPlayerCommon
{
    public class Compression
    {
        private static byte[] CompressionBuffer = new byte[Common.MAX_MESSAGE_SIZE];
        public const int COMPRESSION_THRESHOLD = 4096;
        public static bool compressionEnabled = false;

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

        public static ByteArray CompressIfNeeded(ByteArray inputBytes)
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

        public static ByteArray DecompressIfNeeded(ByteArray inputBytes)
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

        public static bool BytesAreCompressed(ByteArray inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            return BitConverter.ToBoolean(inputBytes.data, 0);
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
        /// Appends the decompressed header.
        /// </summary>
        /// <returns>The message with the prepended header</returns>
        /// <param name="inputBytes">Input bytes.</param>
        public static ByteArray AddCompressionHeader(ByteArray inputBytes, bool value)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            ByteArray returnBytes = ByteRecycler.GetObject(inputBytes.Length + 1);
            BitConverter.GetBytes(value).CopyTo(returnBytes.data, 0);
            Array.Copy(inputBytes.data, 0, returnBytes.data, 1, inputBytes.Length);
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

        public static ByteArray RemoveDecompressedHeader(ByteArray inputBytes)
        {
            if (inputBytes == null)
            {
                throw new Exception("Input bytes are null");
            }
            ByteArray returnBytes = ByteRecycler.GetObject(inputBytes.Length - 1);
            Array.Copy(inputBytes.data, 1, returnBytes.data, 0, inputBytes.Length - 1);
            return returnBytes;
        }

        public static byte[] Compress(byte[] inputBytes)
        {
            byte[] returnBytes = null;
            lock (CompressionBuffer)
            {
                using (MemoryStream ms = new MemoryStream(CompressionBuffer))
                {
                    using (GZipStream gs = new GZipStream(ms, CompressionMode.Compress))
                    {
                        gs.Write(inputBytes, 0, inputBytes.Length);
                    }
                    returnBytes = ms.ToArray();
                }
            }
            return returnBytes;
        }

        public static ByteArray Compress(ByteArray inputBytes)
        {

            int compressSize = 0;
            lock (CompressionBuffer)
            {
                using (MemoryStream ms = new MemoryStream(CompressionBuffer))
                {
                    using (GZipStream gs = new GZipStream(ms, CompressionMode.Compress, true))
                    {
                        gs.Write(inputBytes.data, 0, inputBytes.Length);
                    }
                    compressSize = (int)ms.Position;
                }
            }
            ByteArray returnBytes = ByteRecycler.GetObject(compressSize);
            Array.Copy(CompressionBuffer, 0, returnBytes.data, 0, compressSize);
            return returnBytes;

        }

        public static byte[] Decompress(byte[] inputBytes)
        {

            byte[] returnBytes = null;
            lock (CompressionBuffer)
            {
                using (MemoryStream outputStream = new MemoryStream(CompressionBuffer))
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
            }
            return returnBytes;
        }

        public static ByteArray Decompress(ByteArray inputBytes)
        {
            ByteArray returnBytes = null;
            lock (CompressionBuffer)
            {
                int totalRead = 0;
                using (MemoryStream ms = new MemoryStream(inputBytes.data, 0, inputBytes.Length))
                {
                    using (GZipStream gs = new GZipStream(ms, CompressionMode.Decompress))
                    {
                        int thisRead;
                        while ((thisRead = gs.Read(CompressionBuffer, totalRead, 4096)) > 0)
                        {
                            totalRead += thisRead;
                        }
                    }
                }
                returnBytes = ByteRecycler.GetObject(totalRead);
                Array.Copy(CompressionBuffer, 0, returnBytes.data, 0, totalRead);
            }
            return returnBytes;
        }
    }
}
