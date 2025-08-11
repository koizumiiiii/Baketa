# AR風翻訳UI完全置き換え設計書

## 概要

Google翻訳のカメラ機能のような、元テキストが翻訳後テキストに置き換わったように見えるAR風UIを実装する。
現在のオーバーレイシステムを完全に置き換え、設定項目を大幅に簡素化する。

## 設計コンセプト

### 基本方針
- **完全な置き換え**: 元テキストの正確な位置に翻訳テキストを重ね表示
- **自動調整**: フォントサイズ、色、透明度は全て自動計算
- **設定レス**: 追加設定項目なし、既存の不要設定も削除
- **段階的移行**: 旧コードは@Obsolete化して保持、完全移行後に削除

### 技術的アプローチ
- OCR結果の`TextChunk.CombinedBounds`を活用した正確な位置合わせ
- バウンディングボックスの高さからフォントサイズを自動計算
- 元テキストを完全に隠す背景色の自動選択

## 実装アーキテクチャ

### 1. ARTranslationOverlayWindow（新規作成）

```csharp
/// <summary>
/// AR風翻訳表示専用オーバーレイウィンドウ
/// 元テキストの正確な位置に翻訳テキストを重ね表示
/// </summary>
public partial class ARTranslationOverlayWindow : Window
{
    // 自動フォントサイズ計算
    private int CalculateOptimalFontSize(Rectangle textBounds, string translatedText)
    {
        // OCR領域の高さの80%をベースにフォントサイズを計算
        var baseFontSize = (int)(textBounds.Height * 0.8);
        
        // 翻訳テキストの長さに応じて調整
        var textLengthFactor = Math.Min(1.0, (double)textBounds.Width / (translatedText.Length * baseFontSize * 0.6));
        
        return Math.Max(8, (int)(baseFontSize * textLengthFactor));
    }
    
    // 自動背景色計算（元テキスト隠蔽用）
    private Color CalculateOptimalBackgroundColor(Rectangle textBounds)
    {
        // 元画像の背景色を検出し、適切な隠蔽色を計算
        // 初期実装では白/黒の自動選択
        return Colors.White; // TODO: 画像解析による動的色選択
    }
    
    // 完全位置重ね表示
    public void ShowAtExactPosition(TextChunk chunk)
    {
        // 元テキストと同じ位置・サイズに配置
        Position = new PixelPoint(chunk.CombinedBounds.X, chunk.CombinedBounds.Y);
        Width = chunk.CombinedBounds.Width;
        Height = chunk.CombinedBounds.Height;
        
        // 自動調整されたスタイルを適用
        var fontSize = CalculateOptimalFontSize(chunk.CombinedBounds, chunk.TranslatedText);
        var backgroundColor = CalculateOptimalBackgroundColor(chunk.CombinedBounds);
        
        ApplyARStyle(fontSize, backgroundColor);
    }
}
```

### 2. TextChunk拡張（AR用メソッド追加）

```csharp
/// <summary>
/// AR表示用の拡張メソッド
/// </summary>
public sealed class TextChunk
{
    // 既存のメンバーは維持...
    
    /// <summary>
    /// AR表示用の正確な位置を取得
    /// </summary>
    public Point GetARPosition() => new(CombinedBounds.X, CombinedBounds.Y);
    
    /// <summary>
    /// AR表示用のサイズを取得
    /// </summary>
    public Size GetARSize() => new(CombinedBounds.Width, CombinedBounds.Height);
    
    /// <summary>
    /// AR表示用の最適フォントサイズを計算
    /// </summary>
    public int CalculateARFontSize()
    {
        // OCR領域の高さの80%をデフォルトとして計算
        var baseFontSize = (int)(CombinedBounds.Height * 0.8);
        return Math.Max(8, Math.Min(72, baseFontSize));
    }
    
    /// <summary>
    /// AR表示が可能かどうかを判定
    /// </summary>
    public bool CanShowAR()
    {
        return CombinedBounds.Width > 0 && CombinedBounds.Height > 0 && 
               !string.IsNullOrEmpty(TranslatedText);
    }
}
```

