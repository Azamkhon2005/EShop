using Microsoft.AspNetCore.Mvc;
using OrdersService.DTOs;
using OrdersService.Services;

namespace OrdersService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        _logger.LogInformation("Попытка создать заказ для UserId: {UserId}", request.UserId);
        var order = await _orderService.CreateOrderAsync(request);
        if (order == null)
        {
            _logger.LogError("Не удалось создать заказ для UserId: {UserId}", request.UserId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Не удалось создать заказ.");
        }
        return CreatedAtAction(nameof(GetOrderById), new { orderId = order.Id }, order);
    }

    [HttpGet("user/{userId}")]
    [ProducesResponseType(typeof(IEnumerable<OrderResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrdersByUserId(string userId)
    {
        _logger.LogInformation("Получение заказов для UserId: {UserId}", userId);
        var orders = await _orderService.GetOrdersByUserIdAsync(userId);
        return Ok(orders);
    }

    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid orderId)
    {
        _logger.LogInformation("Получение заказа по Id: {OrderId}", orderId);
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("Заказ {OrderId} не найден.", orderId);
            return NotFound();
        }
        return Ok(order);
    }

    [HttpGet("{orderId}/status")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderStatus(Guid orderId)
    {
        _logger.LogInformation("Получение статуса заказа: {OrderId}", orderId);
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("Заказ {OrderId} для проверки статуса не найден.", orderId);
            return NotFound();
        }
        return Ok(order.Status);
    }
}