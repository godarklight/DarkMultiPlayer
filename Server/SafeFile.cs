using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public static class SafeFile
    {
        private static HashSet<char> invalidChars;

        private static void BuildInvalidChars()
        {
            //https://gist.github.com/doctaphred/d01d05291546186941e1b7ddc02034d3
            //I am aware of Path.GetInvalidPathChars but I would rather keep the same restrictions on all systems.
            invalidChars = new HashSet<char>();
            invalidChars.Add('<');
            invalidChars.Add('>');
            invalidChars.Add(':');
            invalidChars.Add('"');
            invalidChars.Add('/');
            invalidChars.Add('\\');
            invalidChars.Add('|');
            invalidChars.Add('?');
            invalidChars.Add('*');
            invalidChars.Add('$');
            invalidChars.Add('\r');
            invalidChars.Add('\n');
        }

        public static bool IsNameSafe(string unsafePart)
        {
            if (invalidChars == null)
            {
                BuildInvalidChars();
            }
            foreach (char c in unsafePart)
            {
                if (c < 32)
                {
                    return false;
                }
                if (invalidChars.Contains(c))
                {
                    return false;
                }
            }
            return true;
        }
    }
}