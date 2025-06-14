using MassTransit;
using OrdersService.Contracts;
using OrdersService.Models;
using OrdersService.Services;

namespace OrdersService.Consumers;

public class PaymentStatusEventConsumer : IConsumer<PaymentStatusEvent>
{
    private readonly ILogger<PaymentStatusEventConsumer> _logger;
    private readonly IOrderService _orderService;

    public PaymentStatusEventConsumer(ILogger<PaymentStatusEventConsumer> logger, IOrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
    }

    public async Task Consume(ConsumeContext<PaymentStatusEvent> context)
    {
        var paymentEvent = context.Message;
        _logger.LogInformation(
            "Получено событие PaymentStatusEvent для OrderId: {OrderId}, UserId: {UserId}, Success: {IsSuccess}, MessageId: {MessageId}",
            paymentEvent.OrderId, paymentEvent.UserId, paymentEvent.IsSuccess, context.MessageId);
        var newStatus = paymentEvent.IsSuccess ? OrderStatus.FINISHED : OrderStatus.CANCELLED;

        bool updateResult = await _orderService.UpdateOrderStatusAsync(paymentEvent.OrderId, newStatus, paymentEvent.Reason);

        if (updateResult)
        {
            _logger.LogInformation("Статус заказа {OrderId} успешно обновлен на {NewStatus} на основе PaymentStatusEvent.",
                paymentEvent.OrderId, newStatus);
        }
        else
        {
            _logger.LogError(
                "Не удалось обновить статус заказа {OrderId} на {NewStatus} на основе PaymentStatusEvent. Возможно, заказ не найден или произошла ошибка при сохранении.",
                paymentEvent.OrderId, newStatus);
        }
    }
}