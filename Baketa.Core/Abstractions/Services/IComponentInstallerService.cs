namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #256] Phase 3.5: コンポーネントのアトミックインストールとSHA256検証
/// </summary>
public interface IComponentInstallerService
{
    /// <summary>
    /// ダウンロード済みファイルのSHA256を検証
    /// </summary>
    /// <param name="filePath">検証対象ファイルパス</param>
    /// <param name="expectedChecksum">期待するSHA256ハッシュ値（hex形式）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検証成功ならtrue</returns>
    Task<bool> VerifyChecksumAsync(
        string filePath,
        string expectedChecksum,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// コンポーネントをアトミックにインストール
    /// 一時展開→リネームによるアトミック更新を実行
    /// </summary>
    /// <param name="componentId">コンポーネントID</param>
    /// <param name="componentVersion">バージョン</param>
    /// <param name="variant">バリアント（cpu/cuda等）</param>
    /// <param name="zipFilePath">ダウンロード済みZIPファイルパス</param>
    /// <param name="finalPath">最終インストールパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task InstallAtomicallyAsync(
        string componentId,
        string componentVersion,
        string variant,
        string zipFilePath,
        string finalPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ディスク容量チェック
    /// </summary>
    /// <param name="targetPath">インストール先パス</param>
    /// <param name="requiredBytes">必要バイト数</param>
    /// <param name="multiplier">必要容量の倍率（デフォルト3倍：ダウンロード+展開+バックアップ）</param>
    /// <returns>十分な空き容量があればtrue</returns>
    bool HasSufficientDiskSpace(string targetPath, long requiredBytes, int multiplier = 3);
}
