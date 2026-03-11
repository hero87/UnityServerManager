# 🚀 Deployment Guide - Self-Hosted on Linux Server

Deploy Unity Server Manager on the same Linux server it manages.

## 📋 Prerequisites

- Linux server (Ubuntu 20.04+ recommended)
- .NET 10 Runtime installed on server
- SSH access to the server
- Domain name or server IP address

## 🔧 Step 1: Install .NET 10 Runtime on Server

```bash
# SSH into your server
ssh root@your-server-ip

# Download and install .NET 10
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --runtime aspnetcore

# Add to PATH (add to ~/.bashrc for persistence)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

Or use package manager (Ubuntu):

```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0
```

## 📦 Step 2: Publish the Application

On your **development machine**:

```bash
# Navigate to project directory
cd UnityServerManager

# Publish for Linux x64
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

# Create a zip file
cd publish
tar -czf UnityServerManager.tar.gz *
```

## 📤 Step 3: Upload to Server

```bash
# From your development machine
scp UnityServerManager.tar.gz root@your-server-ip:/opt/

# SSH into server
ssh root@your-server-ip

# Extract files
cd /opt
mkdir -p UnityServerManager
tar -xzf UnityServerManager.tar.gz -C UnityServerManager
cd UnityServerManager
chmod +x UnityServerManager
```

## ⚙️ Step 4: Configure for Local Management

Create `appsettings.Production.json` on the server:

```bash
nano /opt/UnityServerManager/appsettings.Production.json
```

Add this configuration:

```json
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
```

## 🔐 Step 5: Setup SSH Key Authentication

Since the app is managing the same server it runs on:

```bash
# Generate SSH key (if not exists)
ssh-keygen -t rsa -b 4096 -f /root/.ssh/id_rsa -N ""

# Add to authorized_keys
cat /root/.ssh/id_rsa.pub >> /root/.ssh/authorized_keys
chmod 600 /root/.ssh/authorized_keys

# Test local SSH
ssh localhost "echo SSH working!"
```

## 🔧 Step 6: Create Systemd Service

```bash
sudo nano /etc/systemd/system/unity-server-manager.service
```

Add this content:

```ini
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
```

Enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable unity-server-manager
sudo systemctl start unity-server-manager
sudo systemctl status unity-server-manager
```

## 🌐 Step 7: Setup Nginx Reverse Proxy (Optional but Recommended)

Install Nginx:

```bash
sudo apt-get update
sudo apt-get install -y nginx
```

Create Nginx configuration:

```bash
sudo nano /etc/nginx/sites-available/unity-server-manager
```

Add this configuration:

```nginx
server {
    listen 80;
    server_name your-domain.com;  # or your server IP

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Increase timeout for large uploads
        proxy_read_timeout 600s;
        proxy_connect_timeout 600s;
        proxy_send_timeout 600s;
        
        # Large file upload support
        client_max_body_size 2G;
    }
}
```

Enable the site:

```bash
sudo ln -s /etc/nginx/sites-available/unity-server-manager /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

## 🔒 Step 8: Setup SSL with Let's Encrypt (Optional)

```bash
sudo apt-get install -y certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

## 🔥 Step 9: Configure Firewall

```bash
# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Allow your Unity game port
sudo ufw allow 7777/tcp

# Enable firewall
sudo ufw enable
sudo ufw status
```

## ✅ Step 10: Access Your Application

Open browser and navigate to:
- **Without Nginx:** `http://your-server-ip:5000`
- **With Nginx:** `http://your-domain.com` or `http://your-server-ip`
- **With SSL:** `https://your-domain.com`

## 🔄 Updating the Application

```bash
# On development machine, publish new version
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish
cd publish
tar -czf UnityServerManager.tar.gz *
scp UnityServerManager.tar.gz root@your-server-ip:/tmp/

# On server
sudo systemctl stop unity-server-manager
cd /opt/UnityServerManager
tar -xzf /tmp/UnityServerManager.tar.gz
chmod +x UnityServerManager
sudo systemctl start unity-server-manager
sudo systemctl status unity-server-manager
```

## 📊 Monitoring and Logs

