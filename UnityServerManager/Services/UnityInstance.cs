namespace UnityServerManager.Web.Services;

public sealed record UnityInstance(
    string ProcessId,
    string User,
    double CpuPercent,
    double MemoryPercent,
    string MemoryMB,
    string StartTime,
    string Uptime,
    string Command,
    string ExecutablePath,
    int? Port,
    bool IsRunning
);
