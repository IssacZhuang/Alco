namespace Ron.NET;

public enum TokenType
{
    OpenParenthesis,    // (
    ClosingParenthesis, // )
    
    OpenBrace,          // {
    ClosingBrace,       // }
    
    OpenBracket,        // [
    ClosingBracket,     // ]
    
    Colon,              // :
    Comma,              // ,
    
    Identifier,         
    Char,
    String,
    Number,
    
    True,
    False,
    
    Eof
}