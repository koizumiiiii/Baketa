using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Services;

/// <summary>
/// SentencePiece互換のUnicode正規化サービス
/// Google SentencePieceの標準正規化処理を実装
/// </summary>
public sealed class SentencePieceNormalizer(
    ILogger<SentencePieceNormalizer> logger,
    SentencePieceNormalizationOptions? options = null) : IDisposable
{
    private readonly ILogger<SentencePieceNormalizer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SentencePieceNormalizationOptions _options = options ?? SentencePieceNormalizationOptions.Default;
    private bool _disposed;

    /// <summary>
    /// スペースプレフィックス記号（SentencePiece標準）
    /// </summary>
    public const char SpaceSymbol = '\u2581'; // ▁

    /// <summary>
    /// テキストをSentencePiece互換形式に正規化
    /// </summary>
    /// <param name="input">入力テキスト</param>
    /// <returns>正規化されたテキスト</returns>
    public string Normalize(string input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        try
        {
            var normalized = input;

            // ステップ1: Unicode NFKC正規化
            if (_options.ApplyNfkcNormalization)
            {
                normalized = ApplyNfkcNormalization(normalized);
            }

            // ステップ2: 制御文字の処理
            if (_options.RemoveControlCharacters)
            {
                normalized = RemoveControlCharacters(normalized);
            }

            // ステップ3: 空白文字の正規化
            if (_options.NormalizeWhitespace)
            {
                normalized = NormalizeWhitespace(normalized);
            }

            // ステップ4: プレフィックススペース記号の付与
            if (_options.AddPrefixSpace)
            {
                normalized = AddPrefixSpaceSymbol(normalized);
            }

            _logger.LogDebug("テキスト正規化完了: '{Input}' -> '{Output}'", input, normalized);
            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト正規化中にエラーが発生: '{Input}'", input);
            throw new InvalidOperationException($"Failed to normalize text: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Unicode NFKC正規化を適用
    /// 互換文字の統一化と結合文字の正規化
    /// </summary>
    private string ApplyNfkcNormalization(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        try
        {
            // .NET標準のNFKC正規化を適用
            var normalized = input.Normalize(NormalizationForm.FormKC);
            
            _logger.LogTrace("NFKC正規化: '{Input}' -> '{Output}'", input, normalized);
            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NFKC正規化に失敗、元のテキストを返します: '{Input}'", input);
            return input;
        }
    }

    /// <summary>
    /// 制御文字の除去・変換
    /// SentencePieceの標準動作に従って処理
    /// </summary>
    private string RemoveControlCharacters(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        
        foreach (var c in input)
        {
            var category = char.GetUnicodeCategory(c);
            
            // 制御文字の処理
            if (category == UnicodeCategory.Control)
            {
                // 保持する制御文字（タブ・改行）
                if (c == '\t' || c == '\n' || c == '\r')
                {
                    // タブと改行は空白に変換
                    sb.Append(' ');
                }
                // その他の制御文字は除去（何も追加しない）
            }
            else
            {
                // 制御文字以外はそのまま保持
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        _logger.LogTrace("制御文字処理: '{Input}' -> '{Output}'", input, result);
        return result;
    }

    /// <summary>
    /// 空白文字の正規化
    /// 連続空白の統一化と全角空白の変換
    /// </summary>
    private string NormalizeWhitespace(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        bool previousWasSpace = false;

        foreach (var c in input)
        {
            bool isWhitespace = char.IsWhiteSpace(c);
            
            if (isWhitespace)
            {
                // 連続する空白は単一の空白に統一
                if (!previousWasSpace)
                {
                    sb.Append(' '); // 全て半角空白に統一
                    previousWasSpace = true;
                }
                // 連続する空白は追加しない
            }
            else
            {
                sb.Append(c);
                previousWasSpace = false;
            }
        }

        var result = sb.ToString();
        _logger.LogTrace("空白正規化: '{Input}' -> '{Output}'", input, result);
        return result;
    }

    /// <summary>
    /// プレフィックススペース記号の付与
    /// SentencePieceの単語境界マーカーを追加
    /// </summary>
    private string AddPrefixSpaceSymbol(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 先頭にスペース記号を追加し、内部の空白もスペース記号に変換
        var result = SpaceSymbol + input.Replace(' ', SpaceSymbol);
        
        _logger.LogTrace("プレフィックススペース付与: '{Input}' -> '{Output}'", input, result);
        return result;
    }

    /// <summary>
    /// プレフィックススペース記号を除去してテキストを復元
    /// </summary>
    /// <param name="input">スペース記号付きテキスト</param>
    /// <returns>復元されたテキスト</returns>
    public string RemovePrefixSpaceSymbol(string input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(input))
            return input;

        try
        {
            // スペース記号を通常の空白に戻し、先頭のスペース記号を除去
            var result = input.Replace(SpaceSymbol, ' ');
            
            // 先頭の空白を除去（元のプレフィックススペース記号）
            if (result.StartsWith(' '))
            {
                result = result[1..];
            }

            _logger.LogTrace("プレフィックススペース除去: '{Input}' -> '{Output}'", input, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プレフィックススペース除去中にエラーが発生: '{Input}'", input);
            throw new InvalidOperationException($"Failed to remove prefix space symbol: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 正規化オプションを取得
    /// </summary>
    public SentencePieceNormalizationOptions GetOptions() => _options;

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger.LogDebug("SentencePieceNormalizer disposed");
    }
}

/// <summary>
/// SentencePiece正規化オプション
/// </summary>
public sealed class SentencePieceNormalizationOptions
{
    /// <summary>
    /// Unicode NFKC正規化を適用するか
    /// </summary>
    public bool ApplyNfkcNormalization { get; set; } = true;

    /// <summary>
    /// 制御文字を除去するか
    /// </summary>
    public bool RemoveControlCharacters { get; set; } = true;

    /// <summary>
    /// 空白文字を正規化するか
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// プレフィックススペース記号を付与するか
    /// </summary>
    public bool AddPrefixSpace { get; set; } = true;

    /// <summary>
    /// デフォルト設定
    /// </summary>
    public static SentencePieceNormalizationOptions Default => new()
    {
        ApplyNfkcNormalization = true,
        RemoveControlCharacters = true,
        NormalizeWhitespace = true,
        AddPrefixSpace = true
    };

    /// <summary>
    /// OPUS-MT向け設定
    /// </summary>
    public static SentencePieceNormalizationOptions OpusMt => new()
    {
        ApplyNfkcNormalization = true,
        RemoveControlCharacters = true,
        NormalizeWhitespace = true,
        AddPrefixSpace = true
    };

    /// <summary>
    /// 正規化なし設定（テスト用）
    /// </summary>
    public static SentencePieceNormalizationOptions None => new()
    {
        ApplyNfkcNormalization = false,
        RemoveControlCharacters = false,
        NormalizeWhitespace = false,
        AddPrefixSpace = false
    };
}
