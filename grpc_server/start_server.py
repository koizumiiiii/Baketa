# 🔥 [ULTRATHINK_FIX] Phase 6: 全warnings完全抑制（import文より前に実行）
# 問題: torch.cuda/transformersの警告がstderrでハング → C#プロセス起動失敗
# 解決策: import前にwarnings抑制 + 環境変数PYTHONWARNINGS設定
import warnings
warnings.filterwarnings('ignore')

# 🔥 [ULTRATHINK_FIX] Phase 6: Python組み込みwarnings環境変数設定
import os
os.environ["PYTHONWARNINGS"] = "ignore"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"  # TensorFlow警告抑制
os.environ["TOKENIZERS_PARALLELISM"] = "false"  # HuggingFace並列化無効

# 🔥 [Issue #198] CUDA DLLロードエラー対策（Phase 3: miniconda PATH除外）
# 問題: minicondaのCUDA DLL (cublas64_12.dll) がPATHにあるとtorchインポート時にOSErrorが発生
# CUDA_VISIBLE_DEVICES="" だけでは不十分 - torchはPATH上のDLLをロードしようとする
# 解決策: minicondaのパスをPATH環境変数から除外してからインポート
import sys

def _sanitize_path_for_cuda():
    """minicondaのCUDA DLLパスをPATHから除外"""
    path = os.environ.get("PATH", "")
    path_parts = path.split(os.pathsep)

    # miniconda/anacondaのパスを除外（CUDA DLL競合を防止）
    sanitized_parts = []
    excluded_parts = []
    for part in path_parts:
        part_lower = part.lower()
        if "miniconda" in part_lower or "anaconda" in part_lower:
            excluded_parts.append(part)
        else:
            sanitized_parts.append(part)

    if excluded_parts:
        print(f"[INFO] CUDA DLL競合防止: PATH から以下を除外しました:", file=sys.stderr)
        for p in excluded_parts:
            print(f"  - {p}", file=sys.stderr)
        os.environ["PATH"] = os.pathsep.join(sanitized_parts)
        return True
    return False

def _check_cuda_availability():
    """CTranslate2のCUDAサポートを直接チェック（DLLロードより信頼性が高い）"""
    try:
        import ctranslate2
        cuda_types = ctranslate2.get_supported_compute_types("cuda")
        if cuda_types:
            print(f"[INFO] CTranslate2 CUDA対応: {cuda_types}", file=sys.stderr)
            return True
    except Exception as e:
        print(f"[INFO] CTranslate2 CUDAチェックエラー: {e}", file=sys.stderr)
    return False

# Phase 3: minicondaパスを除外してCUDA DLL競合を防止
_path_sanitized = _sanitize_path_for_cuda()

# ctranslate2を先にインポート
import ctranslate2

# CTranslate2のCUDAサポートを直接チェック
_cuda_available = _check_cuda_availability()
if not _cuda_available:
    # CUDAが利用不可の場合のみ環境変数で無効化
    print("[INFO] CUDA利用不可: CPUモードで起動します", file=sys.stderr)
    os.environ["CUDA_VISIBLE_DEVICES"] = ""
    os.environ["CUDA_HOME"] = ""

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
from pathlib import Path

# 🔥 [HOTFIX alpha-0.1.12] Python Embeddable版でのprotosモジュールインポート修正
# Root cause: python310._pthの"."はvendor/python/を基準とするため、
#             WorkingDirectory=grpc_serverでもカレントディレクトリがsys.pathに含まれない
# Fix: 明示的にカレントディレクトリをsys.pathに追加
if os.getcwd() not in sys.path:
    sys.path.insert(0, os.getcwd())

import grpc
from grpc import aio
# 🔥 [PACKAGE_SIZE_FIX] torch削除（約200MB削減）
# GPU検出はctranslate2.get_device_count()を使用
import ctranslate2

# Proto生成ファイル（コンパイル後にインポート可能になります）
from protos import translation_pb2_grpc

from translation_server import TranslationServicer
from engines.ctranslate2_engine import CTranslate2Engine
from resource_monitor import ResourceMonitor  # Phase 1.1: GPU/VRAM監視

# 🔧 [UNICODE_FIX] Windows環境でのUnicodeEncodeError対策
# sys.stdout/stderrをUTF-8に再設定（cp932 → utf-8）
# これにより、ログ出力時のUnicodeエンコーディングエラーを防止
# 🔥 [PYINSTALLER_FIX] コンソールなしモードではstdout/stderrがNoneの場合がある
try:
    if sys.stdout is not None and hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    if sys.stderr is not None and hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8')
