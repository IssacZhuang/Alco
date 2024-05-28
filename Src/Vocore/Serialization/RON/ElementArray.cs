using System;
using System.Collections.Generic;
using System.Text;

namespace Ron.NET;

public class ElementArray : IElement
{
    public List<IElement> Elements;

    public ElementArray(params IElement[] elements)
    {
        Elements = new List<IElement>(elements);
    }

    public IElement this[int index]
    {
        get
        {
            if (index < 0 || index >= Elements.Count)
                throw new IndexOutOfRangeException($"Indexer must be between 0 and {Elements.Count - 1}. Actual value was {index}.");

            return Elements[index];
        }
    }

    public IElement this[string elementName] => throw new System.NotImplementedException();

    public Token[] Tokenize()
    {
        List<Token> tokens = new List<Token>();
        
        // The reason we can do this here, but can't do this in the elementset, is because (right now) arrays are only
        // allowed *inside* a struct. However, a struct does not need to be contained inside another struct, at which
        // point we don't want to include the parentheses for it.
        tokens.Add(new Token(TokenType.OpenBracket, null, 0));

        int i = 0;
        foreach (IElement element in Elements)
        {
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
        
        tokens.Add(new Token(TokenType.ClosingBracket, null, 0));

        return tokens.ToArray();
    }
}