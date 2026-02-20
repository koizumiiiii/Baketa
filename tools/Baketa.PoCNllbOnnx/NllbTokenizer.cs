// NLLB-200 トークナイザー (C# 実装)
// SentencePiece BPE + NLLB固有の言語トークンを統合

using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace Baketa.PoCNllbOnnx;

/// <summary>
/// NLLB-200 用トークナイザー
/// SentencePiece BPE モデル + 言語コードトークン
/// </summary>
sealed class NllbTokenizer
{
    private readonly Tokenizer _spTokenizer;
    private readonly Dictionary<string, int> _langCodeToId;
    private readonly Dictionary<int, string> _idToLangCode;

    // 特殊トークン
    public const int BosTokenId = 0;  // <s>
    public const int PadTokenId = 1;  // <pad>
    public const int EosTokenId = 2;  // </s>
    public const int UnkTokenId = 3;  // <unk>

    // NLLB-200 (fairseq) は SentencePiece の内部IDに +1 のオフセットを適用
    // SP内部: 0=<unk>, 1=<s>, 2=</s>, 3=最初のサブワード
    // HF/fairseq: 0=<s>, 1=<pad>, 2=</s>, 3=<unk>, 4=最初のサブワード
    private const int FairseqOffset = 1;

    public string SourceLanguage { get; set; } = "eng_Latn";

    private NllbTokenizer(Tokenizer spTokenizer, Dictionary<string, int> langCodes)
    {
        _spTokenizer = spTokenizer;
        _langCodeToId = langCodes;
        _idToLangCode = langCodes.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    /// <summary>
    /// モデルディレクトリからトークナイザーを作成
    /// </summary>
    public static NllbTokenizer FromDirectory(string modelDir)
    {
        var spModelPath = Path.Combine(modelDir, "sentencepiece.bpe.model");
        var langCodesPath = Path.Combine(modelDir, "lang_codes.json");

        if (!File.Exists(spModelPath))
            throw new FileNotFoundException("SentencePiece モデルが見つかりません", spModelPath);

        // SentencePiece モデル読み込み
        using var stream = File.OpenRead(spModelPath);
        var spTokenizer = SentencePieceTokenizer.Create(stream);

        // 言語コードマッピング読み込み
        Dictionary<string, int> langCodes;
        if (File.Exists(langCodesPath))
        {
            var json = File.ReadAllText(langCodesPath);
            langCodes = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                ?? throw new InvalidOperationException("lang_codes.json の解析に失敗");
        }
        else
        {
            // フォールバック: 主要言語のみ
            langCodes = new Dictionary<string, int>
            {
                ["eng_Latn"] = 256047,
                ["jpn_Jpan"] = 256079,
                ["zho_Hans"] = 256200,
                ["zho_Hant"] = 256201,
                ["kor_Hang"] = 256098,
                ["fra_Latn"] = 256057,
                ["deu_Latn"] = 256042,
                ["spa_Latn"] = 256161,
            };
        }

        return new NllbTokenizer(spTokenizer, langCodes);
    }

    /// <summary>
    /// テキストをトークンIDに変換
    /// 出力: [src_lang_id, sp_token_1, sp_token_2, ..., eos_id]
    /// </summary>
    public int[] Encode(string text)
    {
        if (!_langCodeToId.TryGetValue(SourceLanguage, out var srcLangId))
            throw new ArgumentException($"未知の言語コード: {SourceLanguage}");

        // SentencePiece でトークナイズ
        var encoded = _spTokenizer.EncodeToIds(text);

        // SP 特殊トークン (0=unk, 1=bos, 2=eos) をフィルタリング
        var spTokens = new List<int>();
        foreach (var id in encoded)
        {
            if (id >= 3) // SP の通常トークンのみ
                spTokens.Add(id + FairseqOffset);
        }

        // [src_lang] + [sp_tokens + offset...] + [eos]
        var result = new int[spTokens.Count + 2];
        result[0] = srcLangId;
        for (int i = 0; i < spTokens.Count; i++)
            result[i + 1] = spTokens[i];
        result[^1] = EosTokenId;

        return result;
    }

    /// <summary>
    /// ターゲット言語コードのトークンIDを取得
    /// </summary>
    public int GetLanguageTokenId(string langCode)
    {
        if (_langCodeToId.TryGetValue(langCode, out var id))
            return id;
        throw new ArgumentException($"未知の言語コード: {langCode}");
    }

    /// <summary>
    /// トークンIDをテキストに変換（特殊トークンを除外）
    /// </summary>
    public string Decode(ReadOnlySpan<int> tokenIds)
    {
        // 特殊トークンと言語コードをフィルタリングし、オフセットを元に戻す
        var filtered = new List<int>();
        foreach (var id in tokenIds)
        {
            if (id == BosTokenId || id == EosTokenId || id == PadTokenId)
                continue;
            if (_idToLangCode.ContainsKey(id))
                continue;
            // fairseq オフセットを元に戻して SP 内部IDに変換
            filtered.Add(id - FairseqOffset);
        }

        if (filtered.Count == 0)
            return string.Empty;

        return _spTokenizer.Decode(filtered) ?? string.Empty;
    }
}
