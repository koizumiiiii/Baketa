# ウィンドウキャプチャの重複表示問題と修正方針

## 概要
現在の実装では、選択したウィンドウの上に他のウィンドウが重なっている場合、重なった部分も一緒にキャプチャされてしまう問題があります。これにより、翻訳対象として意図しないウィンドウの内容が含まれ、OCR精度と翻訳品質に影響を与えています。

## 問題の詳細

### 1. 現在の実装の問題点
- **BitBlt方式**: 画面座標の指定領域をそのままキャプチャするため、重なったウィンドウも含む
- **PrintWindow方式**: フォールバック時のみ使用され、ウィンドウ固有の内容キャプチャが十分でない
- **ウィンドウ状態管理**: 対象ウィンドウが部分的に隠れている場合の処理が不十分

### 2. 実際の影響事例
- Steamウィンドウの上にVS Code、ファイルエクスプローラーが重なった状態でキャプチャ
- 意図しないアプリケーションの日本語・英語テキストがOCR対象になる
- 翻訳結果に関係のない情報が混入する

### 3. 技術的な根本原因
```csharp
// 現在の実装（GdiScreenCapturer.cs）
bool captureSuccess = Gdi32Methods.BitBlt(
    memoryDC.DangerousGetHandle(),
    0, 0, width, height,
    screenDC.DangerousGetHandle(),
    rect.left, rect.top,  // 画面座標ベースのキャプチャ
    BitBltFlags.SRCCOPY);
```

## 影響範囲分析

### 直接影響するコンポーネント
1. **Baketa.Infrastructure.Platform.Windows.Capture.GdiScreenCapturer**
   - `CaptureWindowAsync()` メソッド
   - キャプチャ方式の優先順位
   - ウィンドウ状態チェック

2. **Baketa.Infrastructure.Platform.Windows.NativeMethods.User32Methods**
   - 追加のWindows APIの定義が必要
   - ウィンドウ状態管理API

3. **Baketa.Application.Services.Capture.AdvancedCaptureService**
   - キャプチャ結果の品質評価
   - デバッグ情報の拡充

### 間接影響するコンポーネント
1. **OCR処理**
   - PaddleOcrEngine: 不要な情報の処理負荷
   - 検出精度への影響

2. **翻訳処理**
   - 不正確な翻訳結果
   - パフォーマンス低下

3. **ユーザーエクスペリエンス**
   - 予期しない翻訳結果
   - 信頼性の低下

## 修正方針

### Phase 1: PrintWindow優先方式への変更
```csharp
// 推奨される修正アプローチ
public async Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd)
{
    // 1. ウィンドウ状態の詳細チェック
    if (!IsWindowSuitableForCapture(hWnd))
        throw new InvalidOperationException("ウィンドウがキャプチャに適していません");
    
    // 2. PrintWindowを最初に試行（ウィンドウ固有の内容）
    if (TryPrintWindow(hWnd, memoryDC, out var success) && success)
    {
        return CreateImageFromCapture();
    }
    
    // 3. 必要に応じてウィンドウを前面に移動
    if (shouldBringToFront)
    {
        BringWindowToForeground(hWnd);
        await Task.Delay(100); // ウィンドウの移動を待機
    }
    
    // 4. BitBltをフォールバックとして実行
    return await BitBltCapture(hWnd);
}
```

### Phase 2: ウィンドウ状態管理の強化
1. **ウィンドウ可視性チェック**
   ```csharp
   private bool IsWindowSuitableForCapture(IntPtr hWnd)
   {
       return User32Methods.IsWindow(hWnd) && 
              User32Methods.IsWindowVisible(hWnd) && 
              !User32Methods.IsIconic(hWnd);
   }
   ```

2. **一時的なウィンドウ前面表示**
   ```csharp
   private void BringWindowToForeground(IntPtr hWnd)
   {
       User32Methods.SetForegroundWindow(hWnd);
       User32Methods.BringWindowToTop(hWnd);
   }
   ```

### Phase 3: 高度なキャプチャオプション
1. **クライアント領域のみキャプチャ**
   ```csharp
   public async Task<IWindowsImage> CaptureClientAreaAsync(IntPtr hWnd)
   {
       User32Methods.GetClientRect(hWnd, out RECT clientRect);
       // クライアント領域のみをキャプチャ
   }
   ```

2. **重複ウィンドウの検出と警告**
   ```csharp
   private bool HasOverlappingWindows(IntPtr targetHWnd, RECT targetRect)
   {
       // 他のウィンドウが対象領域に重なっているかチェック
   }
   ```

## 必要な追加API

### User32Methods.csに追加すべきAPI
```csharp
[DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool BringWindowToTop(IntPtr hWnd);

[DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

[DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
internal static extern IntPtr GetTopWindow(IntPtr hWnd);

[DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
internal static extern IntPtr GetNextWindow(IntPtr hWnd, GetWindowCmd uCmd);
```

## 実装上の注意点

### 1. PrintWindowの制限事項
- 一部のアプリケーション（特にゲーム）では動作しない場合がある
- DirectXやOpenGLを使用するアプリケーションでは失敗する可能性
- DPI設定によって結果が変わる場合がある

### 2. パフォーマンス考慮事項
- ウィンドウの前面表示はユーザー体験に影響する
- 必要最小限での実行が重要
- キャプチャの頻度と品質のバランス

### 3. 後方互換性
- 既存の機能を壊さないよう段階的に実装
- 設定により新旧の動作を切り替え可能にする
- デバッグ情報の充実

## テスト計画

### 1. 基本動作テスト
- [ ] 単一ウィンドウのキャプチャ
- [ ] 重複ウィンドウでのキャプチャ
- [ ] 最小化/最大化状態でのキャプチャ
- [ ] 異なるDPI設定でのキャプチャ

### 2. アプリケーション別テスト
- [ ] Steam（ゲームプラットフォーム）
- [ ] ブラウザ（Chrome、Firefox、Edge）
- [ ] ゲームアプリケーション
- [ ] オフィスアプリケーション

### 3. パフォーマンステスト
- [ ] キャプチャ時間の測定
- [ ] メモリ使用量の監視
- [ ] 連続キャプチャでの安定性

## 実装優先度

### 高優先度（即座に実装）
1. PrintWindow優先方式への変更
2. ウィンドウ状態チェックの強化
3. 必要なWindows APIの追加

### 中優先度（近い将来に実装）
1. クライアント領域のみキャプチャ
2. 重複ウィンドウの検出
3. 設定による動作切り替え

### 低優先度（将来的に検討）
1. 高度なウィンドウ管理
2. 自動ウィンドウ前面表示
3. アプリケーション固有の最適化

## 参考資料

### Windows API ドキュメント
- [PrintWindow function](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)
- [BitBlt function](https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)
- [SetForegroundWindow function](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)

### 関連Issue
- Issue #79: αテスト向けOPUS-MT translation support
- 今後作成予定: Window Capture Overlay Issue

---

**作成日**: 2025-07-16  
**最終更新**: 2025-07-16  
**ステータス**: 設計完了、実装待ち