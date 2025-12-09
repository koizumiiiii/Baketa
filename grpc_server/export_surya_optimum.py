#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Surya OCR ONNX Export using Hugging Face Optimum
Issue #189: Phase 4B - Optimumによる高度なONNXエクスポート

目的:
- Surya Recognition Model (SuryaModel) のONNXエクスポート
- KV CacheとDynamic Axesの自動処理
"""
import sys
import os
import time
from pathlib import Path

if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

OUTPUT_DIR = Path(__file__).parent.parent / "Models" / "surya-onnx" / "recognition"


def ensure_output_dir():
    """出力ディレクトリを作成"""
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    print(f"出力ディレクトリ: {OUTPUT_DIR}")


def check_surya_model_info():
    """Suryaモデルの情報を取得"""
    print("=== Surya Model Info ===")

    try:
        from surya.foundation import FoundationPredictor

        print("FoundationPredictorをロード中...")
        fp = FoundationPredictor()
        model = fp.model

        print(f"Model type: {type(model).__name__}")
        print(f"Model class: {model.__class__.__module__}.{model.__class__.__name__}")

        # モデルの設定を確認
        if hasattr(model, 'config'):
            config = model.config
            print(f"Model config type: {type(config).__name__}")
            print(f"Model name or path: {getattr(config, '_name_or_path', 'unknown')}")

        return model

    except Exception as e:
        print(f"ERROR: {e}")
        import traceback
        traceback.print_exc()
        return None


def try_optimum_export():
    """Optimumを使用したONNXエクスポート試行"""
    print("\n=== Optimum ONNX Export ===")

    try:
        from optimum.onnxruntime import ORTModelForVision2Seq
        from optimum.exporters.onnx import main_export

        # Suryaモデルの情報取得
        from surya.foundation import FoundationPredictor
        fp = FoundationPredictor()

        # モデルのHuggingFace Hub IDを確認
        model = fp.model
        config = getattr(model, 'config', None)
        model_id = getattr(config, '_name_or_path', None) if config else None

        print(f"Model ID: {model_id}")

        if model_id:
            print(f"\nOptimumエクスポート試行: {model_id}")
            output_path = OUTPUT_DIR / "optimum"
            output_path.mkdir(parents=True, exist_ok=True)

            # Optimumでエクスポート
            start_time = time.time()

            # 方法1: ORTModelForVision2Seq.from_pretrained
            try:
                ort_model = ORTModelForVision2Seq.from_pretrained(
                    model_id,
                    export=True,
                    trust_remote_code=True
                )
                ort_model.save_pretrained(str(output_path))
                print(f"エクスポート成功: {output_path}")
                print(f"所要時間: {time.time() - start_time:.2f}秒")
                return True
            except Exception as e1:
                print(f"ORTModelForVision2Seq失敗: {e1}")

                # 方法2: main_export
                try:
                    print("\nmain_exportを試行...")
                    main_export(
                        model_id,
                        output=str(output_path),
                        trust_remote_code=True
                    )
                    print(f"エクスポート成功: {output_path}")
                    return True
                except Exception as e2:
                    print(f"main_export失敗: {e2}")

        return False

    except ImportError as e:
        print(f"Optimumインポートエラー: {e}")
        print("pip install optimum[onnxruntime] を実行してください")
        return False
    except Exception as e:
        print(f"エクスポートエラー: {e}")
        import traceback
        traceback.print_exc()
        return False


def try_vision_encoder_export():
    """Vision Encoderのみを分離してエクスポート"""
    print("\n=== Vision Encoder Export ===")

    try:
        import torch
        from surya.foundation import FoundationPredictor

        print("FoundationPredictorをロード...")
        fp = FoundationPredictor()
        model = fp.model

        # Vision Encoder部分を探す
        vision_encoder = None
        for name, module in model.named_children():
            print(f"  - {name}: {type(module).__name__}")
            if 'vision' in name.lower() or 'encoder' in name.lower() or 'image' in name.lower():
                vision_encoder = module
                print(f"    ✅ Vision Encoder候補: {name}")

        if vision_encoder is None:
            # モデル構造を詳しく調査
            print("\nモデルの詳細構造:")
            for name, module in model.named_modules():
                if len(name.split('.')) <= 2:  # 2階層までのみ表示
                    print(f"  {name}: {type(module).__name__}")

            print("\nVision Encoder特定失敗 - モデル構造が複雑")
            return False

        # Vision Encoderのエクスポート
        print(f"\nVision Encoderをエクスポート中...")
        vision_encoder.eval()

        # 入力形状を推測（一般的なVision Encoderの入力）
        dummy_input = torch.randn(1, 3, 224, 224)

        output_path = OUTPUT_DIR / "vision_encoder.onnx"

        torch.onnx.export(
            vision_encoder,
            dummy_input,
            str(output_path),
            input_names=['pixel_values'],
            output_names=['image_embeddings'],
            dynamic_axes={
                'pixel_values': {0: 'batch', 2: 'height', 3: 'width'},
                'image_embeddings': {0: 'batch'}
            },
            opset_version=14
        )

        file_size = output_path.stat().st_size / (1024 * 1024)
        print(f"Vision Encoder エクスポート成功: {file_size:.1f} MB")
        return True

    except Exception as e:
        print(f"Vision Encoder エクスポート失敗: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    print("=" * 60)
    print("Surya OCR ONNX Export using Hugging Face Optimum")
    print("=" * 60)

    ensure_output_dir()

    # Step 1: モデル情報確認
    model = check_surya_model_info()

    if model is None:
        print("\nモデルロード失敗")
        return 1

    # Step 2: Optimumエクスポート試行
    if try_optimum_export():
        print("\n✅ Optimumエクスポート成功!")
        return 0

    # Step 3: Vision Encoder分離エクスポート
    print("\nOptimum失敗 - Vision Encoder分離を試行...")
    if try_vision_encoder_export():
        print("\n✅ Vision Encoderエクスポート成功!")
        return 0

    print("\n❌ すべてのエクスポート方法が失敗しました")
    print("代替案: PyTorchモデルを事前配布することを推奨")
    return 1


if __name__ == "__main__":
    sys.exit(main())
