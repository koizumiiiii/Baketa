import datetime

from google.protobuf import timestamp_pb2 as _timestamp_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class OcrErrorType(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    OCR_ERROR_TYPE_UNSPECIFIED: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_UNKNOWN: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_MODEL_LOAD_ERROR: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_INVALID_IMAGE: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_PROCESSING_ERROR: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_TIMEOUT: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_OUT_OF_MEMORY: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_UNSUPPORTED_LANGUAGE: _ClassVar[OcrErrorType]
    OCR_ERROR_TYPE_SERVICE_UNAVAILABLE: _ClassVar[OcrErrorType]

class OcrEngineType(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    OCR_ENGINE_TYPE_UNSPECIFIED: _ClassVar[OcrEngineType]
    OCR_ENGINE_TYPE_SURYA: _ClassVar[OcrEngineType]
    OCR_ENGINE_TYPE_PADDLE_VL: _ClassVar[OcrEngineType]
    OCR_ENGINE_TYPE_PP_OCRV5: _ClassVar[OcrEngineType]
OCR_ERROR_TYPE_UNSPECIFIED: OcrErrorType
OCR_ERROR_TYPE_UNKNOWN: OcrErrorType
OCR_ERROR_TYPE_MODEL_LOAD_ERROR: OcrErrorType
OCR_ERROR_TYPE_INVALID_IMAGE: OcrErrorType
OCR_ERROR_TYPE_PROCESSING_ERROR: OcrErrorType
OCR_ERROR_TYPE_TIMEOUT: OcrErrorType
OCR_ERROR_TYPE_OUT_OF_MEMORY: OcrErrorType
OCR_ERROR_TYPE_UNSUPPORTED_LANGUAGE: OcrErrorType
OCR_ERROR_TYPE_SERVICE_UNAVAILABLE: OcrErrorType
OCR_ENGINE_TYPE_UNSPECIFIED: OcrEngineType
OCR_ENGINE_TYPE_SURYA: OcrEngineType
OCR_ENGINE_TYPE_PADDLE_VL: OcrEngineType
OCR_ENGINE_TYPE_PP_OCRV5: OcrEngineType

class BoundingBox(_message.Message):
    __slots__ = ("points", "x", "y", "width", "height")
    POINTS_FIELD_NUMBER: _ClassVar[int]
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    WIDTH_FIELD_NUMBER: _ClassVar[int]
    HEIGHT_FIELD_NUMBER: _ClassVar[int]
    points: _containers.RepeatedCompositeFieldContainer[Point]
    x: int
    y: int
    width: int
    height: int
    def __init__(self, points: _Optional[_Iterable[_Union[Point, _Mapping]]] = ..., x: _Optional[int] = ..., y: _Optional[int] = ..., width: _Optional[int] = ..., height: _Optional[int] = ...) -> None: ...

class Point(_message.Message):
    __slots__ = ("x", "y")
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    x: float
    y: float
    def __init__(self, x: _Optional[float] = ..., y: _Optional[float] = ...) -> None: ...

class TextRegion(_message.Message):
    __slots__ = ("text", "bounding_box", "confidence", "language", "line_index")
    TEXT_FIELD_NUMBER: _ClassVar[int]
    BOUNDING_BOX_FIELD_NUMBER: _ClassVar[int]
    CONFIDENCE_FIELD_NUMBER: _ClassVar[int]
    LANGUAGE_FIELD_NUMBER: _ClassVar[int]
    LINE_INDEX_FIELD_NUMBER: _ClassVar[int]
    text: str
    bounding_box: BoundingBox
    confidence: float
    language: str
    line_index: int
    def __init__(self, text: _Optional[str] = ..., bounding_box: _Optional[_Union[BoundingBox, _Mapping]] = ..., confidence: _Optional[float] = ..., language: _Optional[str] = ..., line_index: _Optional[int] = ...) -> None: ...

class DetectedRegion(_message.Message):
    __slots__ = ("bounding_box", "confidence", "region_index")
    BOUNDING_BOX_FIELD_NUMBER: _ClassVar[int]
    CONFIDENCE_FIELD_NUMBER: _ClassVar[int]
    REGION_INDEX_FIELD_NUMBER: _ClassVar[int]
    bounding_box: BoundingBox
    confidence: float
    region_index: int
    def __init__(self, bounding_box: _Optional[_Union[BoundingBox, _Mapping]] = ..., confidence: _Optional[float] = ..., region_index: _Optional[int] = ...) -> None: ...

class OcrError(_message.Message):
    __slots__ = ("error_type", "message", "details", "is_retryable")
    ERROR_TYPE_FIELD_NUMBER: _ClassVar[int]
    MESSAGE_FIELD_NUMBER: _ClassVar[int]
    DETAILS_FIELD_NUMBER: _ClassVar[int]
    IS_RETRYABLE_FIELD_NUMBER: _ClassVar[int]
    error_type: OcrErrorType
    message: str
    details: str
    is_retryable: bool
    def __init__(self, error_type: _Optional[_Union[OcrErrorType, str]] = ..., message: _Optional[str] = ..., details: _Optional[str] = ..., is_retryable: bool = ...) -> None: ...

class OcrRequest(_message.Message):
    __slots__ = ("request_id", "image_data", "image_format", "languages", "engine", "options", "timestamp")
    class OptionsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    IMAGE_DATA_FIELD_NUMBER: _ClassVar[int]
    IMAGE_FORMAT_FIELD_NUMBER: _ClassVar[int]
    LANGUAGES_FIELD_NUMBER: _ClassVar[int]
    ENGINE_FIELD_NUMBER: _ClassVar[int]
    OPTIONS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    request_id: str
    image_data: bytes
    image_format: str
    languages: _containers.RepeatedScalarFieldContainer[str]
    engine: OcrEngineType
    options: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, request_id: _Optional[str] = ..., image_data: _Optional[bytes] = ..., image_format: _Optional[str] = ..., languages: _Optional[_Iterable[str]] = ..., engine: _Optional[_Union[OcrEngineType, str]] = ..., options: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class OcrResponse(_message.Message):
    __slots__ = ("request_id", "is_success", "regions", "region_count", "processing_time_ms", "error", "engine_name", "engine_version", "metadata", "timestamp")
    class MetadataEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    IS_SUCCESS_FIELD_NUMBER: _ClassVar[int]
    REGIONS_FIELD_NUMBER: _ClassVar[int]
    REGION_COUNT_FIELD_NUMBER: _ClassVar[int]
    PROCESSING_TIME_MS_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    ENGINE_NAME_FIELD_NUMBER: _ClassVar[int]
    ENGINE_VERSION_FIELD_NUMBER: _ClassVar[int]
    METADATA_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    request_id: str
    is_success: bool
    regions: _containers.RepeatedCompositeFieldContainer[TextRegion]
    region_count: int
    processing_time_ms: int
    error: OcrError
    engine_name: str
    engine_version: str
    metadata: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, request_id: _Optional[str] = ..., is_success: bool = ..., regions: _Optional[_Iterable[_Union[TextRegion, _Mapping]]] = ..., region_count: _Optional[int] = ..., processing_time_ms: _Optional[int] = ..., error: _Optional[_Union[OcrError, _Mapping]] = ..., engine_name: _Optional[str] = ..., engine_version: _Optional[str] = ..., metadata: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class OcrHealthCheckRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class OcrHealthCheckResponse(_message.Message):
    __slots__ = ("is_healthy", "status", "details", "timestamp")
    class DetailsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    IS_HEALTHY_FIELD_NUMBER: _ClassVar[int]
    STATUS_FIELD_NUMBER: _ClassVar[int]
    DETAILS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    is_healthy: bool
    status: str
    details: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, is_healthy: bool = ..., status: _Optional[str] = ..., details: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class OcrIsReadyRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class OcrIsReadyResponse(_message.Message):
    __slots__ = ("is_ready", "status", "details", "timestamp")
    class DetailsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    IS_READY_FIELD_NUMBER: _ClassVar[int]
    STATUS_FIELD_NUMBER: _ClassVar[int]
    DETAILS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    is_ready: bool
    status: str
    details: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, is_ready: bool = ..., status: _Optional[str] = ..., details: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class DetectRequest(_message.Message):
    __slots__ = ("request_id", "image_data", "image_format", "engine", "options", "timestamp")
    class OptionsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    IMAGE_DATA_FIELD_NUMBER: _ClassVar[int]
    IMAGE_FORMAT_FIELD_NUMBER: _ClassVar[int]
    ENGINE_FIELD_NUMBER: _ClassVar[int]
    OPTIONS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    request_id: str
    image_data: bytes
    image_format: str
    engine: OcrEngineType
    options: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, request_id: _Optional[str] = ..., image_data: _Optional[bytes] = ..., image_format: _Optional[str] = ..., engine: _Optional[_Union[OcrEngineType, str]] = ..., options: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class DetectResponse(_message.Message):
    __slots__ = ("request_id", "is_success", "regions", "region_count", "processing_time_ms", "error", "engine_name", "timestamp")
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    IS_SUCCESS_FIELD_NUMBER: _ClassVar[int]
    REGIONS_FIELD_NUMBER: _ClassVar[int]
    REGION_COUNT_FIELD_NUMBER: _ClassVar[int]
    PROCESSING_TIME_MS_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    ENGINE_NAME_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    request_id: str
    is_success: bool
    regions: _containers.RepeatedCompositeFieldContainer[DetectedRegion]
    region_count: int
    processing_time_ms: int
    error: OcrError
    engine_name: str
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, request_id: _Optional[str] = ..., is_success: bool = ..., regions: _Optional[_Iterable[_Union[DetectedRegion, _Mapping]]] = ..., region_count: _Optional[int] = ..., processing_time_ms: _Optional[int] = ..., error: _Optional[_Union[OcrError, _Mapping]] = ..., engine_name: _Optional[str] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class RecognizeBatchRequest(_message.Message):
    __slots__ = ("batch_id", "requests", "timestamp")
    BATCH_ID_FIELD_NUMBER: _ClassVar[int]
    REQUESTS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    batch_id: str
    requests: _containers.RepeatedCompositeFieldContainer[OcrRequest]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, batch_id: _Optional[str] = ..., requests: _Optional[_Iterable[_Union[OcrRequest, _Mapping]]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class RecognizeBatchResponse(_message.Message):
    __slots__ = ("batch_id", "is_success", "responses", "success_count", "total_count", "total_processing_time_ms", "error", "timestamp")
    BATCH_ID_FIELD_NUMBER: _ClassVar[int]
    IS_SUCCESS_FIELD_NUMBER: _ClassVar[int]
    RESPONSES_FIELD_NUMBER: _ClassVar[int]
    SUCCESS_COUNT_FIELD_NUMBER: _ClassVar[int]
    TOTAL_COUNT_FIELD_NUMBER: _ClassVar[int]
    TOTAL_PROCESSING_TIME_MS_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    batch_id: str
    is_success: bool
    responses: _containers.RepeatedCompositeFieldContainer[OcrResponse]
    success_count: int
    total_count: int
    total_processing_time_ms: int
    error: OcrError
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, batch_id: _Optional[str] = ..., is_success: bool = ..., responses: _Optional[_Iterable[_Union[OcrResponse, _Mapping]]] = ..., success_count: _Optional[int] = ..., total_count: _Optional[int] = ..., total_processing_time_ms: _Optional[int] = ..., error: _Optional[_Union[OcrError, _Mapping]] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...
