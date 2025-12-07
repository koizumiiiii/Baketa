"""
Generated Protocol Buffer files

Compile proto files:
  python -m grpc_tools.protoc -I./protos --python_out=./protos --grpc_python_out=./protos ./protos/translation.proto
  python -m grpc_tools.protoc -I./protos --python_out=./protos --grpc_python_out=./protos ./protos/ocr.proto

Generated files:
- translation_pb2.py, translation_pb2_grpc.py (Translation Service)
- ocr_pb2.py, ocr_pb2_grpc.py (OCR Service)
"""

# Translation Service
try:
    from . import translation_pb2
    from . import translation_pb2_grpc
except ImportError:
    translation_pb2 = None
    translation_pb2_grpc = None

# OCR Service
try:
    from . import ocr_pb2
    from . import ocr_pb2_grpc
except ImportError:
    ocr_pb2 = None
    ocr_pb2_grpc = None
