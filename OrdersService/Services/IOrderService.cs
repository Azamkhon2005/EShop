using OrdersService.DTOs;
using OrdersService.Models;

namespace OrdersService.Services;

public interface IOrderService
{
    Task<OrderResponseDto?> CreateOrderAsync(CreateOrderRequestDto request);
    Task<IEnumerable<OrderResponseDto>> GetOrdersByUserIdAsync(string userId);
    Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId);
    Task<bool> UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus, string? reason = null);
}