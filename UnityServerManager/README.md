# Unity Server Manager - Features

## Configuration (appsettings.json)
```json
"ServerManagement": {
  "Host": "91.98.20.28",           // Linux server IP
  "Port": 22,                       // SSH port
  "Username": "root",               // SSH username
  "Password": "Mjwk3hTkWbqA",      // SSH password
  "PrivateKeyPath": "",             // Optional: SSH key path
  "RemoteDeployPath": "/opt/unity-server",
  "ServiceName": "unity-server",    // systemd service name
  "UnityServerPort": 7777          // Unity game server port
}
```

## Features

### 1. **Server Status Monitoring**
- Real-time status display (Running/Stopped)
- Shows:
  - Active State (active/inactive)
  - Sub-State (running/dead/etc)
  - Process ID (PID)
  - Memory usage
  - Uptime

### 2. **Service Control**
- **Start**: Starts the Unity server instance
- **Stop**: Stops the Unity server instance  
- **Restart**: Restarts the Unity server instance
- Buttons are disabled based on current state

### 3. **Port Monitoring**
- **Check Ports** button: Shows which ports the Unity server is listening on
- Displays network connections for the running process
- Useful to verify Unity server is listening on the configured port

### 4. **Upload Build**
- Upload Unity Linux build files via SFTP
- Deploys to the configured RemoteDeployPath

## How It Works

1. **SSH Connection**: Uses SSH.NET library to connect to your Linux server
2. **Systemd Control**: Executes `systemctl` commands to manage the service
3. **Status Checking**: Runs `systemctl show` to get detailed status
4. **Port Checking**: Uses `netstat`/`ss` to find listening ports for the process

## Usage

1. Configure your server details in `appsettings.json`
2. Make sure your Unity server is configured as a systemd service on Linux
3. Access the web interface to monitor and control your server
4. Use Start/Stop/Restart to control the service
5. Click "Check Ports" to see what ports are in use

## Systemd Service Setup (on Linux server)

Create `/etc/systemd/system/unity-server.service`:
```ini
[Unit]
Description=Unity Game Server
After=network.target

[Service]
Type=simple
User=unity
WorkingDirectory=/opt/unity-server
ExecStart=/opt/unity-server/YourUnityServer.x86_64 -batchmode -nographics -logFile /var/log/unity-server.log
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Then enable it:
```bash
sudo systemctl daemon-reload
sudo systemctl enable unity-server
sudo systemctl start unity-server
```
