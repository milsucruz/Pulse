using Application.Interfaces;
using Infrastructure.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Resilience;
using Infrastructure.Senders;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, cfg) =>
    cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddOptions<RabbitMqConfiguration>()
    .BindConfiguration("RabbitMq")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

builder.Services.AddKeyedSingleton<INotificationSender, EmailSender>("email");
builder.Services.AddKeyedSingleton<INotificationSender, PushSender>("push");
builder.Services.AddNotificationSenderPolicies();

builder.Services.AddHostedService<EmailNotificationConsumer>();
builder.Services.AddHostedService<PushNotificationConsumer>();

var host = builder.Build();
host.Run();
