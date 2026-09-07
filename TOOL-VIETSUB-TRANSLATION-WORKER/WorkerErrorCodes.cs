namespace VideoMaker.Vietsub.Translation.Worker;

internal static class VietsubTranslationErrorCodes
{
    public const string RuntimeInvalid = "TRANSLATION_RUNTIME_INVALID";
    public const string ResourceConfirmationRequired = "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED";
    public const string RuntimeUnsupportedPlatform = "TRANSLATION_RUNTIME_UNSUPPORTED_PLATFORM";
    public const string RuntimeOutOfMemory = "TRANSLATION_RUNTIME_OUT_OF_MEMORY";
    public const string BackendLoadFailed = "TRANSLATION_BACKEND_LOAD_FAILED";
    public const string ProcessFailed = "TRANSLATION_PROCESS_FAILED";
    public const string WorkerProtocolInvalid = "TRANSLATION_WORKER_PROTOCOL_INVALID";
    public const string ModelNotReady = "TRANSLATION_MODEL_NOT_READY";
    public const string ResultInvalid = "TRANSLATION_RESULT_INVALID";
    public const string JobConflict = "TRANSLATION_JOB_CONFLICT";
}
