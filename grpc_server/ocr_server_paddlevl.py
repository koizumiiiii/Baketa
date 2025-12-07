#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa OCR Server - PaddleOCR-VL Engine
Issue #189: 次世代OCRエンジン移行

PaddleOCR-VL (Vision-Language Model) を使用したgRPCサーバー実装。
0.9Bパラメータ、109言語対応。
"""

import sys
import os
import io
import time
import logging
import argparse
from concurrent import futures
from datetime import datetime
from typing import Optional, List

import grpc
from google.protobuf.timestamp_pb2 import Timestamp

# UTF-8エンコーディング強制（Windows対応）
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

# gRPC生成コードのインポート
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from protos import ocr_pb2, ocr_pb2_grpc
except ImportError:
    ocr_pb2 = None
    ocr_pb2_grpc = None

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class PaddleVLOcrEngine:
    """PaddleOCR-VL エンジンラッパー (真のVision-Language Model)

    PaddleOCR-VL-0.9B: NaViT + ERNIE-4.5-0.3B ベースの109言語対応VLM
    https://huggingface.co/PaddlePaddle/PaddleOCR-VL
    """

    VERSION = "VL-0.9B"  # PaddleOCR-VL バージョン

    def __init__(self, device: str = "cuda"):
        self.device = device
        self.pipeline = None
        self.is_loaded = False
        # 一時ファイル用ディレクトリ
        self._temp_dir = None

    def load(self) -> bool:
        """モデルをロード (真のPaddleOCR-VL API)"""
        try:
            logger.info(f"PaddleOCR-VL モデルをロード中... (device: {self.device})")
            start_time = time.time()

            # 真のPaddleOCR-VL API
            from paddleocr import PaddleOCRVL

            # 環境変数でGPU設定（PaddleOCR-VLはPaddlePaddle経由でGPU使用）
            import os
            if self.device == "cuda":
                # PaddlePaddleのGPU設定
                os.environ.setdefault("CUDA_VISIBLE_DEVICES", "0")
            else:
                os.environ["CUDA_VISIBLE_DEVICES"] = ""

            # PaddleOCR-VL 初期化
            # 109言語対応、自動言語検出
            self.pipeline = PaddleOCRVL()

            # 一時ファイル用ディレクトリ作成
            import tempfile
            self._temp_dir = tempfile.mkdtemp(prefix="paddleocr_vl_")

            elapsed = time.time() - start_time
            logger.info(f"PaddleOCR-VL モデルロード完了 ({elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"PaddleOCR-VLライブラリが見つかりません: {e}")
            logger.error("pip install paddlepaddle-gpu 'paddleocr[doc-parser]' を実行してください")
            return False
        except Exception as e:
            logger.exception(f"モデルロードエラー: {e}")
            return False

    # 画像サイズ上限（10MB）- Decompression Bomb攻撃対策
    MAX_IMAGE_SIZE = 10 * 1024 * 1024

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """画像からテキストを認識 (真のPaddleOCR-VL API)

        PaddleOCR-VL-0.9Bは109言語を自動検出するため、languagesパラメータは参考情報として扱います。
        """
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        # 入力画像サイズ検証（セキュリティ対策）
        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズが上限を超えています: {len(image_bytes)} bytes (上限: {self.MAX_IMAGE_SIZE} bytes)")

        try:
            from PIL import Image
            import uuid

            # バイトデータから画像を読み込み
            image = Image.open(io.BytesIO(image_bytes))

            # RGB変換
            if image.mode != "RGB":
                image = image.convert("RGB")

            # PaddleOCR-VLは画像パスを受け取るため、一時ファイルに保存
            temp_image_path = os.path.join(self._temp_dir, f"{uuid.uuid4()}.png")
            image.save(temp_image_path, "PNG")

            if languages:
                logger.info(f"言語ヒント: {languages} (PaddleOCR-VLは自動検出)")

            logger.info(f"OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            try:
                # PaddleOCR-VL predict実行
                output = self.pipeline.predict(temp_image_path)

                elapsed = time.time() - start_time

                # 結果を整形
                regions = []
                idx = 0

                # PaddleOCR-VLの結果構造を解析
                for res in output:
                    # 結果オブジェクトからテキストとBBoxを取得
                    # APIによって構造が異なる可能性があるため、複数の方法を試行
                    if hasattr(res, 'text_regions'):
                        # text_regions属性がある場合
                        for text_region in res.text_regions:
                            text = getattr(text_region, 'text', '')
                            confidence = getattr(text_region, 'confidence', 0.0)
                            bbox = getattr(text_region, 'bbox', None)
                            polygon = getattr(text_region, 'polygon', None)

                            if text:
                                region = self._create_region(text, confidence, bbox, polygon, idx)
                                regions.append(region)
                                idx += 1
                    elif hasattr(res, 'rec_polys'):
                        # rec_polys属性がある場合（レガシー形式）
                        for i, (poly, text_info) in enumerate(zip(res.rec_polys, res.rec_texts)):
                            text = text_info if isinstance(text_info, str) else text_info[0]
                            confidence = 0.9  # デフォルト信頼度
                            if isinstance(text_info, tuple) and len(text_info) > 1:
                                confidence = float(text_info[1])

                            region = self._create_region_from_poly(text, confidence, poly, idx)
                            regions.append(region)
                            idx += 1
                    else:
                        # dictとして直接アクセス
                        res_dict = res if isinstance(res, dict) else getattr(res, '__dict__', {})
                        logger.debug(f"結果構造: {type(res)}, keys: {res_dict.keys() if isinstance(res_dict, dict) else 'N/A'}")

            finally:
                # 一時ファイル削除
                try:
                    os.remove(temp_image_path)
                except OSError:
                    pass

            logger.info(f"OCR完了: {len(regions)}領域検出 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "PaddleOCR-VL",
                "engine_version": self.VERSION
            }

        except Exception as e:
            logger.exception(f"OCRエラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "PaddleOCR-VL",
                "engine_version": self.VERSION
            }

    def _create_region(self, text: str, confidence: float, bbox, polygon, idx: int) -> dict:
        """領域情報を作成"""
        points = []
        x, y, width, height = 0, 0, 0, 0

        if polygon:
            points = [{"x": float(p[0]), "y": float(p[1])} for p in polygon]
            x_coords = [p[0] for p in polygon]
            y_coords = [p[1] for p in polygon]
            x, y = int(min(x_coords)), int(min(y_coords))
            width = int(max(x_coords) - x)
            height = int(max(y_coords) - y)
        elif bbox:
            x, y = int(bbox[0]), int(bbox[1])
            width = int(bbox[2] - bbox[0]) if len(bbox) >= 4 else 0
            height = int(bbox[3] - bbox[1]) if len(bbox) >= 4 else 0

        return {
            "text": text,
            "confidence": float(confidence),
            "bbox": {
                "points": points,
                "x": x,
                "y": y,
                "width": width,
                "height": height,
            },
            "line_index": idx
        }

    def _create_region_from_poly(self, text: str, confidence: float, poly, idx: int) -> dict:
        """ポリゴンから領域情報を作成"""
        points = [{"x": float(p[0]), "y": float(p[1])} for p in poly]
        x_coords = [p[0] for p in poly]
        y_coords = [p[1] for p in poly]
        x, y = int(min(x_coords)), int(min(y_coords))
        width = int(max(x_coords) - x)
        height = int(max(y_coords) - y)

        return {
            "text": text,
            "confidence": float(confidence),
            "bbox": {
                "points": points,
                "x": x,
                "y": y,
                "width": width,
                "height": height,
            },
            "line_index": idx
        }


class OcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer if ocr_pb2_grpc else object):
    """gRPC OCRサービス実装"""

    def __init__(self, engine: PaddleVLOcrEngine):
        self.engine = engine

    def Recognize(self, request, context):
        """OCR認識を実行"""
        try:
            # 言語リスト取得
            languages = list(request.languages) if request.languages else None

            # OCR実行
            result = self.engine.recognize(request.image_data, languages)

            # レスポンス構築
            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = result["success"]
            response.processing_time_ms = result["processing_time_ms"]
            response.engine_name = result["engine_name"]
            response.engine_version = result["engine_version"]
            response.region_count = len(result["regions"])

            # テキスト領域を追加
            for region_data in result["regions"]:
                region = response.regions.add()
                region.text = region_data["text"]
                region.confidence = region_data["confidence"]
                region.line_index = region_data["line_index"]

                # BoundingBox
                bbox = region_data["bbox"]
                region.bounding_box.x = bbox["x"]
                region.bounding_box.y = bbox["y"]
                region.bounding_box.width = bbox["width"]
                region.bounding_box.height = bbox["height"]

                for point in bbox["points"]:
                    p = region.bounding_box.points.add()
                    p.x = point["x"]
                    p.y = point["y"]

            # タイムスタンプ
            response.timestamp.FromDatetime(datetime.utcnow())

            return response

        except Exception as e:
            logger.error(f"Recognize error: {e}")
            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = False
            response.error.error_type = ocr_pb2.OCR_ERROR_TYPE_PROCESSING_ERROR
            response.error.message = str(e)
            response.timestamp.FromDatetime(datetime.utcnow())
            return response

    def HealthCheck(self, request, context):
        """ヘルスチェック"""
        response = ocr_pb2.OcrHealthCheckResponse()
        response.is_healthy = self.engine.is_loaded
        response.status = "healthy" if self.engine.is_loaded else "unhealthy"
        response.details["engine"] = "PaddleOCR-VL"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "PaddleOCR-VL"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


def serve(port: int = 50053, device: str = "cuda"):
    """gRPCサーバーを起動"""

    # エンジン初期化
    engine = PaddleVLOcrEngine(device=device)
    if not engine.load():
        logger.error("エンジンロード失敗")
        return

    # gRPCサーバー起動
    # max_workers=1: GPU処理の競合を避けるためシングルワーカーに制限
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=1))

    if ocr_pb2_grpc:
        ocr_pb2_grpc.add_OcrServiceServicer_to_server(
            OcrServiceServicer(engine), server
        )

    server.add_insecure_port(f'[::]:{port}')
    server.start()

    logger.info(f"PaddleOCR-VL gRPCサーバー起動 (port: {port})")

    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        logger.info("サーバー停止中...")
        server.stop(5)


def main():
    """エントリーポイント"""
    parser = argparse.ArgumentParser(description="PaddleOCR-VL gRPC Server")
    parser.add_argument("--port", type=int, default=50053, help="gRPCポート番号")
    parser.add_argument("--device", type=str, default="cuda",
                        choices=["cuda", "cpu"], help="実行デバイス")
    parser.add_argument("--test", action="store_true", help="テストモード")

    args = parser.parse_args()

    if args.test:
        # テストモード: モデルロードのみ
        engine = PaddleVLOcrEngine(device=args.device)
        if engine.load():
            logger.info("テスト成功: モデルロード完了")

            # サンプル画像でテスト（あれば）
            test_image_path = "test_images/chrono_trigger.png"
            if os.path.exists(test_image_path):
                with open(test_image_path, "rb") as f:
                    image_bytes = f.read()
                result = engine.recognize(image_bytes, ["ja", "en"])
                logger.info(f"テスト結果: {result}")
        else:
            logger.error("テスト失敗: モデルロードエラー")
            sys.exit(1)
    else:
        serve(port=args.port, device=args.device)


if __name__ == "__main__":
    main()
