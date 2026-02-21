using System.IO;
using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace Baketa.Infrastructure.Translation.Onnx;

/// <summary>
/// NLLB-200 用トークナイザー
/// SentencePiece BPE モデル + 言語コードトークン + ボキャブラリマッピング
/// </summary>
internal sealed class NllbTokenizer
{
    private readonly Tokenizer _spTokenizer;
    private readonly Dictionary<string, int> _langCodeToId;
    private readonly Dictionary<int, string> _idToLangCode;

    // ボキャブラリスライシング用マッピング（Issue #452）
    // null の場合はマッピング不要（オリジナルvocab使用）
    private readonly int[]? _newToOld;  // new_id → old_fairseq_id
    private readonly Dictionary<int, int>? _oldToNew;  // old_fairseq_id → new_id

    // 特殊トークン（スライス後も位置は変わらない）
    public const int BosTokenId = 0;  // <s>
    public const int PadTokenId = 1;  // <pad>
    public const int EosTokenId = 2;  // </s>
    public const int UnkTokenId = 3;  // <unk>

    // NLLB-200 (fairseq) は SentencePiece の内部IDに +1 のオフセットを適用
    // SP内部: 0=<unk>, 1=<s>, 2=</s>, 3=最初のサブワード
    // HF/fairseq: 0=<s>, 1=<pad>, 2=</s>, 3=<unk>, 4=最初のサブワード
    private const int FairseqOffset = 1;

    public string SourceLanguage { get; set; } = "eng_Latn";

    private NllbTokenizer(
        Tokenizer spTokenizer,
        Dictionary<string, int> langCodes,
        int[]? newToOld = null,
        Dictionary<int, int>? oldToNew = null)
    {
        _spTokenizer = spTokenizer;
        _langCodeToId = langCodes;
        _idToLangCode = langCodes.ToDictionary(kv => kv.Value, kv => kv.Key);
        _newToOld = newToOld;
        _oldToNew = oldToNew;
    }

    /// <summary>
    /// モデルディレクトリからトークナイザーを作成
    /// </summary>
    public static NllbTokenizer FromDirectory(string modelDir)
    {
        var spModelPath = Path.Combine(modelDir, "sentencepiece.bpe.model");
        var langCodesPath = Path.Combine(modelDir, "lang_codes.json");
        var vocabMappingPath = Path.Combine(modelDir, "vocab_mapping.json");

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
            // フォールバック: 主要言語のみ（オリジナルvocab用）
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
                ["rus_Cyrl"] = 256147,
                ["arb_Arab"] = 256011,
            };
        }

        // ボキャブラリマッピング読み込み（Issue #452: スライス済みモデル用）
        int[]? newToOld = null;
        Dictionary<int, int>? oldToNew = null;
        if (File.Exists(vocabMappingPath))
        {
            var mappingJson = File.ReadAllText(vocabMappingPath);
            var mapping = JsonSerializer.Deserialize<VocabMappingData>(mappingJson)
                ?? throw new InvalidOperationException("vocab_mapping.json の解析に失敗");

            newToOld = mapping.NewToOld;
            oldToNew = [];
            for (int i = 0; i < newToOld.Length; i++)
                oldToNew[newToOld[i]] = i;
        }

        return new NllbTokenizer(spTokenizer, langCodes, newToOld, oldToNew);
    }

    /// <summary>
    /// テキストをトークンIDに変換
    /// 出力: [src_lang_id, token_1, token_2, ..., eos_id]
    /// </summary>
    public int[] Encode(string text)
    {
        if (!_langCodeToId.TryGetValue(SourceLanguage, out var srcLangId))
            throw new ArgumentException($"未知の言語コード: {SourceLanguage}");

        // SentencePiece でトークナイズ
        var encoded = _spTokenizer.EncodeToIds(text);

        // SP 特殊トークン (0=unk, 1=bos, 2=eos) をフィルタリング
        var tokens = new List<int>();
        foreach (var spId in encoded)
        {
            if (spId < 3) continue; // SP の特殊トークンをスキップ

            var fairseqId = spId + FairseqOffset;

            if (_oldToNew != null)
            {
                // スライス済みモデル: fairseq ID → new ID にマッピング
                if (_oldToNew.TryGetValue(fairseqId, out var newId))
                    tokens.Add(newId);
                else
                    tokens.Add(UnkTokenId); // スライスで除外されたトークンは <unk> に
            }
            else
            {
                tokens.Add(fairseqId);
            }
        }

        // [src_lang] + [tokens...] + [eos]
        var result = new int[tokens.Count + 2];
        result[0] = srcLangId;
        for (int i = 0; i < tokens.Count; i++)
            result[i + 1] = tokens[i];
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
    /// 指定のNLLB言語コードをサポートしているか
    /// </summary>
    public bool SupportsLanguage(string langCode)
        => _langCodeToId.ContainsKey(langCode);

    /// <summary>
    /// トークンIDをテキストに変換（特殊トークンを除外）
    /// </summary>
    public string Decode(ReadOnlySpan<int> tokenIds)
    {
        // 特殊トークンと言語コードをフィルタリングし、SP内部IDに変換
        var filtered = new List<int>();
        foreach (var id in tokenIds)
        {
            if (id == BosTokenId || id == EosTokenId || id == PadTokenId)
                continue;
            if (_idToLangCode.ContainsKey(id))
                continue;

            int fairseqId;
            if (_newToOld != null)
            {
                // スライス済みモデル: new ID → old fairseq ID に復元
                if (id >= 0 && id < _newToOld.Length)
                    fairseqId = _newToOld[id];
                else
                    continue; // 範囲外はスキップ
            }
            else
            {
                fairseqId = id;
            }

            // fairseq オフセットを元に戻して SP 内部IDに変換
            filtered.Add(fairseqId - FairseqOffset);
        }

        if (filtered.Count == 0)
            return string.Empty;

        return _spTokenizer.Decode(filtered) ?? string.Empty;
    }

    /// <summary>
    /// vocab_mapping.json のデシリアライズ用モデル
    /// </summary>
    private sealed class VocabMappingData
    {
        [System.Text.Json.Serialization.JsonPropertyName("new_vocab_size")]
        public int NewVocabSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("original_vocab_size")]
        public int OriginalVocabSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("new_to_old")]
        public int[] NewToOld { get; set; } = [];
    }
}
