from google.protobuf import timestamp_pb2 as _timestamp_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from typing import ClassVar as _ClassVar, Iterable as _Iterable, Mapping as _Mapping, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class TranslationErrorType(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    TRANSLATION_ERROR_TYPE_UNSPECIFIED: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_UNKNOWN: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_NETWORK: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_AUTHENTICATION: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_QUOTA_EXCEEDED: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_ENGINE: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_UNSUPPORTED_LANGUAGE: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_INVALID_INPUT: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_TIMEOUT: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_EXCEPTION: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_NETWORK_ERROR: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_PROCESSING_ERROR: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_SERVICE_UNAVAILABLE: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_MODEL_LOAD_ERROR: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_INVALID_REQUEST: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_INVALID_RESPONSE: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_OPERATION_CANCELED: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_UNEXPECTED_ERROR: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_AUTH_ERROR: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_RATE_LIMIT_EXCEEDED: _ClassVar[TranslationErrorType]
    TRANSLATION_ERROR_TYPE_MODEL_ERROR: _ClassVar[TranslationErrorType]
TRANSLATION_ERROR_TYPE_UNSPECIFIED: TranslationErrorType
TRANSLATION_ERROR_TYPE_UNKNOWN: TranslationErrorType
TRANSLATION_ERROR_TYPE_NETWORK: TranslationErrorType
TRANSLATION_ERROR_TYPE_AUTHENTICATION: TranslationErrorType
TRANSLATION_ERROR_TYPE_QUOTA_EXCEEDED: TranslationErrorType
TRANSLATION_ERROR_TYPE_ENGINE: TranslationErrorType
TRANSLATION_ERROR_TYPE_UNSUPPORTED_LANGUAGE: TranslationErrorType
TRANSLATION_ERROR_TYPE_INVALID_INPUT: TranslationErrorType
TRANSLATION_ERROR_TYPE_TIMEOUT: TranslationErrorType
TRANSLATION_ERROR_TYPE_EXCEPTION: TranslationErrorType
TRANSLATION_ERROR_TYPE_NETWORK_ERROR: TranslationErrorType
TRANSLATION_ERROR_TYPE_PROCESSING_ERROR: TranslationErrorType
TRANSLATION_ERROR_TYPE_SERVICE_UNAVAILABLE: TranslationErrorType
TRANSLATION_ERROR_TYPE_MODEL_LOAD_ERROR: TranslationErrorType
TRANSLATION_ERROR_TYPE_INVALID_REQUEST: TranslationErrorType
TRANSLATION_ERROR_TYPE_INVALID_RESPONSE: TranslationErrorType
TRANSLATION_ERROR_TYPE_OPERATION_CANCELED: TranslationErrorType
TRANSLATION_ERROR_TYPE_UNEXPECTED_ERROR: TranslationErrorType
TRANSLATION_ERROR_TYPE_AUTH_ERROR: TranslationErrorType
TRANSLATION_ERROR_TYPE_RATE_LIMIT_EXCEEDED: TranslationErrorType
TRANSLATION_ERROR_TYPE_MODEL_ERROR: TranslationErrorType

class Rectangle(_message.Message):
    __slots__ = ("x", "y", "width", "height")
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    WIDTH_FIELD_NUMBER: _ClassVar[int]
    HEIGHT_FIELD_NUMBER: _ClassVar[int]
    x: int
    y: int
    width: int
    height: int
    def __init__(self, x: _Optional[int] = ..., y: _Optional[int] = ..., width: _Optional[int] = ..., height: _Optional[int] = ...) -> None: ...

class Language(_message.Message):
    __slots__ = ("code", "display_name", "name", "native_name", "region_code", "is_auto_detect", "is_right_to_left")
    CODE_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    NAME_FIELD_NUMBER: _ClassVar[int]
    NATIVE_NAME_FIELD_NUMBER: _ClassVar[int]
    REGION_CODE_FIELD_NUMBER: _ClassVar[int]
    IS_AUTO_DETECT_FIELD_NUMBER: _ClassVar[int]
    IS_RIGHT_TO_LEFT_FIELD_NUMBER: _ClassVar[int]
    code: str
    display_name: str
    name: str
    native_name: str
    region_code: str
    is_auto_detect: bool
    is_right_to_left: bool
    def __init__(self, code: _Optional[str] = ..., display_name: _Optional[str] = ..., name: _Optional[str] = ..., native_name: _Optional[str] = ..., region_code: _Optional[str] = ..., is_auto_detect: bool = ..., is_right_to_left: bool = ...) -> None: ...

class TranslationContext(_message.Message):
    __slots__ = ("game_profile_id", "scene_id", "dialogue_id", "screen_region", "tags", "priority", "additional_context", "genre", "domain")
    class AdditionalContextEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    GAME_PROFILE_ID_FIELD_NUMBER: _ClassVar[int]
    SCENE_ID_FIELD_NUMBER: _ClassVar[int]
    DIALOGUE_ID_FIELD_NUMBER: _ClassVar[int]
    SCREEN_REGION_FIELD_NUMBER: _ClassVar[int]
    TAGS_FIELD_NUMBER: _ClassVar[int]
    PRIORITY_FIELD_NUMBER: _ClassVar[int]
    ADDITIONAL_CONTEXT_FIELD_NUMBER: _ClassVar[int]
    GENRE_FIELD_NUMBER: _ClassVar[int]
    DOMAIN_FIELD_NUMBER: _ClassVar[int]
    game_profile_id: str
    scene_id: str
    dialogue_id: str
    screen_region: Rectangle
    tags: _containers.RepeatedScalarFieldContainer[str]
    priority: int
    additional_context: _containers.ScalarMap[str, str]
    genre: str
    domain: str
    def __init__(self, game_profile_id: _Optional[str] = ..., scene_id: _Optional[str] = ..., dialogue_id: _Optional[str] = ..., screen_region: _Optional[_Union[Rectangle, _Mapping]] = ..., tags: _Optional[_Iterable[str]] = ..., priority: _Optional[int] = ..., additional_context: _Optional[_Mapping[str, str]] = ..., genre: _Optional[str] = ..., domain: _Optional[str] = ...) -> None: ...

class TranslationError(_message.Message):
    __slots__ = ("error_code", "message", "details", "is_retryable", "error_type", "exception_type", "exception_message", "exception_stack_trace")
    ERROR_CODE_FIELD_NUMBER: _ClassVar[int]
    MESSAGE_FIELD_NUMBER: _ClassVar[int]
    DETAILS_FIELD_NUMBER: _ClassVar[int]
    IS_RETRYABLE_FIELD_NUMBER: _ClassVar[int]
    ERROR_TYPE_FIELD_NUMBER: _ClassVar[int]
    EXCEPTION_TYPE_FIELD_NUMBER: _ClassVar[int]
    EXCEPTION_MESSAGE_FIELD_NUMBER: _ClassVar[int]
    EXCEPTION_STACK_TRACE_FIELD_NUMBER: _ClassVar[int]
    error_code: str
    message: str
    details: str
    is_retryable: bool
    error_type: TranslationErrorType
    exception_type: str
    exception_message: str
    exception_stack_trace: str
    def __init__(self, error_code: _Optional[str] = ..., message: _Optional[str] = ..., details: _Optional[str] = ..., is_retryable: bool = ..., error_type: _Optional[_Union[TranslationErrorType, str]] = ..., exception_type: _Optional[str] = ..., exception_message: _Optional[str] = ..., exception_stack_trace: _Optional[str] = ...) -> None: ...

class TranslateRequest(_message.Message):
    __slots__ = ("source_text", "source_language", "target_language", "request_id", "context", "preferred_engine", "options", "timestamp")
    class OptionsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    SOURCE_TEXT_FIELD_NUMBER: _ClassVar[int]
    SOURCE_LANGUAGE_FIELD_NUMBER: _ClassVar[int]
    TARGET_LANGUAGE_FIELD_NUMBER: _ClassVar[int]
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    CONTEXT_FIELD_NUMBER: _ClassVar[int]
    PREFERRED_ENGINE_FIELD_NUMBER: _ClassVar[int]
    OPTIONS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    source_text: str
    source_language: Language
    target_language: Language
    request_id: str
    context: TranslationContext
    preferred_engine: str
    options: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, source_text: _Optional[str] = ..., source_language: _Optional[_Union[Language, _Mapping]] = ..., target_language: _Optional[_Union[Language, _Mapping]] = ..., request_id: _Optional[str] = ..., context: _Optional[_Union[TranslationContext, _Mapping]] = ..., preferred_engine: _Optional[str] = ..., options: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class TranslateResponse(_message.Message):
    __slots__ = ("request_id", "source_text", "translated_text", "source_language", "target_language", "engine_name", "confidence_score", "processing_time_ms", "is_success", "error", "metadata", "timestamp")
    class MetadataEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: str
        def __init__(self, key: _Optional[str] = ..., value: _Optional[str] = ...) -> None: ...
    REQUEST_ID_FIELD_NUMBER: _ClassVar[int]
    SOURCE_TEXT_FIELD_NUMBER: _ClassVar[int]
    TRANSLATED_TEXT_FIELD_NUMBER: _ClassVar[int]
    SOURCE_LANGUAGE_FIELD_NUMBER: _ClassVar[int]
    TARGET_LANGUAGE_FIELD_NUMBER: _ClassVar[int]
    ENGINE_NAME_FIELD_NUMBER: _ClassVar[int]
    CONFIDENCE_SCORE_FIELD_NUMBER: _ClassVar[int]
    PROCESSING_TIME_MS_FIELD_NUMBER: _ClassVar[int]
    IS_SUCCESS_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    METADATA_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    request_id: str
    source_text: str
    translated_text: str
    source_language: Language
    target_language: Language
    engine_name: str
    confidence_score: float
    processing_time_ms: int
    is_success: bool
    error: TranslationError
    metadata: _containers.ScalarMap[str, str]
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, request_id: _Optional[str] = ..., source_text: _Optional[str] = ..., translated_text: _Optional[str] = ..., source_language: _Optional[_Union[Language, _Mapping]] = ..., target_language: _Optional[_Union[Language, _Mapping]] = ..., engine_name: _Optional[str] = ..., confidence_score: _Optional[float] = ..., processing_time_ms: _Optional[int] = ..., is_success: bool = ..., error: _Optional[_Union[TranslationError, _Mapping]] = ..., metadata: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class BatchTranslateRequest(_message.Message):
    __slots__ = ("requests", "batch_id", "timestamp")
    REQUESTS_FIELD_NUMBER: _ClassVar[int]
    BATCH_ID_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    requests: _containers.RepeatedCompositeFieldContainer[TranslateRequest]
    batch_id: str
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, requests: _Optional[_Iterable[_Union[TranslateRequest, _Mapping]]] = ..., batch_id: _Optional[str] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class BatchTranslateResponse(_message.Message):
    __slots__ = ("responses", "batch_id", "success_count", "failure_count", "total_processing_time_ms", "timestamp")
    RESPONSES_FIELD_NUMBER: _ClassVar[int]
    BATCH_ID_FIELD_NUMBER: _ClassVar[int]
    SUCCESS_COUNT_FIELD_NUMBER: _ClassVar[int]
    FAILURE_COUNT_FIELD_NUMBER: _ClassVar[int]
    TOTAL_PROCESSING_TIME_MS_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    responses: _containers.RepeatedCompositeFieldContainer[TranslateResponse]
    batch_id: str
    success_count: int
    failure_count: int
    total_processing_time_ms: int
    timestamp: _timestamp_pb2.Timestamp
    def __init__(self, responses: _Optional[_Iterable[_Union[TranslateResponse, _Mapping]]] = ..., batch_id: _Optional[str] = ..., success_count: _Optional[int] = ..., failure_count: _Optional[int] = ..., total_processing_time_ms: _Optional[int] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class HealthCheckRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class HealthCheckResponse(_message.Message):
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
    def __init__(self, is_healthy: bool = ..., status: _Optional[str] = ..., details: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class IsReadyRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class IsReadyResponse(_message.Message):
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
    def __init__(self, is_ready: bool = ..., status: _Optional[str] = ..., details: _Optional[_Mapping[str, str]] = ..., timestamp: _Optional[_Union[_timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...
