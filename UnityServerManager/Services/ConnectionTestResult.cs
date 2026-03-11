namespace UnityServerManager.Web.Services;

public sealed record ConnectionTestResult(
    bool IsConnected,
    string Message,
    int? DetectedPort = null,
    string? ErrorDetails = null
);
