using Xunit;

namespace Baketa.Infrastructure.Tests.TestCollections;

/// <summary>
/// PortManagementServiceテストコレクション
/// ポート競合を避けるため、関連テストを順次実行
/// </summary>
[CollectionDefinition("PortManagement")]
public class PortManagementCollection : ICollectionFixture<PortManagementFixture>
{
}

/// <summary>
/// PortManagementテスト用フィクスチャ
/// </summary>
public class PortManagementFixture : IDisposable
{
    /// <summary>
    /// テスト開始時にポートレジストリファイルをクリーンアップ
    /// </summary>
    public PortManagementFixture()
    {
        // 古いポートレジストリファイルを削除
        var registryFile = Path.Combine(Environment.CurrentDirectory, "translation_ports.json");
        if (File.Exists(registryFile))
        {
            try
            {
                File.Delete(registryFile);
            }
            catch
            {
                // ファイル削除失敗は無視（他のプロセスが使用中の可能性）
            }
        }
    }

    public void Dispose()
    {
        // リソースクリーンアップ（必要に応じて）
    }
}