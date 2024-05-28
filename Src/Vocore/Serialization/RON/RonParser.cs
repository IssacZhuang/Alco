using System;
using System.Collections.Generic;

namespace Ron.NET;

internal class RonParser
{
    private string _ron;

    private static Dictionary<string, TokenType> _keywords;

    private int _start;
    private int _line;
    private int _current;

    static RonParser()
    {
        _keywords = new Dictionary<string, TokenType>()
        {
            ["true"] = TokenType.True,
            ["false"] = TokenType.False
        };
    }
    
    public RonParser(string ron)
    {
        _ron = ron;
    }
    
    public Token[] Parse()
    {
        List<Token> tokens = new List<Token>();

        while (!IsEof)
        {
            _start = _current;
            char c = Advance();

            switch (c)
            {
                case '(':
                    AddToken(ref tokens, TokenType.OpenParenthesis);
                    break;
                case ')':
                    AddToken(ref tokens, TokenType.ClosingParenthesis);
                    break;
                
                case '{':
                    AddToken(ref tokens, TokenType.OpenBrace);
                    break;
                case '}':
                    AddToken(ref tokens, TokenType.ClosingBrace);
                    break;
                
                case '[':
                    AddToken(ref tokens, TokenType.OpenBracket);
                    break;
                case ']':
                    AddToken(ref tokens, TokenType.ClosingBracket);
                    break;
                
                case ':':
                    AddToken(ref tokens, TokenType.Colon);
                    break;
                case ',':
                    AddToken(ref tokens, TokenType.Comma);
                    break;
                
                case '\n':
                    _line++;
                    break;
                
                case ' ':
                case '\r':
                case '\t':
                    break;
                
                case '\'':
                    while (Peek() != '\'')
                        Advance();

                    //if (_current - _start != 3)
                    //    throw Error("Char must be exactly 1.");

                    Advance();

                    AddToken(ref tokens, TokenType.Char, char.Parse(_ron[(_start + 1)..(_current - 1)]));

                    break;
                
                case '"':
                    while (Peek() != '"')
                        Advance();

                    Advance();
                    
                    AddToken(ref tokens, TokenType.String, _ron[(_start + 1)..(_current - 1)]);
                    break;
                
                case '/':
                    switch (Peek())
                    {
                        case '/':
                            while (Advance() != '\n') { }
                            goto SKIP_DEFAULT;

                        case '*':
                            Advance();
                            char a;
                            while ((a = Advance()) != '*' && Peek() != '/')
                            {
                                if (a == '\n')
                                    _line++;
                            }

                            Advance();
                            goto SKIP_DEFAULT;
                    }
                    
                    goto default;
                    
                    SKIP_DEFAULT: ;
                    
                    break;

                default:
                    if (IsDigit(c) || (c == '-' && IsDigit(Peek())))
                    {
                        while (IsDigit(Peek()) || IsDigit(Peek(1)))
                            Advance();

                        AddToken(ref tokens, TokenType.Number, double.Parse(_ron[_start.._current]));
                    }
                    else if (IsAlphanumeric(c))
                    {
                        while (!IsEof && IsAlphanumeric(Peek()))
                            Advance();
                        
                        string word = _ron[_start.._current];

                        if (!_keywords.TryGetValue(word, out TokenType type))
                            type = TokenType.Identifier;

                        AddToken(ref tokens, type, word);
                    }
                    else
                        throw Error($"Unexpected token '{c}'");

                    break;
            }
        }
        
        AddToken(ref tokens, TokenType.Eof);
        
        return tokens.ToArray();
    }

    private bool IsDigit(char c) => c is >= '0' and <= '9';

    private bool IsAlphanumeric(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-' || IsDigit(c);

    private char Advance() => _ron[_current++];

    private char Peek(int peekAmount = 0)
    {
        if (_current + peekAmount >= _ron.Length)
            return '\0';
        
        return _ron[_current + peekAmount];
    }

    private void AddToken(ref List<Token> tokens, TokenType type) => AddToken(ref tokens, type, null!);
    
    private void AddToken(ref List<Token> tokens, TokenType type, object literal)
    {
        tokens.Add(new Token(type, literal, _line + 1));
    }

    private bool IsEof => _current >= _ron.Length;

    private Exception Error(string message)
    {
        return new Exception($"Error on line {_line + 1}: " + message);
    }
}