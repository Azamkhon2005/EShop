using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentsService.Contracts;
using PaymentsService.Services;

namespace PaymentsService.Consumers;

public class ProcessPaymentCommandConsumer : IConsumer<ProcessPaymentCommand>
{
    private readonly ILogger<ProcessPaymentCommandConsumer> _logger;
    private readonly IAccountService _accountService;

    public ProcessPaymentCommandConsumer(ILogger<ProcessPaymentCommandConsumer> logger, IAccountService accountService)
    {
        _logger = logger;
        _accountService = accountService;
    }

    public async Task Consume(ConsumeContext<ProcessPaymentCommand> context)
    {
        var command = context.Message;
        _logger.LogInformation("Получена команда ProcessPaymentCommand для OrderId: {OrderId}, UserId: {UserId}, Amount: {Amount}, MessageId: {MessageId}",
            command.OrderId, command.UserId, command.Amount, context.MessageId);

        if (context.MessageId is null)
        {
            _logger.LogError("MessageId is null для ProcessPaymentCommand OrderId: {OrderId}. Этого не должно происходить.", command.OrderId);
            throw new InvalidOperationException("MessageId не может быть null.");
        }

        try
        {
            var (success, reason) = await _accountService.ProcessPaymentAsync(
                command.UserId,
                command.Amount,
                command.OrderId,
                context.MessageId.Value
            );

            if (success)
            {
                _logger.LogInformation("Обработка платежа для OrderId {OrderId} успешно завершена (или уже была обработана).", command.OrderId);
            }
            else
            {
                _logger.LogWarning("Обработка платежа для OrderId {OrderId} не удалась. Причина: {Reason}", command.OrderId, reason);
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Исключение параллельного доступа при обработке платежа для OrderId: {OrderId}. Сообщение будет повторено.", command.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Необработанное исключение при обработке платежа для OrderId: {OrderId}. Сообщение будет повторено или отправлено в dead-letter.", command.OrderId);
            throw;
        }
    }
}