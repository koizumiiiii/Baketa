# Issue 8-2: 画像前処理パイプラインの設計と実装

## 概要
OCR処理の前に画像に一連の処理を適用するパイプラインを設計・実装します。このパイプラインにより、OCR精度を向上させるためのフィルターや処理を柔軟に組み合わせることが可能になります。

## 目的・理由
ゲーム画面からのテキスト抽出は、様々な画像処理ステップを組み合わせることで精度が向上します。パイプラインアプローチにより：

1. 複数の画像処理フィルターを順序付けて適用できる
2. ゲームタイトルやシーンに基づいて最適な処理手順を切り替えられる
3. 処理ステップの追加・削除・置換が容易になる
4. 各ステップの結果を視覚的に確認でき、デバッグが容易になる

## 詳細
- パイプラインの基本構造とインターフェースの設計
- フィルター間のデータ受け渡し方法の実装
- OCR前処理に特化したフィルターセットの実装
- パイプライン実行エンジンの実装

## タスク分解
- [ ] パイプラインの基本構造設計
  - [ ] `IImagePipeline`インターフェースの設計
  - [ ] `IImagePipelineStep`インターフェースの設計
  - [ ] パイプライン実行フローの設計
- [ ] 基本パイプライン実装クラスの作成
  - [ ] `ImagePipeline`クラスの実装
  - [ ] パイプラインステップの追加・削除機能
  - [ ] 条件分岐ステップの実装
- [ ] OCR最適化のための標準フィルターの実装
  - [ ] グレースケール変換フィルター
  - [ ] コントラスト強調フィルター
  - [ ] ノイズ除去フィルター
  - [ ] 二値化フィルター
  - [ ] モルフォロジー演算フィルター（膨張・収縮）
  - [ ] エッジ検出フィルター
- [ ] パイプライン実行エンジンの実装
  - [ ] 非同期実行サポート
  - [ ] 中間結果の保存と取得メカニズム
  - [ ] エラーハンドリング
- [ ] パイプライン構成のシリアライズ/デシリアライズ機能
  - [ ] JSONベースの構成管理
  - [ ] パラメータの動的な変更サポート
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.OCR.Abstractions.Pipeline
{
    /// <summary>
    /// 画像処理パイプラインを表すインターフェース
    /// </summary>
    public interface IImagePipeline
    {
        /// <summary>
        /// パイプラインに処理ステップを追加します
        /// </summary>
        /// <param name="step">追加するパイプラインステップ</param>
        /// <returns>自身のインスタンス（メソッドチェーン用）</returns>
        IImagePipeline AddStep(IImagePipelineStep step);
        
        /// <summary>
        /// パイプラインを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <returns>処理結果と中間結果を含むパイプライン実行結果</returns>
        Task<PipelineResult> ExecuteAsync(IAdvancedImage input);
        
        /// <summary>
        /// パイプライン構成を名前付きプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">保存するプロファイル名</param>
        Task SaveProfileAsync(string profileName);
        
        /// <summary>
        /// 名前付きプロファイルからパイプライン構成を読み込みます
        /// </summary>
        /// <param name="profileName">読み込むプロファイル名</param>
        /// <returns>読み込まれたパイプライン</returns>
        Task<IImagePipeline> LoadProfileAsync(string profileName);
    }
    
    /// <summary>
    /// パイプラインの個別処理ステップを表すインターフェース
    /// </summary>
    public interface IImagePipelineStep
    {
        /// <summary>
        /// ステップの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// ステップの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// ステップのパラメータ定義
        /// </summary>
        IReadOnlyCollection<PipelineStepParameter> Parameters { get; }
        
        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>処理結果画像</returns>
        Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context);
        
        /// <summary>
        /// パラメータ値を設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定する値</param>
        void SetParameter(string parameterName, object value);
        
        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        object GetParameter(string parameterName);
    }
    
    /// <summary>
    /// パイプライン実行結果を表すクラス
    /// </summary>
    public class PipelineResult
    {
        /// <summary>
        /// パイプライン処理の最終結果
        /// </summary>
        public IAdvancedImage Result { get; }
        
        /// <summary>
        /// 各ステップの中間結果
        /// </summary>
        public IReadOnlyDictionary<string, IAdvancedImage> IntermediateResults { get; }
        
        /// <summary>
        /// パイプライン実行の処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; }
    }
    
    /// <summary>
    /// パイプラインステップのパラメータを表すクラス
    /// </summary>
    public class PipelineStepParameter
    {
        /// <summary>
        /// パラメータ名
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// パラメータの説明
        /// </summary>
        public string Description { get; }
        
        /// <summary>
        /// パラメータの型
        /// </summary>
        public Type ParameterType { get; }
        
        /// <summary>
        /// デフォルト値
        /// </summary>
        public object DefaultValue { get; }
        
        /// <summary>
        /// 最小値（数値パラメータの場合）
        /// </summary>
        public object? MinValue { get; }
        
        /// <summary>
        /// 最大値（数値パラメータの場合）
        /// </summary>
        public object? MaxValue { get; }
        
        /// <summary>
        /// 選択肢（列挙型パラメータの場合）
        /// </summary>
        public IReadOnlyCollection<object>? Options { get; }
    }
}
```

## 実装上の注意点
- パイプラインはメモリ効率を考慮して設計する（不要な中間結果の早期解放）
- 各フィルターステップは独立して単体テスト可能にする
- パラメータ変更の影響範囲を限定し、パフォーマンスに影響を与えないようにする
- 複雑なパイプラインの実行時にも応答性を維持するため、キャンセレーション対応を実装する
- パイプライン構成の永続化は拡張性を考慮して設計する

## 関連Issue/参考
- 親Issue: #8 OpenCVベースのOCR前処理最適化
- 依存Issue: #8-1 OpenCVラッパークラスの設計と実装
- 関連Issue: #5-1 IAdvancedImageインターフェースの設計と実装
- 参照: E:\dev\Baketa\docs\3-architecture\ocr\preprocessing-pipeline.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.3 非同期プログラミング)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: medium`
- `component: ocr`
