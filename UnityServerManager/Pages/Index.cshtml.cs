using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using UnityServerManager.Web.Services;
using Microsoft.AspNetCore.Http.Features;

namespace UnityServerManager.Web.Pages;

public class IndexModel : PageModel
{
    private readonly LinuxServerManagerService _serverManager;

    public IndexModel(LinuxServerManagerService serverManager, IOptions<ServerManagementOptions> options)
    {
        _serverManager = serverManager;
        Server = options.Value;
    }

    [BindProperty]
    public IFormFile? BuildFile { get; set; }

    [BindProperty]
    public IFormFileCollection? BuildFiles { get; set; }

    public ServerManagementOptions Server { get; }

    public string? StatusMessage { get; private set; }

    public string? Output { get; private set; }

    public ServerStatus? ServerStatus { get; private set; }

    public string? PortInfo { get; private set; }

    public string? DirectoryListing { get; private set; }

    public string? ProcessList { get; private set; }

    public string? AllPortsInfo { get; private set; }

    public List<UnityInstance> UnityInstances { get; private set; } = new();

    public string? InstanceLogs { get; private set; }

    [BindProperty]
    public string? DirectoryPath { get; set; } = "/root";

    public ConnectionTestResult? TestResult { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            UnityInstances = await _serverManager.GetUnityInstancesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection Error: {ex.Message}";
        }
    }

    [RequestSizeLimit(2_147_483_648)] // 2 GB
    [RequestFormLimits(MultipartBodyLengthLimit = 2_147_483_648)]
    public async Task OnPostUploadAsync()
    {
        if (BuildFiles is null || BuildFiles.Count == 0)
        {
            StatusMessage = "Please select a Unity build folder.";
            await OnGetAsync();
            return;
        }

        // Calculate total size
        var totalSizeMB = BuildFiles.Sum(f => f.Length) / (1024.0 * 1024.0);
        StatusMessage = $"Uploading {BuildFiles.Count} files ({totalSizeMB:F2} MB)... Please wait.";

        try
        {
            var result = await _serverManager.UploadFolderAsync(BuildFiles);
            StatusMessage = result.Message;
            Output = result.Output;

            // Auto-start server after successful upload
            if (result.Success)
            {
                StatusMessage += "\n\n🚀 Starting new Unity instance...";

                try
                {
                    // Find the executable in uploaded files (should be in root or first level)
                    var uploadedExec = BuildFiles
                        .Where(f => f.FileName.EndsWith(".x86_64"))
                        .OrderBy(f => f.FileName.Count(c => c == '/' || c == '\\')) // Get the one closest to root
                        .FirstOrDefault();

                    if (uploadedExec != null)
                    {
                        var execPath = $"{Server.RemoteDeployPath}/{uploadedExec.FileName}".Replace('\\', '/');
                        StatusMessage += $"\n📂 Executable: {execPath}";

                        // Also show the command that will be executed
                        var execDir = Path.GetDirectoryName(execPath)?.Replace('\\', '/');
                        var execFile = Path.GetFileName(execPath);
                        StatusMessage += $"\n🔧 Will run: cd '{execDir}' && nohup ./{execFile} -batchmode -nographics > server.log 2>&1 &";

                        var startResult = await _serverManager.StartInstanceAsync(execPath);
                        StatusMessage += $"\n{startResult.Message}";

                        if (startResult.Success)
                        {
                            await Task.Delay(2000);
                            StatusMessage += "\n\n✅ Upload complete and instance started!";
                        }
                        else
                        {
                            StatusMessage += "\n\n⚠️ Upload complete but failed to start instance automatically.";
                            StatusMessage += $"\n❌ Error: {startResult.Message}";
                        }

                        if (!string.IsNullOrEmpty(startResult.Output))
                        {
                            Output += "\n\n=== Instance Start Output ===\n" + startResult.Output;
                        }
                    }
                    else
                    {
                        StatusMessage += "\n\n⚠️ No .x86_64 executable found in uploaded files.";
                    }
                }
                catch (Exception startEx)
                {
                    StatusMessage += $"\n\n❌ Error starting instance: {startEx.Message}";
                    Output += $"\n\n=== Start Instance Error ===\n{startEx}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
            Output = ex.ToString();
        }

        await OnGetAsync();
    }

    public async Task OnPostListDirectoryAsync()
    {
        DirectoryListing = await _serverManager.ListDirectoryAsync(DirectoryPath ?? "/");
        await OnGetAsync();
    }

    public async Task OnPostViewProcessesAsync()
    {
        ProcessList = await _serverManager.GetRunningProcessesAsync();
        await OnGetAsync();
    }

    public async Task OnPostCheckAllPortsAsync()
    {
        AllPortsInfo = await _serverManager.GetPortUsageAsync();
        await OnGetAsync();
    }

    public async Task OnPostTestConnectionAsync()
    {
        TestResult = await _serverManager.TestConnectionAsync();

        if (TestResult.IsConnected && TestResult.DetectedPort.HasValue && TestResult.DetectedPort != Server.Port)
        {
            StatusMessage = $"⚠️ SSH is running on port {TestResult.DetectedPort}, but your config uses port {Server.Port}. Update appsettings.json!";
        }
        else if (TestResult.IsConnected)
        {
            StatusMessage = TestResult.Message;
            await OnGetAsync();
        }
        else
        {
            StatusMessage = $"❌ {TestResult.Message}";
            if (!string.IsNullOrEmpty(TestResult.ErrorDetails))
            {
                Output = TestResult.ErrorDetails;
            }
        }
    }

    public async Task OnPostStopInstanceAsync(string pid)
    {
        var result = await _serverManager.StopInstanceAsync(pid);
        StatusMessage = result.Message;
        Output = result.Output;
        await OnGetAsync();
    }

    public async Task OnPostRestartInstanceAsync(string pid, string execPath)
    {
        var result = await _serverManager.RestartInstanceAsync(pid, execPath);
        StatusMessage = result.Message;
        Output = result.Output;

        if (result.Success)
            await Task.Delay(2000); // give the new process time to appear in ps aux

        await OnGetAsync();

        if (result.Success && UnityInstances.Any())
        {
            var execFileName = Path.GetFileName(execPath);
            var newInstance = UnityInstances
                .FirstOrDefault(i => i.ExecutablePath == execPath ||
                                     i.ExecutablePath.EndsWith(execFileName));

            var targetPid = newInstance?.ProcessId ?? UnityInstances[0].ProcessId;
            InstanceLogs = await _serverManager.GetInstanceLogsAsync(targetPid);
        }
    }

    public async Task OnPostStartInstanceAsync(string execPath)
    {
        var result = await _serverManager.StartInstanceAsync(execPath);
        StatusMessage = result.Message;
        Output = result.Output;
        await OnGetAsync();
    }

    public async Task OnPostViewInstanceLogsAsync(string pid)
    {
        InstanceLogs = await _serverManager.GetInstanceLogsAsync(pid);
        await OnGetAsync();
    }

    private async Task RunCommand(Func<Task<OperationResult>> operation)
    {
        var result = await operation();
        StatusMessage = result.Message;
        Output = result.Output;
    }
}
