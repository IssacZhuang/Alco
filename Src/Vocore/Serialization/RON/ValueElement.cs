using System;

namespace Ron.NET;

public struct ValueElement<T> : IElement
{
    public T Value;
    public ElementType Type;

    public ValueElement(T value, ElementType type)
    {
        Value = value;
        Type = type;
    }

    public IElement this[int index] => throw new NotSupportedException("Value element cannot be indexed.");

    public IElement this[string elementName] => throw new NotSupportedException("Value element cannot be indexed.");

    public Token[] Tokenize()
    {
        TokenType type = Type switch
        {
            ElementType.String => TokenType.String,
            ElementType.Number => TokenType.Number,
            ElementType.Char => TokenType.Char,
            ElementType.Enum => TokenType.Identifier,
            ElementType.Bool => (bool) (object) Value ? TokenType.True : TokenType.False,
            _ => throw new ArgumentOutOfRangeException()
        };

        Token token = new Token(type, Value, 0);
        return new[] { token };
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}