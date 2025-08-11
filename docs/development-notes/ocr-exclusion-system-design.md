# OCR除外システム設計仕様書

## 概要

Baketaのリアルタイム翻訳システムにおいて、ユーザーの意図しないテキスト（UI要素、装飾文字、背景テキスト等）の翻訳表示を制御するためのシステム設計について検討した結果をまとめる。

## 問題の背景

### 現在の課題
- 全画面OCRによる過検出問題
- ユーザーの意図しないテキスト（HP/MP等のステータス、装飾文字）まで翻訳表示
- 固定的な領域選択では動的なゲーム画面に対応困難
- ユーザー体験として洗練されていない

### 従来アプローチの限界
- **完全非表示方式**: 後で必要になった時の復旧が困難
- **ワード個別対応**: 無限の言語・ゲーム固有用語に対応不可能
- **事前領域指定**: ゲームのUI変更に柔軟に対応できない

## 推奨解決策：最小化表示システム

### 基本コンセプト
```
完全除外 → 最小化表示 + インタラクティブ展開
```

**核心思想**: 「情報を失わず、邪魔にならず、必要時にアクセス可能」

## プラン別機能差別化

Baketaのサブスクリプションプラン（Issue #76）に基づき、OCR除外機能もプラン別に差別化を実装する。

### プラン構成と機能

#### 🆓 **Free プラン**
- **価格**: 無料
- **OCR除外機能**: 基本的な構造・位置ベース除外のみ
- **分析手法**: ヒューリスティック（ルールベース）
- **学習機能**: ローカル学習（限定的）
- **広告**: あり

#### 💰 **Standard プラン**
- **価格**: 月額 100円
- **OCR除外機能**: Free プランと同等（広告なし）
- **分析手法**: ヒューリスティック（ルールベース）
- **学習機能**: ローカル学習（限定的）
- **広告**: なし

#### 💎 **Pro プラン**
- **価格**: 月額 800円
- **OCR除外機能**: AI による高精度文脈分析
- **分析手法**: Gemini Vision API による画像解析
- **学習機能**: クラウド学習同期 + ローカル学習
- **広告**: なし
- **特別機能**: ゲーム特化最適化、予測的最小化

### システム設計

#### 1. 表示状態管理
```csharp
public enum DisplayState
{
    Normal,      // 通常表示
    Minimized,   // 最小化（小さな点やアイコン）
    Expanded,    // 一時展開（ホバー時）
    Hidden       // 完全非表示（オプション）
}

public class MinimizedTranslationElement
{
    public string OriginalText { get; set; }
    public string Translation { get; set; }
    public DisplayState State { get; set; } = DisplayState.Minimized;
    public Rectangle MinimizedBounds { get; set; }
    public Rectangle ExpandedBounds { get; set; }
}
```

#### 2. 最小化スタイル
```csharp
public enum MinimizationStyle
{
    Dot,           // 小さな点 (⚪)
    Icon,          // アイコン表示 (🎮)
    Abbreviation,  // 略語表示 (H M E)
    Bar,           // 小さなバー (━━)
    Number         // 数字のみ (3個)
}
```

#### 3. インタラクション設計
```
通常翻訳: この魔法は強力です
────────────────────────────
最小化UI: ⚪ HP  ⚪ MP  ⚪ EXP
         └─ クリック/ホバーで展開
```

**展開トリガー**:
- **ホバー**: 一時的な内容確認（3秒間表示）
- **クリック**: 展開/縮小の切り替え
- **ダブルクリック**: 永続的な展開

## 技術実装設計

### プラン別実装アーキテクチャ

#### プラン判定とサービス分岐
```csharp
public class PlanBasedContextAnalyzer : IContextAnalyzer
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IGeminiVisionAnalyzer _geminiAnalyzer;     // Pro プラン専用
    private readonly IHeuristicAnalyzer _heuristicAnalyzer;     // Free/Standard プラン
    
    public async Task<ContextAnalysisResult> AnalyzeAsync(
        IImage screenCapture, 
        List<OCRResult> ocrResults)
    {
        var subscription = await _subscriptionService.GetCurrentSubscriptionAsync();
        
        return subscription.Plan switch
        {
            SubscriptionPlan.Pro => 
                await _geminiAnalyzer.AnalyzeScreenAsync(screenCapture, ocrResults),
            
            SubscriptionPlan.Standard or SubscriptionPlan.Free => 
                await _heuristicAnalyzer.AnalyzeAsync(ocrResults),
            
            _ => await _heuristicAnalyzer.AnalyzeAsync(ocrResults)
        };
    }
}
```

