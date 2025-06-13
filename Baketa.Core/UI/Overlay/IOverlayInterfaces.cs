using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.UI.Overlay;

/// <summary>
/// オーバーレイウィンドウのコアインターフェース（MVP版）
/// プラットフォーム非依存の基本機能を定義
/// </summary>
public interface IOverlayWindow : IDisposable
{
    // === 必須コア機能 ===
    
    /// <summary>
    /// オーバーレイウィンドウが表示されているかどうか
    /// </summary>
    bool IsVisible { get; }
    
    /// <summary>
    /// ウィンドウハンドル
    /// </summary>
    nint Handle { get; }
    
    /// <summary>
    /// オーバーレイの不透明度（固定値、読み取り専用）
    /// デフォルト値：0.9（視認性と背景透過のバランス）
    /// </summary>
    double Opacity { get; }
    
    /// <summary>
    /// クリックスルーが有効かどうか
    /// </summary>
    bool IsClickThrough { get; set; }
    
    /// <summary>
    /// インタラクティブ領域（翻訳テキスト、一時非表示ボタン等）
    /// HitTestAreasに含まれない領域は全てマウスイベントをパススルー
    /// 含まれる領域はマウスイベントを受け付ける
    /// </summary>
    IReadOnlyList<Geometry.Rect> HitTestAreas { get; }
    
    /// <summary>
    /// ウィンドウの位置
    /// </summary>
    Geometry.Point Position { get; set; }
    
    /// <summary>
    /// ウィンドウのサイズ
    /// </summary>
    Geometry.Size Size { get; set; }
    
    /// <summary>
    /// ターゲットウィンドウハンドル
    /// </summary>
    nint TargetWindowHandle { get; set; }
    
    /// <summary>
    /// オーバーレイウィンドウを表示します
    /// </summary>
    void Show();
    
    /// <summary>
    /// オーバーレイウィンドウを非表示にします
    /// </summary>
    void Hide();
    
    /// <summary>
    /// ヒットテスト領域を追加します
    /// </summary>
    /// <param name="area">追加する領域</param>
    void AddHitTestArea(Geometry.Rect area);
    
    /// <summary>
    /// ヒットテスト領域を削除します
    /// </summary>
    /// <param name="area">削除する領域</param>
    void RemoveHitTestArea(Geometry.Rect area);
    
    /// <summary>
    /// すべてのヒットテスト領域をクリアします
    /// </summary>
    void ClearHitTestAreas();
    
    /// <summary>
    /// コンテンツを更新します
    /// 頻繁な呼び出しに対応：MVPでは基本的なビットマップ転送、
    /// 将来的には差分更新による最適化を検討
    /// </summary>
    /// <param name="content">表示するコンテンツ（nullの場合はテストコンテンツ）</param>
    void UpdateContent(object? content = null);
    
    /// <summary>
    /// オーバーレイをターゲットウィンドウに合わせて調整します
    /// </summary>
    void AdjustToTargetWindow();
}

/// <summary>
/// オーバーレイウィンドウ管理サービスのインターフェース
/// </summary>
public interface IOverlayWindowManager
{
    /// <summary>
    /// 新しいオーバーレイウィンドウを作成します
    /// </summary>
    /// <param name="targetWindowHandle">ターゲットウィンドウのハンドル</param>
    /// <param name="initialSize">初期サイズ</param>
    /// <param name="initialPosition">初期位置</param>
    /// <returns>作成されたオーバーレイウィンドウ</returns>
    Task<IOverlayWindow> CreateOverlayWindowAsync(
        nint targetWindowHandle,
        Geometry.Size initialSize,
        Geometry.Point initialPosition);
    
    /// <summary>
    /// 指定されたハンドルのオーバーレイウィンドウを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>オーバーレイウィンドウ（見つからない場合はnull）</returns>
    IOverlayWindow? GetOverlayWindow(nint handle);
    
    /// <summary>
    /// すべてのオーバーレイウィンドウを閉じます
    /// </summary>
    Task CloseAllOverlaysAsync();
    
    /// <summary>
    /// アクティブなオーバーレイウィンドウの数
    /// </summary>
    int ActiveOverlayCount { get; }
}