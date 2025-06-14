using Microsoft.EntityFrameworkCore;
using PaymentsService.Data;
using MassTransit;
using PaymentsService.Consumers;
using PaymentsService.Services;
using PaymentsService.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.AddMassTransit(mt =>
{
    mt.AddConsumer<ProcessPaymentCommandConsumer>();

    mt.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConfig = builder.Configuration.GetSection("RabbitMQ");
        cfg.Host(rabbitMqConfig["Host"], rabbitMqConfig["VirtualHost"], h =>
        {
            h.Username(rabbitMqConfig["Username"]);
            h.Password(rabbitMqConfig["Password"]);
        });

        var queueNames = builder.Configuration.GetSection("MessageQueueNames").Get<MessageQueueConfig>();
        if (queueNames == null) throw new InvalidOperationException("Конфигурация MessageQueueNames отсутствует.");

        cfg.ReceiveEndpoint(queueNames.ProcessPaymentCommandQueue, ep =>
        {
            ep.PrefetchCount = 16;
            ep.UseMessageRetry(r => r.Interval(2, 100));
            ep.ConfigureConsumer<ProcessPaymentCommandConsumer>(context);
        });
    });
});
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddHostedService<OutboxProcessorService>(); // Для Transactional Outbox

builder.Services.Configure<MessageQueueConfig>(builder.Configuration.GetSection("MessageQueueNames"));


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PaymentsService API", Version = "v1" });
});

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PaymentsService API v1");
        c.RoutePrefix = string.Empty;
    });
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        dbContext.Database.Migrate();
    }
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
public class MessageQueueConfig
{
    public string ProcessPaymentCommandQueue { get; set; } = null!;
    public string PaymentStatusEventQueue { get; set; } = null!;
}