using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentsService.DTOs;
using PaymentsService.Services;

namespace PaymentsService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(IAccountService accountService, ILogger<AccountsController> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Попытка создать счет для UserId: {UserId}", request.UserId);

        var account = await _accountService.CreateAccountAsync(request.UserId);
        if (account == null)
        {
            _logger.LogWarning("Создание счета не удалось, UserId {UserId} может уже существовать.", request.UserId);
            return Conflict(new { message = $"Счет для UserId '{request.UserId}' уже существует." });
        }
        return CreatedAtAction(nameof(GetBalance), new { userId = account.UserId }, account);
    }

    [HttpPost("{userId}/deposit")]
    [ProducesResponseType(typeof(AccountResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deposit(string userId, [FromBody] DepositRequestDto request)
    {
        if (request.Amount <= 0)
        {
            ModelState.AddModelError(nameof(request.Amount), "Сумма пополнения должна быть положительной.");
        }
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Попытка пополнить на {Amount} для UserId: {UserId}", request.Amount, userId);
        try
        {
            var account = await _accountService.DepositAsync(userId, request.Amount);
            if (account == null)
            {
                _logger.LogWarning("Пополнение не удалось, счет не найден для UserId: {UserId}", userId);
                return NotFound(new { message = $"Счет для UserId '{userId}' не найден." });
            }
            return Ok(account);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Проблема параллельного доступа при пополнении для UserId: {UserId}. Пожалуйста, повторите попытку.", userId);
            return Conflict(new { message = "Не удалось обновить баланс из-за одновременного изменения. Пожалуйста, попробуйте еще раз." });
        }
    }

    [HttpGet("{userId}/balance")]
    [ProducesResponseType(typeof(BalanceResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance(string userId)
    {
        _logger.LogInformation("Получение баланса для UserId: {UserId}", userId);
        var balance = await _accountService.GetBalanceAsync(userId);
        if (balance == null)
        {
            _logger.LogWarning("Проверка баланса не удалась, счет не найден для UserId: {UserId}", userId);
            return NotFound(new { message = $"Счет для UserId '{userId}' не найден." });
        }
        return Ok(balance);
    }
}