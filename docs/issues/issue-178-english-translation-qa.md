# Issue #178: 英語翻訳品質チェック

## 📋 概要
英語ネイティブスピーカーまたは翻訳専門家によるUIテキストの品質チェックを実施し、自然で正確な英語表現を確保します。

## 🎯 目的
- ネイティブレベルの英語表現
- 文化的な適切性の確保
- UIテキストの統一性と一貫性
- 翻訳の正確性と自然さの向上

## 📦 Epic
**Epic 5: 多言語対応** (#176 - #178)

## 🔗 依存関係
- **Blocks**: なし (β版リリース前の最終チェック)
- **Blocked by**: #177 (言語切替機能)
- **Related**: #176 (リソースファイル作成)

## 📝 要件

### 機能要件

#### 1. チェック対象
**すべてのUIテキスト**
- MainWindow のボタン、ラベル
- SettingsWindow のテキスト
- LoginWindow のフォームラベル、エラーメッセージ
- PremiumPlanDialog のマーケティングコピー
- ダイアログメッセージ
- ツールチップ
- プレースホルダーテキスト

**エラーメッセージ**
- ネットワークエラー
- 認証エラー
- 翻訳エラー
- OCRエラー

**マーケティングコピー**
- Premium機能の説明
- 広告バナーのテキスト

#### 2. チェック観点

**文法・スペル**
- スペルミス (typo) の検出
- 文法エラーの修正
- 句読点の適切性

**自然さ**
- ネイティブスピーカーにとって自然な表現
- 直訳ではなく意訳
- カジュアルさとフォーマルさのバランス

**一貫性**
- 用語統一 (例: "Translation" vs "Translate")
- トーン統一 (フレンドリー vs ビジネスライク)
- 大文字・小文字の統一 (例: "Live Translation" vs "live translation")

**文化的適切性**
- 日本語特有の表現の適切な変換
- 英語圏で理解される表現

**文字数制限**
- UIレイアウトに収まるテキスト長
- 長すぎるテキストの短縮

#### 3. レビュープロセス
1. **初回翻訳**: 開発者による機械翻訳 + 調整
2. **第一次レビュー**: 英語ネイティブスピーカーによるチェック
3. **修正**: レビュー結果を反映
4. **第二次レビュー**: 修正内容の再確認
5. **承認**: 最終版の承認

### 非機能要件

1. **品質基準**
   - 文法エラー: 0件
   - スペルミス: 0件
   - ネイティブスピーカー評価: 4.0/5.0以上

2. **納期**
   - β版リリースの1週間前までに完了

## 🏗️ 実装方針

### 1. 翻訳品質チェックリスト

#### MainWindow
| Key | 日本語 | 現在の英語 | レビュー後 | 評価 | コメント |
|-----|--------|-----------|----------|------|---------|
| `MainWindow_TargetButton` | 対象ウィンドウ選択 | Select Target Window | | ⭐⭐⭐⭐⭐ | 自然な表現 |
| `MainWindow_LiveButton` | Live翻訳 | Live Translation | | ⭐⭐⭐⭐⭐ | 問題なし |
| `MainWindow_SingleshotButton` | Singleshot | Singleshot | | ⭐⭐⭐⭐ | 技術用語として許容 |
| `MainWindow_SettingsButton` | 設定 | Settings | | ⭐⭐⭐⭐⭐ | 標準的 |
| `MainWindow_ExitButton` | 終了 | Exit | | ⭐⭐⭐⭐⭐ | 問題なし |
| `MainWindow_SelectedWindow` | [選択中: {0}] | [Selected: {0}] | | ⭐⭐⭐⭐ | 簡潔で明瞭 |
| `MainWindow_TranslationCount` | 翻訳済み: {0} | Translated: {0} | | ⭐⭐⭐⭐ | 文法的に正しい |

#### SettingsWindow
| Key | 日本語 | 現在の英語 | レビュー後 | 評価 | コメント |
|-----|--------|-----------|----------|------|---------|
| `Settings_Title` | 設定 | Settings | | ⭐⭐⭐⭐⭐ | 標準的 |
| `Settings_Theme` | テーマ | Theme | | ⭐⭐⭐⭐⭐ | 問題なし |
| `Settings_ThemeLight` | Light | Light | | ⭐⭐⭐⭐⭐ | 統一性あり |
| `Settings_ThemeDark` | Dark | Dark | | ⭐⭐⭐⭐⭐ | 統一性あり |
| `Settings_FontSize` | フォントサイズ | Font Size | | ⭐⭐⭐⭐⭐ | 自然 |
| `Settings_Language` | 言語 | Language | | ⭐⭐⭐⭐⭐ | 標準的 |
| `Settings_CurrentPlan` | 現在のプラン | Current Plan | | ⭐⭐⭐⭐⭐ | 明瞭 |
| `Settings_UpgradeToPremium` | Premiumにアップグレード | Upgrade to Premium | | ⭐⭐⭐⭐⭐ | マーケティング的に適切 |

#### LoginWindow
| Key | 日本語 | 現在の英語 | レビュー後 | 評価 | コメント |
|-----|--------|-----------|----------|------|---------|
| `Login_Title` | ログイン | Login | | ⭐⭐⭐⭐⭐ | 標準的 |
| `Login_Email` | メールアドレス | Email | | ⭐⭐⭐⭐⭐ | 簡潔 |
| `Login_Password` | パスワード | Password | | ⭐⭐⭐⭐⭐ | 標準的 |
| `Login_LoginButton` | ログイン | Login | | ⭐⭐⭐⭐⭐ | 統一性あり |
| `Login_SignUpButton` | 新規登録 | Sign Up | | ⭐⭐⭐⭐⭐ | 一般的な表現 |
| `Login_ForgotPassword` | パスワードを忘れた | Forgot Password | | ⭐⭐⭐⭐ | 疑問形 "Forgot Password?" も検討 |
| `Login_ErrorInvalidEmail` | 無効なメールアドレスです | Invalid email address | | ⭐⭐⭐⭐⭐ | 明確 |
| `Login_ErrorPasswordTooShort` | パスワードは8文字以上必要です | Password must be at least 8 characters | | ⭐⭐⭐⭐⭐ | 文法的に正しい |
| `Login_ErrorLoginFailed` | ログインに失敗しました | Login failed | | ⭐⭐⭐⭐ | "Login failed. Please try again." がより親切 |

#### PremiumPlanDialog (マーケティングコピー)
| Key | 日本語 | 現在の英語 | レビュー後 | 評価 | コメント |
|-----|--------|-----------|----------|------|---------|
| `Premium_Title` | Baketa Premium | Baketa Premium | | ⭐⭐⭐⭐⭐ | ブランド名 |
| `Premium_FeatureAdFree` | 広告非表示 | Ad-free | | ⭐⭐⭐⭐⭐ | 簡潔で効果的 |
| `Premium_FeatureCloudTranslation` | クラウド翻訳 (Google Gemini) | Cloud Translation (Google Gemini) | | ⭐⭐⭐⭐⭐ | 正確 |
| `Premium_FeaturePrioritySupport` | 優先サポート | Priority Support | | ⭐⭐⭐⭐⭐ | 標準的 |
| `Premium_FeatureEarlyAccess` | 新機能への優先アクセス | Early Access to New Features | | ⭐⭐⭐⭐⭐ | マーケティング的に適切 |
| `Premium_Monthly` | 月額 ¥500 | ¥500/month | | ⭐⭐⭐⭐⭐ | 簡潔 |
| `Premium_Yearly` | 年額 ¥5,000 (17% OFF) | ¥5,000/year (17% OFF) | | ⭐⭐⭐⭐⭐ | セール表記として適切 |
| `Premium_Cancel` | キャンセル | Cancel | | ⭐⭐⭐⭐⭐ | 標準的 |

#### エラーメッセージ
| Key | 日本語 | 現在の英語 | レビュー後 | 評価 | コメント |
|-----|--------|-----------|----------|------|---------|
| `Error_NetworkError` | ネットワークエラーが発生しました | Network error occurred | | ⭐⭐⭐⭐ | "A network error occurred" がより自然 |
| `Error_AuthenticationFailed` | 認証に失敗しました | Authentication failed | | ⭐⭐⭐⭐⭐ | 明確 |
| `Error_TranslationFailed` | 翻訳に失敗しました | Translation failed | | ⭐⭐⭐⭐ | "Translation failed. Please try again." がより親切 |
| `Error_OcrFailed` | OCRに失敗しました | OCR failed | | ⭐⭐⭐⭐ | "OCR processing failed" がより明確 |
| `Error_WindowNotFound` | 対象ウィンドウが見つかりません | Target window not found | | ⭐⭐⭐⭐⭐ | 明瞭 |

### 2. レビュー実施方法

#### 方法1: クラウドソーシング (推奨)
- **プラットフォーム**: Upwork, Fiverr, Gengo
- **募集条件**: 英語ネイティブスピーカー、UIテキスト翻訳経験者
- **納品物**: レビュー済みリソースファイル、コメント付きスプレッドシート
- **費用**: $50-100 (約¥7,500-15,000)
- **納期**: 3-5営業日

#### 方法2: コミュニティレビュー
- **Reddit**: r/translator, r/languagelearning
- **Discord**: 翻訳コミュニティサーバー
- **GitHub**: Issue/PRでのレビュー依頼
- **費用**: 無料 (ボランティアベース)
- **納期**: 1-2週間

#### 方法3: AI支援レビュー（バックアッププラン）
- **ツール**: ChatGPT, Claude, DeepL, Grammarly
- **用途**: 初期チェック、文法ミス検出、自然さの評価
- **実施手順**:
  1. **Grammarly**: 文法・スペルミスの自動検出
  2. **ChatGPT/Claude**: 各翻訳の自然さを5段階評価
  3. **DeepL**: プロ翻訳との比較
- **注意**: AIのみに依存せず、可能な限り人間によるレビューを実施
- **利用シーン**:
  - ネイティブレビュアーが確保できない場合
  - 初期チェックの品質向上
  - 第二次レビュー前の事前チェック

#### AIレビュープロンプト例
```
以下の英語翻訳について、ネイティブスピーカーの観点からレビューしてください:

Key: MainWindow_LiveButton
Japanese: Live翻訳
English: Live Translation

評価基準:
1. 文法・スペル: 正しいか
2. 自然さ: ネイティブにとって自然な表現か
3. 簡潔さ: UIボタンに適した長さか
4. 一貫性: 他の翻訳と用語が統一されているか

評価 (1-5):
改善案:
コメント:
```

### 3. レビューシート例 (Google Sheets)

**列構成**:
- **A列**: リソースキー
- **B列**: 日本語 (原文)
- **C列**: 現在の英語
- **D列**: レビュー後の英語
- **E列**: 評価 (⭐1-5)
- **F列**: コメント
- **G列**: ステータス (未確認/修正中/承認済み)

### 4. 承認プロセス
1. レビュアーが **D列** に修正案を記入
2. レビュアーが **E列** に評価を入力
3. レビュアーが **F列** にコメントを記入
4. 開発者が修正内容を確認し、**G列** を「承認済み」に変更
5. `Strings.en.resx` を更新
6. 第二次レビュー実施 (必要に応じて)

### 5. 品質チェックリスト

#### 文法・スペルチェック
- [ ] すべての翻訳にスペルミスがない
- [ ] 文法エラーがない（Grammarlyで検証）
- [ ] 句読点が適切に使用されている
- [ ] 冠詞（a, an, the）が正しく使用されている

#### 自然さチェック
- [ ] ネイティブスピーカーにとって自然な表現
- [ ] 直訳ではなく、意訳が適切に行われている
- [ ] カジュアル/フォーマルのトーンが一貫している
- [ ] 業界標準の用語が使用されている

#### 一貫性チェック
- [ ] 用語が統一されている（例: "Translation" vs "Translate"）
- [ ] 大文字・小文字の使い方が統一されている
- [ ] 数値フォーマットが統一されている
- [ ] 日付フォーマットが統一されている

#### 文化的適切性チェック
- [ ] 日本語特有の表現が適切に変換されている
- [ ] 英語圏で理解される表現になっている
- [ ] 文化的に不適切な表現がない
- [ ] ユーモアや婉曲表現が適切に翻訳されている

#### UI/UXチェック
- [ ] ボタンテキストが20文字以内
- [ ] ラベルテキストが30文字以内
- [ ] エラーメッセージが明確で理解しやすい
- [ ] ダイアログメッセージが簡潔で行動指向

#### 技術的チェック
- [ ] フォーマット文字列（{0}, {1}）が正しく配置されている
- [ ] リソースキーの命名規則に従っている
- [ ] すべてのキーが日本語と英語で定義されている
- [ ] エスケープ文字が正しく処理されている

### 6. 自動テスト (文字数制限チェック)
```csharp
public class TranslationLengthTests
{
    [Theory]
    [InlineData("MainWindow_TargetButton", 30)]  // ボタンは30文字以内
    [InlineData("MainWindow_LiveButton", 20)]
    [InlineData("Settings_UpgradeToPremium", 40)]
    public void EnglishTranslation_文字数制限内(string key, int maxLength)
    {
        // Arrange
        Strings.Culture = new CultureInfo("en-US");

        // Act
        var text = Strings.ResourceManager.GetString(key, Strings.Culture);

        // Assert
        text.Should().NotBeNullOrEmpty();
        text!.Length.Should().BeLessOrEqualTo(maxLength,
            $"{key} の英語翻訳が長すぎます (最大{maxLength}文字): \"{text}\"");
    }
}
```

## ✅ 受け入れ基準

### レビュー完了基準
- [ ] すべてのUIテキストがレビュー済み
- [ ] すべてのエラーメッセージがレビュー済み
- [ ] すべてのマーケティングコピーがレビュー済み
- [ ] 文法エラー: 0件
- [ ] スペルミス: 0件
- [ ] ネイティブスピーカー評価: 平均4.0/5.0以上

### レビュー品質基準
- [ ] 自然で読みやすい英語表現
- [ ] 用語統一されている
- [ ] トーンが統一されている
- [ ] 文化的に適切な表現
- [ ] UIレイアウトに収まる文字数

### 修正完了基準
- [ ] レビュー結果を `Strings.en.resx` に反映
- [ ] 文字数制限テストが全て通過
- [ ] 実際のUIで表示確認
- [ ] 第二次レビュー (必要に応じて) 完了

## 📊 見積もり
- **作業時間**: 8時間
  - レビュー依頼準備: 2時間（チェックリスト作成、レビュアー選定）
  - レビュー期間: 3-5営業日（外部委託）
  - 修正作業: 3時間（レビュー結果反映、テスト実行）
  - 再確認・承認: 3時間（第二次レビュー、最終チェック）
- **外部費用**: $50-100 (約¥7,500-15,000)
- **優先度**: 🟡 Medium
- **リスク**: 🟢 Low
  - **主要リスク**: レビュアーの確保、レビュー期間の遅延
  - **軽減策**: 複数のレビュアー候補を確保、バックアッププラン（AI支援レビュー）、明確な納期設定

## 📌 備考
- レビューは β版リリース前の最終チェック
- v1.0リリース前に再度レビュー実施を推奨
- 将来的に他言語 (中国語、韓国語等) 追加時も同様のレビューを実施
- レビュー結果はドキュメントとして保存し、今後の参考資料とする

## 📄 レビュー依頼テンプレート (英語)

```
Title: UI Text Review for Baketa (Windows Translation Overlay App)

Description:
We are looking for a native English speaker to review and improve the UI text for Baketa, a real-time translation overlay application for Windows games.

Requirements:
- Native English speaker
- Experience with UI/UX copywriting (preferred)
- Familiarity with software localization

Deliverables:
- Reviewed resource file (Strings.en.resx)
- Comments and suggestions in Google Sheets
- Rating (1-5 stars) for each text item

Scope:
- Approximately 80-100 text items (buttons, labels, error messages, marketing copy)
- No technical writing required

Budget: $50-100
Timeline: 3-5 business days

Please include samples of your previous UI text review work.
```
