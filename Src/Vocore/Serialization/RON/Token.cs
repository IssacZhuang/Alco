namespace Ron.NET;

public struct Token
{
    public TokenType TokenType;
    public object Literal;
    public int Line;

    public Token(TokenType tokenType, object literal, int line)
    {
        TokenType = tokenType;
        Line = line;
        Literal = literal;
    }

    public override string ToString()
    {
        return $"Token: {TokenType.ToString()}, Line: {Line}, Literal: {Literal ?? "None"}";
    }
}