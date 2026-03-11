using UnityServerManager.Web.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to allow large file uploads (2GB limit)
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 2_147_483_648; // 2 GB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30); // Keep connection alive
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// Configure form options for large file uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 2_147_483_648; // 2 GB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
    options.BufferBody = false; // Don't buffer large files in memory
    options.MemoryBufferThreshold = 1024 * 1024 * 10; // 10MB threshold
});

builder.Services.Configure<ServerManagementOptions>(builder.Configuration.GetSection("ServerManagement"));
builder.Services.AddSingleton<LinuxServerManagerService>();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