### 3. 設定システム簡素化

#### 削除対象の設定項目
- **フォントサイズ設定** → 自動計算に置き換え
- **透明度設定** → 不要（完全置き換えのため）
- **色設定** → 自動計算に置き換え
- **マスキング設定** → 不要（常に有効）

#### 影響を受けるファイル
- `Baketa.Core/Settings/OverlaySettings.cs` - 不要プロパティ削除
- `Baketa.UI/Views/Settings/OverlaySettingsView.axaml` - UI要素削除
- `Baketa.UI/ViewModels/Settings/OverlaySettingsViewModel.cs` - プロパティ削除

### 4. 移行戦略

#### Phase 1: 新ARシステム実装
1. `ARTranslationOverlayWindow`の新規作成
2. `TextChunk`にAR用メソッド追加
3. AR専用のマネージャクラス作成

#### Phase 2: 旧システム@Obsolete化
1. `TranslationOverlayWindow`に`[Obsolete]`属性追加
2. `TranslationResultOverlayManager`に`[Obsolete]`属性追加
3. 旧式表示メソッドに非推奨マーク

#### Phase 3: 設定システム更新
1. 不要な設定プロパティを削除
2. 設定UIから該当要素を削除
3. 設定バリデーションの更新

#### Phase 4: 完全移行後のクリーンアップ
1. @Obsolete化したクラス・メソッドの削除
2. 不要な設定ファイルの削除
3. テストケースの更新

## 実装の利点

### ユーザー体験
- ✅ **直感的UX**: Google翻訳カメラのような自然な体験
- ✅ **設定不要**: 複雑な設定なしで最適な表示
- ✅ **高精度表示**: OCR座標に基づく正確な位置合わせ

### 技術的利点
- ✅ **保守性向上**: コード量削減とシンプル化
- ✅ **テスタビリティ**: 設定項目減少により単体テストが簡素化
- ✅ **安全な移行**: 旧コード保持で後戻り可能

### アーキテクチャ的利点
- ✅ **既存資産活用**: `TextChunk`、`PositionedTextResult`の詳細位置情報活用
- ✅ **イベント駆動維持**: 現在のアーキテクチャパターンを継承
- ✅ **技術的負債削減**: 不要な設定システムの削除

## 実装スケジュール

### Week 1: 基盤実装
- [ ] `ARTranslationOverlayWindow`新規作成
- [ ] `TextChunk`AR用メソッド追加
- [ ] AR専用マネージャ実装

### Week 2: 統合・テスト
- [ ] 既存システムとの統合
- [ ] 視覚的テスト・調整
- [ ] パフォーマンステスト

### Week 3: 設定システム更新
- [ ] 不要設定項目削除
- [ ] 設定UI更新
- [ ] 移行テスト

### Week 4: 完全移行
- [ ] 旧システム@Obsolete化
- [ ] ドキュメント更新
- [ ] 最終テスト

## リスク管理

### 技術的リスク
- **フォントサイズ自動計算の精度**: 様々なゲーム・解像度での検証が必要
- **位置合わせの精度**: DPI設定やスケーリングの影響を考慮

### 緩和策
- 段階的実装による問題の早期発見
- 旧システム保持による安全な後戻り
- 豊富なテストケースによる品質保証

## 成功指標

### 定量的指標
- 設定項目数: 現在の50%以下に削減
- コード行数: オーバーレイ関連コードの30%削減
- テスト実行時間: 設定関連テストの削除により短縮

### 定性的指標
- ユーザビリティの向上
- 直感的な操作感の実現
- 保守性の改善

---

**作成日**: 2025-07-28  
**作成者**: Claude Code  
**ステータス**: 設計完了・実装待ち