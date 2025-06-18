using ApiSpalatorie.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApiSpalatorie.Helpers;
using ApiSpalatorie.Data;
using System.Net.Http.Json;

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

        private async Task<RouteResponse> GetOptimizedRoute(List<(double lat, double lng)> waypoints)
        {
            var baseUrl = "https://routes.googleapis.com/directions/v2:computeRoutes";
            var client = new HttpClient();

            var origin = new
            {
                location = new { latLng = new { latitude = waypoints.First().lat, longitude = waypoints.First().lng } }
            };

            var destination = new
            {
                location = new { latLng = new { latitude = waypoints.Last().lat, longitude = waypoints.Last().lng } }
            };

            var intermediates = waypoints.Count > 2
                ? waypoints.Skip(1).SkipLast(1).Select(p => new
                {
                    location = new { latLng = new { latitude = p.lat, longitude = p.lng } }
                }).ToList()
                : null;

            var body = new
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

            var response = await client.PostAsJsonAsync(baseUrl, body);
            var rawContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("❌ Google API ERROR:");
                Console.WriteLine(rawContent);
                throw new InvalidOperationException($"Google Maps API error: {rawContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<RouteResponse>(new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result!;
        }

        // GET api/planner/route?date=2025-05-12
        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(DateTime date)
        {
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == date.Date && o.ServiceType == "PickupDelivery")
                .OrderBy(o => o.DeliveryAddress)
                .ToListAsync();

            if (orders.Count < 2)
                return BadRequest("Need at least 2 addresses to create a route.");

            // Step 1: Extract coordinates
            var coords = orders.Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value)).ToList();

            // Step 2: Solve TSP
            var orderedIndexes = TspRouteOptimizer.SolveTsp(coords);
            var orderedOrders = orderedIndexes.Select(i => orders[i]).ToList();

            // Step 3: Rebuild ordered coord list
            var orderedCoords = orderedOrders
                .Select(o => (o.DeliveryLatitude!.Value, o.DeliveryLongitude!.Value))
                .ToList();

            // Step 4: Call Google route API
            var routeData = await GetOptimizedRoute(orderedCoords);
            var route = routeData.routes?.FirstOrDefault();

            if (route == null)
                return BadRequest("No route found from Google Maps API.");

            // Step 5: Return full route + order info
            return Ok(new
            {
                route,
                orders = orderedOrders.Select((o, i) => new
                {
                    index = i + 1,
                    lat = o.DeliveryLatitude,
                    lng = o.DeliveryLongitude,
                    address = o.DeliveryAddress,
                    phone = o.TelephoneNumber,
                    id = o.Id,
                    customer = o.CustomerId
                })
            });
        }
    }
}
