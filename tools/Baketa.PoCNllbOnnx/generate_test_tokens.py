"""
テスト用トークンデータの生成スクリプト
Optimum でエクスポートした NLLB-200 モデルを使って:
1. テスト文をトークナイズ
2. Python (Optimum) で翻訳を実行（正解データ生成）
3. トークンIDを JSON に保存（C# PoC で使用）
"""
import json
import os
import sys

# ONNX モデルディレクトリ
MODEL_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "models", "nllb-200-onnx")
MODEL_DIR = os.path.abspath(MODEL_DIR)

def main():
    print(f"モデルディレクトリ: {MODEL_DIR}")

    from transformers import AutoTokenizer
    from optimum.onnxruntime import ORTModelForSeq2SeqLM

    # トークナイザー読み込み
    print("トークナイザー読み込み中...")
    tokenizer = AutoTokenizer.from_pretrained(MODEL_DIR)

    # テストケース
    test_cases = [
        {
            "source_text": "Hello, how are you?",
            "source_lang": "eng_Latn",
            "target_lang": "jpn_Jpan",
        },
        {
            "source_text": "The quick brown fox jumps over the lazy dog.",
            "source_lang": "eng_Latn",
            "target_lang": "jpn_Jpan",
        },
        {
            "source_text": "Press Start to begin the game.",
            "source_lang": "eng_Latn",
            "target_lang": "jpn_Jpan",
        },
    ]

    # ONNX モデル読み込み（Python で正解翻訳を生成）
    print("ONNX モデル読み込み中...")
    model = ORTModelForSeq2SeqLM.from_pretrained(MODEL_DIR)

    results = []
    for tc in test_cases:
        print(f"\n--- テスト: '{tc['source_text']}' ({tc['source_lang']} -> {tc['target_lang']}) ---")

        # ソース言語設定
        tokenizer.src_lang = tc["source_lang"]
        inputs = tokenizer(tc["source_text"], return_tensors="pt")
        input_ids = inputs["input_ids"][0].tolist()

        # ターゲット言語トークンID
        target_lang_token_id = tokenizer.convert_tokens_to_ids(tc["target_lang"])

        print(f"  入力トークンIDs: {input_ids}")
        print(f"  ターゲット言語ID: {target_lang_token_id}")

        # Python (Optimum) で翻訳実行
        outputs = model.generate(
            **inputs,
            forced_bos_token_id=target_lang_token_id,
            max_length=128,
            num_beams=1,  # グリーディサーチ（C# PoC と同条件）
        )
        output_ids = outputs[0].tolist()
        translation = tokenizer.decode(output_ids, skip_special_tokens=True)

        print(f"  出力トークンIDs: {output_ids}")
        print(f"  翻訳結果: {translation}")

        result = {
            "SourceText": tc["source_text"],
            "SourceLang": tc["source_lang"],
            "TargetLang": tc["target_lang"],
            "InputIds": input_ids,
            "TargetLangTokenId": target_lang_token_id,
            "ExpectedTranslation": translation,
            "ExpectedOutputIds": output_ids,
        }
        results.append(result)

    # 最初のテストケースを C# PoC 用に保存
    first = results[0]
    test_data_path = os.path.join(MODEL_DIR, "test_tokens.json")
    with open(test_data_path, "w", encoding="utf-8") as f:
        json.dump(first, f, ensure_ascii=False, indent=2)
    print(f"\nテストデータ保存: {test_data_path}")

    # 全テストケースも保存
    all_data_path = os.path.join(MODEL_DIR, "all_test_tokens.json")
    with open(all_data_path, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    print(f"全テストデータ保存: {all_data_path}")

if __name__ == "__main__":
    main()
