# 翻訳モデル差異分析レポート

このレポートは旧名前空間 Baketa.Core.Models.Translation と新名前空間 Baketa.Core.Translation.Models の間の機能差異を分析したものです。

## クラス別差異分析

### Language

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.Language`
- 新名前空間: `Baketa.Core.Translation.Models.Language`
- 旧ファイル行数: 132
- 新ファイル行数: 153

#### 主要な差異

##### 旧名前空間のみの機能

- プロパティ:
  - `NativeName`
  - `RegionCode`
  - `IsAutoDetect`


##### 新名前空間のみの機能

- プロパティ:
  - `IsRightToLeft`


- メソッド:
  - `FromCode()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### LanguageDetectionModels

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.LanguageDetectionModels`
- 新名前空間: `Baketa.Core.Translation.Models.LanguageDetectionModels`
- 旧ファイル行数: 58
- 新ファイル行数: 159

#### 主要な差異

##### 旧名前空間のみの機能

- プロパティ:
  - `LanguageDetectionResult`
  - `DetectedLanguage`
  - `AlternativeLanguages`
  - `ProcessingTimeMs`
  - `LanguageDetection`
  - `Language`


##### 新名前空間のみの機能

- メソッド:
  - `AddAlternativeLanguage()`
  - `AddAlternativeLanguage()`
  - `Clone()`
  - `Clone()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### LanguagePair

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.LanguagePair`
- 新名前空間: `Baketa.Core.Translation.Models.LanguagePair`
- 旧ファイル行数: 59
- 新ファイル行数: 99

#### 主要な差異

##### 旧名前空間のみの機能

- メソッド:
  - `Create()`
  - `Equals()`


##### 新名前空間のみの機能

- プロパティ:
  - `LanguagePair`


- メソッド:
  - `FromString()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### TranslationError

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.TranslationError`
- 新名前空間: `Baketa.Core.Translation.Models.TranslationError`
- 旧ファイル行数: 173
- 新ファイル行数: 152

#### 主要な差異

##### 旧名前空間のみの機能

- プロパティ:
  - `ErrorCode`
  - `Message`


- メソッド:
  - `Create()`
  - `FromException()`


##### 新名前空間のみの機能

- メソッド:
  - `Clone()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### TranslationErrorType

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.TranslationErrorType`
- 新名前空間: `Baketa.Core.Translation.Models.TranslationErrorType`
- 旧ファイル行数: 53
- 新ファイル行数: 53

#### 主要な差異

#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### TranslationRequest

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.TranslationRequest`
- 新名前空間: `Baketa.Core.Translation.Models.TranslationRequest`
- 旧ファイル行数: 88
- 新ファイル行数: 121

#### 主要な差異

##### 旧名前空間のみの機能

- メソッド:
  - `Create()`
  - `CreateWithContext()`


##### 新名前空間のみの機能

- プロパティ:
  - `Timestamp`


- メソッド:
  - `Clone()`
  - `GenerateCacheKey()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### TranslationResponse

#### 基本情報

- 旧名前空間: `Baketa.Core.Models.Translation.TranslationResponse`
- 新名前空間: `Baketa.Core.Translation.Models.TranslationResponse`
- 旧ファイル行数: 200
- 新ファイル行数: 185

#### 主要な差異

##### 旧名前空間のみの機能

- メソッド:
  - `CreateSuccessWithConfidence()`
  - `CreateErrorFromException()`


##### 新名前空間のみの機能

- プロパティ:
  - `Timestamp`


- メソッド:
  - `Clone()`


#### 統合方針

新名前空間のモデルを基準として、旧名前空間の固有機能を統合します。

### TranslationCacheModels

- 新名前空間にのみ存在するクラス
- ファイル: `E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationCacheModels.cs`
- 行数: 129

#### 統合方針

このクラスはそのまま維持します。

### TranslationContext

- 新名前空間にのみ存在するクラス
- ファイル: `E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationContext.cs`
- 行数: 174

#### 統合方針

このクラスはそのまま維持します。

### TranslationManagementModels

- 新名前空間にのみ存在するクラス
- ファイル: `E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationManagementModels.cs`
- 行数: 550

#### 統合方針

このクラスはそのまま維持します。


## 参照分析

以下のファイルには両方の名前空間への参照が含まれており、優先的に対応が必要です：

- `E:\dev\Baketa\Baketa.Application\Translation\StandardTranslationPipeline.cs`
- `E:\dev\Baketa\Baketa.Application\Translation\StandardTranslationService.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Abstractions\ITranslationEngine.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Abstractions\ITranslationEngineDiscovery.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Abstractions\IWebApiTranslationEngine.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationEngineAdapter.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationEngineBase.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationExtensions.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Common\WebApiTranslationEngineBase.cs`
- `E:\dev\Baketa\Baketa.Core\Translation\Testing\SimpleEngine.cs`
