using Infrastructure.Messaging;
using Infrastructure.Resilience;
using Infrastructure.Senders;
using Serilog;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, cfg) =>
    cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddOptions<RabbitMqConfiguration>()
    .BindConfiguration("RabbitMq")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddKeyedSingleton<INotificationSender, EmailSender>("email");
builder.Services.AddKeyedSingleton<INotificationSender, PushSender>("push");
builder.Services.AddNotificationSenderPolicies();

builder.Services.AddHostedService<EmailNotificationConsumer>();
builder.Services.AddHostedService<PushNotificationConsumer>();

var host = builder.Build();
host.Run();
