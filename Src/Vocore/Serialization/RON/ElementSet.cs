using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ron.NET;

public class ElementSet : IElement
{
    public Dictionary<string, IElement> Elements;

    public ElementSet(params KeyValuePair<string, IElement>[] elements)
    {
        Elements = new Dictionary<string, IElement>(elements);
    }

    public IElement this[int index]
    {
        get
        {
            if (index < 0 || index >= Elements.Count)
                throw new IndexOutOfRangeException($"Indexer must be between 0 and {Elements.Count - 1}. Actual value was {index}.");

            return Elements.ElementAt(index).Value;
        }
    }

    public IElement this[string elementName]
    {
        get
        {
            if (!Elements.TryGetValue(elementName, out IElement value))
                throw new KeyNotFoundException($"The given key \"{elementName}\" was not found in the set.");

            return value;
        }
    }

    public Token[] Tokenize()
    {
        List<Token> tokens = new List<Token>();
        int i = 0;
        foreach ((string name, IElement element) in Elements)
        {
            tokens.Add(new Token(TokenType.Identifier, name, 0));
            tokens.Add(new Token(TokenType.Colon, null, 0));
            if (element is ElementSet)
            {
                tokens.Add(new Token(TokenType.OpenParenthesis, null, 0));
                tokens.AddRange(element.Tokenize());
                tokens.Add(new Token(TokenType.ClosingParenthesis, null, 0));
            }
            else
                tokens.AddRange(element.Tokenize());

            if (++i < Elements.Count)
                tokens.Add(new Token(TokenType.Comma, null, 0));
        }

        return tokens.ToArray();
    }
}