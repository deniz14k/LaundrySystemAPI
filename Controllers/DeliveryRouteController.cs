using ApiSpalatorie.Data;
using ApiSpalatorie.Helpers;
using ApiSpalatorie.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiSpalatorie.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class DeliveryRouteController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly GoogleMapsSettings _maps;
        private readonly HttpClient _httpClient;

        public DeliveryRouteController(ApplicationDbContext db, IOptions<GoogleMapsSettings> maps)
        {
            _db = db;
            _maps = maps.Value;
            _httpClient = new HttpClient();
        }

        // GET: api/deliveryroute
        [HttpGet]
        public async Task<IActionResult> GetAllRoutes()
        {
            var routes = await _db.DeliveryRoutes
                .Include(r => r.Orders)
                    .ThenInclude(ro => ro.Order)
                .Select(r => new
                {
                    r.Id,
                    r.DriverName,
                    r.CreatedAt,
                    Orders = r.Orders.OrderBy(o => o.StopIndex).Select(o => new
                    {
                        o.OrderId,
                        o.StopIndex,
                        o.IsCompleted,
                        o.Order.DeliveryAddress,
                        o.Order.CustomerId,
                        o.Order.TelephoneNumber,
                        TotalPrice = o.Order.Items.Sum(i => i.Price)
                    })
                })
                .ToListAsync();

            return Ok(routes);
        }

        // GET: api/deliveryroute/eligible-orders
        [HttpGet("eligible-orders")]
        public async Task<IActionResult> GetEligibleOrders()
        {
            var orders = await _db.Orders
                .Where(o =>
                    o.ServiceType == "PickupDelivery" &&
                    o.DeliveryLatitude != null &&
                    o.DeliveryLongitude != null &&
                    !_db.DeliveryRouteOrders.Any(ro => ro.OrderId == o.Id)
                )
                .Select(o => new
                {
                    o.Id,
                    o.CustomerId,
                    o.TelephoneNumber,
                    o.DeliveryAddress,
                    o.DeliveryLatitude,
                    o.DeliveryLongitude,
                    o.Status,
                    o.ReceivedDate
                })
                .ToListAsync();

            return Ok(orders);
        }

        // PATCH: api/deliveryroute/{routeId}/complete/{orderId}
        [HttpPatch("{routeId}/complete/{orderId}")]
        public async Task<IActionResult> MarkOrderAsCompleted(int routeId, int orderId)
        {
            var routeOrder = await _db.DeliveryRouteOrders
                .FirstOrDefaultAsync(ro => ro.DeliveryRouteId == routeId && ro.OrderId == orderId);

            if (routeOrder == null)
                return NotFound("Order not found in this route.");

            if (routeOrder.IsCompleted)
                return BadRequest("Order is already marked as completed.");

            routeOrder.IsCompleted = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Order marked as completed." });
        }

        // GET: api/deliveryroute/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetRouteWithOrders(int id)
        {
            var routeEntity = await _db.DeliveryRoutes
                .Include(r => r.Orders)
                    .ThenInclude(ro => ro.Order)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (routeEntity == null)
                return NotFound();

            // Build coordinates list by StopIndex ascending
            var orderedRouteOrders = routeEntity.Orders.OrderBy(ro => ro.StopIndex).ToList();
            var coords = orderedRouteOrders
                .Select(ro => (ro.Order.DeliveryLatitude!.Value, ro.Order.DeliveryLongitude!.Value))
                .ToList();

            // Compute polyline via Google Routes API
            var routeResponse = await ComputeRouteAsync(coords);
            var encodedPolyline = routeResponse.routes?.FirstOrDefault()?.polyline?.encodedPolyline;

            // Prepare DTO
            var result = new
            {
                routeId = routeEntity.Id,
                driverName = routeEntity.DriverName,
                createdAt = routeEntity.CreatedAt,
                polyline = encodedPolyline,
                orders = orderedRouteOrders.Select(ro => new
                {
                    id = ro.Order.Id,
                    index = ro.StopIndex,
                    address = ro.Order.DeliveryAddress,
                    customer = ro.Order.CustomerId,
                    phone = ro.Order.TelephoneNumber,
                    price = ro.Order.Items.Sum(i => i.Price),
                    lat = ro.Order.DeliveryLatitude,
                    lng = ro.Order.DeliveryLongitude,
                    isCompleted = ro.IsCompleted
                })
            };

            return Ok(result);
        }

        // Helper: call Google Routes API to get optimized polyline
        private async Task<RouteResponse> ComputeRouteAsync(List<(double lat, double lng)> waypoints)
        {
            var url = "https://routes.googleapis.com/directions/v2:computeRoutes";
            var origin = new
            {
                location = new { latLng = new { latitude = waypoints.First().lat, longitude = waypoints.First().lng } }
            };
            var destination = new
            {
                location = new { latLng = new { latitude = waypoints.Last().lat, longitude = waypoints.Last().lng } }
            };
            var intermediates = waypoints.Count > 2
                ? waypoints.Skip(1).SkipLast(1)
                    .Select(p => new { location = new { latLng = new { latitude = p.lat, longitude = p.lng } } })
                    .ToList()
                : null;
            var payload = new { origin, destination, travelMode = "DRIVE", routingPreference = "TRAFFIC_AWARE", intermediates };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", _maps.ApiKey);
            _httpClient.DefaultRequestHeaders.Add("X-Goog-FieldMask", "*");

            var response = await _httpClient.PostAsJsonAsync(url, payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RouteResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}
