using ClamAVGuardian.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ClamAVGuardianService";
});
builder.Services.AddHostedService<GuardianWorker>();

var host = builder.Build();
host.Run();
