namespace UnityServerManager.Web.Services;

public sealed record OperationResult(bool Success, string Message, string Output = "");
