using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Vocore.Tool.AssetsCompiler
{
    public class PathExtensions
    {
        public static string GetRelativePath(string relativeTo, string path)
        {
            var relativeFull = Path.GetFullPath(relativeTo);
            var pathFull = Path.GetFullPath(path);

            var rootRelative = Path.GetPathRoot(relativeFull);
            var rootFull = Path.GetPathRoot(pathFull);

            // paths do not share a common root:
            if (rootRelative != rootFull)
            {
                return path;
            }

            var separatorArray = new[] { Path.DirectorySeparatorChar };
            var splitRelative = relativeFull.Split(separatorArray, StringSplitOptions.RemoveEmptyEntries);
            var splitPath = pathFull.Split(separatorArray).Where(x => x != string.Empty).ToArray();

            if (splitPath.Length < splitRelative.Length)
            {
                return path;
            }

            int count = 0;
            while (count < splitRelative.Length)
            {
                if (splitRelative[count] != splitPath[count])
                {
                    return path;
                }
                count++;
            }

            return Path.Combine(splitPath.Skip(count).ToArray());
        }


        public static string GetBashPath(string path)
        {
            var splitPath = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (!splitPath.Any())
            {
                return path;
            }

            if (splitPath[0].Length == 2 && splitPath[0][1] == ':' && char.IsLetter(splitPath[0][0]))
            {
                return string.Join("/", new[] { $"/{char.ToLower(splitPath[0][0])}" }.Concat(splitPath.Skip(1)));
            }
            if (path.StartsWith("\\\\"))
            {
                // local function to insert slashes:
                IEnumerable<string> GetModifiedSegments()
                {
                    yield return $"//{splitPath.First()}";
                    foreach (var seg in splitPath.Skip(1))
                    {
                        var builder = new StringBuilder("");
                        foreach (var c in seg)
                        {
                            if (c == ' ' || c == '$' || c == '(' || c == ')')
                            {
                                builder.Append('\\');
                            }
                            builder.Append(c);
                        }
                        yield return builder.ToString();
                    }
                }

                return string.Join("/", GetModifiedSegments());

            }
            return string.Join("/", splitPath);
        }
    }
}