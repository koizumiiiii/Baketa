# モデルマッピング実装の推奨事項

## 1. 問題の背景：重複する名前空間

Baketaプロジェクトでは翻訳関連のデータモデルが2つの異なる名前空間に定義されています：

1. `Baketa.Core.Models.Translation`
2. `Baketa.Core.Translation.Models`

これにより、以下の問題が発生しています：

- 型の曖昧参照エラー（CS0104）
- 重複コードによるメンテナンス性の低下
- 実装時の混乱とエラー発生リスク

## 2. 短期的解決策：モデル間マッピング実装

名前空間の統一は基盤システム完了後に実施予定ですが、それまでは以下のモデルマッピングアプローチを推奨します。

### 2.1 拡張メソッドによるマッピング実装

```csharp
namespace Baketa.Core.Translation.Common
{
    /// <summary>
    /// 翻訳モデル間のマッピングを提供する拡張メソッド
    /// </summary>
    public static class TranslationModelMappingExtensions
    {
        /// <summary>
        /// Core.ModelsのTranslationRequestをCore.Translation.ModelsのTranslationRequestに変換します
        /// </summary>
        public static Translation.Models.TranslationRequest ToTranslationModel(
            this Core.Models.Translation.TranslationRequest source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Translation.Models.TranslationRequest
            {
                SourceText = source.SourceText,
                SourceLanguage = source.SourceLanguage.ToTranslationModel(),
                TargetLanguage = source.TargetLanguage.ToTranslationModel(),
                Context = source.Context != null 
                    ? new Translation.Models.TranslationContext { 
                        GameProfileId = source.Context,
                        // 他のコンテキストプロパティは必要に応じて設定
                      } 
                    : null,
                // Timestampは自動設定
            };
        }
        
        /// <summary>
        /// Core.Translation.ModelsのTranslationRequestをCore.ModelsのTranslationRequestに変換します
        /// </summary>
        public static Core.Models.Translation.TranslationRequest ToCoreModel(
            this Translation.Models.TranslationRequest source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Core.Models.Translation.TranslationRequest
            {
                SourceText = source.SourceText,
                SourceLanguage = source.SourceLanguage.ToCoreModel(),
                TargetLanguage = source.TargetLanguage.ToCoreModel(),
                Context = source.Context?.ToString()
                // Options, RequestIdはCore.Modelsでは読み取り専用のため設定不可
            };
        }
        
        /// <summary>
        /// Core.ModelsのLanguageをCore.Translation.ModelsのLanguageに変換します
        /// </summary>
        public static Translation.Models.Language ToTranslationModel(
            this Core.Models.Translation.Language source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Translation.Models.Language
            {
                Code = source.Code,
                Name = source.Name,
                DisplayName = source.Name // 代替として名前を使用
            };
        }
        
        /// <summary>
        /// Core.Translation.ModelsのLanguageをCore.ModelsのLanguageに変換します
        /// </summary>
        public static Core.Models.Translation.Language ToCoreModel(
            this Translation.Models.Language source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Core.Models.Translation.Language
            {
                Code = source.Code,
                Name = source.Name
                // 他のプロパティはCore.Modelsには存在しない
            };
        }
        
        /// <summary>
        /// Core.ModelsのTranslationResponseをCore.Translation.ModelsのTranslationResponseに変換します
        /// </summary>
        public static Translation.Models.TranslationResponse ToTranslationModel(
            this Core.Models.Translation.TranslationResponse source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            var result = new Translation.Models.TranslationResponse
            {
                RequestId = source.RequestId,
                SourceText = source.SourceText,
                TranslatedText = source.TranslatedText,
                SourceLanguage = source.SourceLanguage.ToTranslationModel(),
                TargetLanguage = source.TargetLanguage.ToTranslationModel(),
                EngineName = source.EngineName,
                IsSuccess = source.IsSuccess,
                ProcessingTimeMs = source.ProcessingTimeMs
            };
            
            if (source.Error != null)
            {
                result.Error = source.Error.ToTranslationModel();
            }
            
            return result;
        }
        
        /// <summary>
        /// Core.Translation.ModelsのTranslationResponseをCore.ModelsのTranslationResponseに変換します
        /// </summary>
        public static Core.Models.Translation.TranslationResponse ToCoreModel(
            this Translation.Models.TranslationResponse source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            var result = new Core.Models.Translation.TranslationResponse
            {
                RequestId = source.RequestId, 
                SourceText = source.SourceText,
                TranslatedText = source.TranslatedText,
                SourceLanguage = source.SourceLanguage.ToCoreModel(),
                TargetLanguage = source.TargetLanguage.ToCoreModel(),
                EngineName = source.EngineName,
                IsSuccess = source.IsSuccess,
                ProcessingTimeMs = source.ProcessingTimeMs
            };
            
            if (source.Error != null)
            {
                result.Error = source.Error.ToCoreModel();
            }
            
            return result;
        }
        
        /// <summary>
        /// Core.ModelsのTranslationErrorをCore.Translation.ModelsのTranslationErrorに変換します
        /// </summary>
        public static Translation.Models.TranslationError ToTranslationModel(
            this Core.Models.Translation.TranslationError source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Translation.Models.TranslationError
            {
                ErrorCode = source.ErrorCode,
                Message = source.Message,
                ErrorType = (Translation.Models.TranslationErrorType)source.ErrorType
            };
        }
        
        /// <summary>
        /// Core.Translation.ModelsのTranslationErrorをCore.ModelsのTranslationErrorに変換します
        /// </summary>
        public static Core.Models.Translation.TranslationError ToCoreModel(
            this Translation.Models.TranslationError source)
        {
            ArgumentNullException.ThrowIfNull(source);
            
            return new Core.Models.Translation.TranslationError
            {
                ErrorCode = source.ErrorCode,
                Message = source.Message,
                ErrorType = (Core.Models.Translation.TranslationErrorType)source.ErrorType
            };
        }
    }
}
```

