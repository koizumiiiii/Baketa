namespace Baketa.Infrastructure.OCR.PaddleOCR;

/// <summary>
/// OCR初期化時の例外
/// </summary>
public class OcrInitializationException : Exception
{
    public OcrInitializationException()
    {
    }

    public OcrInitializationException(string message) : base(message)
    {
    }

    public OcrInitializationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
