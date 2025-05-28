"""
テスト用のSentencePieceモデルファイルを作成するスクリプト

このスクリプトはGoogle SentencePieceライブラリを使用して、
テスト用の小さなSentencePieceモデルファイルを作成します。

必要なライブラリ:
pip install sentencepiece

使用方法:
python create_test_sentencepiece_model.py
"""

import sentencepiece as spm
import os

def create_test_model():
    """テスト用のSentencePieceモデルを作成"""
    
    # テスト用の語彙を含むサンプルテキストを作成
    sample_texts = [
        "Hello world",
        "こんにちは世界",
        "How are you?",
        "元気ですか？",
        "Good morning",
        "おはよう",
        "Thank you",
        "ありがとう",
        "Yes, No",
        "はい、いいえ",
        "I am fine",
        "私は元気です",
        "This is a test",
        "これはテストです",
        "machine translation",
        "機械翻訳",
        "artificial intelligence",
        "人工知能",
        "deep learning",
        "深層学習",
        "natural language processing",
        "自然言語処理"
    ]
    
    # サンプルテキストファイルを作成
    input_file = "test_corpus.txt"
    with open(input_file, "w", encoding="utf-8") as f:
        for text in sample_texts:
            f.write(text + "\n")
    
    # SentencePieceモデルを訓練
    model_prefix = "test-sentencepiece"
    
    # 小さなモデルを作成（vocab_size=1000）
    spm.SentencePieceTrainer.train(
        input=input_file,
        model_prefix=model_prefix,
        vocab_size=1000,
        model_type="unigram",
        character_coverage=0.995,
        unk_id=0,
        bos_id=1,
        eos_id=2,
        pad_id=3,
        normalization_rule_name="nfkc"
    )
    
    # 作成されたファイルを確認
    model_file = f"{model_prefix}.model"
    vocab_file = f"{model_prefix}.vocab"
    
    if os.path.exists(model_file):
        print(f"✅ SentencePieceモデルファイルが作成されました: {model_file}")
        print(f"   ファイルサイズ: {os.path.getsize(model_file)} bytes")
    else:
        print("❌ モデルファイルの作成に失敗しました")
    
    if os.path.exists(vocab_file):
        print(f"✅ 語彙ファイルが作成されました: {vocab_file}")
        
    # 作成したモデルをテスト
    test_model(model_file)
    
    # 一時ファイルをクリーンアップ
    os.remove(input_file)
    
    return model_file

def test_model(model_file):
    """作成したモデルをテスト"""
    print("\n📝 モデルテスト:")
    
    # モデルを読み込み
    sp = spm.SentencePieceProcessor()
    sp.load(model_file)
    
    # テストケース
    test_cases = [
        "Hello",
        "World",
        "こんにちは",
        "機械翻訳",
        "This is a test sentence."
    ]
    
    for text in test_cases:
        # エンコード
        tokens = sp.encode(text)
        pieces = sp.encode_as_pieces(text)
        
        # デコード
        decoded = sp.decode(tokens)
        
        print(f"  入力: {text}")
        print(f"  トークン: {tokens}")
        print(f"  ピース: {pieces}")
        print(f"  デコード: {decoded}")
        print()

def main():
    print("🚀 テスト用SentencePieceモデル作成スクリプト")
    print("=" * 50)
    
    try:
        model_file = create_test_model()
        
        print("\n✅ 作成完了!")
        print(f"作成されたモデル: {model_file}")
        print("\nこのファイルをBaketaプロジェクトのModels/SentencePieceディレクトリにコピーしてください。")
        
    except ImportError:
        print("❌ エラー: sentencepieceライブラリがインストールされていません")
        print("以下のコマンドでインストールしてください:")
        print("pip install sentencepiece")
        
    except Exception as e:
        print(f"❌ エラー: {e}")

if __name__ == "__main__":
    main()