### 1. 構造・空間ベース除外システム (Free/Standard プラン)

#### パラダイムシフト
```
従来: 「何を除外するか」(言語依存・スケールしない)
新規: 「どこを除外するか」(言語非依存・スケール可能)
```

#### 空間認識アルゴリズム
```csharp
public class SpatialExclusionAnalyzer
{
    public bool IsLikelyUIElement(OCRResult result, ScreenContext context)
    {
        return 
            IsInFixedUIArea(result.Bounds) ||           // 固定UI領域
            IsSmallIsolatedText(result) ||              // 小さい孤立テキスト
            IsNumericalOnlyInCorner(result) ||          // 角の数値のみ
            IsRepeatingAtFixedInterval(result);         // 定期更新される固定位置
    }
}
```

#### 視覚的特徴検出
```csharp
public class VisualPatternAnalyzer
{
    public bool IsUIDecoration(OCRResult result, ImageContext imageContext)
    {
        return 
            HasUIBackgroundPattern(result.Bounds, imageContext) ||  // UI背景パターン
            IsPartOfProgressBar(result, imageContext) ||            // プログレスバー的
            HasIconNearby(result.Bounds, imageContext) ||           // アイコン併設
            IsInBorderedArea(result.Bounds, imageContext);          // 枠線内の固定表示
    }
}
```

### 2. AI分析システム (Pro プラン専用)

#### Gemini Vision API による文脈分析
```csharp
public class GeminiVisionAnalyzer : IGeminiVisionAnalyzer
{
    private readonly IGeminiVisionService _geminiVision;
    private readonly IContextAnalysisCache _cache;
    private readonly IAPIUsageController _usageController;
    
    public async Task<ContextAnalysisResult> AnalyzeScreenAsync(
        IImage screenCapture, 
        List<OCRResult> ocrResults)
    {
        // API使用量制限チェック
        if (!await _usageController.CanMakeRequestAsync())
        {
            // 制限達成時はヒューリスティック分析にフォールバック
            return await _fallbackAnalyzer.AnalyzeAsync(ocrResults);
        }
        
        // 画像にOCR結果の境界を重畳
        var annotatedImage = AnnotateImageWithBounds(screenCapture, ocrResults);
        
        // Gemini Vision APIでの分析
        var prompt = BuildContextAnalysisPrompt(ocrResults);
        var analysis = await _geminiVision.AnalyzeImageAsync(annotatedImage, prompt);
        
        // 使用量記録
        await _usageController.RecordUsageAsync(analysis.ProcessingTime, screenCapture.Size);
        
        return ParseAnalysisResult(analysis, ocrResults);
    }
    
    private string BuildContextAnalysisPrompt(List<OCRResult> ocrResults)
    {
        var textList = string.Join("\n", ocrResults.Select((r, i) => $"{i+1}. \"{r.Text}\""));
        
        return $"""
            この画像はゲーム画面です。赤い境界線で囲まれた各テキストについて、以下の観点で分析してください：

            検出されたテキスト:
            {textList}

            各テキストについて以下をJSON形式で回答してください：
            {{
                "text_analysis": [
                    {{
                        "id": 1,
                        "text": "検出されたテキスト",
                        "category": "dialogue|status|menu|decoration|system|narrative",
                        "importance": "high|medium|low",
                        "translation_worthy": true/false,
                        "reason": "判定理由"
                    }}
                ]
            }}

            判定基準:
            - dialogue/narrative: プレイヤーが読むべき会話・ストーリー（翻訳価値: 高）
            - status: HP/MP等のステータス表示（翻訳価値: 低）  
            - menu: メニュー・ボタンテキスト（翻訳価値: 中）
            - decoration: 装飾的なテキスト（翻訳価値: 低）
            - system: システムメッセージ（翻訳価値: 中）
            """;
    }
}
```

#### Pro プラン限定UI
```csharp
public class ProPlanPromptService
{
    public async Task ShowAIAnalysisInProgressAsync()
    {
        await _notificationService.ShowAsync(
            "🤖 AI分析中... 高精度な文脈理解でテキストを分類しています",
            NotificationType.Information);
    }
    
    public async Task ShowUpgradePromptAsync()
    {
        var message = """
            🚀 Pro プランでAI分析をお試しください！
            
            ✨ Gemini AI がゲーム画面を理解し、重要なテキストのみを自動選別
            📊 ゲームジャンル別の最適化で精度向上
            🎯 手動設定不要の知的な除外システム
            
            初回14日間無料トライアル
            """;
            
        await _notificationService.ShowAsync(message, NotificationType.Upgrade);
    }
}
```

