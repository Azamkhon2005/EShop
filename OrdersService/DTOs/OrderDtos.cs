using OrdersService.Models;
using System.ComponentModel.DataAnnotations;

namespace OrdersService.DTOs;

public class CreateOrderRequestDto
{
    [Required]
    public string UserId { get; set; } = null!;
    [Required]
    [Range(0.01, (double)decimal.MaxValue)]
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}

public class OrderResponseDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}