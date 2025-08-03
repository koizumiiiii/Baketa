using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;
using Google.Protobuf;
using Sentencepiece;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// SentencePiece .modelファイル（Protobuf形式）のパーサー
/// Google.Protobufを使用して安全にモデルデータを抽出する
/// </summary>
public sealed class SentencePieceModelParser(ILogger<SentencePieceModelParser> logger) : IDisposable
{
    private readonly ILogger<SentencePieceModelParser> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private bool _disposed;

    /// <summary>
    /// SentencePiece .modelファイルをパースしてモデルオブジェクトを構築
    /// </summary>
    /// <param name="modelPath">モデルファイルのパス</param>
    /// <returns>パース済みSentencePieceModel</returns>
    public async Task<SentencePieceModel> ParseModelAsync(string modelPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));
            
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"SentencePiece model file not found: {modelPath}");

        try
        {
            _logger.LogInformation("SentencePieceモデルファイルの解析開始: {ModelPath}", modelPath);
            
            // TODO: 実際のProtobuf解析実装
            // 現在は基本構造のみ実装、proto定義取得後に完全実装
            
            var model = await ParseProtobufModelAsync(modelPath).ConfigureAwait(false);
            
            // Trie木構築
            model.SearchTrie = BuildTrieForFastSearch(model.Vocabulary);
            
            _logger.LogInformation("SentencePieceモデル解析完了: 語彙数={VocabularySize}", model.VocabularySize);
            
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SentencePieceモデルの解析に失敗: {ModelPath}", modelPath);
            throw new InvalidOperationException($"Failed to parse SentencePiece model: {modelPath}", ex);
        }
    }

    /// <summary>
    /// Google.Protobufを使用したSentencePieceモデルの解析
    /// 実際の.modelファイルから語彙とメタデータを抽出
    /// </summary>
    private async Task<SentencePieceModel> ParseProtobufModelAsync(string modelPath)
    {
        try
        {
            _logger.LogInformation("SentencePieceモデルファイルの読み込み開始: {ModelPath}", modelPath);
            
            ModelProto protoModel;
            
            // Protobufファイルの読み込み
            await using var fileStream = File.OpenRead(modelPath);
            protoModel = ModelProto.Parser.ParseFrom(fileStream);
            
            _logger.LogInformation("Protobufモデル解析完了: 語彙数={PieceCount}", protoModel.Pieces.Count);
            
            // 特殊トークンIDを実際のモデルから取得
            var trainerSpec = protoModel.TrainerSpec;
            var specialTokens = new NativeSpecialTokens
            {
                // Helsinki-NLP OPUS-MTでは強制的にBOS=EOS=0を使用
                BosId = 0, // trainerSpec?.BosId ?? 0 から強制変更
                UnkId = trainerSpec?.UnkId ?? 1,  
                EosId = 0, // trainerSpec?.EosId ?? 0 から強制変更
                PadId = trainerSpec?.PadId ?? 60715 // Helsinki-NLP: VocabSize-1
            };
            
            var model = new SentencePieceModel
            {
                Version = "1.0.0",
                NormalizerType = protoModel.NormalizerSpec?.Name ?? "nfkc_with_prefix",
                SpecialTokens = specialTokens
            };
            
            _logger.LogDebug("特殊トークンID設定完了: BOS={Bos}, EOS={Eos}, UNK={Unk}, PAD={Pad}",
                model.SpecialTokens.BosId, model.SpecialTokens.EosId, 
                model.SpecialTokens.UnkId, model.SpecialTokens.PadId);
            
            // 語彙辞書の構築
            for (int i = 0; i < protoModel.Pieces.Count; i++)
            {
                var piece = protoModel.Pieces[i];
                model.Vocabulary[piece.Piece] = i;
                model.ReverseVocabulary[i] = piece.Piece;
                
                // スコア情報も保存
                if (piece.HasScore)
                {
                    model.PieceScores[piece.Piece] = piece.Score;
                }
            }
            
            _logger.LogInformation("語彙辞書構築完了: 語彙数={VocabularySize}", model.VocabularySize);
            
            // 正規化設定の取得
            if (protoModel.NormalizerSpec != null)
            {
                var normalizerSpec = protoModel.NormalizerSpec;
                _logger.LogDebug("正規化設定: AddDummyPrefix={AddPrefix}, RemoveExtraWhitespaces={RemoveWhitespace}", 
                    normalizerSpec.AddDummyPrefix, normalizerSpec.RemoveExtraWhitespaces);
                
                // 正規化パラメータを保存（将来の実装で使用）
                model.NormalizationSettings = new Dictionary<string, object>
                {
                    ["AddDummyPrefix"] = normalizerSpec.AddDummyPrefix,
                    ["RemoveExtraWhitespaces"] = normalizerSpec.RemoveExtraWhitespaces,
                    ["EscapeWhitespaces"] = normalizerSpec.EscapeWhitespaces
                };
            }
            
            _logger.LogInformation("SentencePieceモデル解析完了: 語彙数={VocabularySize}", model.VocabularySize);
            
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SentencePieceモデルの読み込みに失敗: {ModelPath}", modelPath);
            throw new InvalidOperationException($"Failed to parse SentencePiece model: {modelPath}", ex);
        }
    }

    /// <summary>
    /// 語彙辞書からTrie木を構築（最適化版）
    /// SentencePiece仕様に従った語彙順序での構築
    /// </summary>
    private static TrieNode BuildTrieForFastSearch(Dictionary<string, int> vocabulary)
    {
        var root = new TrieNode();
        
        // 語彙を辞書順でソートしてTrie木に追加
        // SentencePieceの仕様では、同じ長さのマッチの場合は辞書順で最初のものを選択
        var sortedVocabulary = vocabulary
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToArray();
        
        foreach (var (token, tokenId) in sortedVocabulary)
        {
            root.AddToken(token, tokenId);
        }
        
        // Trie木の最適化を実行
        root.Optimize();
        
        return root;
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _logger.LogDebug("SentencePieceModelParser disposed");
    }
}