### 3. 学習システム設計

#### ネガティブフィードバック学習
```
全OCR実行 → オーバーレイ表示 → ユーザー除外指示 → 学習蓄積 → 次回自動最小化
```

**学習データ構造**:
```csharp
public class MinimizationPattern
{
    public string TextPattern { get; set; }
    public Rectangle RelativePosition { get; set; }    // 相対位置
    public SizeRange SizeRange { get; set; }          // サイズ範囲
    public VisualFeatures VisualContext { get; set; } // 視覚的特徴
    public MinimizationStyle PreferredStyle { get; set; }
    public bool AutoMinimize { get; set; }
}
```

#### 構造学習システム
```csharp
public class StructuralLearning
{
    public void LearnExclusionArea(Rectangle excludedArea, ScreenContext context)
    {
        var pattern = new ExclusionPattern
        {
            RelativePosition = NormalizePosition(excludedArea),    // 相対位置
            SizeRange = CalculateSizeRange(excludedArea),          // サイズ範囲
            VisualContext = ExtractVisualFeatures(excludedArea),   // 視覚的特徴
            BehaviorPattern = AnalyzeBehavior(excludedArea)        // 行動パターン
        };
        
        _patterns.Add(pattern);
    }
}
```

### 3. アニメーションシステム

#### 展開アニメーション
```csharp
public class ExpandAnimationService
{
    public async Task ExpandElementAsync(MinimizedTranslationElement element)
    {
        // スムーズな拡大アニメーション
        await AnimateAsync(
            from: element.MinimizedBounds,
            to: element.ExpandedBounds,
            duration: TimeSpan.FromMilliseconds(200),
            easing: EasingType.EaseOutQuart
        );
        
        // テキスト内容のフェードイン
        await FadeInTextAsync(element.Translation);
    }
}
```

## ゲーム特化最適化

### ジャンル別戦略
```csharp
public class GenreSpecificMinimization
{
    public MinimizationStrategy GetStrategy(GameGenre genre)
    {
        return genre switch
        {
            GameGenre.RPG => new RPGMinimization
            {
                StatusElements = MinimizationStyle.Abbreviation,  // HP → H
                MenuItems = MinimizationStyle.Icon,               // アイコン化
                FlavorText = MinimizationStyle.Dot                // 点で表示
            },
            
            GameGenre.Action => new ActionMinimization
            {
                QuickInfo = MinimizationStyle.Bar,                // 邪魔にならないバー
                StatusUpdates = MinimizationStyle.Number          // 数字のみ
            },
            
            _ => new DefaultMinimization()
        };
    }
}
```

## ユーザー設定システム

### 設定UI設計
```
┌─ 最小化設定 ─────────────────────┐
│ ○ ホバーで一時展開                │
│ ○ クリックで展開/縮小              │
│ ○ 自動最小化の積極性              │
│   控えめ ←●────→ 積極的          │
│                                 │
│ ○ 最小化スタイル                 │
│   [点] [アイコン] [略語] [バー]    │
│                                 │
│ □ ゲーム固有の学習を有効          │
└─────────────────────────────────┘
```

### プライバシー対応とプラン別データ管理

#### プラン別データ管理戦略
| プラン | 学習データ | プライバシー | クラウド同期 | 精度 |
|--------|-----------|-------------|-------------|------|
| **Free** | ローカルのみ | ★★★★★ | なし | ★★☆☆☆ |
| **Standard** | ローカルのみ | ★★★★★ | なし | ★★☆☆☆ |
| **Pro** | ローカル + クラウド | ★★★★☆ | オプトイン | ★★★★★ |

#### Pro プラン向けクラウド学習
```csharp
public class ProPlanLearningService
{
    public async Task<bool> EnableCloudLearningAsync()
    {
        // Pro プランユーザーのみクラウド学習が利用可能
        var subscription = await _subscriptionService.GetCurrentSubscriptionAsync();
        if (subscription.Plan != SubscriptionPlan.Pro)
        {
            await ShowUpgradeRequiredAsync("クラウド学習機能");
            return false;
        }
        
        // プライバシー同意の確認
        var consent = await _privacyService.RequestConsentAsync(
            DataUsageType.CloudLearning,
            "Pro プラン特典：AIの精度向上のため、匿名化された学習データをクラウドで共有しますか？");
        
        if (consent)
        {
            await _cloudLearningService.EnableAsync();
            return true;
        }
        
        return false;
    }
}
```

