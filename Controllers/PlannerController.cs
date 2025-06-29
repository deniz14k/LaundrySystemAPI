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


        private readonly (double lat, double lng) Headquarters = (46.517151, 24.5223398);

        //  ————————————————
        //  Helper: call Google Routes API
        //  ————————————————
        private async Task<RouteResponse> GetOptimizedRoute(List<(double lat, double lng)> waypoints)
        {
            var url = "https://routes.googleapis.com/directions/v2:computeRoutes";
            using var client = new HttpClient();

            var payload = new
            {
                origin = new { location = new { latLng = new { latitude = waypoints.First().lat, longitude = waypoints.First().lng } } },
                destination = new { location = new { latLng = new { latitude = waypoints.Last().lat, longitude = waypoints.Last().lng } } },
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                intermediates = waypoints.Count > 2
                                  ? waypoints.Skip(1).SkipLast(1)
                                      .Select(p => new { location = new { latLng = new { latitude = p.lat, longitude = p.lng } } })
                                      .ToList()
                                  : null
            };

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("X-Goog-Api-Key", _maps.ApiKey);
            client.DefaultRequestHeaders.Add("X-Goog-FieldMask", "*");

            var resp = await client.PostAsJsonAsync(url, payload);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Google API error: {raw}");

            return JsonSerializer.Deserialize<RouteResponse>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }

        //  ————————————————
        //  1️⃣ Manual: create route on chosen order IDs (no HQ)
        //  POST api/planner/create-route
        //  ————————————————
        [HttpPost("create-route")]
        public async Task<IActionResult> CreateRoute([FromBody] CreateRouteRequest request)
        {
            if (request.OrderIds == null || request.OrderIds.Count < 2)
                return BadRequest("At least two orders are required.");

            var used = await _db.DeliveryRouteOrders
                .Where(o => request.OrderIds.Contains(o.OrderId))
                .Select(o => o.OrderId)
                .ToListAsync();
            if (used.Any())
                return BadRequest($"Orders already in routes: {string.Join(',', used)}");

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

        //  ————————————————
        //  2️⃣ Auto: generate & persist route for a specific date, including HQ start/end
        //  POST api/planner/auto-generate
        //  ————————————————
        [HttpPost("auto-generate")]
        public async Task<IActionResult> AutoGenerate([FromBody] AutoGenerateRouteRequest req)
        {
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == req.Date.Date
                            && o.ServiceType == "PickupDelivery"
                            && !_db.DeliveryRouteOrders.Any(r => r.OrderId == o.Id))
                .ToListAsync();

            if (orders.Count < 2)
                return BadRequest("Need at least 2 eligible orders.");

            // HQ + stops + HQ
            var coords = new List<(double lat, double lng)> { Headquarters };
            coords.AddRange(orders.Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value)));
            coords.Add(Headquarters);

            // optimize TSP local
            var orderIdx = TspRouteOptimizer.SolveTsp(coords.Skip(1).SkipLast(1).ToList());
            var ordered = orderIdx.Select(i => orders[i]).ToList();

            // Google polyline
            var routeResp = await GetOptimizedRoute(coords);
            var encoded = routeResp.routes?.FirstOrDefault()?.polyline?.encodedPolyline;

            // salvează DeliveryRoute + DeliveryRouteOrders
            var route = new DeliveryRoute
            {
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User.Identity?.Name,
                DriverName = req.DriverName
            };
            _db.DeliveryRoutes.Add(route);
            await _db.SaveChangesAsync();

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


        //  ————————————————
        //  3️⃣ Preview only: get polyline & data for any date (HQ start/end + TSP)
        //  GET api/planner/route?date=YYYY-MM-DD
        //  ————————————————
        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(DateTime date)
        {
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == date.Date && o.ServiceType == "PickupDelivery")
                .OrderBy(o => o.DeliveryAddress)
                .ToListAsync();

            if (orders.Count < 1)
                return BadRequest("Need at least 1 address to create a route.");

            // 1. coordonate din comenzi
            var coords = orders
                .Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value))
                .ToList();

            // 2. punem HQ la început și la sfârșit
            var waypoints = new List<(double lat, double lng)> { Headquarters };
            waypoints.AddRange(coords);
            waypoints.Add(Headquarters);

            // 3. apel Google Routes
            var routeData = await GetOptimizedRoute(waypoints);
            var route = routeData.routes?.FirstOrDefault();
            if (route == null)
                return BadRequest("No route found from Google Maps API.");

            // 4. price & info
            var totalPrice = orders.Sum(o => o.Items.Sum(i => i.Price));
            var dtoOrders = orders.Select((o, i) => new {
                index = i + 1,
                lat = o.DeliveryLatitude,
                lng = o.DeliveryLongitude,
                address = o.DeliveryAddress,
                phone = o.TelephoneNumber,
                id = o.Id,
                customer = o.CustomerId,
                price = o.Items.Sum(it => it.Price)
            });

            return Ok(new
            {
                polyline = route.polyline!.encodedPolyline,
                totalPrice,
                orders = dtoOrders
            });
        }



        public class AutoGenerateRouteRequest
        {
            public DateTime Date { get; set; }
            public string? DriverName { get; set; }
        }
    }
}
