#!/bin/bash

# Unity Server Manager - Quick Deployment Script
# Usage: ./deploy.sh <server-ip-or-hostname>

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Check if server IP provided
if [ -z "$1" ]; then
    echo -e "${RED}❌ Error: Please provide server IP or hostname${NC}"
    echo "Usage: ./deploy.sh <server-ip-or-hostname>"
    exit 1
fi

SERVER=$1
DEPLOY_PATH="/opt/UnityServerManager"

echo -e "${BLUE}╔════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║  Unity Server Manager Deployment      ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════╝${NC}"
echo ""

# Step 1: Build and Publish
echo -e "${YELLOW}📦 Step 1/5: Building application...${NC}"
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Build failed!${NC}"
    exit 1
fi

# Step 2: Create Archive
echo -e "${YELLOW}📦 Step 2/5: Creating archive...${NC}"
cd publish
tar -czf UnityServerManager.tar.gz *
cd ..

# Step 3: Upload to Server
echo -e "${YELLOW}📤 Step 3/5: Uploading to server ($SERVER)...${NC}"
scp publish/UnityServerManager.tar.gz root@$SERVER:/tmp/

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Upload failed! Check SSH connection.${NC}"
    exit 1
fi

# Step 4: Deploy on Server
echo -e "${YELLOW}🔧 Step 4/5: Installing on server...${NC}"
ssh root@$SERVER << 'ENDSSH'
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
    cat > appsettings.Production.json << 'EOF'
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
EOF
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
    cat > /etc/systemd/system/unity-server-manager.service << 'EOF'
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
EOF
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
ENDSSH

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Deployment failed!${NC}"
    exit 1
fi

# Step 5: Verify
echo -e "${YELLOW}✅ Step 5/5: Verifying deployment...${NC}"
sleep 1

# Clean up
rm -rf publish

echo ""
echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║  ✅ Deployment Successful!            ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
echo ""
echo -e "${BLUE}📊 Service Status:${NC}"
ssh root@$SERVER "systemctl status unity-server-manager --no-pager | head -n 10"
echo ""
echo -e "${BLUE}🌐 Access the application at:${NC}"
echo -e "   http://$SERVER:5000"
echo ""
echo -e "${BLUE}📝 Useful commands:${NC}"
echo -e "   View logs:    ${YELLOW}ssh root@$SERVER 'journalctl -u unity-server-manager -f'${NC}"
echo -e "   Restart:      ${YELLOW}ssh root@$SERVER 'systemctl restart unity-server-manager'${NC}"
echo -e "   Stop:         ${YELLOW}ssh root@$SERVER 'systemctl stop unity-server-manager'${NC}"
echo -e "   Status:       ${YELLOW}ssh root@$SERVER 'systemctl status unity-server-manager'${NC}"
echo ""
