using ApiSpalatorie.Data;
using ApiSpalatorie.Helpers;
using ApiSpalatorie.Models;
using ApiSpalatorie.Models.DTOs;
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
    public class PlannerController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly GoogleMapsSettings _maps;

        public PlannerController(ApplicationDbContext db, IOptions<GoogleMapsSettings> maps)
        {
            _db = db;
            _maps = maps.Value;
        }

        // Helper pentru Google Routes API
        private async Task<RouteResponse> GetOptimizedRoute(List<(double lat, double lng)> waypoints)
        {
            var url = "https://routes.googleapis.com/directions/v2:computeRoutes";
            using var client = new HttpClient();

            var origin = new { location = new { latLng = new { latitude = waypoints.First().lat, longitude = waypoints.First().lng } } };
            var destination = new { location = new { latLng = new { latitude = waypoints.Last().lat, longitude = waypoints.Last().lng } } };
            var intermediates = waypoints.Count > 2
                ? waypoints.Skip(1).SkipLast(1)
                    .Select(p => new { location = new { latLng = new { latitude = p.lat, longitude = p.lng } } })
                    .ToList()
                : null;

            var payload = new
            {
                origin,
                destination,
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                intermediates
            };

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("X-Goog-Api-Key", _maps.ApiKey);
            client.DefaultRequestHeaders.Add("X-Goog-FieldMask", "*");

            var response = await client.PostAsJsonAsync(url, payload);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Google API error: {raw}");

            return JsonSerializer.Deserialize<RouteResponse>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }

        /// <summary>
        /// 1️⃣ Manual: creare rută pe comenzi selectate
        /// POST api/planner/create-route
        /// </summary>
        [HttpPost("create-route")]
        public async Task<IActionResult> CreateRoute([FromBody] CreateRouteRequest request)
        {
            if (request.OrderIds == null || request.OrderIds.Count < 2)
                return BadRequest("At least two orders are required.");

            // verifică comenzi deja asignate
            var used = await _db.DeliveryRouteOrders
                .Where(o => request.OrderIds.Contains(o.OrderId))
                .Select(o => o.OrderId)
                .ToListAsync();
            if (used.Any())
                return BadRequest($"Already in routes: {string.Join(',', used)}");

            var orders = await _db.Orders
                .Where(o => request.OrderIds.Contains(o.Id))
                .ToListAsync();
            if (orders.Count != request.OrderIds.Count)
                return NotFound("Some orders not found.");

            var route = new DeliveryRoute
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User.Identity?.Name,
                DriverName = request.DriverName
            };
            _db.DeliveryRoutes.Add(route);
            await _db.SaveChangesAsync();

            // Persistare fără optimizare TSP (ordinea intrării)
            for (int i = 0; i < orders.Count; i++)
            {
                _db.DeliveryRouteOrders.Add(new DeliveryRouteOrder
                {
                    DeliveryRouteId = route.Id,
                    OrderId = orders[i].Id,
                    StopIndex = i + 1,
                    IsCompleted = false
                });
            }
            await _db.SaveChangesAsync();

            return Ok(new { route.Id, route.DriverName });
        }

        /// <summary>
        /// 2️⃣ Auto: generare rută pentru o dată specifică
        /// POST api/planner/auto-generate
        /// </summary>
        [HttpPost("auto-generate")]
        public async Task<IActionResult> AutoGenerate([FromBody] AutoGenerateRouteRequest req)
        {
            // preia comenzile eligibile pentru acea dată
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == req.Date.Date
                            && o.ServiceType == "PickupDelivery"
                            && !_db.DeliveryRouteOrders.Any(r => r.OrderId == o.Id))
                .ToListAsync();
            if (orders.Count < 2)
                return BadRequest("Need at least 2 eligible orders.");

            // coordonate pentru algoritm
            var coords = orders.Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value)).ToList();

            // optimizare TSP locală
            var orderIdx = TspRouteOptimizer.SolveTsp(coords);
            var ordered = orderIdx.Select(i => orders[i]).ToList();

            // apel Google Routes doar pentru polyline
            var routeResp = await GetOptimizedRoute(coords);
            var encoded = routeResp.routes?.FirstOrDefault()?.polyline?.encodedPolyline;

            // creare DeliveryRoute
            var route = new DeliveryRoute
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User.Identity?.Name,
                DriverName = req.DriverName
            };
            _db.DeliveryRoutes.Add(route);
            await _db.SaveChangesAsync();

            // save ordine optimizate
            for (int i = 0; i < ordered.Count; i++)
            {
                _db.DeliveryRouteOrders.Add(new DeliveryRouteOrder
                {
                    DeliveryRouteId = route.Id,
                    OrderId = ordered[i].Id,
                    StopIndex = i + 1,
                    IsCompleted = false
                });
            }
            await _db.SaveChangesAsync();

            return Ok(new { route.Id, polyline = encoded });
        }

        /// <summary>
        /// 3️⃣ Preview rută pe hartă (fără salvare) pentru orice dată
        /// GET api/planner/route?date=YYYY-MM-DD
        /// </summary>
        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(DateTime date)
        {
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == date.Date && o.ServiceType == "PickupDelivery")
                .OrderBy(o => o.DeliveryAddress)
                .ToListAsync();
            if (orders.Count < 2)
                return BadRequest("Need at least 2 addresses to create a route.");

            var coords = orders.Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value)).ToList();
            var idxs = TspRouteOptimizer.SolveTsp(coords);
            var ordered = idxs.Select(i => orders[i]).ToList();

            var routeData = await GetOptimizedRoute(coords);
            var route = routeData.routes?.FirstOrDefault();
            if (route == null)
                return BadRequest("No route found from Google Maps API.");

            var totalPrice = ordered.Sum(o => o.Items.Sum(i => i.Price));
            return Ok(new
            {
                route,
                totalPrice,
                orders = ordered.Select((o, i) => new
                {
                    index = i + 1,
                    id = o.Id,
                    lat = o.DeliveryLatitude,
                    lng = o.DeliveryLongitude,
                    address = o.DeliveryAddress,
                    phone = o.TelephoneNumber,
                    customer = o.CustomerId,
                    price = o.Items.Sum(it => it.Price)
                })
            });
        }
    }

    public class AutoGenerateRouteRequest
    {
        public DateTime Date { get; set; }
        public string? DriverName { get; set; }
    }
}
