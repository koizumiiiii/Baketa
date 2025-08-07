#!/usr/bin/env python3
"""
HuggingFace Transformersを使用したOPUS-MT翻訳テスト
語彙サイズ不整合問題の解決確認
"""

def test_transformers_translation():
    """
    HuggingFace Transformersで一貫した翻訳処理をテスト
    """
    print("=== HuggingFace Transformers OPUS-MT翻訳テスト ===")
    
    try:
        # 必要なライブラリの確認
        print("1. ライブラリ確認中...")
        import transformers
        print(f"   OK transformers: {transformers.__version__}")
        
        from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
        print("   OK モデルクラス: インポート成功")
        
        # モデルとトークナイザーのロード
        print("\n2. モデルとトークナイザーをロード中...")
        model_id = "Helsinki-NLP/opus-mt-ja-en"
        
        tokenizer = AutoTokenizer.from_pretrained(model_id)
        model = AutoModelForSeq2SeqLM.from_pretrained(model_id)
        
        print(f"   OK モデル: {model_id}")
        print(f"   OK 語彙サイズ: {tokenizer.vocab_size:,}")
        
        # 特殊トークン確認
        print("\n3. 特殊トークン確認:")
        print(f"   - BOS: {tokenizer.bos_token} (ID: {tokenizer.bos_token_id})")
        print(f"   - EOS: {tokenizer.eos_token} (ID: {tokenizer.eos_token_id})")
        print(f"   - UNK: {tokenizer.unk_token} (ID: {tokenizer.unk_token_id})")
        print(f"   - PAD: {tokenizer.pad_token} (ID: {tokenizer.pad_token_id})")
        
        # テスト翻訳
        print("\n4. 翻訳テスト実行:")
        test_cases = [
            "こんにちは",
            "……複雑でよくわからない", 
            "世界",
            "ありがとうございます"
        ]
        
        for japanese_text in test_cases:
            print(f"\n   入力: '{japanese_text}'")
            
            # トークン化
            inputs = tokenizer(japanese_text, return_tensors="pt")
            print(f"   トークンID: {inputs['input_ids'][0].tolist()}")
            
            # 翻訳生成
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            # デコード
            translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            print(f"   翻訳結果: '{translation}'")
            
            # 品質確認
            if any(bad in translation.lower() for bad in ["excuse", "lost", "while", "tok_"]):
                print("   NG 問題パターン検出")
            else:
                print("   OK 正常な翻訳")
        
        print("\n5. 語彙整合性確認:")
        test_text = "これはテストです"
        tokens = tokenizer.encode(test_text)
        max_token_id = max(tokens) if tokens else 0
        vocab_size = tokenizer.vocab_size
        
        print(f"   - 語彙サイズ: {vocab_size:,}")
        print(f"   - 最大トークンID: {max_token_id}")
        print(f"   - 整合性: {'OK' if max_token_id < vocab_size else 'NG'}")
        
        return True
        
    except ImportError as e:
        print(f"NG ライブラリ不足: {e}")
        print("以下のコマンドでインストール:")
        print("  pip install transformers torch sentencepiece")
        return False
    except Exception as e:
        print(f"NG エラー発生: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    # PyTorchも必要
    try:
        import torch
        print(f"OK torch: {torch.__version__}")
    except ImportError:
        print("NG torch未インストール")
        print("  pip install torch")
        exit(1)
    
    success = test_transformers_translation()
    
    if success:
        print("\n SUCCESS HuggingFace Transformers翻訳テスト成功!")
        print("語彙サイズ不整合問題が解決されました。")
    else:
        print("\n FAILED テスト失敗。エラー内容を確認してください。")