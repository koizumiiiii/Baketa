using System;

namespace Baketa.Core.Abstractions.Platform;

/// <summary>
/// プラットフォーム抽象化の基本インターフェース
/// </summary>
public interface IPlatform
{
    /// <summary>
    /// プラットフォーム名
    /// </summary>
    string Name { get; }

    /// <summary>
    /// プラットフォームバージョン
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 機能サポート確認
    /// </summary>
    /// <param name="featureName">機能名</param>
    /// <returns>サポートしている場合はtrue</returns>
    bool SupportsFeature(string featureName);

    /// <summary>
    /// プラットフォーム固有のサービスを取得
    /// </summary>
    /// <typeparam name="T">サービスタイプ</typeparam>
    /// <returns>サービスインスタンス、存在しない場合は例外をスロー</returns>
    T GetService<T>() where T : class;

    /// <summary>
    /// プラットフォーム固有のサービス取得を試みる
    /// </summary>
    /// <typeparam name="T">サービスタイプ</typeparam>
    /// <param name="service">サービスインスタンス（出力）</param>
    /// <returns>取得できた場合はtrue</returns>
    bool TryGetService<T>(out T service) where T : class;
}
