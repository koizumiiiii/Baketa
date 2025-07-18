using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

/// <summary>
/// N-gramモデルの訓練と管理を行うサービス
/// </summary>
public class NgramTrainingService
{
    private readonly ILogger<NgramTrainingService> _logger;
    private readonly string _modelDirectory;
    
    public NgramTrainingService(ILogger<NgramTrainingService> logger, string modelDirectory = "models/ngram")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelDirectory = modelDirectory;
        
        // モデルディレクトリを作成
        if (!Directory.Exists(_modelDirectory))
        {
            Directory.CreateDirectory(_modelDirectory);
        }
    }
    
    /// <summary>
    /// 日本語・英語混在テキスト用のBigramモデルを訓練
    /// </summary>
    public async Task<BigramModel> TrainJapaneseBigramModelAsync()
    {
        _logger.LogInformation("日本語Bigramモデルの訓練開始");
        
        var model = new BigramModel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BigramModel>.Instance);
        var trainingTexts = GetJapaneseTrainingData();
        
        await model.TrainAsync(trainingTexts).ConfigureAwait(false);
        
        var modelPath = Path.Combine(_modelDirectory, "japanese_bigram_model.json");
        await model.SaveAsync(modelPath).ConfigureAwait(false);
        
        _logger.LogInformation("日本語Bigramモデルの訓練完了: {ModelPath}", modelPath);
        
        return model;
    }
    
    /// <summary>
    /// 保存されたBigramモデルを読み込み
    /// </summary>
    public async Task<BigramModel> LoadJapaneseBigramModelAsync()
    {
        var modelPath = Path.Combine(_modelDirectory, "japanese_bigram_model.json");
        
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("日本語Bigramモデルが見つかりません。新規作成します: {ModelPath}", modelPath);
            return await TrainJapaneseBigramModelAsync().ConfigureAwait(false);
        }
        
        var model = new BigramModel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BigramModel>.Instance);
        await model.LoadAsync(modelPath).ConfigureAwait(false);
        
        return model;
    }
    
    /// <summary>
    /// 日本語学習データの取得
    /// </summary>
    private List<string> GetJapaneseTrainingData()
    {
        var basicTexts = new List<string>
        {
            // 技術用語
            "単体テスト", "結合テスト", "システムテスト", "受け入れテスト",
            "設計書", "仕様書", "要件定義書", "技術書",
            "データベース", "テーブル", "インデックス", "クエリ",
            "プログラム", "アプリケーション", "システム", "ソフトウェア",
            "アルゴリズム", "データ構造", "オブジェクト指向", "関数型",
            "インターフェース", "クラス", "メソッド", "変数",
            "フレームワーク", "ライブラリ", "モジュール", "パッケージ",
            
            // 業務・開発用語
            "プロジェクト管理", "タスク管理", "進捗管理", "品質管理",
            "コードレビュー", "ペアプログラミング", "アジャイル", "スクラム",
            "デプロイメント", "リリース", "バージョン管理", "継続的統合",
            "メンテナンス", "運用保守", "監視", "ログ解析",
            "セキュリティ", "脆弱性", "暗号化", "認証",
            "パフォーマンス", "最適化", "チューニング", "負荷テスト",
            "バックアップ", "リストア", "災害復旧", "可用性",
            
            // 一般的な文章パターン
            "これは日本語のテストです",
            "システムの動作確認を行います",
            "データの整合性をチェックします",
            "エラーが発生した場合の対処方法",
            "ユーザーインターフェースの改善",
            "機能の追加と修正",
            "バグの修正と対応",
            "新しい機能の開発",
            "既存システムの改善",
            "運用環境への適用",
            
            // 技術的な文章
            "APIの応答時間を測定する",
            "SQLクエリの最適化を行う",
            "HTTPステータスコードを確認する",
            "JSONデータの解析を実装する",
            "XMLファイルの処理を追加する",
            "REST APIの設計を検討する",
            "WebSocketの実装を行う",
            "OAuth認証を導入する",
            "SSL証明書を更新する",
            "DNS設定を変更する",
            
            // 日本語固有の表現
            "お疲れ様です",
            "よろしくお願いします",
            "ありがとうございます",
            "申し訳ありません",
            "確認いたします",
            "対応いたします",
            "検討いたします",
            "修正いたします",
            "追加いたします",
            "削除いたします",
            
            // 混在テキストパターン
            "JavaScript開発",
            "Python実装",
            "Java設計",
            "C#コード",
            "HTML構造",
            "CSS記述",
            "SQL文",
            "Linux環境",
            "Windows環境",
            "Mac環境",
            
            // 数値や記号を含む文章
            "バージョン1.0.0",
            "ポート番号8080",
            "IPアドレス192.168.1.1",
            "HTTPコード200",
            "エラーコード404",
            "CPU使用率90%",
            "メモリ使用量512MB",
            "ディスク容量1TB",
            "ネットワーク速度100Mbps",
            "処理時間1.5秒",
        };
        
        // 文章を拡張
        var extendedTexts = new List<string>(basicTexts);
        
        // 組み合わせ文章を生成
        var combinationPatterns = new[]
        {
            "{0}の{1}",
            "{0}を{1}",
            "{0}に{1}",
            "{0}で{1}",
            "{0}と{1}",
            "{0}は{1}",
            "{0}が{1}",
            "{0}から{1}",
            "{0}まで{1}",
            "{0}による{1}",
        };
        
        var nouns = new[] { "システム", "データ", "機能", "処理", "設定", "環境", "管理", "制御" };
        var verbs = new[] { "実行", "確認", "変更", "追加", "削除", "更新", "作成", "取得" };
        
        foreach (var pattern in combinationPatterns)
        {
            foreach (var noun in nouns)
            {
                foreach (var verb in verbs)
                {
                    extendedTexts.Add(string.Format(CultureInfo.InvariantCulture, pattern, noun, verb));
                }
            }
        }
        
        return extendedTexts;
    }
    
    /// <summary>
    /// カスタム学習データでモデルを再訓練
    /// </summary>
    public async Task<BigramModel> RetrainWithCustomDataAsync(IEnumerable<string> additionalTexts)
    {
        _logger.LogInformation("カスタムデータでBigramモデルを再訓練");
        
        var model = new BigramModel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BigramModel>.Instance);
        var baseTrainingTexts = GetJapaneseTrainingData();
        var allTrainingTexts = baseTrainingTexts.Concat(additionalTexts);
        
        await model.TrainAsync(allTrainingTexts).ConfigureAwait(false);
        
        var modelPath = Path.Combine(_modelDirectory, "japanese_bigram_model_custom.json");
        await model.SaveAsync(modelPath).ConfigureAwait(false);
        
        _logger.LogInformation("カスタムBigramモデルの訓練完了: {ModelPath}", modelPath);
        
        return model;
    }
    
    /// <summary>
    /// モデルの品質を評価
    /// </summary>
    public Task<ModelEvaluationResult> EvaluateModelAsync(BigramModel model)
    {
        _logger.LogInformation("Bigramモデルの品質評価開始");
        
        var testTexts = GetTestTexts();
        var results = new List<EvaluationCase>();
        
        foreach (var (correctText, corruptedText) in testTexts)
        {
            var likelihood = model.CalculateLikelihood(correctText);
            var corruptedLikelihood = model.CalculateLikelihood(corruptedText);
            
            var isCorrect = likelihood > corruptedLikelihood;
            
            results.Add(new EvaluationCase(
                correctText,
                corruptedText,
                likelihood,
                corruptedLikelihood,
                isCorrect));
        }
        
        var accuracy = results.Count(r => r.IsCorrect) / (double)results.Count;
        
        _logger.LogInformation("Bigramモデルの品質評価完了: 精度 {Accuracy:F2}%", accuracy * 100);
        
        return Task.FromResult(new ModelEvaluationResult(accuracy, results));
    }
    
    /// <summary>
    /// テストケースを取得
    /// </summary>
    private IEnumerable<(string correctText, string corruptedText)> GetTestTexts()
    {
        return
        [
            ("単体テスト", "車体テスト"),
            ("設計書", "役計書"),
            ("データベース", "データベース"),
            ("システム", "システム"),
            ("プログラム", "プログラム"),
            ("アルゴリズム", "アルゴリズム"),
            ("インターフェース", "インターフェース"),
            ("オブジェクト指向", "オブジェクト指向"),
            ("フレームワーク", "フレームワーク"),
            ("デプロイメント", "デプロイメント"),
            ("オンボーディング", "オンボーデイング"),
            ("魔法体験", "院法体勝"),
            ("設計", "恐計"),
            ("APIの応答時間", "APIの応答時間"),
            ("SQLクエリ", "SQLクエリ"),
            ("HTTPステータス", "HTTPステータス"),
            ("JSONデータ", "JSONデータ"),
            ("XMLファイル", "XMLファイル"),
            ("REST API", "REST API"),
            ("WebSocket", "WebSocket"),
        ];
    }
}

/// <summary>
/// モデル評価結果
/// </summary>
public class ModelEvaluationResult(double accuracy, IEnumerable<EvaluationCase> cases)
{
    public double Accuracy { get; } = accuracy;
    public IReadOnlyList<EvaluationCase> Cases { get; } = [.. cases];
}

/// <summary>
/// 評価ケース
/// </summary>
public class EvaluationCase(string correctText, string corruptedText, double correctLikelihood, double corruptedLikelihood, bool isCorrect)
{
    public string CorrectText { get; } = correctText;
    public string CorruptedText { get; } = corruptedText;
    public double CorrectLikelihood { get; } = correctLikelihood;
    public double CorruptedLikelihood { get; } = corruptedLikelihood;
    public bool IsCorrect { get; } = isCorrect;
}
