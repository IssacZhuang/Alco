using System;
using System.Text;

namespace Ron.NET;

public interface IElement
{
    public IElement this[int index] { get; }
    
    public IElement this[string elementName] { get; }

    public Token[] Tokenize();

    public static string Serialize(IElement element, SerializeOptions options = SerializeOptions.None)
    {
        StringBuilder code = new StringBuilder();
        Token[] tokens = element.Tokenize();

        int indendationLevel = 0;
        const int indentationSpaces = 4;
        
        foreach (Token token in tokens)
        {
            switch (token.TokenType)
            {
                case TokenType.OpenParenthesis:
                    if (IsPrettyPrint(options))
                    {
                        indendationLevel++;
                        code.AppendLine("(");
                        code.Append(Pad("", indendationLevel * indentationSpaces));
                    }
                    else
                        code.Append('(');
                    break;
                case TokenType.ClosingParenthesis:
                    if (IsPrettyPrint(options))
                    {
                        indendationLevel--;
                        code.Append("\n" + Pad(")", indendationLevel * indentationSpaces));
                    }
                    else
                        code.Append(')');
                    break;
                case TokenType.OpenBrace:
                    code.Append('{');
                    break;
                case TokenType.ClosingBrace:
                    code.Append('}');
                    break;
                case TokenType.OpenBracket:
                    if (IsPrettyPrint(options))
                    {
                        code.AppendLine("[");
                        indendationLevel++;
                        code.Append(Pad("", indendationLevel * indentationSpaces));
                    }
                    else
                        code.Append('[');
                    break;
                case TokenType.ClosingBracket:
                    if (IsPrettyPrint(options))
                    {
                        indendationLevel--;
                        code.Append('\n' + Pad("]", indendationLevel * indentationSpaces));
                    }
                    else
                        code.Append("]");
                    break;
                case TokenType.Colon:
                    if (IsPrettyPrint(options))
                        code.Append(": ");
                    else
                        code.Append(':');
                    break;
                case TokenType.Comma:
                    if (IsPrettyPrint(options))
                    {
                        code.AppendLine(",");
                        code.Append(Pad("", indendationLevel * indentationSpaces));
                    }
                    else
                        code.Append(',');
                    break;
                case TokenType.Identifier:
                    code.Append(token.Literal);
                    break;
                case TokenType.Char:
                    code.Append($"'{token.Literal}'");
                    break;
                case TokenType.String:
                    code.Append($"\"{token.Literal}\"");
                    break;
                case TokenType.Number:
                    code.Append(token.Literal);
                    break;
                case TokenType.True:
                    code.Append("true");
                    break;
                case TokenType.False:
                    code.Append("false");
                    break;
                case TokenType.Eof:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return code.ToString();
    }

    private static string Pad(string text, int numSpaces)
    {
        return text.PadLeft(text.Length + numSpaces);
    }

    private static bool IsPrettyPrint(SerializeOptions options) => options == SerializeOptions.PrettyPrint;
}