namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// ONNX Runtime Execution Provider Factory Interface
/// 各種GPU最適化技術のプロバイダーを統一的に管理
/// Phase 4: 統合GPU最適化基盤
/// 
/// 注記: SessionOptions作成はInfrastructure層のファクトリークラスで実装
/// Core層は実装非依存を維持するため、設定情報のみを提供
/// </summary>
public interface IExecutionProviderFactory
{
    /// <summary>
    /// プロバイダータイプ
    /// </summary>
    ExecutionProvider Type { get; }

    /// <summary>
    /// 現在の環境でこのプロバイダーが利用可能かチェック
    /// </summary>
    /// <param name="environment">GPU環境情報</param>
    /// <returns>サポート状況</returns>
    bool IsSupported(GpuEnvironmentInfo environment);

    /// <summary>
    /// プロバイダーの優先度を取得
    /// 数値が大きいほど優先度が高い
    /// </summary>
    /// <param name="environment">GPU環境情報</param>
    /// <returns>優先度 (0-100)</returns>
    int Priority(GpuEnvironmentInfo environment);

    /// <summary>
    /// プロバイダー情報を取得（ログ・UI表示用）
    /// </summary>
    /// <param name="environment">GPU環境情報</param>
    /// <returns>プロバイダー情報文字列</returns>
    string GetProviderInfo(GpuEnvironmentInfo environment);

    /// <summary>
    /// プロバイダー設定オプションを取得
    /// Infrastructure層でSessionOptions作成時に使用
    /// </summary>
    /// <param name="environment">GPU環境情報</param>
    /// <returns>プロバイダー設定辞書</returns>
    Dictionary<string, string> GetProviderOptions(GpuEnvironmentInfo environment);
}