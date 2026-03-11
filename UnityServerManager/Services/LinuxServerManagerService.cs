using Microsoft.Extensions.Options;
using Renci.SshNet;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace UnityServerManager.Web.Services;

public class LinuxServerManagerService
{
    private readonly ServerManagementOptions _options;

    public LinuxServerManagerService(IOptions<ServerManagementOptions> options)
    {
        _options = options.Value;
    }

    public Task<OperationResult> StartAsync() => ExecuteServiceCommandAsync("start");
    public Task<OperationResult> StopAsync() => ExecuteServiceCommandAsync("stop");
    public Task<OperationResult> RestartAsync() => ExecuteServiceCommandAsync("restart");

    public async Task<ServerStatus> GetStatusAsync()
    {
        try
        {
            var commandText = $"systemctl show {_options.ServiceName} --no-page";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            var properties = output.Split('\n')
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

            var activeState = properties.GetValueOrDefault("ActiveState", "unknown");
            var subState = properties.GetValueOrDefault("SubState", "unknown");
            var mainPid = properties.GetValueOrDefault("MainPID", "0");
            var memoryUsage = properties.GetValueOrDefault("MemoryCurrent", "0");
            var activeEnterTimestamp = properties.GetValueOrDefault("ActiveEnterTimestamp", "");

            var uptime = "N/A";
            if (!string.IsNullOrEmpty(activeEnterTimestamp) && activeState == "active")
            {
                if (DateTimeOffset.TryParse(activeEnterTimestamp, out var startTime))
                {
                    var elapsed = DateTimeOffset.UtcNow - startTime.ToUniversalTime();
                    uptime = elapsed.ToString(@"dd\.hh\:mm\:ss");
                }
            }

            var memoryMB = long.TryParse(memoryUsage, out var mem) ? $"{mem / (1024 * 1024)} MB" : "N/A";

            return new ServerStatus(
                IsRunning: activeState == "active",
                IsActive: activeState == "active",
                Status: activeState,
                SubState: subState,
                MainPid: mainPid,
                Memory: memoryMB,
                Uptime: uptime
            );
        }
        catch
        {
            return new ServerStatus(false, false, "unknown", "unknown", "0", "N/A", "N/A");
        }
    }

    public async Task<string> GetListeningPortsAsync()
    {
        try
        {
            var status = await GetStatusAsync();
            if (!status.IsRunning || status.MainPid == "0")
            {
                return "Service is not running";
            }

            var commandText = $"netstat -tulpn | grep {status.MainPid} || ss -tulpn | grep {status.MainPid}";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return string.IsNullOrWhiteSpace(command.Result) ? "No listening ports found" : command.Result;
            });

