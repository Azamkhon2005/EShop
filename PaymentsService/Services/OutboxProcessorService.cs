using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentsService.Data;
using PaymentsService.Models;
using System.Text.Json;

namespace PaymentsService.Services;

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
        _logger.LogInformation("Сервис обработки Outbox запускается.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла ошибка в цикле выполнения сервиса обработки Outbox.");
            }
            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Сервис обработки Outbox останавливается.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messagesToProcess = await dbContext.OutboxMessages
            .Where(om => om.SentAt == null)
            .OrderBy(om => om.CreatedAt)
            .Take(50)
            .ToListAsync(stoppingToken);

        if (!messagesToProcess.Any())
        {
            return;
        }

        _logger.LogInformation("Найдено {Count} сообщений в outbox для обработки.", messagesToProcess.Count);

        foreach (var outboxMessage in messagesToProcess)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                object? messagePayload = null;
                if (outboxMessage.MessageType == nameof(PaymentStatusEvent))
                {
                    messagePayload = JsonSerializer.Deserialize<PaymentStatusEvent>(outboxMessage.Payload);
                }
                else
                {
                    _logger.LogWarning("Неизвестный тип сообщения в outbox: {MessageType}, Id: {Id}. Пропуск.", outboxMessage.MessageType, outboxMessage.Id);
                    
                    outboxMessage.SentAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    continue;
                }

                if (messagePayload == null)
                {
                    _logger.LogError("Не удалось десериализовать полезную нагрузку для сообщения outbox Id: {Id}. Полезная нагрузка: {Payload}", outboxMessage.Id, outboxMessage.Payload);
                    outboxMessage.SentAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    continue;
                }

                var endpointUri = new Uri($"queue:{outboxMessage.Destination}");
                var sendEndpoint = await bus.GetSendEndpoint(endpointUri);

                await sendEndpoint.Send(messagePayload, messagePayload.GetType(), ctx =>
                {
                    ctx.CorrelationId = outboxMessage.CorrelationId;
                    if (messagePayload is PaymentStatusEvent pse)
                    {
                        ctx.MessageId = pse.EventId;
                    }

                }, stoppingToken);

                outboxMessage.SentAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Успешно отправлено сообщение Id: {OutboxId}, Type: {Type}, CorrelationId: {CorrelationId} в {Destination}",
                    outboxMessage.Id, outboxMessage.MessageType, outboxMessage.CorrelationId, outboxMessage.Destination);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось отправить сообщение outbox Id: {Id}. Оно будет повторено.", outboxMessage.Id);
            }
        }
    }
}