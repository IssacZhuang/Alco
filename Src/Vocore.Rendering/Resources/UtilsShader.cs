namespace Vocore.Rendering
{
    /// <summary>
    /// Utility class for handling shader pragmas.
    /// </summary>
    public static class UtilsShader
    {
        /// <summary>
        /// The key used to identify a pragma directive.
        /// </summary>
        public const string KeyPragma = "#pragma";

        /// <summary>
        /// Parses the shader text and retrieves all shader pragmas.
        /// </summary>
        /// <param name="shaderText">The shader text to parse.</param>
        /// <returns>An array of <see cref="ShaderPragma"/> objects representing the shader pragmas.</returns>
        public static ShaderPragma[] GetShaderPragma(string shaderText)
        {
            //syntax: #pragma name value1 value2 value3 .. valueN
            List<ShaderPragma> shaderPragmas = new List<ShaderPragma>();
            using StringReader reader = new StringReader(shaderText);
            string? line;
            bool isInCommentBlock = false;
            while ((line = reader.ReadLine()) != null)
            {
                //remove spaces in the front
                line = line.TrimStart();
                // skip comments
                if (line.StartsWith("//"))
                {
                    continue;
                }

                if (line.Contains("/*"))
                {
                    isInCommentBlock = true;
                }

                if (line.Contains("*/"))
                {
                    isInCommentBlock = false;
                }

                if (isInCommentBlock)
                {
                    continue;
                }

                if (line.StartsWith(KeyPragma))
                {
                    string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length > 1)
                    {
                        ShaderPragma pragma = new ShaderPragma
                        {
                            Name = tokens[1],
                            Values = tokens.Skip(2).ToArray()
                        };
                        shaderPragmas.Add(pragma);
                    }
                }
            }

            return shaderPragmas.ToArray();
        }
    }
}
