namespace Baketa.Core.Models;

public enum GeminiConnectionResult
{
    Success,
    NoKey,
    InvalidKey,
    RateLimited,
    NetworkError,
    Timeout,
    Failed
}
