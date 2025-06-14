using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using MassTransit;
using OrdersService.Services;
using OrdersService.Models;
using OrdersService.Consumers;
using OrdersService.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));

builder.Services.AddMassTransit(mt =>
{
    mt.AddConsumer<PaymentStatusEventConsumer>();

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

        cfg.ReceiveEndpoint(queueNames.PaymentStatusEventQueue, ep =>
        {
            ep.PrefetchCount = 16;
            ep.UseMessageRetry(r => r.Interval(2, 100));
            ep.ConfigureConsumer<PaymentStatusEventConsumer>(context);
        });
    });
});

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddHostedService<OutboxProcessorService>();

builder.Services.Configure<MessageQueueConfig>(builder.Configuration.GetSection("MessageQueueNames"));


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "OrdersService API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrdersService API v1");
        c.RoutePrefix = string.Empty;
    });

    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
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