namespace UnityServerManager.Web.Services;

public sealed record ServerStatus(
    bool IsRunning,
    bool IsActive,
    string Status,
    string SubState,
    string MainPid,
    string Memory,
    string Uptime
);
