#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
OCR Engine Comparison Test
Issue #189: 次世代OCRエンジン移行

Surya OCR vs PaddleOCR-VL の比較テスト
"""

import sys
import os
import time
import json
import argparse
from pathlib import Path
from typing import Dict, List, Any

# UTF-8エンコーディング強制
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')


def test_surya_ocr(image_path: str, languages: List[str] = None) -> Dict[str, Any]:
    """Surya OCRでテスト"""
    try:
        from ocr_server_surya import SuryaOcrEngine

        engine = SuryaOcrEngine(device="cuda")
        if not engine.load():
            return {"error": "モデルロード失敗", "success": False}

        with open(image_path, "rb") as f:
            image_bytes = f.read()

        result = engine.recognize(image_bytes, languages or ["ja", "en"])
        return result

    except ImportError as e:
        return {"error": f"Surya OCRインポートエラー: {e}", "success": False}
    except Exception as e:
        return {"error": str(e), "success": False}


def test_paddlevl_ocr(image_path: str, languages: List[str] = None) -> Dict[str, Any]:
    """PaddleOCR-VLでテスト"""
    try:
        from ocr_server_paddlevl import PaddleVLOcrEngine

        engine = PaddleVLOcrEngine(device="cuda")
        if not engine.load():
            return {"error": "モデルロード失敗", "success": False}

        with open(image_path, "rb") as f:
            image_bytes = f.read()

        result = engine.recognize(image_bytes, languages or ["ja", "en"])
        return result

    except ImportError as e:
        return {"error": f"PaddleOCRインポートエラー: {e}", "success": False}
    except Exception as e:
        return {"error": str(e), "success": False}


def compare_results(image_name: str, surya_result: Dict, paddle_result: Dict) -> Dict:
    """結果を比較"""
    comparison = {
        "image": image_name,
        "surya": {
            "success": surya_result.get("success", False),
            "region_count": len(surya_result.get("regions", [])),
            "processing_time_ms": surya_result.get("processing_time_ms", 0),
            "texts": [r["text"] for r in surya_result.get("regions", [])],
            "avg_confidence": 0.0
        },
        "paddle": {
            "success": paddle_result.get("success", False),
            "region_count": len(paddle_result.get("regions", [])),
            "processing_time_ms": paddle_result.get("processing_time_ms", 0),
            "texts": [r["text"] for r in paddle_result.get("regions", [])],
            "avg_confidence": 0.0
        }
    }

    # 平均信頼度を計算
    if surya_result.get("regions"):
        confidences = [r["confidence"] for r in surya_result["regions"]]
        comparison["surya"]["avg_confidence"] = sum(confidences) / len(confidences)

    if paddle_result.get("regions"):
        confidences = [r["confidence"] for r in paddle_result["regions"]]
        comparison["paddle"]["avg_confidence"] = sum(confidences) / len(confidences)

    return comparison


def print_comparison(comparison: Dict):
    """比較結果を表示"""
    print(f"\n{'='*60}")
    print(f"画像: {comparison['image']}")
    print(f"{'='*60}")

    print(f"\n【Surya OCR】")
    if comparison["surya"]["success"]:
        print(f"  検出数: {comparison['surya']['region_count']}")
        print(f"  処理時間: {comparison['surya']['processing_time_ms']}ms")
        print(f"  平均信頼度: {comparison['surya']['avg_confidence']:.2f}")
        print(f"  検出テキスト:")
        for text in comparison["surya"]["texts"][:5]:  # 最初の5件
            print(f"    - {text}")
        if len(comparison["surya"]["texts"]) > 5:
            print(f"    ... 他{len(comparison['surya']['texts'])-5}件")
    else:
        print(f"  エラー: {comparison['surya'].get('error', '不明')}")

    print(f"\n【PaddleOCR-VL】")
    if comparison["paddle"]["success"]:
        print(f"  検出数: {comparison['paddle']['region_count']}")
        print(f"  処理時間: {comparison['paddle']['processing_time_ms']}ms")
        print(f"  平均信頼度: {comparison['paddle']['avg_confidence']:.2f}")
        print(f"  検出テキスト:")
        for text in comparison["paddle"]["texts"][:5]:
            print(f"    - {text}")
        if len(comparison["paddle"]["texts"]) > 5:
            print(f"    ... 他{len(comparison['paddle']['texts'])-5}件")
    else:
        print(f"  エラー: {comparison['paddle'].get('error', '不明')}")


def main():
    parser = argparse.ArgumentParser(description="OCR Engine Comparison Test")
    parser.add_argument("--images", type=str, default="test_images",
                        help="テスト画像のディレクトリ")
    parser.add_argument("--engine", type=str, default="both",
                        choices=["surya", "paddle", "both"],
                        help="テストするエンジン")
    parser.add_argument("--output", type=str, default="ocr_comparison_results.json",
                        help="結果出力ファイル")
    parser.add_argument("--single", type=str, default=None,
                        help="単一画像をテスト")

    args = parser.parse_args()

    # テスト画像一覧
    if args.single:
        image_files = [args.single]
    else:
        images_dir = Path(args.images)
        if not images_dir.exists():
            print(f"エラー: 画像ディレクトリが見つかりません: {images_dir}")
            return

        image_files = list(images_dir.glob("*.png")) + list(images_dir.glob("*.jpg"))

    if not image_files:
        print("テスト画像が見つかりません")
        return

    print(f"テスト画像数: {len(image_files)}")

    results = []

    for image_path in image_files:
        image_path = str(image_path)
        image_name = os.path.basename(image_path)

        print(f"\n処理中: {image_name}")

        surya_result = {"success": False, "regions": []}
        paddle_result = {"success": False, "regions": []}

        if args.engine in ["surya", "both"]:
            print("  Surya OCR...")
            surya_result = test_surya_ocr(image_path)

        if args.engine in ["paddle", "both"]:
            print("  PaddleOCR-VL...")
            paddle_result = test_paddlevl_ocr(image_path)

        comparison = compare_results(image_name, surya_result, paddle_result)
        results.append(comparison)
        print_comparison(comparison)

    # 結果をJSONに保存
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)

    print(f"\n結果を保存: {args.output}")

    # サマリー
    print(f"\n{'='*60}")
    print("サマリー")
    print(f"{'='*60}")

    if args.engine in ["surya", "both"]:
        surya_success = sum(1 for r in results if r["surya"]["success"])
        surya_total_time = sum(r["surya"]["processing_time_ms"] for r in results)
        print(f"Surya OCR: {surya_success}/{len(results)}成功, 合計{surya_total_time}ms")

    if args.engine in ["paddle", "both"]:
        paddle_success = sum(1 for r in results if r["paddle"]["success"])
        paddle_total_time = sum(r["paddle"]["processing_time_ms"] for r in results)
        print(f"PaddleOCR-VL: {paddle_success}/{len(results)}成功, 合計{paddle_total_time}ms")


if __name__ == "__main__":
    main()
