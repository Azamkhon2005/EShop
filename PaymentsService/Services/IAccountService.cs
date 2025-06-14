using PaymentsService.Models;
using PaymentsService.DTOs;

namespace PaymentsService.Services;

public interface IAccountService
{
    Task<AccountResponseDto?> CreateAccountAsync(string userId);
    Task<AccountResponseDto?> DepositAsync(string userId, decimal amount);
    Task<BalanceResponseDto?> GetBalanceAsync(string userId);
    Task<(bool success, string? reason)> ProcessPaymentAsync(string userId, decimal amount, Guid orderId, Guid messageId);
}