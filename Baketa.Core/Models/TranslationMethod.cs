namespace Baketa.Core.Models;

public enum TranslationMethod { Local, Gemini, Google }

public enum GoogleTranslationResult
{
    Success, InvalidKey, ServiceDisabled, BillingDisabled, Forbidden,
    RateLimited, NetworkError, Timeout, Failed
}
