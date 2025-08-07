#!/usr/bin/env python3
"""
Helsinki-NLP/opus-mt-ja-enモデルを32,000語彙サイズに合わせてONNX形式にエクスポートするスクリプト
"""

import os
import sys
from pathlib import Path
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
from optimum.onnxruntime import ORTModelForSeq2SeqLM

def export_to_onnx_32k():
    """
    Helsinki-NLP/opus-mt-ja-enをONNX形式にエクスポート
    """
    # 設定
    model_id = "Helsinki-NLP/opus-mt-ja-en"
    base_dir = Path(__file__).parent.parent  # E:\dev\Baketa
    output_dir = base_dir / "Models" / "ONNX_32k"
    
    print(f"=== OPUS-MT ONNX Export (32k語彙対応) ===")
    print(f"モデル: {model_id}")
    print(f"出力先: {output_dir}")
    
    try:
        # 出力ディレクトリを作成
        output_dir.mkdir(parents=True, exist_ok=True)
        
        print(f"\n1. トークナイザーをロード中...")
        tokenizer = AutoTokenizer.from_pretrained(model_id)
        print(f"   語彙サイズ: {tokenizer.vocab_size:,}")
        
        print(f"\n2. PyTorchモデルをロードしてONNXにエクスポート中...")
        # export=Trueで自動的にONNX形式に変換
        model = ORTModelForSeq2SeqLM.from_pretrained(model_id, export=True)
        
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
        
        # トークナイザー情報を確認
        print(f"\n📊 トークナイザー情報:")
        print(f"   - 語彙サイズ: {tokenizer.vocab_size:,}")
        print(f"   - BOS Token: {tokenizer.bos_token} (ID: {tokenizer.bos_token_id})")
        print(f"   - EOS Token: {tokenizer.eos_token} (ID: {tokenizer.eos_token_id})")
        print(f"   - UNK Token: {tokenizer.unk_token} (ID: {tokenizer.unk_token_id})")
        print(f"   - PAD Token: {tokenizer.pad_token} (ID: {tokenizer.pad_token_id})")
        
        # テストトークン化
        test_text = "こんにちは、世界！"
        tokens = tokenizer.encode(test_text)
        print(f"\n🧪 テストトークン化:")
        print(f"   - 入力: '{test_text}'")
        print(f"   - トークン: {tokens}")
        print(f"   - 最大トークンID: {max(tokens) if tokens else 0}")
        
        return True
        
    except Exception as e:
        print(f"\n❌ エラーが発生しました: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    print("必要なライブラリ:")
    print("  pip install transformers optimum[onnxruntime] sentencepiece torch")
    print()
    
    success = export_to_onnx_32k()
    if success:
        print("\n🎉 ONNX変換が正常に完了しました!")
        print("Baketa.Infrastructure.Translation.Local.Onnx.AlphaOpusMtTranslationEngineで")
        print("Models/ONNX_32k/encoder_model.onnxとdecoder_model.onnxを使用してください。")
    else:
        print("\n💥 ONNX変換に失敗しました。エラーログを確認してください。")
        sys.exit(1)