#### GDPR準拠
```csharp
public enum LearningDataType
{
    LocalOnly,              // ローカル学習のみ
    AnonymousContribution,  // 匿名化してクラウド貢献
    NoLearning             // 学習機能無効
}

// 既存のPrivacyConsentServiceを活用
await _privacyService.RequestConsentAsync(
    DataUsageType.LearningDataCollection,
    "学習データをクラウドで集約して全体の除外精度を向上させますか？"
);
```

## 実装ロードマップ

### Phase 1: 基本最小化システム (1週間)
- 最小化状態の表示（点スタイル）
- クリック展開機能
- 基本アニメーション
- 状態管理システム
- **プラン判定システム基盤**

### Phase 2: Free/Standard プラン機能 (1-2週間)
- ホバー一時展開
- 複数の最小化スタイル
- ヒューリスティック分析システム
- ローカル学習システム
- 設定UI実装

### Phase 3: Pro プラン機能 (2-3週間)
- **Gemini Vision API統合**
- **AI文脈分析システム**
- **クラウド学習機能**
- **API使用量制御**
- **プレミアム機能UI**

### Phase 4: 高度化・最適化 (2-4週間)
- ゲーム特化最適化（Pro プラン）
- 予測的最小化（Pro プラン）
- パフォーマンス最適化
- 包括的テスト
- **サブスクリプション連携**

## 技術的実現可能性評価

### ✅ 高実現可能性
- **基本最小化**: AvaloniaのTransform機能で実装
- **状態管理**: 単純なenum状態機械
- **クリック検出**: 既存オーバーレイシステム拡張
- **位置ベース除外**: OCRから座標情報取得済み

### ✅ 中実現可能性 (全プラン)
- **アニメーション**: Avaloniaアニメーションライブラリ
- **視覚パターン認識**: OpenCVでの境界・背景検出
- **学習システム**: 既存設定システムの拡張

### 💎 **Pro プラン限定機能 (高実現可能性)**
- **Gemini Vision API分析**: 既存Gemini統合基盤を活用
- **AI文脈判定**: プロンプトベースの高精度分析
- **クラウド学習**: 既存プライバシーシステムと統合
- **API使用量制御**: 既存サブスクリプション管理と連携

### ⚠️ 将来拡張機能
- **ゲーム連動**: ゲーム状態との同期
- **マルチモーダル分析**: 音声・動作パターン統合

## 期待される効果

### ユーザー体験の向上
1. **情報損失ゼロ**: すべての情報にアクセス可能
2. **直感的操作**: クリック/ホバーで即座に確認
3. **学習適応**: ユーザーの使用パターンに自動最適化
4. **ゲーム邪魔しない**: 最小限の画面占有

### 技術的利点
1. **言語非依存**: どの言語のゲームでも動作
2. **スケーラブル**: パターン数が有限で管理可能
3. **保守性**: 新しい言語・ゲーム用語への対応不要
4. **拡張性**: 新しい最小化スタイルや学習方法の追加容易

## ビジネス価値とユーザー価値

### Free/Standard プランでの価値提供
- **基本除外機能**: 構造・位置ベースの効果的な除外
- **手動カスタマイズ**: ユーザーの好みに応じた調整可能
- **十分な機能性**: 基本的なゲーム翻訳体験を損なわない

### Pro プランでの付加価値
- **AI による知的判断**: 人間のような文脈理解
- **自動最適化**: 手動設定が不要
- **ゲーム特化**: ジャンル別の最適化
- **継続的改善**: クラウド学習による精度向上

### 収益モデル
```
Gemini Vision API コスト: 約 ¥50-150/月/ユーザー
Pro プラン価格: ¥800/月
粗利: ¥650-750/月/ユーザー

付加価値:
- 手動設定の手間を大幅削減
- ゲーム体験の向上
- 継続的な機能改善
```

## まとめ

この「プラン別最小化表示システム」は、従来の除外アプローチの課題を根本的に解決する革新的なソリューションである。

**Free/Standard プラン**では基本的だが十分な機能を提供し、**Pro プラン**では AI による高精度な文脈理解で圧倒的な付加価値を提供する。完全な情報保持と直感的なユーザーインタラクションを両立し、Baketaのリアルタイム翻訳システムの使い勝手を大幅に向上させると同時に、持続可能なビジネスモデルの構築に貢献する。

実装は段階的に進め、まず全プラン向けの基本機能を確立してから Pro プラン限定機能を追加することで、技術的リスクを最小化しながら確実に品質向上と収益化を図ることができる。