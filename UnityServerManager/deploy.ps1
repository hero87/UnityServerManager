# Unity Server Manager - Quick Deployment Script (PowerShell)
# Usage: .\deploy.ps1 -Server <server-ip-or-hostname>

param(
    [Parameter(Mandatory=$true)]
    [string]$Server
)

$ErrorActionPreference = "Stop"

Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Blue
Write-Host "║  Unity Server Manager Deployment      ║" -ForegroundColor Blue
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Blue
Write-Host ""

# Step 1: Build and Publish
Write-Host "📦 Step 1/5: Building application..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Step 2: Create Archive
Write-Host "📦 Step 2/5: Creating archive..." -ForegroundColor Yellow
Set-Location publish
tar -czf UnityServerManager.tar.gz *
Set-Location ..

# Step 3: Upload to Server
Write-Host "📤 Step 3/5: Uploading to server ($Server)..." -ForegroundColor Yellow
scp publish/UnityServerManager.tar.gz root@${Server}:/tmp/

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Upload failed! Check SSH connection." -ForegroundColor Red
    exit 1
}

# Step 4: Deploy on Server
Write-Host "🔧 Step 4/5: Installing on server..." -ForegroundColor Yellow

$deployScript = @'
set -e

# Stop service if running
if systemctl is-active --quiet unity-server-manager; then
    echo "Stopping unity-server-manager service..."
    systemctl stop unity-server-manager
fi

# Create directory if doesn't exist
mkdir -p /opt/UnityServerManager

# Extract files
echo "Extracting files..."
cd /opt/UnityServerManager
tar -xzf /tmp/UnityServerManager.tar.gz
chmod +x UnityServerManager

# Create appsettings.Production.json if doesn't exist
if [ ! -f appsettings.Production.json ]; then
    echo "Creating production configuration..."
    cat > appsettings.Production.json << 'EOFCONFIG'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ServerManagement": {
    "Host": "localhost",
    "Port": 22,
    "Username": "root",
    "Password": "",
    "PrivateKeyPath": "/root/.ssh/id_rsa",
    "ServiceName": "unity-server",
    "RemoteDeployPath": "/root",
    "UnityServerPort": 7777
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  }
}
EOFCONFIG
fi

# Setup SSH key for local management if doesn't exist
if [ ! -f /root/.ssh/id_rsa ]; then
    echo "Setting up SSH key for local management..."
    ssh-keygen -t rsa -b 4096 -f /root/.ssh/id_rsa -N ""
    cat /root/.ssh/id_rsa.pub >> /root/.ssh/authorized_keys
    chmod 600 /root/.ssh/authorized_keys
fi

# Create systemd service if doesn't exist
if [ ! -f /etc/systemd/system/unity-server-manager.service ]; then
    echo "Creating systemd service..."
    cat > /etc/systemd/system/unity-server-manager.service << 'EOFSERVICE'
[Unit]
Description=Unity Server Manager Web Application
After=network.target

[Service]
Type=notify
User=root
WorkingDirectory=/opt/UnityServerManager
ExecStart=/opt/UnityServerManager/UnityServerManager
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=unity-server-manager
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOFSERVICE
    systemctl daemon-reload
    systemctl enable unity-server-manager
fi

# Start service
echo "Starting unity-server-manager service..."
systemctl start unity-server-manager

# Wait a moment for service to start
sleep 2

# Check status
systemctl status unity-server-manager --no-pager
'@

ssh root@$Server $deployScript

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Deployment failed!" -ForegroundColor Red
    exit 1
}

# Step 5: Verify
Write-Host "✅ Step 5/5: Verifying deployment..." -ForegroundColor Yellow
Start-Sleep -Seconds 1

# Clean up
Remove-Item -Path publish -Recurse -Force

Write-Host ""
Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✅ Deployment Successful!            ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Service Status:" -ForegroundColor Blue
ssh root@$Server "systemctl status unity-server-manager --no-pager | head -n 10"
Write-Host ""
Write-Host "🌐 Access the application at:" -ForegroundColor Blue
Write-Host "   http://$Server:5000"
Write-Host ""
Write-Host "📝 Useful commands:" -ForegroundColor Blue
Write-Host "   View logs:    " -NoNewline -ForegroundColor Blue
Write-Host "ssh root@$Server 'journalctl -u unity-server-manager -f'" -ForegroundColor Yellow
Write-Host "   Restart:      " -NoNewline -ForegroundColor Blue
Write-Host "ssh root@$Server 'systemctl restart unity-server-manager'" -ForegroundColor Yellow
Write-Host "   Stop:         " -NoNewline -ForegroundColor Blue
Write-Host "ssh root@$Server 'systemctl stop unity-server-manager'" -ForegroundColor Yellow
Write-Host "   Status:       " -NoNewline -ForegroundColor Blue
Write-Host "ssh root@$Server 'systemctl status unity-server-manager'" -ForegroundColor Yellow
Write-Host ""
