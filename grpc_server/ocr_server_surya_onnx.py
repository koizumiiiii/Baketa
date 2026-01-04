#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa OCR Server - Surya OCR ONNX Engine
Issue #197: Recognition ONNX化によるサイズ削減

PyTorch依存を排除し、ONNX Runtime + Optimumで軽量化した推論エンジン。
- モデルサイズ: 1.1GB → ~180MB
- torch依存: 不要（onnxruntimeのみ）
- GPU対応: DirectML (Windows) / CUDA (Linux)
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
from pathlib import Path

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


class SuryaOnnxEngine:
    """
    Surya OCR ONNX推論エンジン
    Recognition.mdの推奨実装: optimum.onnxruntime.ORTModelForVision2Seq
    """

    VERSION = "0.17.x-onnx"

    # デフォルトモデルパス
    DEFAULT_MODEL_PATH = "Models/surya-onnx/recognition"

    def __init__(self, model_path: Optional[str] = None, use_gpu: bool = True):
        """
        Args:
            model_path: ONNXモデルのパス（Noneの場合はデフォルト）
            use_gpu: GPU使用フラグ（DirectML/CUDA）
        """
        self.model_path = Path(model_path) if model_path else Path(self.DEFAULT_MODEL_PATH)
        self.use_gpu = use_gpu
        self.model = None
        self.processor = None
        self.is_loaded = False

    def _get_execution_provider(self) -> str:
        """
        最適な実行プロバイダーを選択
        Windows: DmlExecutionProvider (DirectML)
        Linux/Other: CUDAExecutionProvider or CPUExecutionProvider
        """
        import onnxruntime as ort

        available = ort.get_available_providers()
        logger.info(f"利用可能なONNX Runtime Providers: {available}")

        if self.use_gpu:
            # Windows: DirectML優先
            if "DmlExecutionProvider" in available:
                return "DmlExecutionProvider"
            # CUDA
            if "CUDAExecutionProvider" in available:
                return "CUDAExecutionProvider"

        return "CPUExecutionProvider"

    def load(self) -> bool:
        """ONNXモデルをロード"""
        try:
            logger.info(f"Surya ONNX モデルをロード中...")
            logger.info(f"モデルパス: {self.model_path}")
            total_start = time.time()

            # モデルパスの検証
            if not self.model_path.exists():
                # 環境変数でオーバーライド
                env_path = os.environ.get("BAKETA_SURYA_MODEL_DIR")
                if env_path:
                    self.model_path = Path(env_path)
                    logger.info(f"環境変数からパス取得: {self.model_path}")

            if not self.model_path.exists():
                logger.error(f"モデルパスが存在しません: {self.model_path}")
                return False

            # 実行プロバイダー決定
            provider = self._get_execution_provider()
            logger.info(f"使用プロバイダー: {provider}")

            # Optimum ORTModelForVision2Seq でロード
            # Recognition.mdの推奨実装
            import_start = time.time()
            from optimum.onnxruntime import ORTModelForVision2Seq
            from transformers import AutoProcessor
            logger.info(f"[Timing] Import完了: {time.time() - import_start:.2f}秒")

            # プロセッサのロード
            proc_start = time.time()
            try:
                self.processor = AutoProcessor.from_pretrained(
                    str(self.model_path),
                    trust_remote_code=True
                )
                logger.info(f"[Timing] Processor: {time.time() - proc_start:.2f}秒")
            except Exception as e:
                logger.warning(f"Processorロード失敗（代替使用）: {e}")
                # フォールバック: HuggingFaceから直接取得
                self.processor = AutoProcessor.from_pretrained(
                    "vikp/surya_rec",
                    trust_remote_code=True
                )

            # ONNXモデルのロード
            model_start = time.time()
            self.model = ORTModelForVision2Seq.from_pretrained(
                str(self.model_path),
                provider=provider,
                trust_remote_code=True
            )
            logger.info(f"[Timing] Model: {time.time() - model_start:.2f}秒")

            elapsed = time.time() - total_start
            logger.info(f"Surya ONNX モデルロード完了 (合計: {elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"必要なライブラリが見つかりません: {e}")
            logger.error("pip install optimum[onnxruntime] transformers を実行してください")
            return False
        except Exception as e:
            logger.exception(f"モデルロードエラー: {e}")
            return False

    # 画像サイズ上限（10MB）- Decompression Bomb攻撃対策
    MAX_IMAGE_SIZE = 10 * 1024 * 1024

    # リサイズ設定
    MAX_IMAGE_DIMENSION = 2048

    def _resize_image_if_needed(self, image: "Image.Image") -> tuple["Image.Image", float]:
        """画像が大きすぎる場合はリサイズ（アスペクト比維持）"""
        width, height = image.size
        max_dim = max(width, height)

        if max_dim <= self.MAX_IMAGE_DIMENSION:
            return image, 1.0

        scale = self.MAX_IMAGE_DIMENSION / max_dim
        new_width = int(width * scale)
        new_height = int(height * scale)

        logger.info(f"画像リサイズ: {width}x{height} → {new_width}x{new_height}")

        from PIL import Image
        resized = image.resize((new_width, new_height), Image.Resampling.LANCZOS)

        return resized, scale

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """
        画像からテキストを認識（ONNX推論）

        Recognition.mdの推奨実装:
        - processor() で前処理
        - model.generate() で推論
        - processor.batch_decode() でデコード
        """
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        # 入力画像サイズ検証
        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズ超過: {len(image_bytes)} bytes")

        try:
            from PIL import Image
            import numpy as np

            # バイトデータから画像を読み込み
            image = Image.open(io.BytesIO(image_bytes))

            # RGB変換
            if image.mode != "RGB":
                image = image.convert("RGB")

            # リサイズ
            image, scale = self._resize_image_if_needed(image)

            logger.info(f"OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            # 前処理（Recognition.mdの実装）
            pixel_values = self.processor(
                images=image,
                return_tensors="pt"
            ).pixel_values

            # ONNX推論（generate）
            generated_ids = self.model.generate(pixel_values)

            # デコード
            texts = self.processor.batch_decode(
                generated_ids,
                skip_special_tokens=True
            )

            elapsed = time.time() - start_time

            # 結果を整形
            # Note: ONNX版は行単位のbboxを返さない可能性がある
            # その場合は全体テキストとして返す
            regions = []
            if texts:
                full_text = texts[0] if texts else ""

                # 画像全体を1つの領域として返す
                width, height = image.size
                inv_scale = 1.0 / scale if scale != 1.0 else 1.0

                region = {
                    "text": full_text,
                    "confidence": 0.9,  # ONNX版は信頼度を返さないことが多い
                    "bbox": {
                        "points": [
                            {"x": 0, "y": 0},
                            {"x": float(width) * inv_scale, "y": 0},
                            {"x": float(width) * inv_scale, "y": float(height) * inv_scale},
                            {"x": 0, "y": float(height) * inv_scale},
                        ],
                        "x": 0,
                        "y": 0,
                        "width": int(width * inv_scale),
                        "height": int(height * inv_scale),
                    },
                    "line_index": 0
                }
                regions.append(region)

            logger.info(f"OCR完了: {len(regions)}領域 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "Surya OCR (ONNX)",
                "engine_version": self.VERSION
            }

        except Exception as e:
            logger.exception(f"OCRエラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "Surya OCR (ONNX)",
                "engine_version": self.VERSION
            }


class OcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer if ocr_pb2_grpc else object):
    """gRPC OCRサービス実装"""

    def __init__(self, engine: SuryaOnnxEngine):
        self.engine = engine

    def Recognize(self, request, context):
        """OCR認識を実行"""
        try:
            languages = list(request.languages) if request.languages else None
            result = self.engine.recognize(request.image_data, languages)

            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = result["success"]
            response.processing_time_ms = result["processing_time_ms"]
            response.engine_name = result["engine_name"]
            response.engine_version = result["engine_version"]
            response.region_count = len(result["regions"])

            for region_data in result["regions"]:
                region = response.regions.add()
                region.text = region_data["text"]
                region.confidence = region_data["confidence"]
                region.line_index = region_data["line_index"]

                bbox = region_data["bbox"]
                region.bounding_box.x = bbox["x"]
                region.bounding_box.y = bbox["y"]
                region.bounding_box.width = bbox["width"]
                region.bounding_box.height = bbox["height"]

                for point in bbox["points"]:
                    p = region.bounding_box.points.add()
                    p.x = point["x"]
                    p.y = point["y"]

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
        response.details["engine"] = "Surya OCR (ONNX)"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "Surya OCR (ONNX)"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


def serve(port: int = 50052, model_path: Optional[str] = None, use_gpu: bool = True):
    """gRPCサーバーを起動"""

    # エンジン初期化
    engine = SuryaOnnxEngine(model_path=model_path, use_gpu=use_gpu)
    if not engine.load():
        logger.error("エンジンロード失敗")
        return

    # gRPCサーバー起動
    MAX_MESSAGE_LENGTH = 50 * 1024 * 1024  # 50MB

    server = grpc.server(
        futures.ThreadPoolExecutor(max_workers=1),
        options=[
            ('grpc.max_receive_message_length', MAX_MESSAGE_LENGTH),
            ('grpc.max_send_message_length', MAX_MESSAGE_LENGTH),
            ('grpc.keepalive_time_ms', 30000),
            ('grpc.keepalive_timeout_ms', 10000),
            ('grpc.keepalive_permit_without_calls', True),
            ('grpc.http2.min_time_between_pings_ms', 10000),
            ('grpc.http2.max_pings_without_data', 0),
            ('grpc.http2.min_ping_interval_without_data_ms', 10000),
        ]
    )

    if ocr_pb2_grpc:
        ocr_pb2_grpc.add_OcrServiceServicer_to_server(
            OcrServiceServicer(engine), server
        )

    # localhost only - Windowsファイアウォールダイアログを回避
    server.add_insecure_port(f'127.0.0.1:{port}')
    server.start()

    logger.info(f"Surya OCR (ONNX) gRPCサーバー起動 (port: {port})")

    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        logger.info("サーバー停止中...")
        server.stop(5)


def main():
    """エントリーポイント"""
    parser = argparse.ArgumentParser(description="Surya OCR ONNX gRPC Server")
    parser.add_argument("--port", type=int, default=50052, help="gRPCポート番号")
    parser.add_argument("--model-path", type=str, default=None,
                        help="ONNXモデルのパス")
    parser.add_argument("--cpu", action="store_true", help="CPUモードで実行")
    parser.add_argument("--test", action="store_true", help="テストモード")

    args = parser.parse_args()

    if args.test:
        engine = SuryaOnnxEngine(model_path=args.model_path, use_gpu=not args.cpu)
        if engine.load():
            logger.info("テスト成功: モデルロード完了")
        else:
            logger.error("テスト失敗: モデルロードエラー")
            sys.exit(1)
    else:
        serve(port=args.port, model_path=args.model_path, use_gpu=not args.cpu)


if __name__ == "__main__":
    main()
