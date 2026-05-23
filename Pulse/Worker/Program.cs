using Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<EmailNotificationConsumer>();

var host = builder.Build();
host.Run();
