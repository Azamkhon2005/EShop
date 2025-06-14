namespace PaymentsService.Contracts;

public class ProcessPaymentCommand
{
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
    public decimal Amount { get; set; }
}