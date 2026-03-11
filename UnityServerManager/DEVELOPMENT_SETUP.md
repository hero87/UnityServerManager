# Development Setup Guide

## ? Current Setup (Local Development)

Your app is configured for **local development** with remote server control:

```
???????????????????         SSH          ???????????????????????
?   Your PC       ? ???????????????????? ?  Linux Server       ?
?  localhost:5001 ?   Port 22            ?  91.98.20.28        ?
?                 ?                      ?                     ?
?  [Web UI]       ?  Controls via SSH    ?  [Unity Server]     ?
?  ASP.NET Core   ?  ????????????????    ?  systemd service    ?
???????????????????                      ?  Port 7777          ?
                                          ???????????????????????
```

## ?? What You Have Now

### **? Local Web Interface** (`localhost:5001`)
- Runs on your Windows PC
- Modern, responsive UI with real-time status
- Visual indicators (pulsing badges, color-coded status)
- All controls work via SSH to remote server

### **? Remote Server Control** (via SSH)
- Start/Stop/Restart Unity server instance
- Real-time status monitoring
- Port checking
- Memory and uptime tracking
- Upload builds via SFTP

## ?? How to Use

### 1. **Start the App Locally**
```bash
# In Visual Studio: Press F5
# Or via command line:
dotnet run
```

### 2. **Access the UI**
Open browser to: `https://localhost:5001`

### 3. **Monitor Your Server**
- Status updates automatically on page load
- Click "Refresh Status" button for manual refresh
- Green pulsing badge = Server is running
- Red badge = Server is stopped

### 4. **Control Your Server**
- **Start Server**: Launches Unity server on remote machine
- **Stop Server**: Gracefully stops the Unity server
- **Restart Server**: Restarts the service (useful after updates)
- **Check Ports**: Shows which ports Unity server is listening on

### 5. **Upload New Builds**
- Click "Choose File" and select your Unity Linux build
- Click "Upload to Server"
- File is transferred via SFTP to `/opt/unity-server`

## ?? New UI Features

### **Enhanced Visuals**
- ?? Pulsing green badge when server is running
- ?? Red badge when server is stopped
- Color-coded borders on status card
- Icons for better visual recognition
- Professional dark terminal output style

### **Connection Info Bar**
Shows at top of page:
- Remote Host IP
- Service name
- Game server port
- SSH port
- "Development Mode" indicator

### **Smart Button States**
- Start button disabled when server is running
- Stop/Restart buttons disabled when server is stopped
- Check Ports button disabled when server is stopped

### **Better Output Display**
- Status messages in dismissible alerts
- Port information in styled pre-formatted blocks
- Command output in terminal-style dark theme

## ?? Configuration

All settings in `appsettings.json`:

```json
{
  "ServerManagement": {
    "Host": "91.98.20.28",            // Your Linux server IP
    "Port": 22,                        // SSH port
    "Username": "root",                // SSH username
    "Password": "Mjwk3hTkWbqA",       // SSH password
    "PrivateKeyPath": "",              // Optional: Use SSH key instead
    "RemoteDeployPath": "/opt/unity-server",
    "ServiceName": "unity-server",     // systemd service name
    "UnityServerPort": 7777           // Unity game server port
  }
}
```

## ?? Security Tips for Development

1. **Don't commit sensitive credentials**
   - Add `appsettings.json` to `.gitignore`
   - Use User Secrets for sensitive data

2. **Use SSH Keys (Recommended)**
   - Generate SSH key pair
   - Copy public key to server
   - Set `PrivateKeyPath` in config
   - Leave `Password` empty

3. **For Production**
   - Deploy app to the server itself
   - Use localhost SSH connection
   - Store credentials in environment variables
   - Use HTTPS with valid certificate

## ?? Checklist

### **Development (Current Setup)**
- ? Run app locally on your PC
- ? Control remote server via SSH
- ? Good for testing and development
- ?? Keep credentials secure

### **Production (Future)**
- ?? Deploy app to Linux server
- ?? Configure systemd service for web app
- ?? Set up Nginx reverse proxy
- ?? Enable HTTPS with Let's Encrypt
- ?? Use SSH keys or environment variables

## ?? Troubleshooting

### **Can't connect to server**
- Check if SSH port 22 is open
- Verify username and password
- Test SSH connection manually: `ssh root@91.98.20.28`

### **Status shows "unknown"**
- Server might not have systemd
- Service name might be incorrect
- Check: `sudo systemctl status unity-server`

### **Buttons don't work**
- Check SSH credentials
- Ensure you have sudo rights
- Verify service is configured correctly

### **Upload fails**
- Check RemoteDeployPath exists
- Verify SFTP permissions
- Ensure enough disk space

## ?? Next Steps

Want to deploy to production? Ask me to:
1. Create deployment scripts
2. Generate systemd service file for web app
3. Configure Nginx reverse proxy
4. Set up SSL/HTTPS
5. Create automated deployment pipeline
