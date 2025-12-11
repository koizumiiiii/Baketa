#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Issue #197: Surya Recognition Model PyTorch Dynamic Quantization
ONNX変換が不可能なため、PyTorchの動的量子化でサイズ削減を行う。

目的:
- PyTorchモデル (1.1GB) → 量子化モデル (~300-400MB) に圧縮
- アーキテクチャ（MQA）を変更せずにサイズ削減
- ダウンロードサイズ問題の緩和
"""

import sys
import os
import time
from pathlib import Path

# UTF-8出力設定
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

# 出力ディレクトリ
OUTPUT_DIR = Path("Models/surya-quantized")


def check_dependencies():
    """依存関係のチェック"""
    print("=== 依存関係チェック ===")

    missing = []

    try:
        import torch
        print(f"✅ torch: {torch.__version__}")
        if torch.cuda.is_available():
            print(f"   CUDA: {torch.cuda.get_device_name(0)}")
    except ImportError:
        print("❌ torch not installed")
        missing.append("torch")

    try:
        import surya
        print(f"✅ surya: installed")
    except ImportError:
        print("❌ surya not installed")
        missing.append("surya-ocr")

    if missing:
        print()
        print("⚠️ 不足パッケージをインストールしてください:")
        print(f"   pip install {' '.join(missing)}")
        return False

    print()
    return True


def quantize_model():
    """モデルを動的量子化"""
    print("=== PyTorch Dynamic Quantization ===")

    import torch
    from surya.recognition import RecognitionPredictor, FoundationPredictor

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    start_time = time.time()

    # Step 1: オリジナルモデルをロード
    print("[1/4] オリジナルモデルをロード中...")
    print("      (数分かかる可能性があります...)")

    # Suryaのデフォルトモデルをロード（新API: Surya 0.17+）
    # FoundationPredictorにモデルが含まれる
    foundation = FoundationPredictor(device="cpu")
    model = foundation.model
    processor = foundation.processor

    # オリジナルサイズの計算
    original_size = sum(p.numel() * p.element_size() for p in model.parameters()) / (1024 * 1024)
    print(f"      オリジナルモデルサイズ: {original_size:.1f} MB (メモリ上)")

    # Step 2: モデルをCPUに移動（量子化はCPUで行う）
    print("[2/4] モデルをCPUに移動...")
    model = model.cpu()
    model.eval()

    # Step 3: 動的量子化を実行
    print("[3/4] 動的量子化を実行中...")
    print("      Linear層をFloat32 → Int8 に変換...")

    quantized_model = torch.quantization.quantize_dynamic(
        model,
        {torch.nn.Linear},  # 対象レイヤー
        dtype=torch.qint8
    )

    # Step 4: 量子化モデルを保存
    print("[4/4] 量子化モデルを保存中...")

    output_path = OUTPUT_DIR / "surya_rec_quantized.pth"
    torch.save(quantized_model.state_dict(), output_path)

    # プロセッサ保存はスキップ（Surya 0.17+ では processor が関数になっている）
    # processor_path = OUTPUT_DIR / "processor"
    # processor.save_pretrained(str(processor_path))

    elapsed = time.time() - start_time

    # 結果表示
    print()
    print("=== 量子化完了 ===")
    print(f"処理時間: {elapsed:.1f}秒")

    # ファイルサイズ確認
    if output_path.exists():
        file_size = output_path.stat().st_size / (1024 * 1024)
        print(f"出力ファイル: {output_path}")
        print(f"ファイルサイズ: {file_size:.1f} MB")

        reduction = (1 - file_size / 1100) * 100  # 元サイズを1.1GBと仮定
        print(f"サイズ削減率: {reduction:.1f}%")

        if file_size < 500:
            print("✅ 目標サイズ達成！")
        else:
            print("⚠️ 期待より大きいですが、改善は見られます")

    return True


def test_quantized_model():
    """量子化モデルのテスト"""
    print()
    print("=== 量子化モデルのテスト ===")

    import torch

    model_path = OUTPUT_DIR / "surya_rec_quantized.pth"

    if not model_path.exists():
        print("❌ 量子化モデルが見つかりません")
        return False

    try:
        # まずはstate_dictのロードを試みる
        print("[1/2] State dictをロード中...")
        state_dict = torch.load(model_path, map_location="cpu", weights_only=False)
        print(f"      State dict keys: {len(state_dict)} 個")

        print("[2/2] テスト完了")
        print("✅ 量子化モデルのロードに成功")

        return True

    except Exception as e:
        print(f"❌ テスト失敗: {e}")
        import traceback
        traceback.print_exc()
        return False


def show_output_files():
    """生成されたファイルの一覧を表示"""
    print()
    print("=== 生成ファイル ===")

    if not OUTPUT_DIR.exists():
        print("❌ 出力ディレクトリが存在しません")
        return

    total_size = 0
    for f in sorted(OUTPUT_DIR.rglob("*")):
        if f.is_file():
            size_mb = f.stat().st_size / (1024 * 1024)
            total_size += size_mb
            rel_path = f.relative_to(OUTPUT_DIR)
            print(f"  {rel_path}: {size_mb:.2f} MB")

    print()
    print(f"合計: {total_size:.2f} MB")


def main():
    print("=" * 60)
    print("Surya Recognition Model PyTorch Dynamic Quantization")
    print("Issue #197: ダウンロードサイズ削減（ONNX代替案）")
    print("=" * 60)
    print()

    # 依存関係チェック
    if not check_dependencies():
        sys.exit(1)

    # 量子化実行
    success = quantize_model()

    if not success:
        print()
        print("❌ 量子化に失敗しました")
        sys.exit(1)

    # テスト
    test_quantized_model()

    # 結果表示
    show_output_files()

    print()
    print("=" * 60)
    print("次のステップ:")
    print("1. 生成された量子化モデルをGitHub Releasesにアップロード")
    print("2. appsettings.jsonのComponentDownload設定を更新")
    print("3. ocr_server_surya.pyを量子化モデル読み込みに修正")
    print("=" * 60)


if __name__ == "__main__":
    main()
