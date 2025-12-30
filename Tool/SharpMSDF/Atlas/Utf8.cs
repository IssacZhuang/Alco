using System;
using System.Collections.Generic;
using System.Text;

namespace SharpMSDF.Atlas
{
    public static class Utf8
    {
        public static void Utf8Decode(List<uint> codepoints, string utf8String)
        {
            bool start = true;
            int rBytes = 0;
            uint cp = 0;

            byte[] bytes = Encoding.UTF8.GetBytes(utf8String);

            for (int i = 0; i < bytes.Length; ++i)
            {
                byte b = bytes[i];

                if (rBytes > 0)
                {
                    rBytes--;
                    if ((b & 0xC0) == 0x80)
                        cp |= (uint)(b & 0x3F) << (6 * rBytes);
                    else
                        continue; // error
                }
                else if ((b & 0x80) == 0)
                {
                    cp = b;
                    rBytes = 0;
                }
                else if ((b & 0x40) != 0)
                {
                    int block = 0;
                    while (((b << block) & 0x40) != 0 && block < 4)
                        block++;

                    if (block < 4)
                    {
                        cp = (uint)(b & (0x3F >> block)) << (6 * block);
                        rBytes = block;
                    }
                    else
                        continue; // error
                }
                else
                {
                    continue; // error
                }

                if (rBytes == 0)
                {
                    if (!(start && cp == 0xFEFF)) // skip BOM
                        codepoints.Add(cp);
                    start = false;
                }
            }
        }
    }
}
