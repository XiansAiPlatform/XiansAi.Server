namespace Shared.Services;

public class TokenLimitExceededException : Exception
{
    public TokenLimitExceededException(string message) : base(message)
    {
    }
}

