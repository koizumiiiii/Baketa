using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baketa.Core.UI.Overlay;
using Baketa.Core.UI.Geometry;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Overlay.Tests;

/// <summary>
/// オーバーレイ機能の基本テストクラス
/// </summary>
public static class OverlayBasicTests
{
    /// <summary>
    /// オーバーレイウィンドウの作成テスト
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="logger">ロガー</param>
    /// <returns>テスト結果</returns>
    public static async Task<bool> TestOverlayCreationAsync(
        IOverlayWindowManager manager, 
        ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("オーバーレイ作成テストを開始します。");
            
            // デフォルト設定でオーバーレイを作成
            var overlay = await OverlayWindowFactory.CreateDefaultOverlayAsync(manager, nint.Zero).ConfigureAwait(false);
            
            // 基本プロパティの確認
            if (overlay.Handle == nint.Zero)
            {
                logger?.LogError("オーバーレイハンドルが無効です。");
                return false;
            }
            
            if (Math.Abs(overlay.Opacity - 0.9) > 0.01)
            {
                logger?.LogError("オーバーレイの透明度が期待値と異なります。期待値: 0.9, 実際: {Opacity}", overlay.Opacity);
                return false;
            }
            
            // 表示テスト
            overlay.Show();
            
            if (!overlay.IsVisible)
            {
                logger?.LogError("オーバーレイが表示されませんでした。");
                return false;
            }
            
            // 非表示テスト
            overlay.Hide();
            
            if (overlay.IsVisible)
            {
                logger?.LogError("オーバーレイが非表示になりませんでした。");
                return false;
            }
            
            // リソース解放
            overlay.Dispose();
            
            logger?.LogInformation("オーバーレイ作成テストが成功しました。");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogError(ex, "オーバーレイ作成テスト中に無効な操作エラーが発生しました。");
            return false;
        }
        catch (ExternalException ex)
        {
            logger?.LogError(ex, "オーバーレイ作成テスト中に外部エラーが発生しました。");
            return false;
        }
    }
    
    /// <summary>
    /// クリックスルー機能のテスト
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="logger">ロガー</param>
    /// <returns>テスト結果</returns>
    public static async Task<bool> TestClickThroughAsync(
        IOverlayWindowManager manager,
        ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("クリックスルーテストを開始します。");
            
            var overlay = await OverlayWindowFactory.CreateDefaultOverlayAsync(manager, nint.Zero).ConfigureAwait(false);
            
            // 初期状態（クリックスルー有効）
            if (!overlay.IsClickThrough)
            {
                logger?.LogError("初期状態でクリックスルーが無効になっています。");
                return false;
            }
            
            // クリックスルー無効に変更
            overlay.IsClickThrough = false;
            
            if (overlay.IsClickThrough)
            {
                logger?.LogError("クリックスルーの無効化に失敗しました。");
                return false;
            }
            
            // クリックスルー有効に変更
            overlay.IsClickThrough = true;
            
            if (!overlay.IsClickThrough)
            {
                logger?.LogError("クリックスルーの有効化に失敗しました。");
                return false;
            }
            
            overlay.Dispose();
            
            logger?.LogInformation("クリックスルーテストが成功しました。");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogError(ex, "クリックスルーテスト中に無効な操作エラーが発生しました。");
            return false;
        }
        catch (ExternalException ex)
        {
            logger?.LogError(ex, "クリックスルーテスト中に外部エラーが発生しました。");
            return false;
        }
    }
    
    /// <summary>
    /// ヒットテスト領域のテスト
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="logger">ロガー</param>
    /// <returns>テスト結果</returns>
    public static async Task<bool> TestHitTestAreasAsync(
        IOverlayWindowManager manager,
        ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation("ヒットテスト領域テストを開始します。");
            
            var overlay = await OverlayWindowFactory.CreateDefaultOverlayAsync(manager, nint.Zero).ConfigureAwait(false);
            
            // 初期状態（領域なし）
            if (overlay.HitTestAreas.Count != 0)
            {
                logger?.LogError("初期状態でヒットテスト領域が存在します。");
                return false;
            }
            
            // 領域追加
            var testArea = new Rect(10, 10, 100, 50);
            overlay.AddHitTestArea(testArea);
            
            if (overlay.HitTestAreas.Count != 1)
            {
                logger?.LogError("ヒットテスト領域の追加に失敗しました。");
                return false;
            }
            
            if (overlay.HitTestAreas[0] != testArea)
            {
                logger?.LogError("追加されたヒットテスト領域が期待値と異なります。");
                return false;
            }
            
            // 領域削除
            overlay.RemoveHitTestArea(testArea);
            
            if (overlay.HitTestAreas.Count != 0)
            {
                logger?.LogError("ヒットテスト領域の削除に失敗しました。");
                return false;
            }
            
            // 複数領域のテスト
            overlay.AddHitTestArea(new Rect(0, 0, 50, 50));
            overlay.AddHitTestArea(new Rect(100, 100, 50, 50));
            overlay.AddHitTestArea(new Rect(200, 200, 50, 50));
            
            if (overlay.HitTestAreas.Count != 3)
            {
                logger?.LogError("複数ヒットテスト領域の追加に失敗しました。");
                return false;
            }
            
            // 全削除
            overlay.ClearHitTestAreas();
            
            if (overlay.HitTestAreas.Count != 0)
            {
                logger?.LogError("全ヒットテスト領域の削除に失敗しました。");
                return false;
            }
            
            overlay.Dispose();
            
            logger?.LogInformation("ヒットテスト領域テストが成功しました。");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogError(ex, "ヒットテスト領域テスト中に無効な操作エラーが発生しました。");
            return false;
        }
        catch (ExternalException ex)
        {
            logger?.LogError(ex, "ヒットテスト領域テスト中に外部エラーが発生しました。");
            return false;
        }
    }
    
    /// <summary>
    /// すべての基本テストを実行
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="logger">ロガー</param>
    /// <returns>すべてのテストが成功したかどうか</returns>
    public static async Task<bool> RunAllBasicTestsAsync(
        IOverlayWindowManager manager,
        ILogger? logger = null)
    {
        logger?.LogInformation("オーバーレイ基本テストスイートを開始します。");
        
        var tests = new (string Name, Func<Task<bool>> Test)[]
        {
            ("オーバーレイ作成テスト", () => TestOverlayCreationAsync(manager, logger)),
            ("クリックスルーテスト", () => TestClickThroughAsync(manager, logger)),
            ("ヒットテスト領域テスト", () => TestHitTestAreasAsync(manager, logger))
        };
        
        var successCount = 0;
        
        foreach (var test in tests)
        {
            var (testName, testFunc) = test;
            
            logger?.LogInformation("実行中: {TestName}", testName);
            
            var result = await testFunc().ConfigureAwait(false);
            
            if (result)
            {
                successCount++;
                logger?.LogInformation("✓ {TestName} - 成功", testName);
            }
            else
            {
                logger?.LogError("✗ {TestName} - 失敗", testName);
            }
        }
        
        var allSuccess = successCount == tests.Length;
        
        logger?.LogInformation("テスト結果: {SuccessCount}/{TotalCount} 成功", successCount, tests.Length);
        
        if (allSuccess)
        {
            logger?.LogInformation("すべてのテストが成功しました！");
        }
        else
        {
            logger?.LogWarning("一部のテストが失敗しました。");
        }
        
        return allSuccess;
    }
}