namespace PaymentsService.DTOs;

public class CreateAccountRequestDto
{
    public string UserId { get; set; } = null!;
}

public class DepositRequestDto
{
    public decimal Amount { get; set; }
}

public class AccountResponseDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BalanceResponseDto
{
    public string UserId { get; set; } = null!;
    public decimal Balance { get; set; }
}