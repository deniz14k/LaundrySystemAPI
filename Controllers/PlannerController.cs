using AplicatieSpalatorie.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiSpalatorie.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class PlannerController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public PlannerController(ApplicationDbContext db) => _db = db;

        // GET api/planner/route?date=2025-05-12
        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(DateTime date)
        {
            // TODO: replace this stub with real TSP/nearest-neighbor logic
            var orders = await _db.Orders
                .Where(o => o.ReceivedDate.Date == date.Date
                         && o.ServiceType == "PickupDelivery")
                .OrderBy(o => o.DeliveryLatitude) // naive sort
                .Select(o => new {
                    o.Id,
                    o.DeliveryAddress,
                    o.DeliveryLatitude,
                    o.DeliveryLongitude
                })
                .ToListAsync();
            return Ok(orders);
        }
    }

}
