using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Models;
using PokemonTCG.API.Services;
using System.Security.Claims;

namespace PokemonTCG.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            IUserService userService,
            ILogger<OrdersController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var orders = await _userService.GetOrdersAsync(userId);
                
                return Ok(orders.Select(order => new
                {
                    id = order.Id,
                    total = order.Total,
                    status = order.Status,
                    createdAt = order.CreatedAt,
                    items = order.Items.Select(item => new
                    {
                        id = item.Id,
                        cardId = item.CardId,
                        name = item.CardName,
                        image = item.CardImage,
                        price = item.Price,
                        quantity = item.Quantity
                    })
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders");
                return StatusCode(500, new { message = "An error occurred while fetching orders" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                
                var items = request.Items.Select(item => new OrderItem
                {
                    CardId = item.CardId,
                    CardName = item.CardName,
                    CardImage = item.CardImage,
                    Price = item.Price,
                    Quantity = item.Quantity
                }).ToList();
                
                var order = await _userService.CreateOrderAsync(userId, request.Total, items);
                
                return Ok(new
                {
                    id = order.Id,
                    total = order.Total,
                    status = order.Status,
                    createdAt = order.CreatedAt,
                    items = order.Items.Select(item => new
                    {
                        id = item.Id,
                        cardId = item.CardId,
                        name = item.CardName,
                        image = item.CardImage,
                        price = item.Price,
                        quantity = item.Quantity
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                return StatusCode(500, new { message = "An error occurred while creating the order" });
            }
        }
    }

    public class CreateOrderRequest
    {
        public decimal Total { get; set; }
        public List<OrderItemRequest> Items { get; set; } = new List<OrderItemRequest>();
    }

    public class OrderItemRequest
    {
        public string CardId { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public string CardImage { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}