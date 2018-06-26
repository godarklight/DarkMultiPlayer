using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DarkMultiPlayerServer
{
    /// <summary>
    /// Contains extension methods
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Reverses a <see cref="byte[]"/> if the <see cref="BitConverter"/> is Big-Endian
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static byte[] ReverseIfBigEndian(this byte[] array)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(array);
            return array;
        }

        /// <summary>
        /// Reads the next int from the collection and removes it
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static int ReadNextInt(this List<byte> bytes)
        {
            int num = BitConverter.ToInt32(bytes.ToArray(), 0);
            bytes.RemoveRange(0, 4);
            return num;
        }
    }
}
