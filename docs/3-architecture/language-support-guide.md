# 言語対応ガイド

新しい言語を追加する際の対応箇所と手順をまとめたドキュメント。
翻訳対応（OCR+NLLB）と i18n（アプリUI多言語化）の両方をカバーする。

## 対応可能言語一覧

NLLB-200（スライス済み30言語）と Surya OCR の両方で対応している **20言語** が翻訳対応の上限。

| # | Baketa | NLLB | 言語 | 難易度 | i18n resx | 翻訳UI | OCR UI | 備考 |
|---|--------|------|------|:------:|:---------:|:------:|:------:|------|
| 1 | en | eng_Latn | 英語 | — | ○ (base) | ○ | ○ | 対応済み |
| 2 | ja | jpn_Jpan | 日本語 | — | ○ | ○ | ○ | 対応済み |
| 3 | zh-CN | zho_Hans | 中国語(簡体字) | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 4 | zh-TW | zho_Hant | 中国語(繁体字) | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 5 | ko | kor_Hang | 韓国語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 6 | fr | fra_Latn | フランス語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 7 | de | deu_Latn | ドイツ語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 8 | es | spa_Latn | スペイン語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 9 | ru | rus_Cyrl | ロシア語 | B | × | × | × | Noto Sans追加必要（キリル文字） |
| 10 | ar | arb_Arab | アラビア語 | C | × | × | × | **優先度最低**: RTL対応+フォント必要 |
| 11 | pt | por_Latn | ポルトガル語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 12 | it | ita_Latn | イタリア語 | A | ○ | ○ | ○ | Phase 1対応済み (Issue #475) |
| 13 | nl | nld_Latn | オランダ語 | A | × | × | × | |
| 14 | pl | pol_Latn | ポーランド語 | A | × | × | × | |
| 15 | tr | tur_Latn | トルコ語 | A | × | × | × | |
| 16 | vi | vie_Latn | ベトナム語 | A | × | × | × | |
| 17 | th | tha_Thai | タイ語 | B | × | × | × | Noto Sans Thai追加必要 |
| 18 | id | ind_Latn | インドネシア語 | A | × | × | × | |
| 19 | hi | hin_Deva | ヒンディー語 | B | × | × | × | Noto Sans Devanagari追加必要 |
| 20 | uk | ukr_Cyrl | ウクライナ語 | B | × | × | × | Noto Sans追加必要（キリル文字） |

> NLLBのみ対応（Surya非対応）の10言語: cs, hu, ro, ms, bn, fi, nb, da, el, sv
> これらはOCR認識できないため翻訳対応の対象外。

### 難易度分類

| 難易度 | 追加作業 | 対象言語数 | 言語 |
|:------:|---------|:----------:|------|
| **A** | 標準手順のみ | 14言語 | en, ja (済), zh-CN, zh-TW, ko, fr, de, es, pt, it (Phase 1済), nl, pl, tr, vi, id |
| **B** | 標準手順 + Noto Sansフォント追加 | 4言語 | ru, uk (キリル), th (タイ文字), hi (デーヴァナーガリー) |
| **C** | 標準手順 + フォント + RTLレイアウト実装 | 1言語 | ar (アラビア語) — **優先度最低** |

> **Noto Sansフォント追加後は、19言語（A+B）が同一の標準手順で対応可能。**
> アラビア語（C）のみRTL（右から左）レイアウトの実装が別途必要。

---

## Part A: 翻訳対応の追加手順

新しい言語の翻訳（OCR認識 + NLLB翻訳）を有効化する手順。
**BaketaToNllb, SuryaOcrEngine, lang_codes.json は20言語すべて定義済みのため変更不要。**

### A-1. UI翻訳言語選択に追加（必須）

**ファイル**: `Baketa.UI/Models/TranslationModels.cs`

```csharp
// SupportedLanguages に追加
public static readonly IReadOnlyList<LanguageInfo> SupportedLanguages =
[
    // ... 既存 ...
    new() { Code = "ko", DisplayName = "韓国語", NativeName = "한국어", Flag = "🇰🇷", RegionCode = "KR" },
];

// SupportedLanguagePairs に追加（両方向）
public static readonly IReadOnlyList<string> SupportedLanguagePairs =
[
    // ... 既存 ...
    "ko-ja", "ja-ko", "ko-en", "en-ko",
];
```

### A-2. OCR言語選択UIに追加（必須）

**ファイル**: `Baketa.UI/ViewModels/Settings/OcrSettingsViewModel.cs` L70-71

```csharp
LanguageOptions = ["Japanese", "English", "Chinese", "Korean", "French", ...];
TargetLanguageOptions = ["Japanese", "English", "Chinese", "Korean", "French", ...];
```

### A-3. OCR設定バリデーションに追加（必須）

**ファイル**: `Baketa.Core/Settings/OcrSettings.cs` L43-46

```csharp
[SettingMetadata(SettingLevel.Basic, "OCR", "認識言語",
    Description = "OCRで認識する言語",
    ValidValues = ["ja", "en", "zh", "ko", "fr", ..., "multi"])]
public string RecognitionLanguage { get; set; } = "ja";
```

### A-4. デフォルト言語リストで有効化（推奨）

**ファイル**: `Baketa.Core/Translation/Configuration/LanguageConfiguration.cs`

`GetDefaultSupportedLanguages()` 内の該当言語の `IsSupported = true` に変更。

### A-5. Language静的プロパティ確認（確認のみ）

**ファイル**: `Baketa.Core/Translation/Models/Language.cs`

`Language.Korean`, `Language.French` 等が既に定義されているか確認。
なければ静的プロパティと `FromCode()` の switch ケースを追加。

### A-6. 言語コード変換に追加（推奨）

**ファイル**: `Baketa.Core/Utilities/LanguageCodeConverter.cs`

```csharp
// 表示名 → コード
{ "Korean", "ko" }, { "韓国語", "ko" },
// コード → 日本語表示名
{ "ko", "韓国語" },
```

**ファイル**: `Baketa.Core/Abstractions/Translation/LanguageCodeNormalizer.cs`

```csharp
// バリエーション追加
{ "kor", "ko" }, { "korean", "ko" },
```

### 変更不要のファイル（既に定義済み）

| ファイル | 理由 |
|---------|------|
| `OnnxTranslationEngine.cs` BaketaToNllb | 19言語マッピング定義済み |
| `SuryaOcrEngine.cs` SupportedLanguages | 20言語定義済み |
| `NllbTokenizer.cs` | lang_codes.json から動的読み込み |
| `lang_codes.json` | 30言語定義済み |
| `ocr.proto` / `ocr_server_surya.py` | 言語はパラメータで渡すだけ |

---

## Part B: i18n（アプリUI多言語化）の追加手順

アプリ自体の表示言語を追加する手順。

### B-1. resxファイル作成（必須）

**ファイル**: `Baketa.UI/Resources/Strings.{lang}.resx`（新規作成）

```
Strings.resx       ← ベース（英語）: 約524エントリ
Strings.ja.resx    ← 日本語（既存）
Strings.ko.resx    ← 韓国語（新規）← これを作成
```

**ファイル**: `Baketa.Core/Resources/Messages.{lang}.resx`（新規作成）

```
Messages.resx      ← ベース（英語）
Messages.ja.resx   ← 日本語（既存）
Messages.ko.resx   ← 韓国語（新規）
```

### B-2. LocalizationService確認（確認のみ）

**ファイル**: `Baketa.UI/Services/LocalizationService.cs` L116-133

15言語が `SupportedLanguages` に定義済み:
ja, en, zh-CN, zh-TW, ko, es, fr, de, it, pt, ru, ar, hi, th, vi

**上記15言語ならコード変更不要。** それ以外（nl, pl, tr, id, uk）を追加する場合はここにも追加。

### B-3. 翻訳の作成方法

524エントリの翻訳が必要。以下のアプローチを推奨:

1. `Strings.resx`（英語ベース）を元にNLLBまたはGeminiで自動翻訳
2. ネイティブスピーカーまたはAIレビューで品質確認
3. `Strings.{lang}.resx` として保存

### B-4. Webページの多言語化（必要に応じて）

`docs/pages/` 配下の公開Webページも多言語化の対象。

**現状:**
- auth系ページ（`auth/`配下）: `auth/shared/i18n.js` で **10言語対応済み** (ja, en, ko, zh-CN, zh-TW, fr, de, it, es, pt) — Issue #475 Phase 1
- それ以外のページ: **日本語ハードコード**（多言語未対応）

| ページ | パス | 現状 | 対応方法 |
|-------|------|------|---------|
| ランディングページ | `docs/pages/index.html` | 日本語のみ | i18n対応 or 言語別HTML |
| 利用規約 | `docs/pages/terms-of-service.html` | 日本語のみ | 言語別HTML推奨（法的文書） |
| プライバシーポリシー | `docs/pages/privacy-policy.html` | 日本語のみ | 言語別HTML推奨（法的文書） |
| 料金ページ | `docs/pages/pricing.html` | 日本語のみ | i18n対応 or 言語別HTML |
| パスワードリセット | `docs/pages/forgot-password.html` | 日本語のみ | i18n.js方式で対応可能 |
| auth系ページ | `docs/pages/auth/*/index.html` | 10言語対応済み | Issue #475で8言語追加完了 |

**auth系i18nの仕組み** (`docs/pages/auth/shared/i18n.js`):
- `?lang=ko` クエリパラメータまたはブラウザ言語で自動切替（10言語対応）
- `getLanguage()` → `applyTranslations()` パターン
- `SUPPORTED_LANGUAGES` 配列で対応言語を定義、ブラウザ言語のプレフィックスマッチにも対応
- 追加言語対応: `SUPPORTED_LANGUAGES`配列と各ページのtranslationsオブジェクトに言語キーを追加

**注意**: 利用規約・プライバシーポリシーは法的文書のため、機械翻訳ではなく正式な翻訳を推奨。

---

## Part C: 言語固有の注意事項

### 難易度B: Noto Sansフォント追加が必要な言語

現在 `LanguageFontConverter.cs` は ja→Noto Sans JP, en→Noto Sans, その他→Noto Sans SC にルーティングしている。
以下の言語は独自書体を持つため、対応するNoto Sansフォントの追加が必要。

| 言語 | 書体 | 必要フォント | 対応方法 |
|------|------|-------------|---------|
| ru, uk | キリル文字 | Noto Sans (Latin+Cyrillic対応) | 既存Noto Sansで対応可能な可能性あり。要検証 |
| th | タイ文字 | Noto Sans Thai | フォントバンドル追加 + LanguageFontConverter にルーティング追加 |
| hi | デーヴァナーガリー | Noto Sans Devanagari | フォントバンドル追加 + LanguageFontConverter にルーティング追加 |

**対応手順:**
1. `Baketa.UI/Assets/Fonts/` にフォントファイルを追加
2. `Baketa.UI/Resources/FontResources.axaml` にフォントリソース定義を追加
3. `Baketa.UI/Converters/LanguageFontConverter.cs` に言語→フォントのルーティングを追加

> フォント追加完了後は、これら4言語も難易度Aと同じ標準手順で対応可能になる。

### 難易度C: アラビア語 (ar) — 優先度最低

**RTL（右から左）レイアウト対応 + アラビア文字フォントが必要。**

アラビア文字は連結形（文字の位置によって字形が変化する）を持ち、文字の形状自体が複雑。
RTLレイアウトはUI全体に影響するため、実装コストが高い。**他の19言語をすべて対応した後に着手する。**

- データモデル: `IsRightToLeft = true` フラグは `Language.cs`, `LanguageConfiguration.cs`, `LocalizationService.cs` に定義済み
- 未実装: Avalonia UI の `FlowDirection` 切替、RTL対応レイアウト調整
- 必要フォント: Noto Sans Arabic
- 追加工: テキストオーバーレイの右揃え表示、RTL言語検出ロジック

### 中国語 (zh-CN / zh-TW)

特殊処理は既に実装済みのため、標準手順のみで対応可能。

- `ChineseVariant` enum による簡体字/繁体字自動判定あり
- `ChineseVariant.cs` の `FromLanguageCode()` で処理
- OPUS-MT プレフィックス（`>>cmn_Hans<<`）自動適用

---

## チェックリストテンプレート

新言語 `{lang}` を追加する際にコピーして使用:

```
## {言語名} ({lang}) 対応

### 翻訳対応
- [ ] TranslationModels.cs: SupportedLanguages に追加
- [ ] TranslationModels.cs: SupportedLanguagePairs に追加
- [ ] OcrSettingsViewModel.cs: LanguageOptions / TargetLanguageOptions に追加
- [ ] OcrSettings.cs: ValidValues に追加
- [ ] LanguageConfiguration.cs: IsSupported = true に変更
- [ ] Language.cs: 静的プロパティ・FromCode() 確認
- [ ] LanguageCodeConverter.cs: 表示名マッピング追加
- [ ] LanguageCodeNormalizer.cs: バリエーション追加

### i18n
- [ ] Strings.{lang}.resx 作成（524エントリ）
- [ ] Messages.{lang}.resx 作成
- [ ] LocalizationService.cs: SupportedLanguages に含まれるか確認（15言語は定義済み）
- [ ] docs/pages/auth/: i18n.js の各ページ translations に追加
- [ ] docs/pages/: ランディングページ・利用規約等の多言語化（必要に応じて）

### 検証
- [ ] dotnet build 成功
- [ ] dotnet test 成功
- [ ] OCR言語選択に表示される
- [ ] 翻訳言語ペア選択に表示される
- [ ] 実際の翻訳が動作する
- [ ] アプリUI言語切替が動作する
```

---

## 関連ファイルクイックリファレンス

| ファイル | パス | 役割 |
|---------|------|------|
| TranslationModels.cs | `Baketa.UI/Models/` | 翻訳UI言語選択リスト |
| OcrSettingsViewModel.cs | `Baketa.UI/ViewModels/Settings/` | OCR言語選択UI |
| OcrSettings.cs | `Baketa.Core/Settings/` | OCR言語バリデーション |
| LanguageConfiguration.cs | `Baketa.Core/Translation/Configuration/` | デフォルト言語リスト |
| Language.cs | `Baketa.Core/Translation/Models/` | 言語モデル定義 |
| LanguageCodeConverter.cs | `Baketa.Core/Utilities/` | 表示名↔コード変換 |
| LanguageCodeNormalizer.cs | `Baketa.Core/Abstractions/Translation/` | 言語コード正規化 |
| OnnxTranslationEngine.cs | `Baketa.Infrastructure/Translation/Onnx/` | Baketa→NLLBマッピング |
| NllbTokenizer.cs | `Baketa.Infrastructure/Translation/Onnx/` | NLLBトークンID |
| SuryaOcrEngine.cs | `Baketa.Infrastructure/OCR/Engines/` | Surya対応言語宣言 |
| LocalizationService.cs | `Baketa.UI/Services/` | i18n言語切替 |
| Strings.resx / Strings.ja.resx | `Baketa.UI/Resources/` | UI文字列リソース |
| Messages.resx / Messages.ja.resx | `Baketa.Core/Resources/` | Core層メッセージリソース |
| lang_codes.json | `Models/nllb-200-onnx-int8/` | NLLB言語トークンID（30言語） |
| i18n.js | `docs/pages/auth/shared/` | auth系Webページの多言語化 |
| index.html 他 | `docs/pages/` | 公開Webページ（ランディング・利用規約等） |
| LanguageFontConverter.cs | `Baketa.UI/Converters/` | 言語→フォントルーティング |
| OverlayFontFamilyConverter.cs | `Baketa.Application/Converters/` | オーバーレイフォント選択 |

---

## 実施記録

### Phase 1: Tier 1 LTR言語サポート (Issue #475)

**対象言語**: zh-CN, zh-TW, ko, fr, de, it, es, pt（8言語）

**実施日**: 2026-03

**完了項目**:

| カテゴリ | 項目 | 状態 |
|---------|------|:----:|
| 翻訳対応 | TranslationModels.cs: SupportedLanguages / SupportedLanguagePairs | ○ |
| 翻訳対応 | OcrSettingsViewModel.cs: LanguageOptions / TargetLanguageOptions | ○ |
| 翻訳対応 | OcrSettings.cs: ValidValues | ○ |
| 翻訳対応 | LanguageConfiguration.cs: IsSupported = true | ○ |
| 翻訳対応 | Language.cs: 静的プロパティ・FromCode() | ○ |
| 翻訳対応 | LanguageCodeConverter.cs: 表示名マッピング | ○ |
| 翻訳対応 | LanguageCodeNormalizer.cs: バリエーション | ○ |
| i18n | Strings.{lang}.resx（8言語分） | ○ |
| i18n | Messages.{lang}.resx（8言語分） | ○ |
| i18n | LocalizationService.cs: SupportedLanguages | ○（定義済み） |
| i18n | auth系Webページ: i18n.js + 6ページの翻訳 | ○ |
| フォント | LanguageFontConverter.cs: 言語→フォントルーティング | ○ |
| フォント | OverlayFontFamilyConverter.cs: オーバーレイフォント | ○ |

**E2Eテスト結果**:
- ONNX (NLLB-200): 48テスト全通過（クロス言語ペア・長文含む）
- Cloud AI (Gemini): 13テスト全通過（画像翻訳・日英多言語）
- 回帰テスト: 2,550件全通過
