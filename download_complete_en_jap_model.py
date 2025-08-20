#!/usr/bin/env python3
"""
完全なen-japモデルをプロジェクト内にダウンロード
品質確認のため公式から全ファイルを取得
"""
import os
import sys
from transformers import MarianMTModel, MarianTokenizer

def download_complete_model():
    """完全なen-japモデルをダウンロード"""
    print("完全なHelsinki-NLP/opus-mt-en-japモデルをダウンロード中...")
    
    # プロジェクト内の保存先
    project_root = os.path.dirname(os.path.abspath(__file__))
    save_path = os.path.join(project_root, "Models", "HuggingFace", "opus-mt-en-jap-complete")
    
    # ディレクトリを作成
    os.makedirs(save_path, exist_ok=True)
    print(f"保存先: {save_path}")
    
    try:
        # モデルをダウンロード
        print("モデル本体をダウンロード中...")
        model = MarianMTModel.from_pretrained("Helsinki-NLP/opus-mt-en-jap")
        model.save_pretrained(save_path)
        print("モデル本体ダウンロード完了")
        
        # トークナイザーをダウンロード
        print("トークナイザーをダウンロード中...")
        tokenizer = MarianTokenizer.from_pretrained("Helsinki-NLP/opus-mt-en-jap")
        tokenizer.save_pretrained(save_path)
        print("トークナイザーダウンロード完了")
        
        print("\n完全なen-japモデルのダウンロード完了!")
        print(f"保存場所: {save_path}")
        
        # ダウンロードしたファイルを確認
        print("\nダウンロードされたファイル:")
        for file in sorted(os.listdir(save_path)):
            file_path = os.path.join(save_path, file)
            size = os.path.getsize(file_path)
            print(f"   {file} ({size:,} bytes)")
            
        return save_path
        
    except Exception as e:
        print(f"エラーが発生しました: {e}")
        return None

def test_translation_quality(model_path):
    """ダウンロードしたモデルの翻訳品質をテスト"""
    print(f"\n翻訳品質テスト開始: {model_path}")
    
    try:
        # モデルとトークナイザーをロード
        model = MarianMTModel.from_pretrained(model_path)
        tokenizer = MarianTokenizer.from_pretrained(model_path)
        
        # テストケース
        test_cases = [
            "Hello",
            "Hello World",
            "Good morning",
            "How are you?",
            "I love programming",
            "This is a translation test"
        ]
        
        print("翻訳結果:")
        for test_text in test_cases:
            # トークナイズ
            inputs = tokenizer(test_text, return_tensors="pt", padding=True)
            
            # 翻訳実行
            outputs = model.generate(**inputs, max_length=50, num_beams=1, early_stopping=True)
            translated = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            print(f"   '{test_text}' → '{translated}'")
            
        return True
        
    except Exception as e:
        print(f"翻訳テスト中にエラー: {e}")
        return False

if __name__ == "__main__":
    print("en-japモデル完全ダウンロード・品質確認スクリプト")
    print("=" * 60)
    
    # モデルをダウンロード
    model_path = download_complete_model()
    
    if model_path:
        # 品質テスト
        test_translation_quality(model_path)
    else:
        print("ダウンロードに失敗しました")
        sys.exit(1)
    
    print("\nスクリプト完了")