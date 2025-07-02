using Baketa.Infrastructure.Platform.Adapters;

namespace Baketa.Infrastructure.Platform.Adapters.Factory;

/// <summary>
/// アダプターファクトリーの基本インターフェース
/// </summary>
/// <remarks>
/// アダプターインスタンスの作成を一元管理し、環境に応じた適切な実装を提供します
/// </remarks>
public interface IAdapterFactory
{
    /// <summary>
    /// 画像アダプターを作成します
    /// </summary>
    /// <returns>画像アダプターインスタンス</returns>
    IWindowsImageAdapter CreateImageAdapter();

    /// <summary>
    /// キャプチャアダプターを作成します
    /// </summary>
    /// <returns>キャプチャアダプターインスタンス</returns>
    ICaptureAdapter CreateCaptureAdapter();

    /// <summary>
    /// ウィンドウマネージャーアダプターを作成します
    /// </summary>
    /// <returns>ウィンドウマネージャーアダプターインスタンス</returns>
    IWindowManagerAdapter CreateWindowManagerAdapter();
}
