# Tier 2: 中期改善戦略（1-2か月）

## 概要

Tier 1の成果を基に、より根本的なアーキテクチャ改善を実施。翻訳精度を大幅に向上させつつ、さらなる処理速度最適化とシステムの安定性向上を目指します。

## 実装対象項目

### 4. Python→C#移行準備 - BPE自前実装 🔧
**目標**: 翻訳精度0%→50-80%向上、外部依存排除

#### 技術背景
```csharp
// 現在の問題実装
return text.ToCharArray().Select(c => (int)c % 32000).ToArray(); // ガベージ
```
↓
```csharp
// 目標実装
public class OpusMtBpeTokenizer : ITokenizer
{
    public int[] Tokenize(string text) 
    {
        // OPUS-MT正規の語彙を使用した正確なトークン化
    }
}
```

#### 実装詳細

##### Phase A: 語彙ファイル解析システム（1週間）
```csharp
public class OpusMtVocabularyParser
{
    public VocabularyData ParseVocabularyFile(string vocabPath)
    {
        // 標準OPUS-MT .vocabファイル解析
        // 形式: <pad>\t0\n<unk>\t1\n<s>\t2\n</s>\t3\n...
        
        return new VocabularyData
        {
            TokenToId = tokenMapping,
            IdToToken = reverseMapping,
            SpecialTokens = new SpecialTokens
            {
                PadId = 0, UnkId = 1, BosId = 2, EosId = 3
            }
        };
    }
}
```

##### Phase B: BPE分割アルゴリズム（1-2週間）
```csharp
public class BytePairEncoder
{
    private readonly Dictionary<string, int> _vocab;
    private readonly List<(string, string)> _bpePairs;
    
    public string[] ApplyBpe(string text)
    {
        // 1. 文字正規化
        var normalized = NormalizeText(text);
        
        // 2. 初期文字分割
        var tokens = InitialTokenization(normalized);
        
        // 3. BPEペア適用（反復的統合）
        foreach (var (first, second) in _bpePairs)
        {
            tokens = MergePairs(tokens, first, second);
        }
        
        return tokens;
    }
    
    private string[] MergePairs(string[] tokens, string first, string second)
    {
        // BPE統合ロジック：隣接ペアを結合
        // 例: ["un", "known"] → ["unknown"]
    }
}
```

##### Phase C: 統合システム（1週間）
```csharp
public class OpusMtTokenizerFactory
{
    public ITokenizer CreateTokenizer(LanguagePair languagePair)
    {
        var modelPaths = GetModelPaths(languagePair);
        var vocabulary = _parser.ParseVocabularyFile(modelPaths.VocabPath);
        var bpeData = LoadBpeData(modelPaths.BpePath);
        
        return new OpusMtBpeTokenizer(vocabulary, bpeData);
    }
    
    // 将来の言語拡張も自動対応
    private ModelPaths GetModelPaths(LanguagePair pair)
    {
        return new ModelPaths
        {
            VocabPath = $"models/opus-mt-{pair.Source.Code}-{pair.Target.Code}.vocab",
            BpePath = $"models/opus-mt-{pair.Source.Code}-{pair.Target.Code}.bpe",
            ModelPath = $"models/opus-mt-{pair.Source.Code}-{pair.Target.Code}.onnx"
        };
    }
}
```

#### 期待効果
- **翻訳精度**: 0%（tok_形式）→ 50-80%（実際翻訳）
- **外部依存**: Microsoft.ML.Tokenizers依存完全排除
- **拡張性**: 新言語ペア追加が設定ファイル変更のみ
- **メンテナンス**: 完全自己制御、外部ライブラリ更新リスクなし

---

### 5. 統合GPU対応 - DirectML フォールバック 💻
**目標**: GPU非搭載PC（約30%）でも処理速度向上

#### 技術詳細
- **対象**: Intel統合GPU（UHD630以降）、AMD統合GPU
- **技術スタック**: DirectML + ONNX Runtime
- **フォールバック階層**: RTX/GTX → 統合GPU → CPU

#### 実装計画
```csharp
public enum GpuTier
{
    HighEnd,      // RTX4070, RTX3060以上
    MidRange,     // GTX1660, RTX2060
    Integrated,   // Intel UHD630以降、AMD Vega
    CpuFallback   // GPU使用不可
}

public class AdaptiveGpuManager
{
    public GpuTier DetectGpuCapability()
    {
        // 1. 専用GPU検出（CUDA/OpenCL対応）
        // 2. 統合GPU検出（DirectML対応）
        // 3. 性能ベンチマーク実行
        return determinedTier;
    }
    
    public IOcrProcessor CreateOptimalProcessor(GpuTier tier)
    {
        return tier switch
        {
            GpuTier.HighEnd => new CudaOcrProcessor(),
            GpuTier.MidRange => new OpenClOcrProcessor(), 
            GpuTier.Integrated => new DirectMlOcrProcessor(),
            GpuTier.CpuFallback => new CpuOcrProcessor()
        };
    }
}
```