            return output;
        }
        catch (Exception ex)
        {
            return $"Error checking ports: {ex.Message}";
        }
    }

    public async Task<string> ListDirectoryAsync(string path)
    {
        try
        {
            var commandText = $"ls -lha {path} 2>&1";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            return string.IsNullOrWhiteSpace(output) ? "Directory is empty or doesn't exist" : output;
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    public async Task<string> GetRunningProcessesAsync()
    {
        try
        {
            // Show only Unity server processes with -batchmode -nographics flags
            var commandText = @"
echo '=== Unity Server Instances (with -batchmode -nographics) ==='
echo ''
ps aux | grep -E '\-batchmode.*\-nographics|\-nographics.*\-batchmode' | grep -v grep | awk '{printf ""%-10s %-10s %-6s %-6s %-8s %s\n"", $1, $2, $3, $4, $9, substr($0, index($0,$11))}'
if [ $? -ne 0 ] || [ -z ""$(ps aux | grep -E '\-batchmode.*\-nographics|\-nographics.*\-batchmode' | grep -v grep)"" ]; then
    echo 'No Unity server instances running'
fi
echo ''
echo 'Legend: USER | PID | %CPU | %MEM | START_TIME | COMMAND'
";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            return string.IsNullOrWhiteSpace(output) ? "No Unity server processes found" : output;
        }
        catch (Exception ex)
        {
            return $"Error getting processes: {ex.Message}";
        }
    }

    public async Task<List<UnityInstance>> GetUnityInstancesAsync()
    {
        try
        {
            // Simplified command that's more reliable
            var commandText = @"ps aux | grep -E '\-batchmode.*\-nographics|\-nographics.*\-batchmode' | grep -v grep";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            var instances = new List<UnityInstance>();

            if (string.IsNullOrWhiteSpace(output))
                return instances;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    // Parse ps aux output
                    // Format: USER PID %CPU %MEM VSZ RSS TTY STAT START TIME COMMAND
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 11)
                        continue;

                    var user = parts[0];
                    var pid = parts[1];
                    var cpuStr = parts[2];
                    var memStr = parts[3];
                    var start = parts[8];
                    var time = parts[9];

                    // Get command (everything from index 10 onwards)
                    var command = string.Join(" ", parts.Skip(10));

                    double.TryParse(cpuStr, out var cpu);
                    double.TryParse(memStr, out var mem);

                    // Get memory in MB
                    var memMB = "N/A";
                    try
                    {
                        var memCmd = $"ps -p {pid} -o rss= 2>/dev/null";
                        using var sshClient = new SshClient(CreateConnectionInfo());
                        sshClient.Connect();
                        var memResult = sshClient.RunCommand(memCmd);
                        sshClient.Disconnect();

                        if (int.TryParse(memResult.Result.Trim(), out var memKB))
                        {
                            memMB = $"{memKB / 1024} MB";
                        }
                    }
                    catch { }

                    // Get uptime
                    var uptime = time;

                    // Try to find listening port
                    int? port = null;
                    try
                    {
                        var portCmd = $"netstat -tlnp 2>/dev/null | grep {pid} | awk '{{print $4}}' | grep -oE '[0-9]+$' | head -1 || ss -tlnp 2>/dev/null | grep 'pid={pid}' | awk '{{print $4}}' | grep -oE '[0-9]+$' | head -1";
                        using var sshClient = new SshClient(CreateConnectionInfo());
                        sshClient.Connect();
                        var portResult = sshClient.RunCommand(portCmd);
                        sshClient.Disconnect();

                        if (int.TryParse(portResult.Result.Trim(), out var p))
                        {
                            port = p;
                        }
                    }
                    catch { }

                    // Extract executable path (first part of command)
                    var execPath = command.Split(' ')[0];

                    instances.Add(new UnityInstance(
                        pid,
                        user,
                        cpu,
                        mem,
                        memMB,
                        start,
                        uptime,
                        command,
                        execPath,
                        port,
                        true
                    ));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing line: {line} - {ex.Message}");
                }
            }

            return instances;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Unity instances: {ex.Message}");
            return new List<UnityInstance>();
        }
    }

    public async Task<OperationResult> StopInstanceAsync(string pid)
    {
        try
        {
            var commandText = $"kill -15 {pid} && echo 'Instance stopped' || kill -9 {pid}";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            return new OperationResult(true, $"Stopped instance PID {pid}", output);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Failed to stop instance {pid}", ex.Message);
        }
    }

    public async Task<OperationResult> RestartInstanceAsync(string pid, string executablePath)
    {
        try
        {
            Console.WriteLine($"=== Restarting Instance PID: {pid} ===");
            Console.WriteLine($"Executable path: {executablePath}");

            // Stop the instance
            Console.WriteLine("Step 1: Stopping instance...");
            var stopResult = await StopInstanceAsync(pid);

            Console.WriteLine($"Stop result - Success: {stopResult.Success}, Message: {stopResult.Message}");

            if (!stopResult.Success)
            {
                return new OperationResult(false, $"❌ Failed to stop instance {pid}", 
                    $"Cannot restart because stop failed.\n{stopResult.Message}\n{stopResult.Output}");
            }

            // Wait for the process to fully terminate
            Console.WriteLine("Step 2: Waiting for process to terminate...");
            await Task.Delay(2000); // Increased to 2 seconds

            // Verify the process is actually stopped
            var checkCmd = $"ps -p {pid} > /dev/null 2>&1 && echo 'STILL_RUNNING' || echo 'STOPPED'";
            var processStatus = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                sshClient.Connect();
                var result = sshClient.RunCommand(checkCmd);
                sshClient.Disconnect();
                return result.Result.Trim();
            });

            Console.WriteLine($"Process status check: {processStatus}");

            if (processStatus == "STILL_RUNNING")
            {
                Console.WriteLine("Process still running, waiting another 2 seconds...");
                await Task.Delay(2000);
            }

            // Start the instance again with the same executable
            Console.WriteLine("Step 3: Starting instance...");
            var startResult = await StartInstanceAsync(executablePath);

            Console.WriteLine($"Start result - Success: {startResult.Success}, Message: {startResult.Message}");

            if (startResult.Success)
            {
                return new OperationResult(true, 
                    $"✅ Successfully restarted instance!\n" +
                    $"📋 Old PID: {pid}\n" +
                    $"🆕 {startResult.Message}", 
                    $"=== Stop Output ===\n{stopResult.Output}\n\n=== Start Output ===\n{startResult.Output}");
            }
            else
            {
                return new OperationResult(false, 
                    $"⚠️ Instance {pid} stopped but failed to restart\n" +
                    $"Reason: {startResult.Message}", 
                    $"=== Stop Output ===\n{stopResult.Output}\n\n=== Start Error ===\n{startResult.Output}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during restart: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return new OperationResult(false, $"❌ Failed to restart instance {pid}", 
                $"{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
        }
    }

    public async Task<OperationResult> StartInstanceAsync(string executablePath, string args = "")
    {
        try
        {
            var defaultArgs = "-batchmode -nographics";
            var fullArgs = string.IsNullOrEmpty(args) ? defaultArgs : $"{defaultArgs} {args}";

            // Get the directory where the executable is located
            var execDirectory = Path.GetDirectoryName(executablePath)?.Replace('\\', '/');

            // If no directory, assume it's in RemoteDeployPath
            if (string.IsNullOrEmpty(execDirectory))
            {
                execDirectory = _options.RemoteDeployPath;
                executablePath = $"{_options.RemoteDeployPath}/{executablePath}".Replace("//", "/");
            }

            var execFileName = Path.GetFileName(executablePath);

            // Each instance writes to server.log in its own directory
            var logFileName = "server.log";
            var logFilePath = $"{execDirectory}/{logFileName}";

            Console.WriteLine($"Starting instance: {executablePath}");
            Console.WriteLine($"Working directory: {execDirectory}");
            Console.WriteLine($"Log file: {logFilePath}");

            // First, verify the file exists and make it executable
            var verifyCommand = $"test -f '{executablePath}' && echo 'EXISTS' || echo 'NOT_FOUND'";
            var fileExists = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                sshClient.Connect();
                var result = sshClient.RunCommand(verifyCommand);
                sshClient.Disconnect();
                return result.Result.Trim() == "EXISTS";
            });

            if (!fileExists)
            {
                return new OperationResult(false, "❌ Executable file not found", 
                    $"Could not find: {executablePath}\nMake sure the file was uploaded correctly.");
            }

            Console.WriteLine("File exists, setting permissions and starting...");

            // First, ensure permissions are set
            var chmodCmd = $"chmod +x '{executablePath}'";
            await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                sshClient.Connect();
                sshClient.RunCommand(chmodCmd);
                sshClient.Disconnect();
            });

            // Create a startup script on the server to avoid SSH hanging issues
            var scriptPath = $"{execDirectory}/start_server.sh";

            // Use explicit \n for Unix line endings (not Environment.NewLine which is \r\n on Windows)
            var scriptContent = "#!/bin/bash\n" +
                               $"cd '{execDirectory}'\n" +
                               $"nohup ./{execFileName} {fullArgs} > {logFileName} 2>&1 &\n" +
                               "echo $!\n";

            Console.WriteLine($"Creating startup script (log will be: {logFileName})...");

            try
            {
                // Write the script to the server
                await Task.Run(() =>
                {
                    using var sftpClient = new SftpClient(CreateConnectionInfo());
                    sftpClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                    sftpClient.Connect();

                    using var scriptStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(scriptContent));
                    sftpClient.UploadFile(scriptStream, scriptPath, true);

                    sftpClient.Disconnect();
                    Console.WriteLine($"Script uploaded to: {scriptPath}");
                });
            }
            catch (Exception uploadEx)
            {
                Console.WriteLine($"Failed to upload startup script: {uploadEx.Message}");
                return new OperationResult(false, "❌ Failed to upload startup script", uploadEx.ToString());
            }

            // Execute the script with bash explicitly and clean up
            var executeScript = $"bash '{scriptPath}' && rm -f '{scriptPath}'";

            try
            {
                var output = await Task.Run(() =>
                {
                    using var sshClient = new SshClient(CreateConnectionInfo());
                    sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                    sshClient.Connect();

                    Console.WriteLine($"Executing startup script: {scriptPath}");
                    var command = sshClient.RunCommand(executeScript);

                    sshClient.Disconnect();

                    var result = command.Result.Trim();
                    var error = command.Error?.Trim();

                    Console.WriteLine($"Exit status: {command.ExitStatus}");
                    Console.WriteLine($"Output: '{result}'");

                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"Error output: {error}");
                    }

                    // Extract PID from result (might have multiple lines)
                    var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var pidLine = lines.LastOrDefault(); // PID should be the last line

                    if (int.TryParse(pidLine, out var pid) && pid > 0)
                    {
                        return $"✅ Started successfully!\nPID: {pid}";
                    }
                    else if (command.ExitStatus == 0 && !string.IsNullOrEmpty(result))
                    {
                        return $"✅ Process started (exit code: 0)\nOutput: {result}";
                    }
                    else
                    {
                        var errorMsg = $"⚠️ Failed to start or get PID\nExit code: {command.ExitStatus}\nOutput: {result}";
                        if (!string.IsNullOrEmpty(error))
                        {
                            errorMsg += $"\nError: {error}";
                        }
                        return errorMsg;
                    }
                });

                return new OperationResult(true, $"✅ Started new instance: {execFileName}\n📁 Directory: {execDirectory}\n📝 Log file: {logFilePath}", output);
            }
            catch (Exception executeEx)
            {
                Console.WriteLine($"Failed to execute startup script: {executeEx.Message}");
                return new OperationResult(false, "❌ Failed to execute startup script", executeEx.ToString());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception starting instance: {ex.Message}");
            return new OperationResult(false, "❌ Failed to start instance", $"{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
        }
    }

    public async Task<string> GetInstanceLogsAsync(string pid)
    {
        try
        {
            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();

                // Get process info
                var psCmd = $"ps -p {pid} -o pid,user,cmd --no-headers 2>/dev/null";
                var psResult = sshClient.RunCommand(psCmd);
                var processInfo = psResult.Result;

                if (string.IsNullOrWhiteSpace(processInfo))
                {
                    sshClient.Disconnect();
                    return $"Error: Process PID {pid} not found";
                }

                // Get the working directory of the process using pwdx
                var pwdxCmd = $"pwdx {pid} 2>/dev/null";
                var pwdxResult = sshClient.RunCommand(pwdxCmd);
                var execDir = "";

                if (!string.IsNullOrWhiteSpace(pwdxResult.Result))
                {
                    // pwdx output format: "12345: /root/TarneebServer"
                    var pwdxParts = pwdxResult.Result.Split(':', 2);
                    if (pwdxParts.Length == 2)
                    {
                        execDir = pwdxParts[1].Trim();
                    }
                }

                // Extract executable name from ps output
                var parts = processInfo.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var execPath = parts.Length > 2 ? parts[2] : "";
                var execName = Path.GetFileName(execPath);
                var execNameWithoutExt = execName.Replace(".x86_64", "").Replace(".x86", "");

                // If pwdx failed, try to extract from command line
                if (string.IsNullOrEmpty(execDir))
                {
                    execDir = Path.GetDirectoryName(execPath);
                    if (string.IsNullOrEmpty(execDir) || execDir == ".")
                    {
                        execDir = "/root"; // Fallback
                    }
                }

                var result = new System.Text.StringBuilder();
                result.AppendLine($"=== Instance PID: {pid} ===");
                result.AppendLine($"Process: {processInfo.Trim()}");
                result.AppendLine($"Executable: {execName}");
                result.AppendLine($"Directory: {execDir}");
                result.AppendLine("=====================================");
                result.AppendLine();

                // Check log locations - each instance has server.log in its own directory
                var logLocations = new[]
                {
                    $"{execDir}/server.log",                        // Instance-specific log in its own folder
                    $"{execDir}/{execNameWithoutExt}_server.log",  // Old naming (fallback)
                    $"/tmp/unity-{execName}.log",
                    $"/tmp/unity-{execNameWithoutExt}.log",
                    "/var/log/unity-server.log",
                    $"{execDir}/unity.log",
                    $"{execDir}/nohup.out"
                };

                string foundLog = null;
                foreach (var logPath in logLocations)
                {
                    if (string.IsNullOrWhiteSpace(logPath)) continue;

                    var checkCmd = $"test -f '{logPath}' && echo 'exists'";
                    var checkResult = sshClient.RunCommand(checkCmd);

                    if (checkResult.Result.Trim() == "exists")
                    {
                        foundLog = logPath;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(foundLog))
                {
                    result.AppendLine($"=== Log file: {foundLog} ===");
                    result.AppendLine();
                    result.AppendLine("Last 500 lines:");
                    result.AppendLine("=====================================");

                    var tailCmd = $"tail -n 500 '{foundLog}'";
                    var tailResult = sshClient.RunCommand(tailCmd);
                    result.Append(tailResult.Result);

                    result.AppendLine();
                    result.AppendLine("=====================================");
                    result.AppendLine($"Total lines shown: 500 (or less if file is smaller)");
                }
                else
                {
                    result.AppendLine("⚠️  No log file found for this instance!");
                    result.AppendLine();

                    // Try to find log files in the instance directory
                    result.AppendLine($"Searching for log files in {execDir}:");
                    var findCmd = $"find '{execDir}' -maxdepth 1 -name '*.log' -type f 2>/dev/null";
                    var findResult = sshClient.RunCommand(findCmd);

                    if (!string.IsNullOrWhiteSpace(findResult.Result))
                    {
                        result.AppendLine("Found log files:");
                        result.AppendLine(findResult.Result);

                        // Show the first one found
                        var firstLog = findResult.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstLog))
                        {
                            result.AppendLine();
                            result.AppendLine($"=== Contents of {firstLog} (last 500 lines) ===");
                            var tailCmd = $"tail -n 500 '{firstLog}'";
                            var tailResult = sshClient.RunCommand(tailCmd);
                            result.Append(tailResult.Result);
                        }
                    }
                    else
                    {
                        result.AppendLine($"  No log files found in {execDir}");
                        result.AppendLine();
                        result.AppendLine("Expected log file location:");
                        result.AppendLine($"  {execDir}/server.log");
                        result.AppendLine();
                        result.AppendLine("To view logs, run:");
                        result.AppendLine($"  cd {execDir}");
                        result.AppendLine($"  tail -n 500 server.log");
                    }

                    result.AppendLine();
                    result.AppendLine($"Note: Each instance writes to server.log in its own directory.");
                }

                sshClient.Disconnect();
                return result.ToString();
            });

            return string.IsNullOrWhiteSpace(output) ? "No logs available" : output;
        }
        catch (Exception ex)
        {
            return $"Error getting logs: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
        }
    }

    public async Task<string> GetPortUsageAsync()
    {
        try
        {
            var commandText = $"echo '=== All Listening Ports ===' && netstat -tulpn 2>/dev/null || ss -tulpn 2>/dev/null && echo '\n=== Port {_options.UnityServerPort} Usage ===' && netstat -tulpn 2>/dev/null | grep {_options.UnityServerPort} || ss -tulpn 2>/dev/null | grep {_options.UnityServerPort} || echo 'Port {_options.UnityServerPort} is not in use'";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();
                return command.Result;
            });

            return output;
        }
        catch (Exception ex)
        {
            return $"Error checking port usage: {ex.Message}";
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync()
    {
        // First test network connectivity on common SSH ports
        var portsToTest = new[] { 22, 2222, 22222 };
        var openPorts = new List<int>();

        foreach (var port in portsToTest)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(_options.Host, port, cts.Token);
                openPorts.Add(port);
            }
            catch
            {
                // Port not open or timeout
            }
        }

        if (openPorts.Count == 0)
        {
            return new ConnectionTestResult(
                false,
                $"Cannot reach server {_options.Host}. No SSH ports responding.",
                ErrorDetails: "Firewall may be blocking connection, or server is offline."
            );
        }

        // Now try SSH authentication on detected ports
        foreach (var port in openPorts)
        {
            try
            {
                var testOptions = new ServerManagementOptions
                {
                    Host = _options.Host,
                    Port = port,
                    Username = _options.Username,
                    Password = _options.Password,
                    PrivateKeyPath = _options.PrivateKeyPath
                };

                var connectionInfo = CreateConnectionInfoForOptions(testOptions);
                connectionInfo.Timeout = TimeSpan.FromSeconds(10);

                await Task.Run(() =>
                {
                    using var sshClient = new SshClient(connectionInfo);
                    sshClient.Connect();
                    var command = sshClient.RunCommand("echo 'Connection successful'");
                    sshClient.Disconnect();
                });

                return new ConnectionTestResult(
                    true,
                    $"? SSH connection successful on port {port}!",
                    DetectedPort: port
                );
            }
            catch (Exception ex)
            {
                if (port == openPorts.Last())
                {
                    return new ConnectionTestResult(
                        false,
                        $"Port {port} is open but authentication failed",
                        DetectedPort: port,
                        ErrorDetails: ex.Message
                    );
                }
            }
        }

        return new ConnectionTestResult(
            false,
            "Connection test failed",
            ErrorDetails: "Could not authenticate on any detected SSH ports"
        );
    }

    private Renci.SshNet.ConnectionInfo CreateConnectionInfoForOptions(ServerManagementOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath) && File.Exists(options.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(options.Password)
                ? new PrivateKeyFile(options.PrivateKeyPath)
                : new PrivateKeyFile(options.PrivateKeyPath, options.Password);

            return new Renci.SshNet.ConnectionInfo(options.Host, options.Port, options.Username, new PrivateKeyAuthenticationMethod(options.Username, keyFile));
        }

        return new Renci.SshNet.ConnectionInfo(options.Host, options.Port, options.Username, new PasswordAuthenticationMethod(options.Username, options.Password));
    }

    public async Task<OperationResult> UploadBuildAsync(Stream fileStream, string fileName)
    {
        try
        {
            var remoteFilePath = $"{_options.RemoteDeployPath.TrimEnd('/')}/{fileName}";
            var isArchive = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || 
                           fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase);

            // Upload the file
            await Task.Run(() =>
            {
                using var sftpClient = new SftpClient(CreateConnectionInfo());
                sftpClient.Connect();

                EnsureRemoteDirectory(sftpClient, _options.RemoteDeployPath);

                fileStream.Position = 0;
                sftpClient.UploadFile(fileStream, remoteFilePath, true);
                sftpClient.Disconnect();
            });

            var uploadMessage = $"Uploaded to {remoteFilePath}";

            // If it's an archive, extract it
            if (isArchive)
            {
                var extractResult = await ExtractArchiveAsync(remoteFilePath, fileName);
                return new OperationResult(
                    extractResult.Success,
                    extractResult.Success ? $"{uploadMessage}\n{extractResult.Message}" : extractResult.Message,
                    extractResult.Output
                );
            }

            return new OperationResult(true, uploadMessage);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, "Upload failed.", ex.Message);
        }
    }

    public async Task<OperationResult> UploadFolderAsync(IFormFileCollection files)
    {
        try
        {
            var uploadedFiles = new List<string>();
            var errors = new List<string>();
            var totalFiles = files.Count;
            var processedFiles = 0;

            await Task.Run(() =>
            {
                using var sftpClient = new SftpClient(CreateConnectionInfo());

                // Increase timeout for large file uploads
                sftpClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(5);
                sftpClient.OperationTimeout = TimeSpan.FromMinutes(5);

                sftpClient.Connect();

                foreach (var file in files)
                {
                    try
                    {
                        processedFiles++;

                        // Get relative path from the uploaded file
                        // The FileName includes the folder structure when using webkitdirectory
                        var relativePath = file.FileName.Replace('\\', '/');

                        // Determine remote path
                        var remoteFilePath = $"{_options.RemoteDeployPath.TrimEnd('/')}/{relativePath}";
                        var remoteDirectory = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/') ?? _options.RemoteDeployPath;

                        // Ensure remote directory exists
                        EnsureRemoteDirectory(sftpClient, remoteDirectory);

                        // Log file being uploaded
                        var fileSizeMB = file.Length / (1024.0 * 1024.0);
                        Console.WriteLine($"[{processedFiles}/{totalFiles}] Uploading: {relativePath} ({fileSizeMB:F2} MB)");

                        // Upload file with progress callback
                        using var stream = file.OpenReadStream();
                        stream.Position = 0;

                        // For large files, use buffer to prevent timeout
                        sftpClient.BufferSize = 32768; // 32KB buffer
                        sftpClient.UploadFile(stream, remoteFilePath, true, (uploaded) =>
                        {
                            // Progress callback for large files
                            if (fileSizeMB > 10 && uploaded % (1024 * 1024) == 0) // Log every MB for files > 10MB
                            {
                                Console.WriteLine($"  Progress: {uploaded / (1024.0 * 1024.0):F2} MB / {fileSizeMB:F2} MB");
                            }
                        });

                        uploadedFiles.Add(relativePath);
                        Console.WriteLine($"✓ Completed: {relativePath}");
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"{file.FileName}: {ex.Message}";
                        errors.Add(errorMsg);
                        Console.WriteLine($"✗ Failed: {errorMsg}");
                    }
                }

                sftpClient.Disconnect();
            });

            // Make all .x86_64 files executable
            Console.WriteLine("Setting executable permissions...");

            // Use find command for more efficient permission setting
            await SetExecutablePermissionsAsync();

            Console.WriteLine("Permissions set successfully!");

            var totalSizeMB = files.Sum(f => f.Length) / (1024.0 * 1024.0);
            var message = $"✅ Uploaded {uploadedFiles.Count}/{totalFiles} files ({totalSizeMB:F2} MB) to {_options.RemoteDeployPath}\n" +
                         $"✅ Made Unity executables (.x86_64) executable";

            if (errors.Any())
            {
                message += $"\n⚠️ {errors.Count} files failed";
            }

            var output = "Uploaded files:\n" + string.Join("\n", uploadedFiles.Take(50));
            if (uploadedFiles.Count > 50)
            {
                output += $"\n... and {uploadedFiles.Count - 50} more files";
            }

            if (errors.Any())
            {
                output += "\n\nErrors:\n" + string.Join("\n", errors.Take(10));
                if (errors.Count > 10)
                {
                    output += $"\n... and {errors.Count - 10} more errors";
                }
            }

            return new OperationResult(errors.Count == 0, message, output);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, "Folder upload failed.", ex.Message);
        }
    }

    private async Task SetExecutablePermissionsAsync()
    {
        try
        {
            // Use find to set permissions on all .x86_64 files - more efficient than wildcards
            var commandText = $"find {_options.RemoteDeployPath} -type f -name '*.x86_64' -exec chmod +x {{}} \\; 2>/dev/null || true";
            Console.WriteLine($"Running: {commandText}");

            await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromMinutes(2);
                sshClient.Connect();

                var command = sshClient.RunCommand(commandText);
                Console.WriteLine($"chmod completed with exit code: {command.ExitStatus}");

                if (!string.IsNullOrEmpty(command.Result))
                {
                    Console.WriteLine($"chmod output: {command.Result}");
                }

                sshClient.Disconnect();
            });

            Console.WriteLine("All .x86_64 files are now executable");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning setting permissions: {ex.Message}");
            // Not critical - files may still work if uploaded from Linux
        }
    }

    private async Task MakeExecutableAsync(string pattern)
    {
        try
        {
            var commandText = $"chmod +x {_options.RemoteDeployPath}/{pattern} 2>/dev/null || true";
            Console.WriteLine($"Running: {commandText}");

            await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                sshClient.Connect();

                var command = sshClient.RunCommand(commandText);
                Console.WriteLine($"chmod result: {command.ExitStatus} - {command.Result}");

                sshClient.Disconnect();
            });

            Console.WriteLine($"chmod completed for pattern: {pattern}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"chmod warning for {pattern}: {ex.Message}");
            // Ignore errors from chmod - not critical
        }
    }

    private async Task<OperationResult> ExtractArchiveAsync(string archivePath, string fileName)
    {
        try
        {
            var directory = Path.GetDirectoryName(archivePath) ?? _options.RemoteDeployPath;
            var extractCommand = "";

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Extract zip
                extractCommand = $"cd {directory} && unzip -o {archivePath} && chmod +x {directory}/*.x86_64 2>/dev/null; rm {archivePath}";
            }
            else if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                // Extract tar.gz
                extractCommand = $"cd {directory} && tar -xzf {archivePath} && chmod +x {directory}/*.x86_64 2>/dev/null; rm {archivePath}";
            }
            else if (fileName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                // Extract tar
                extractCommand = $"cd {directory} && tar -xf {archivePath} && chmod +x {directory}/*.x86_64 2>/dev/null; rm {archivePath}";
            }

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(extractCommand);
                sshClient.Disconnect();

                var result = new System.Text.StringBuilder();
                if (!string.IsNullOrWhiteSpace(command.Result))
                    result.AppendLine(command.Result);
                if (!string.IsNullOrWhiteSpace(command.Error))
                    result.AppendLine($"Warnings: {command.Error}");

                return result.ToString();
            });

            return new OperationResult(
                true,
                $"✅ Archive extracted successfully to {directory}\n✅ Made Unity executables executable\n✅ Archive file removed",
                output
            );
        }
        catch (Exception ex)
        {
            return new OperationResult(false, "Failed to extract archive.", ex.Message);
        }
    }

    private async Task<OperationResult> ExecuteServiceCommandAsync(string action)
    {
        try
        {
            var commandText = $"sudo systemctl {action} {_options.ServiceName}";

            var output = await Task.Run(() =>
            {
                using var sshClient = new SshClient(CreateConnectionInfo());
                sshClient.Connect();
                var command = sshClient.RunCommand(commandText);
                sshClient.Disconnect();

                return string.Join(Environment.NewLine, new[] { command.Result, command.Error }.Where(x => !string.IsNullOrWhiteSpace(x)));
            });

            return new OperationResult(true, $"Service {action} command sent.", output);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Service {action} failed.", ex.Message);
        }
    }

    private Renci.SshNet.ConnectionInfo CreateConnectionInfo()
    {
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPath) && File.Exists(_options.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(_options.Password)
                ? new PrivateKeyFile(_options.PrivateKeyPath)
                : new PrivateKeyFile(_options.PrivateKeyPath, _options.Password);

            return new Renci.SshNet.ConnectionInfo(_options.Host, _options.Port, _options.Username, new PrivateKeyAuthenticationMethod(_options.Username, keyFile));
        }

        return new Renci.SshNet.ConnectionInfo(_options.Host, _options.Port, _options.Username, new PasswordAuthenticationMethod(_options.Username, _options.Password));
    }

    private static void EnsureRemoteDirectory(SftpClient client, string fullPath)
    {
        if (client.Exists(fullPath))
        {
            return;
        }

        var segments = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";

        foreach (var segment in segments)
        {
            current = $"{current.TrimEnd('/')}/{segment}";
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }
}