```bash
# View application logs
sudo journalctl -u unity-server-manager -f

# View last 100 lines
sudo journalctl -u unity-server-manager -n 100

# Check service status
sudo systemctl status unity-server-manager

# Restart if needed
sudo systemctl restart unity-server-manager
```

## 🐛 Troubleshooting

### Service won't start

```bash
# Check logs
sudo journalctl -u unity-server-manager -xe

# Check permissions
ls -la /opt/UnityServerManager/
chmod +x /opt/UnityServerManager/UnityServerManager

# Check .NET runtime
dotnet --info
```

### Can't upload files

```bash
# Check appsettings.Production.json
cat /opt/UnityServerManager/appsettings.Production.json

# Test SSH locally
ssh localhost "echo test"

# Check SSH key permissions
ls -la /root/.ssh/
```

### Port already in use

```bash
# Check what's using port 5000
sudo lsof -i :5000

# Change port in appsettings.Production.json
nano /opt/UnityServerManager/appsettings.Production.json
```

## 🔐 Security Best Practices

1. **Use SSH Keys** instead of password authentication
2. **Run as non-root user** (create dedicated user):
   ```bash
   sudo useradd -m -s /bin/bash unitymanager
   sudo chown -R unitymanager:unitymanager /opt/UnityServerManager
   # Update systemd service User=unitymanager
   ```
3. **Enable UFW firewall** and only allow necessary ports
4. **Use HTTPS** with Let's Encrypt SSL certificate
5. **Regular updates**:
   ```bash
   sudo apt-get update && sudo apt-get upgrade
   ```
6. **Backup configurations**:
   ```bash
   tar -czf /backup/unity-manager-config.tar.gz /opt/UnityServerManager/appsettings.Production.json
   ```

## 📝 Quick Deployment Script

Save this as `deploy.sh`:

```bash
#!/bin/bash
set -e

echo "🚀 Deploying Unity Server Manager..."

# Publish locally
echo "📦 Publishing application..."
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

# Create archive
cd publish
tar -czf UnityServerManager.tar.gz *
cd ..

# Upload to server
echo "📤 Uploading to server..."
scp publish/UnityServerManager.tar.gz root@$1:/tmp/

# Deploy on server
echo "🔧 Installing on server..."
ssh root@$1 << 'ENDSSH'
systemctl stop unity-server-manager
cd /opt/UnityServerManager
tar -xzf /tmp/UnityServerManager.tar.gz
chmod +x UnityServerManager
systemctl start unity-server-manager
systemctl status unity-server-manager
ENDSSH

echo "✅ Deployment complete!"
echo "🌐 Access at: http://$1"
```

Usage:

```bash
chmod +x deploy.sh
./deploy.sh your-server-ip
```

## 🎯 Architecture Overview

```
┌─────────────────────────────────────────┐
│         Linux Server (Ubuntu)           │
│                                         │
│  ┌───────────────────────────────┐    │
│  │   Nginx (Port 80/443)         │    │
│  │   Reverse Proxy + SSL         │    │
│  └──────────┬────────────────────┘    │
│             │                           │
│             ▼                           │
│  ┌───────────────────────────────┐    │
│  │   Unity Server Manager        │    │
│  │   ASP.NET Core (Port 5000)    │    │
│  │   /opt/UnityServerManager     │    │
│  └──────────┬────────────────────┘    │
│             │ SSH (localhost:22)       │
│             ▼                           │
│  ┌───────────────────────────────┐    │
│  │   Unity Game Servers          │    │
│  │   /root/TarneebServer/        │    │
│  │   /root/CardsServer/          │    │
│  │   (Managed via SSH)           │    │
│  └───────────────────────────────┘    │
│                                         │
└─────────────────────────────────────────┘
        ▲
        │ HTTPS (Internet)
        │
   Browser Access
```

## 📞 Support

If you encounter issues:
1. Check logs: `sudo journalctl -u unity-server-manager -f`
2. Verify SSH: `ssh localhost "echo test"`
3. Check permissions: `ls -la /opt/UnityServerManager/`
4. Test locally: `cd /opt/UnityServerManager && ./UnityServerManager`

---

🎉 **Congratulations!** Your Unity Server Manager is now self-hosted and managing the same server it runs on!
