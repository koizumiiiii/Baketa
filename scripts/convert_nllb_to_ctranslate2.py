#!/usr/bin/env python3
"""
NLLB-200 CTranslate2変換スクリプト

facebook/nllb-200-distilled-600MモデルをCTranslate2 int8形式に変換
メモリ使用量: 2.4GB → 0.5GB (80%削減)
"""

import argparse
import logging
import os
import sys
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s'
)
logger = logging.getLogger(__name__)


def check_dependencies():
    """必要なライブラリの確認"""
    try:
        import ctranslate2
        import transformers
        logger.info(f"✅ ctranslate2 version: {ctranslate2.__version__}")
        logger.info(f"✅ transformers version: {transformers.__version__}")
        return True
    except ImportError as e:
        logger.error(f"❌ 依存ライブラリが不足しています: {e}")
        logger.error("pip install ctranslate2 transformers を実行してください")
        return False


def convert_model(
    model_name: str = "facebook/nllb-200-distilled-600M",
    output_dir: str = "models/nllb-200-ct2",
    quantization: str = "int8"
):
    """
    NLLB-200モデルをCTranslate2形式に変換

    Args:
        model_name: HuggingFace モデル名
        output_dir: 出力ディレクトリ
        quantization: 量子化方式 (int8, int16, float16, float32)
    """
    try:
        import ctranslate2

        logger.info("🔥 [CONVERT_START] NLLB-200 CTranslate2変換開始")
        logger.info(f"   Source Model: {model_name}")
        logger.info(f"   Output Directory: {output_dir}")
        logger.info(f"   Quantization: {quantization}")

        # 出力ディレクトリ作成
        output_path = Path(output_dir)
        output_path.mkdir(parents=True, exist_ok=True)

        logger.info("📥 [DOWNLOAD] HuggingFaceからモデルダウンロード中...")
        logger.info("   初回実行時は2.4GBダウンロードに時間がかかります")

        # CTranslate2変換実行
        converter = ctranslate2.converters.TransformersConverter(model_name)

        logger.info("🔧 [CONVERT] モデル変換中（int8量子化適用）...")
        converter.convert(
            output_dir=str(output_path),
            quantization=quantization,
            force=True
        )

        logger.info("✅ [CONVERT_SUCCESS] 変換完了！")
        logger.info(f"   保存先: {output_path.absolute()}")

        # ファイルサイズ確認
        total_size = sum(
            f.stat().st_size for f in output_path.rglob('*') if f.is_file()
        )
        size_mb = total_size / (1024 * 1024)
        logger.info(f"   変換後サイズ: {size_mb:.1f}MB")
        logger.info(f"   期待メモリ使用量: ~500MB (元: 2.4GB)")

        return True

    except Exception as e:
        logger.error(f"❌ [CONVERT_FAILED] 変換エラー: {e}")
        import traceback
        traceback.print_exc()
        return False


def verify_converted_model(model_dir: str):
    """変換されたモデルの検証"""
    try:
        import ctranslate2

        logger.info("🧪 [VERIFY] 変換モデルの検証開始")

        # モデルロード
        translator = ctranslate2.Translator(model_dir)

        logger.info(f"✅ モデルロード成功")
        logger.info(f"   デバイス: {translator.device}")
        logger.info(f"   計算型: {translator.compute_type}")

        # 簡易翻訳テスト
        logger.info("🧪 [TEST] 簡易翻訳テスト実行")
        test_input = ["▁こんにちは"]  # SentencePieceトークン形式

        results = translator.translate_batch(
            source=[test_input],
            target_prefix=[["eng_Latn"]]
        )

        logger.info(f"✅ 翻訳テスト成功")
        logger.info(f"   出力トークン数: {len(results[0].hypotheses[0])}")

        return True

    except Exception as e:
        logger.error(f"❌ [VERIFY_FAILED] 検証エラー: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(
        description="NLLB-200をCTranslate2形式に変換"
    )
    parser.add_argument(
        "--model",
        default="facebook/nllb-200-distilled-600M",
        help="HuggingFaceモデル名"
    )
    parser.add_argument(
        "--output",
        default="models/nllb-200-ct2",
        help="出力ディレクトリ"
    )
    parser.add_argument(
        "--quantization",
        default="int8",
        choices=["int8", "int16", "float16", "float32"],
        help="量子化方式"
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="変換後に検証テストを実行"
    )

    args = parser.parse_args()

    # 依存関係チェック
    if not check_dependencies():
        sys.exit(1)

    # モデル変換
    success = convert_model(
        model_name=args.model,
        output_dir=args.output,
        quantization=args.quantization
    )

    if not success:
        sys.exit(1)

    # 検証テスト
    if args.verify:
        if not verify_converted_model(args.output):
            sys.exit(1)

    logger.info("🎉 [COMPLETE] すべての処理が完了しました")
    logger.info("   次のステップ: nllb_translation_server.pyをCTranslate2版に更新")


if __name__ == "__main__":
    main()