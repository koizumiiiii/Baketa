"""
gRPC Translation Server Startup Script
Phase 2.2: サーバー起動エントリーポイント

Usage:
    python start_server.py [--port PORT] [--host HOST] [--heavy-model]

Examples:
    python start_server.py
    python start_server.py --port 50051 --host localhost
    python start_server.py --heavy-model  # Use 1.3B model instead of 600M
"""

import asyncio
import argparse
import logging
import signal
import sys
import faulthandler  # 🔥 [PHASE1.3] Windows固有クラッシュ検出用
import traceback  # 🔥 [PHASE1.3] 例外スタックトレース出力用

import grpc
from grpc import aio
import torch

# Proto生成ファイル（コンパイル後にインポート可能になります）
from protos import translation_pb2_grpc

from translation_server import TranslationServicer
from engines.nllb_engine import NllbEngine
from engines.ctranslate2_engine import CTranslate2Engine
from resource_monitor import ResourceMonitor  # Phase 1.1: GPU/VRAM監視

# 🔧 [UNICODE_FIX] Windows環境でのUnicodeEncodeError対策
# sys.stdout/stderrをUTF-8に再設定（cp932 → utf-8）
# これにより、ログ出力時のUnicodeエンコーディングエラーを防止
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('translation_server.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)


class GracefulShutdown:
    """グレースフルシャットダウンハンドラー"""

    def __init__(self):
        self.shutdown_event = asyncio.Event()

    def __enter__(self):
        loop = asyncio.get_event_loop()

        def signal_handler(signum, frame):
            logger.info(f"Received signal {signum}, shutting down gracefully...")
            asyncio.create_task(self._async_shutdown())

        signal.signal(signal.SIGINT, signal_handler)
        signal.signal(signal.SIGTERM, signal_handler)

        return self

    def __exit__(self, exc_type, exc_value, traceback):
        pass

    async def _async_shutdown(self):
        self.shutdown_event.set()

    async def wait_for_shutdown(self):
        await self.shutdown_event.wait()


async def serve(host: str, port: int, use_heavy_model: bool = False, use_ctranslate2: bool = False):
    """gRPCサーバー起動

    Args:
        host: バインドホスト（例: "localhost", "0.0.0.0"）
        port: ポート番号（例: 50051）
        use_heavy_model: Trueで1.3Bモデル、Falseで600Mモデル使用
        use_ctranslate2: TrueでCTranslate2エンジン、Falseでtransformersエンジン使用
    """
    logger.info("=" * 80)
    logger.info("Baketa gRPC Translation Server Starting...")
    logger.info("=" * 80)

    # エンジン選択
    if use_ctranslate2:
        logger.info("Initializing CTranslate2 translation engine...")
        engine = CTranslate2Engine(
            model_path="../models/nllb-200-ct2",  # grpc_serverディレクトリからの相対パス
            device="cuda" if torch.cuda.is_available() else "cpu",
            compute_type="int8"
        )
    else:
        logger.info("Initializing NLLB translation engine...")
        engine = NllbEngine(use_heavy_model=use_heavy_model)

    logger.info("Loading NLLB model (this may take a few minutes)...")
    await engine.load_model()
    logger.info("NLLB model loaded successfully")

    # gRPCサーバー作成
    logger.info("Creating gRPC server...")
    server = aio.server()

    # TranslationServiceを登録
    servicer = TranslationServicer(engine)
    translation_pb2_grpc.add_TranslationServiceServicer_to_server(servicer, server)

    # リスニングアドレス設定
    listen_addr = f'{host}:{port}'
    server.add_insecure_port(listen_addr)

    logger.info(f"Starting gRPC server on {listen_addr}...")
    await server.start()

    logger.info("=" * 80)
    logger.info(f"gRPC Translation Server is running on {listen_addr}")
    logger.info(f"   Engine: {engine.__class__.__name__}")
    logger.info(f"   Model: {engine.model_name}")
    logger.info(f"   Device: {engine.device}")

    # 🔥 [PHASE8_FIX] PythonServerManager.WaitForServerReadyAsync()互換性のため[SERVER_START]出力
    # C#側がStdErrを監視しているため、sys.stderrに直接出力
    sys.stderr.write("[SERVER_START]\n")
    sys.stderr.flush()  # 即座に出力
    logger.info("[SERVER_START] signal sent to stderr for C# detection")
    logger.info(f"   Supported languages: {', '.join(engine.get_supported_languages())}")
    logger.info("=" * 80)
    logger.info("Press Ctrl+C to stop the server")

    # 🔥 [PHASE1.1] GPU/VRAMメモリ監視開始（Gemini最重要推奨）
    resource_monitor = ResourceMonitor(enable_gpu_monitoring=True)
    await resource_monitor.start_monitoring(interval_seconds=300)  # 5分ごと
    logger.info("[PHASE1.1] Resource monitoring started (CPU RAM + GPU VRAM + Handles)")

    # グレースフルシャットダウン待機
    with GracefulShutdown() as shutdown_handler:
        try:
            await shutdown_handler.wait_for_shutdown()
        except KeyboardInterrupt:
            logger.info("Received KeyboardInterrupt, shutting down...")

    # サーバー停止
    logger.info("Stopping gRPC server...")
    await server.stop(grace=5.0)  # 5秒のグレースピリオド
    logger.info("gRPC server stopped")

    # 🔥 [PHASE1.1] リソース監視クリーンアップ
    await resource_monitor.stop_monitoring()
    resource_monitor.cleanup()
    logger.info("[PHASE1.1] Resource monitoring cleanup completed")


def global_exception_handler(exc_type, exc_value, exc_traceback):
    """🔥 [PHASE1.3] グローバル例外ハンドラー - すべての未処理例外をログ出力"""
    if issubclass(exc_type, KeyboardInterrupt):
        # KeyboardInterruptは通常処理
        sys.__excepthook__(exc_type, exc_value, exc_traceback)
        return

    logger.critical("=" * 80)
    logger.critical("🚨 [PHASE1.3] UNCAUGHT EXCEPTION - CRITICAL ERROR")
    logger.critical("=" * 80)
    logger.critical(f"Exception Type: {exc_type.__name__}")
    logger.critical(f"Exception Value: {exc_value}")
    logger.critical("Traceback:")
    logger.critical("".join(traceback.format_exception(exc_type, exc_value, exc_traceback)))
    logger.critical("=" * 80)


def main():
    """コマンドライン引数パース & サーバー起動"""
    # 🔥 [PHASE1.3] faulthandler有効化 - SIGSEGV等のOS-levelクラッシュを検出
    faulthandler.enable(file=sys.stderr, all_threads=True)
    logger.info("[PHASE1.3] faulthandler enabled - OS-level crash detection active")

    # 🔥 [PHASE1.3] グローバル例外ハンドラー設置
    sys.excepthook = global_exception_handler
    logger.info("[PHASE1.3] Global exception handler installed")

    parser = argparse.ArgumentParser(
        description="Baketa gRPC Translation Server"
    )
    parser.add_argument(
        "--port",
        type=int,
        default=50051,
        help="gRPC server port (default: 50051)"
    )
    parser.add_argument(
        "--host",
        type=str,
        default="0.0.0.0",
        help="gRPC server host (default: 0.0.0.0 for all interfaces)"
    )
    parser.add_argument(
        "--heavy-model",
        action="store_true",
        help="Use 1.3B model instead of 600M model (requires more memory)"
    )
    parser.add_argument(
        "--use-ctranslate2",
        action="store_true",
        help="Use CTranslate2 engine for 80%% memory reduction (2.4GB -> 500MB)"
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Enable debug logging"
    )

    args = parser.parse_args()

    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)
        logger.setLevel(logging.DEBUG)

    # 設定情報表示
    logger.info("Server configuration:")
    logger.info(f"  Host: {args.host}")
    logger.info(f"  Port: {args.port}")
    logger.info(f"  Heavy model: {args.heavy_model}")
    logger.info(f"  Use CTranslate2: {args.use_ctranslate2}")
    logger.info(f"  Debug mode: {args.debug}")

    # asyncioイベントループでサーバー起動
    try:
        asyncio.run(
            serve(
                host=args.host,
                port=args.port,
                use_heavy_model=args.heavy_model,
                use_ctranslate2=args.use_ctranslate2
            )
        )
    except KeyboardInterrupt:
        logger.info("Server interrupted by user")
    except Exception as e:
        logger.exception(f"Server error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