#### DirectML統合実装
```csharp
public class DirectMlOcrProcessor : IOcrProcessor
{
    private readonly InferenceSession _onnxSession;
    
    public DirectMlOcrProcessor()
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_DML(0); // DirectML使用
        _onnxSession = new InferenceSession(modelPath, sessionOptions);
    }
    
    public async Task<OcrResult> ProcessAsync(Mat image)
    {
        // DirectML最適化されたONNX推論
        // 統合GPUでも2-3倍の高速化期待
    }
}
```

#### 期待効果
- **統合GPU性能**: CPU比2-3倍高速化
- **カバレッジ**: 全PCの95%以上で何らかのGPU加速
- **消費電力**: 専用GPUより省電力

---

### 6. 量子化実装 - メモリ50%削減 🗜️
**目標**: モデルサイズ・メモリ使用量大幅削減

#### 量子化戦略

##### 動的量子化（Dynamic Quantization）
```csharp
public class ModelQuantizer
{
    public void QuantizeModel(string originalPath, string quantizedPath)
    {
        // ONNX Runtime量子化API使用
        // FP32 → INT8変換（75%サイズ削減）
        
        var quantizationOptions = new QuantizationOptions
        {
            QuantizationMode = QuantizationMode.IntegerOps,
            WeightType = QuantizationWeightType.QInt8
        };
        
        OnnxQuantizer.Quantize(originalPath, quantizedPath, quantizationOptions);
    }
}
```

##### QAT（Quantization Aware Training）準備
```csharp
public class AdaptiveModelLoader
{
    public InferenceSession LoadOptimalModel(HardwareProfile profile)
    {
        var modelPath = profile.AvailableMemory switch
        {
            > 8192 => "opus-mt-ja-en-fp32.onnx",    // フル精度
            > 4096 => "opus-mt-ja-en-fp16.onnx",    // 半精度
            > 2048 => "opus-mt-ja-en-int8.onnx",    // 動的量子化
            _ => "opus-mt-ja-en-int4.onnx"          // 極限量子化
        };
        
        return new InferenceSession(modelPath);
    }
}
```

#### 実装スケジュール
- **Week 1-2**: 動的量子化（INT8）実装
- **Week 3-4**: 半精度（FP16）対応
- **Week 5-6**: 極限量子化（INT4）実験
- **Week 7-8**: 精度・性能バランス調整

#### 期待効果
- **メモリ使用量**: 50-75%削減
- **モデルサイズ**: 300MB → 75-150MB
- **推論速度**: 量子化による10-20%高速化
- **精度劣化**: 5%以内に抑制

---

## 統合アーキテクチャ

```csharp
public class Tier2OptimizedTranslationEngine
{
    private readonly OpusMtBpeTokenizer _nativeTokenizer;      // 自前BPE
    private readonly AdaptiveGpuManager _gpuManager;           // GPU階層管理
    private readonly AdaptiveModelLoader _modelLoader;         // 量子化対応
    
    public async Task<TranslationResult> TranslateAsync(string text)
    {
        // 1. 最適なGPU処理選択
        var gpuTier = _gpuManager.DetectGpuCapability();
        var processor = _gpuManager.CreateOptimalProcessor(gpuTier);
        
        // 2. ハードウェア適応モデル読み込み
        var model = _modelLoader.LoadOptimalModel(GetHardwareProfile());
        
        // 3. 自前トークナイザー使用
        var tokens = _nativeTokenizer.Tokenize(text);
        
        // 4. 最適化推論実行
        var result = await processor.InferenceAsync(tokens);
        
        // 5. 正確なデトークン化
        return _nativeTokenizer.Decode(result);
    }
}
```

## 実装順序・スケジュール

### Month 1: コア技術実装
- **Week 1-2**: BPE自前実装（語彙解析・基本アルゴリズム）
- **Week 3-4**: DirectML統合（統合GPU対応）

### Month 2: 最適化・統合
- **Week 5-6**: 量子化実装（メモリ削減）
- **Week 7-8**: システム統合・パフォーマンステスト

## 成功指標

### 技術指標
- **翻訳精度**: 0% → 50-80%（tok_形式解消）
- **外部依存**: Microsoft.ML.Tokenizers完全排除
- **GPU対応率**: 95%（統合GPU含む）
- **メモリ効率**: 50%削減

### システム指標  
- **総合処理時間**: Tier1比さらに20-30%削減
- **安定性**: 外部ライブラリ依存リスク排除
- **拡張性**: 新言語ペア追加コスト90%削減

## リスク評価・軽減策

### 主要リスク
1. **BPE実装複雑性**: アルゴリズム実装の技術的困難
2. **DirectML互換性**: 古い統合GPUでの動作不安定
3. **量子化精度劣化**: 極限量子化での翻訳品質低下

### 軽減策
1. **段階的実装**: MVP→完全版の段階開発
2. **フォールバック保持**: 従来システム並行維持
3. **精度監視**: 自動品質テスト継続実行

## Tier 3への準備

Tier 2完了時点で以下を評価：
- 翻訳精度の実測値
- 新たなボトルネック特定  
- ユーザー満足度指標
- 競合製品との性能比較

これらを基にTier 3（長期戦略）を策定します。