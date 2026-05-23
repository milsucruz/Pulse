using Infrastructure.Messaging;
using Infrastructure.Resilience;
using Infrastructure.Senders;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqConfiguration>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<EmailSender>();
builder.Services.AddNotificationSenderPolicies();

builder.Services.AddHostedService<EmailNotificationConsumer>();

var host = builder.Build();
host.Run();
