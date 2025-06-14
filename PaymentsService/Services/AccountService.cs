using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentsService.Data;
using PaymentsService.DTOs;
using PaymentsService.Models;
using System.Text.Json;
using MassTransit;

namespace PaymentsService.Services;

public class AccountService : IAccountService
{
    private readonly PaymentDbContext _context;
    private readonly ILogger<AccountService> _logger;
    private readonly MessageQueueConfig _queueConfig;


    public AccountService(PaymentDbContext context, ILogger<AccountService> logger, IOptions<MessageQueueConfig> queueConfigOptions)
    {
        _context = context;
        _logger = logger;
        _queueConfig = queueConfigOptions.Value;
    }

    public async Task<AccountResponseDto?> CreateAccountAsync(string userId)
    {
        if (await _context.Accounts.AnyAsync(a => a.UserId == userId))
        {
            _logger.LogWarning("Счет для UserId: {UserId} уже существует", userId);
            return null;
        }

        var account = new Account
        {
            UserId = userId,
            Balance = 0m
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Счет создан для UserId: {UserId}, AccountId: {AccountId}", userId, account.Id);
        return MapToAccountResponseDto(account);
    }

    public async Task<AccountResponseDto?> DepositAsync(string userId, decimal amount)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Сумма пополнения должна быть положительной. UserId: {UserId}, Amount: {Amount}", userId, amount);
            return null;
        }

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null)
        {
            _logger.LogWarning("Счет для пополнения не найден. UserId: {UserId}", userId);
            return null;
        }

        account.Balance += amount;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Пополнено {Amount} для UserId: {UserId}. Новый баланс: {Balance}", amount, userId, account.Balance);
            return MapToAccountResponseDto(account);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Конфликт параллельного доступа при пополнении для UserId: {UserId}. Может потребоваться механизм повтора.", userId);
            
            throw;
        }
    }

    public async Task<BalanceResponseDto?> GetBalanceAsync(string userId)
    {
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null)
        {
            _logger.LogWarning("Счет для проверки баланса не найден. UserId: {UserId}", userId);
            return null;
        }
        return new BalanceResponseDto { UserId = account.UserId, Balance = account.Balance };
    }

    public async Task<(bool success, string? reason)> ProcessPaymentAsync(string userId, decimal amount, Guid orderId, Guid messageId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var existingInboxMessage = await _context.InboxMessages.FindAsync(messageId);
            if (existingInboxMessage != null && existingInboxMessage.ProcessedAt.HasValue)
            {
                _logger.LogInformation("MessageId {MessageId} (OrderId: {OrderId}) уже обработано. Пропуск.", messageId, orderId);
                await transaction.CommitAsync();
                return (true, "Уже обработано");
            }

            if (existingInboxMessage == null)
            {
                _context.InboxMessages.Add(new InboxMessage
                {
                    MessageId = messageId,
                    MessageType = "ProcessPaymentCommand",
                    ReceivedAt = DateTime.UtcNow
                });
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
            bool paymentSuccessful = false;
            string? failureReason = null;

            if (account == null)
            {
                failureReason = "Счет не найден.";
                _logger.LogWarning("Платеж не удался для OrderId {OrderId}: Счет не найден для UserId {UserId}", orderId, userId);
            }
            else if (account.Balance < amount)
            {
                failureReason = "Недостаточно средств.";
                _logger.LogWarning("Платеж не удался для OrderId {OrderId}: Недостаточно средств для UserId {UserId}. Баланс: {Balance}, Требуется: {Amount}", orderId, userId, account.Balance, amount);
            }
            else
            {
                account.Balance -= amount;
                paymentSuccessful = true;
                _logger.LogInformation("Платеж успешен для OrderId {OrderId}, UserId {UserId}. Списано {Amount}. Новый баланс: {Balance}", orderId, userId, amount, account.Balance);
            }

            var paymentStatusEvent = new PaymentStatusEvent
            {
                EventId = NewId.NextGuid(),
                OrderId = orderId,
                UserId = userId,
                IsSuccess = paymentSuccessful,
                Reason = failureReason,
                ProcessedAt = DateTime.UtcNow
            };

            _context.OutboxMessages.Add(new OutboxMessage
            {
                CorrelationId = orderId,
                MessageType = nameof(PaymentStatusEvent),
                Payload = JsonSerializer.Serialize(paymentStatusEvent),
                Destination = _queueConfig.PaymentStatusEventQueue
            });

            if (existingInboxMessage == null)
            {
                var inboxMsgToUpdate = await _context.InboxMessages.FindAsync(messageId);
                if (inboxMsgToUpdate != null) inboxMsgToUpdate.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                existingInboxMessage.ProcessedAt = DateTime.UtcNow;
            }


            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (paymentSuccessful, failureReason);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Конфликт параллельного доступа при обработке платежа для OrderId: {OrderId}, UserId: {UserId}. Обработка платежа будет повторена очередью сообщений.", orderId, userId);
            
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Ошибка при обработке платежа для OrderId: {OrderId}, UserId: {UserId}", orderId, userId);
            var inboxMsgToUpdate = await _context.InboxMessages.FindAsync(messageId);
            if (inboxMsgToUpdate != null && !inboxMsgToUpdate.ProcessedAt.HasValue)
            {
                inboxMsgToUpdate.ProcessingError = ex.Message.Substring(0, Math.Min(ex.Message.Length, 1000));
                inboxMsgToUpdate.ProcessedAt = DateTime.UtcNow;
                try
                {
                    using var errorTransaction = await _context.Database.BeginTransactionAsync();
                    await _context.SaveChangesAsync();
                    await errorTransaction.CommitAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Не удалось пометить сообщение inbox {MessageId} как ошибочное.", messageId);
                }
            }
            throw;
        }
    }

    private AccountResponseDto MapToAccountResponseDto(Account account) => new()
    {
        Id = account.Id,
        UserId = account.UserId,
        Balance = account.Balance,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt
    };
}

public class PaymentStatusEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public DateTime ProcessedAt { get; set; }
}