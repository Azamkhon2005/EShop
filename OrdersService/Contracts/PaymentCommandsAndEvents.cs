namespace OrdersService.Contracts;

public class ProcessPaymentCommand
{
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
    public decimal Amount { get; set; }
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