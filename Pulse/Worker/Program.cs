using Infrastructure.Messaging;
using Infrastructure.Resilience;
using Infrastructure.Senders;
using Serilog;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, cfg) =>
    cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.Configure<RabbitMqConfiguration>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<PushSender>();
builder.Services.AddNotificationSenderPolicies();

builder.Services.AddHostedService<EmailNotificationConsumer>();
builder.Services.AddHostedService<PushNotificationConsumer>();

var host = builder.Build();
host.Run();
