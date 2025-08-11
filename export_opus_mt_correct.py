#!/usr/bin/env python3
"""
Helsinki-NLP/opus-mt-ja-enモデルを正しい語彙対応でONNX形式にエクスポートするスクリプト
Geminiの推奨に基づく実装
"""

import os
import sys
from pathlib import Path
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
from optimum.onnxruntime import ORTModelForSeq2SeqLM

def export_opus_mt_correct():
    """
    Helsinki-NLP/opus-mt-ja-enを正しい語彙対応でONNX形式にエクスポート
    """
    # 設定
    model_id = "Helsinki-NLP/opus-mt-ja-en"
    base_dir = Path(__file__).parent  # E:\dev\Baketa
    output_dir = base_dir / "Models" / "ONNX_Corrected"
    
    print(f"=== OPUS-MT 正しい語彙対応 ONNX Export ===")
    print(f"モデル: {model_id}")
    print(f"出力先: {output_dir}")
    
    try:
        # 出力ディレクトリを作成
        output_dir.mkdir(parents=True, exist_ok=True)
        
        print(f"\n1. トークナイザーをロード中...")
        tokenizer = AutoTokenizer.from_pretrained(model_id)
        print(f"   語彙サイズ: {tokenizer.vocab_size:,}")
        print(f"   特殊トークン:")
        print(f"     - BOS: {tokenizer.bos_token} (ID: {tokenizer.bos_token_id})")
        print(f"     - EOS: {tokenizer.eos_token} (ID: {tokenizer.eos_token_id})")
        print(f"     - UNK: {tokenizer.unk_token} (ID: {tokenizer.unk_token_id})")
        print(f"     - PAD: {tokenizer.pad_token} (ID: {tokenizer.pad_token_id})")
        
        print(f"\n2. PyTorchモデルからONNXにエクスポート中...")
        # from_transformers=True でPyTorchモデルからONNX変換
        model = ORTModelForSeq2SeqLM.from_pretrained(model_id, from_transformers=True)
        
        print(f"\n3. モデルとトークナイザーを保存中...")
        model.save_pretrained(output_dir)
        tokenizer.save_pretrained(output_dir)
        
        print(f"\n✅ エクスポート完了!")
        print(f"保存先: {output_dir}")
        print(f"\n📁 生成されたファイル:")
        for item in sorted(output_dir.iterdir()):
            if item.is_file():
                size_mb = item.stat().st_size / (1024 * 1024)
                print(f"   - {item.name} ({size_mb:.1f} MB)")
        
        # 重要なファイルの存在確認
        important_files = [
            "encoder_model.onnx",
            "decoder_model.onnx", 
            "tokenizer.json",
            "vocab.json"
        ]
        
        print(f"\n🔍 重要ファイル確認:")
        for filename in important_files:
            filepath = output_dir / filename
            exists = filepath.exists()
            status = "✅" if exists else "❌"
            print(f"   {status} {filename}")
        
        # トークナイザーテスト
        print(f"\n🧪 トークナイザーテスト:")
        test_text = "こんにちは、世界！"
        tokens = tokenizer.encode(test_text)
        decoded = tokenizer.decode(tokens)
        print(f"   - 入力: '{test_text}'")
        print(f"   - トークンID: {tokens}")
        print(f"   - 最大トークンID: {max(tokens) if tokens else 0}")
        print(f"   - デコード結果: '{decoded}'")
        
        # 語彙サイズと整合性確認
        vocab_size = tokenizer.vocab_size
        max_token_id = max(tokens) if tokens else 0
        print(f"\n📊 語彙整合性:")
        print(f"   - 語彙サイズ: {vocab_size:,}")
        print(f"   - 最大トークンID: {max_token_id}")
        print(f"   - 整合性: {'✅ OK' if max_token_id < vocab_size else '❌ NG'}")
        
        return True
        
    except Exception as e:
        print(f"\n❌ エラーが発生しました: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    print("必要なライブラリが正しくインストールされていることを確認中...")
    
    try:
        import transformers
        import optimum
        import sentencepiece
        print(f"✅ transformers: {transformers.__version__}")
        print(f"✅ optimum: {optimum.__version__}")
        print(f"✅ sentencepiece: 利用可能")
    except ImportError as e:
        print(f"❌ 必要なライブラリが不足: {e}")
        print("以下のコマンドでインストールしてください:")
        print("  pip install transformers optimum[onnxruntime] sentencepiece torch")
        sys.exit(1)
    
    print()
    success = export_opus_mt_correct()
    
    if success:
        print("\n🎉 OPUS-MT正しい語彙対応エクスポートが完了しました!")
        print("\n📋 次のステップ:")
        print("1. Models/ONNX_Corrected/tokenizer.json を確認")
        print("2. Models/ONNX_Corrected/vocab.json を確認")
        print("3. C#実装でこれらのファイルを利用")
        print("4. 語彙サイズ不整合問題の解決確認")
    else:
        print("\n💥 エクスポートに失敗しました。エラーログを確認してください。")
        sys.exit(1)