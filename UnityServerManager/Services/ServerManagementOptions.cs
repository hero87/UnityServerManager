namespace UnityServerManager.Web.Services;

public class ServerManagementOptions
{
    public string Host { get; set; } = "162.55.39.146"; /*"91.98.20.28";*/
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string RemoteDeployPath { get; set; } = "/root";
    public string ServiceName { get; set; } = "unity-server";
    public int UnityServerPort { get; set; } = 7777;
}