### 2.2 マッパー使用例

```csharp
// Core.Modelsから変換
var coreRequest = new Core.Models.Translation.TranslationRequest { ... };
var translateRequest = coreRequest.ToTranslationModel();

// Core.Translation.Modelsから変換
var translateResponse = new Translation.Models.TranslationResponse { ... };
var coreResponse = translateResponse.ToCoreModel();
```

## 3. 実装時の注意点

### 3.1 Null安全性の確保

- すべてのマッピングメソッドでnullチェックを実施
- 各プロパティへの代入前のnullチェックを徹底
- 特に参照型プロパティは特に注意が必要

### 3.2 読み取り専用プロパティへの対応

一部のモデルでは読み取り専用プロパティがあり、マッピング時に以下のいずれかの対応が必要です：

1. コンストラクタを通じて値を設定
2. 該当プロパティのマッピングをスキップ
3. 代替値や初期値の設定

### 3.3 型変換の考慮

- 基本型（string, int, bool等）は直接マッピング可能
- 異なる列挙型間はキャスト操作が必要 
- カスタム型は専用の変換メソッドが必要

## 4. 長期的解決策

名前空間統一化に向けた段階的な移行計画：

1. マッピング層を徹底的に活用した中間実装
2. 新しいコードは標準名前空間（`Baketa.Core.Translation.Models`）のみを使用
3. 既存コードの段階的な変換
4. 最終的に重複モデルを削除

## 5. テスト戦略

マッピング実装のテストは以下を含む必要があります：

1. **基本マッピングテスト**: 各モデルの基本プロパティが正しくマッピングされることを確認
2. **Null参照テスト**: nullプロパティが適切に処理されることを検証
3. **エッジケーステスト**: 特殊値（空文字列、最大値など）の処理を確認
4. **双方向変換テスト**: 往復変換（A→B→A）が同値性を保つことを検証

## 6. 具体的なコード例：翻訳パイプライン修正案

以下は、`StandardTranslationPipeline`クラスの修正例です：

```csharp
// プロパティパスからエンジンを取得するメソッド
private async Task<CoreModels.TranslationResponse> ExecuteTranslationWithEngineAsync(
    TransModels.TranslationRequest request,
    Core.Abstractions.Translation.ITranslationEngine engine,
    CancellationToken cancellationToken)
{
    // TransModels.TranslationRequestをCoreModels.TranslationRequestに変換
    var coreRequest = request.ToCoreModel();
    
    // エンジンを使用して翻訳を実行
    var coreResponse = await engine.TranslateAsync(coreRequest, cancellationToken)
        .ConfigureAwait(false);
        
    // レスポンスが有効かチェック
    if (coreResponse == null)
    {
        throw new InvalidOperationException("翻訳エンジンからの応答がnullでした。");
    }
    
    return coreResponse;
}

// 変換されたレスポンスを処理するメソッド
private TransModels.TranslationResponse ProcessEngineResponse(
    TransModels.TranslationRequest request,
    CoreModels.TranslationResponse engineResponse,
    string engineName,
    long processingTimeMs)
{
    if (engineResponse.IsSuccess)
    {
        // 成功レスポンスの生成
        var response = TransModels.TranslationResponse.CreateSuccess(
            request,
            engineResponse.TranslatedText ?? string.Empty,
            engineName,
            processingTimeMs
        );
        
        response.Metadata["FromCache"] = false;
        return response;
    }
    else
    {
        // エラーレスポンスの生成
        var error = engineResponse.Error != null
            ? new TransModels.TranslationError
            {
                ErrorCode = engineResponse.Error.ErrorCode,
                Message = engineResponse.Error.Message,
                ErrorType = TransModels.TranslationErrorType.Unknown
            }
            : null;
            
        var response = TransModels.TranslationResponse.CreateError(
            request,
            error ?? new TransModels.TranslationError
            {
                ErrorCode = "UNKNOWN_ERROR",
                Message = "不明なエラーが発生しました",
                ErrorType = TransModels.TranslationErrorType.Unknown
            },
            engineName
        );
        
        response.ProcessingTimeMs = processingTimeMs;
        response.Metadata["FromCache"] = false;
        return response;
    }
}
```

このアプローチにより、変換処理を集約し、コードの可読性と保守性が向上します。