except (AttributeError, OSError):
    # PyInstaller --noconsole モードではstdout/stderrが無効
    pass

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


async def serve(host: str, port: int, model_path_arg: str | None = None):
    """gRPCサーバー起動

    Args:
        host: バインドホスト（例: "localhost", "0.0.0.0"）
        port: ポート番号（例: 50051）
        model_path_arg: [Issue #185] C#から指定されたモデルパス（Noneの場合はデフォルト）

    Note:
        NLLB-200-distilled-1.3B (CTranslate2 int8) を使用。
        600Mからの精度向上により、日本語翻訳品質が大幅に改善。
    """
    logger.info("=" * 80)
    logger.info("Baketa gRPC Translation Server Starting...")
    logger.info("=" * 80)

    # CTranslate2エンジン（NLLB-200-distilled-1.3B）を使用
    logger.info("Initializing CTranslate2 translation engine...")

    # [Issue #185] モデルパスの決定
    # 優先順位: 1. コマンドライン引数 2. デフォルト（%APPDATA%\Baketa\Models\nllb-200-1.3B-ct2）
    if model_path_arg:
        model_path = Path(model_path_arg)
        logger.info(f"[Issue #185] Using model path from command line: {model_path}")
    else:
        # 🔥 [ALPHA_0.1.2] HuggingFace Hub統合: モデル保存先を%APPDATA%\Baketa\Modelsに変更
        # Gemini推奨: インストール先への書き込みは管理者権限が必要なため、APPDATAを使用
        # 🚀 [Translation Quality] NLLB-200-distilled-1.3B に移行（600Mから精度向上）
        appdata = os.environ.get('APPDATA', os.path.expanduser('~'))
        model_path = Path(appdata) / "Baketa" / "Models" / "nllb-200-1.3B-ct2"
        logger.info(f"[Issue #185] Using default model path: {model_path}")

    # モデル存在チェック・自動ダウンロード
    if not model_path.exists() or not (model_path / "model.bin").exists():
        logger.info("=" * 80)
        logger.info("Model not found. Downloading from HuggingFace Hub...")
        logger.info("Repository: OpenNMT/nllb-200-distilled-1.3B-ct2-int8")
        logger.info("Size: ~1.3GB | This may take several minutes...")
        logger.info("=" * 80)
        model_path.mkdir(parents=True, exist_ok=True)

        try:
            # 🔥 [GEMINI_RECOMMENDATION] 非同期ダウンロード（イベントループブロッキング回避）
            from huggingface_hub import snapshot_download
            from functools import partial

            loop = asyncio.get_running_loop()
            download_func = partial(
                snapshot_download,
                repo_id="OpenNMT/nllb-200-distilled-1.3B-ct2-int8",
                local_dir=str(model_path),
                revision="main"  # TODO: 特定のコミットハッシュに固定（セキュリティ向上）
            )
            await loop.run_in_executor(None, download_func)
            logger.info("=" * 80)
            logger.info("Model download completed successfully.")
            logger.info("=" * 80)
        except Exception as e:
            logger.error("=" * 80)
            logger.error(f"Model download failed: {e}")
            logger.error("Please check:")
            logger.error("  1. Internet connection is available")
            logger.error("  2. Disk space is sufficient (~1.3GB)")
            logger.error("  3. HuggingFace Hub is accessible")
            logger.error("=" * 80)
            raise RuntimeError(f"Failed to download model from HuggingFace Hub: {e}")
    else:
        logger.info("Model found locally. Skipping download.")

    # 🔥 [PACKAGE_SIZE_FIX] GPU検出をpynvml（既存依存）で実行（torch不要）
    # 🔥 [HOTFIX Issue #170] ctranslate2.get_device_count()は存在しないため、pynvmlを使用
    # Root cause: ctranslate2 4.6.0にget_device_count()が存在しない
    # Fix: pynvml（既にrequirements.txtに含まれている）でCUDA検出
    # 🔥 [GEMINI_REVIEW] finally句でpynvml.nvmlShutdown()を確実に実行
    is_cuda_available = False
    nvml_initialized = False
    try:
        import pynvml
        pynvml.nvmlInit()
        nvml_initialized = True
        device_count = pynvml.nvmlDeviceGetCount()
        is_cuda_available = device_count > 0
        if is_cuda_available:
            # GPU情報をログ出力
            handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            gpu_name = pynvml.nvmlDeviceGetName(handle)
            # bytes型の場合はUTF-8デコード（環境によってbytesを返す場合がある）
            if isinstance(gpu_name, bytes):
                gpu_name = gpu_name.decode('utf-8')
            logger.info(f"🎮 GPU detection successful: {device_count} CUDA device(s) found")
            logger.info(f"   Primary GPU: {gpu_name}")
    except Exception as e:
        # Fallback: GPU検出失敗時はCPUモードで動作
        logger.warning(f"⚠️ GPU detection failed ({e.__class__.__name__}), falling back to CPU mode")
        is_cuda_available = False
    finally:
        # 🔥 [GEMINI_REVIEW] nvmlInit()が成功した場合のみShutdown()を実行
        if nvml_initialized:
            pynvml.nvmlShutdown()

    engine = CTranslate2Engine(
        model_path=str(model_path),  # %APPDATA%\Baketa\Models\nllb-200-1.3B-ct2
        device="cuda" if is_cuda_available else "cpu",
        compute_type="int8"
    )

    logger.info("Loading NLLB model (this may take a few minutes)...")
    await engine.load_model()
    logger.info("NLLB model loaded successfully")

    # gRPCサーバー作成
    logger.info("Creating gRPC server...")
    # 🔧 [GEMINI_DEEP_FIX] 完全なKeepAlive設定 - 中間機器による切断を防止
    # 根本原因: 中間機器（ファイアウォール、NAT等）が112秒アイドルでTCP切断
    # 解決策: サーバー側から30秒間隔でPINGを送信し、クライアントからのPINGも許可
    server = aio.server(options=[
        # ★★★ サーバー側のKeepAlive設定 ★★★
        ('grpc.keepalive_time_ms', 30000),  # 30秒ごとにクライアントの生存確認PINGを送信
        ('grpc.keepalive_timeout_ms', 10000),  # PINGの応答待ち時間
        ('grpc.keepalive_permit_without_calls', True),  # RPCがなくてもPINGを許可（アイドル中も接続維持）
        ('grpc.http2.min_time_between_pings_ms', 10000),  # PINGの最低間隔
        ('grpc.http2.max_pings_without_data', 0),  # データなしでのPING回数制限を無効化
        ('grpc.http2.min_ping_interval_without_data_ms', 10000),  # クライアントからのPING最低間隔
    ])

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
    # 🔥 [PYINSTALLER_FIX] コンソールなしモードではstderrがNoneまたは無効
    try:
        if sys.stderr is not None:
            sys.stderr.write("[SERVER_START]\n")
            sys.stderr.flush()  # 即座に出力
            logger.info("[SERVER_START] signal sent to stderr for C# detection")
        else:
            logger.info("[SERVER_START] signal skipped (stderr unavailable)")
    except (OSError, AttributeError):
        # PyInstaller --noconsole モードでは書き込みがエラーになる
        logger.info("[SERVER_START] signal skipped (stderr write failed)")
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
        default="127.0.0.1",
        help="gRPC server host (default: 127.0.0.1 for localhost only, use 0.0.0.0 for all interfaces)"
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Enable debug logging"
    )
    # [Issue #185] モデルパスをC#から指定可能に
    parser.add_argument(
        "--model-path",
        type=str,
        default=None,
        help="Path to CTranslate2 model directory (default: %APPDATA%/Baketa/Models/nllb-200-1.3B-ct2)"
    )

    args = parser.parse_args()

    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)
        logger.setLevel(logging.DEBUG)

    # 設定情報表示
    logger.info("Server configuration:")
    logger.info(f"  Host: {args.host}")
    logger.info(f"  Port: {args.port}")
    logger.info(f"  Model: NLLB-200-distilled-1.3B (CTranslate2 int8)")
    logger.info(f"  Model path: {args.model_path or '(default)'}")
    logger.info(f"  Debug mode: {args.debug}")

    # asyncioイベントループでサーバー起動
    try:
        asyncio.run(
            serve(
                host=args.host,
                port=args.port,
                model_path_arg=args.model_path  # [Issue #185] モデルパス引数を渡す
            )
        )
    except KeyboardInterrupt:
        logger.info("Server interrupted by user")
    except Exception as e:
        logger.exception(f"Server error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
