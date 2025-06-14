using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrdersService.Data;
using OrdersService.DTOs;
using OrdersService.Models;
using System.Text.Json;
using OrdersService.Contracts;
using MassTransit;

namespace OrdersService.Services;

public class OrderService : IOrderService
{
    private readonly OrderDbContext _context;
    private readonly ILogger<OrderService> _logger;
    private readonly MessageQueueConfig _queueConfig;

    public OrderService(OrderDbContext context, ILogger<OrderService> logger, IOptions<MessageQueueConfig> queueConfigOptions)
    {
        _context = context;
        _logger = logger;
        _queueConfig = queueConfigOptions.Value;
    }

    public async Task<OrderResponseDto?> CreateOrderAsync(CreateOrderRequestDto request)
    {
        var order = new Order
        {
            UserId = request.UserId,
            Amount = request.Amount,
            Description = request.Description,
            Status = OrderStatus.NEW
        };

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Создаем команду для PaymentsService
            var paymentCommand = new ProcessPaymentCommand
            {
                OrderId = order.Id,
                UserId = order.UserId,
                Amount = order.Amount
            };

            _context.OutboxMessages.Add(new OutboxMessage
            {
                CorrelationId = order.Id,
                MessageType = nameof(ProcessPaymentCommand),
                Payload = JsonSerializer.Serialize(paymentCommand),
                Destination = _queueConfig.ProcessPaymentCommandQueue
            });

            order.Status = OrderStatus.PROCESSING;
            _context.Orders.Update(order);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Заказ {OrderId} создан для UserId {UserId} и отправлен на обработку платежа.", order.Id, order.UserId);
            return MapToOrderResponseDto(order);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Ошибка при создании заказа для UserId {UserId}", request.UserId);
            return null;
        }
    }

    public async Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        return order != null ? MapToOrderResponseDto(order) : null;
    }

    public async Task<IEnumerable<OrderResponseDto>> GetOrdersByUserIdAsync(string userId)
    {
        var orders = await _context.Orders
            .Where(o => o.UserId == userId)
            .Select(order => MapToOrderResponseDtoStatic(order))
            .ToListAsync();
        return orders;
    }

    public async Task<bool> UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus, string? reason = null)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            _logger.LogWarning("Заказ {OrderId} не найден для обновления статуса.", orderId);
            return false;
        }

        order.Status = newStatus;
        if (newStatus == OrderStatus.CANCELLED && !string.IsNullOrEmpty(reason))
        {
            _logger.LogInformation("Заказ {OrderId} отменен. Причина: {Reason}", orderId, reason);
        }

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Статус заказа {OrderId} обновлен на {NewStatus}", orderId, newStatus);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обновлении статуса заказа {OrderId}", orderId);
            return false;
        }
    }

    private static OrderResponseDto MapToOrderResponseDtoStatic(Order order)
    {
        return new OrderResponseDto
        {
            Id = order.Id,
            UserId = order.UserId,
            Amount = order.Amount,
            Description = order.Description,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }

    private OrderResponseDto? MapToOrderResponseDto(Order order)
    {
        if (order == null) return null;
        return new OrderResponseDto
        {
            Id = order.Id,
            UserId = order.UserId,
            Amount = order.Amount,
            Description = order.Description,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }
}