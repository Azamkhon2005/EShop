using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using OrdersService.Models;
using OrdersService.Contracts;
using System.Text.Json;

namespace OrdersService.Services;

public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public OutboxProcessorService(IServiceProvider serviceProvider, ILogger<OutboxProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Сервис обработки Outbox для OrdersService запускается.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле OutboxProcessorService для OrdersService.");
            }
            await Task.Delay(_pollingInterval, stoppingToken);
        }
        _logger.LogInformation("Сервис обработки Outbox для OrdersService останавливается.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messagesToProcess = await dbContext.OutboxMessages
            .Where(om => om.SentAt == null)
            .OrderBy(om => om.CreatedAt)
            .Take(50)
            .ToListAsync(stoppingToken);

        if (!messagesToProcess.Any()) return;

        _logger.LogInformation("Найдено {Count} сообщений в outbox OrdersService для обработки.", messagesToProcess.Count);

        foreach (var outboxMessage in messagesToProcess)
        {
            if (stoppingToken.IsCancellationRequested) break;
            try
            {
                object? messagePayload = null;
                if (outboxMessage.MessageType == nameof(ProcessPaymentCommand))
                {
                    messagePayload = JsonSerializer.Deserialize<ProcessPaymentCommand>(outboxMessage.Payload);
                }
                else
                {
                    _logger.LogWarning("Неизвестный тип сообщения в outbox OrdersService: {MessageType}, Id: {Id}. Пропуск.", outboxMessage.MessageType, outboxMessage.Id);
                    outboxMessage.SentAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    continue;
                }

                if (messagePayload == null)
                {
                    _logger.LogError("Не удалось десериализовать полезную нагрузку для сообщения outbox OrdersService Id: {Id}.", outboxMessage.Id);
                    outboxMessage.SentAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    continue;
                }

                var endpointUri = new Uri($"queue:{outboxMessage.Destination}");
                var sendEndpoint = await bus.GetSendEndpoint(endpointUri);

                await sendEndpoint.Send(messagePayload, messagePayload.GetType(), ctx =>
                {
                    ctx.CorrelationId = outboxMessage.CorrelationId;
                }, stoppingToken);

                outboxMessage.SentAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Успешно отправлено сообщение (команда) Id: {OutboxId}, Type: {Type}, CorrelationId: {CorrelationId} в {Destination} из OrdersService.",
                    outboxMessage.Id, outboxMessage.MessageType, outboxMessage.CorrelationId, outboxMessage.Destination);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось отправить сообщение outbox OrdersService Id: {Id}. Будет повторено.", outboxMessage.Id);
            }
        }
    }
}