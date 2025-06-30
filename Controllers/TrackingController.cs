// Controllers/TrackingController.cs
using ApiSpalatorie.Data;
using ApiSpalatorie.Models;
using ApiSpalatorie.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ApiSpalatorie.Controllers
{
    [Authorize(Roles = "Admin,Manager,Driver,Customer")]
    [ApiController]
    [Route("api/[controller]")]
    public class TrackingController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public TrackingController(ApplicationDbContext db)
        {
            _db = db;
        }


        // GET: api/tracking/{routeId}/latest
        [HttpGet("{routeId:int}/latest")]
        public async Task<IActionResult> GetLatest(int routeId)
        {
            var latest = await _db.RouteTrackings
                .Where(t => t.DeliveryRouteId == routeId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync();
            if (latest == null) return NoContent(); // not started yet

            return Ok(new
            {
                lat = latest.Latitude,
                lng = latest.Longitude,
                timestamp = latest.Timestamp
            });
        }



        [HttpPost("report")]
        public async Task<IActionResult> Report([FromBody] TrackingReportDto dto)
        {
            // optionally: validate that dto.RouteId exists, etc.
            var track = new RouteTracking
            {
                DeliveryRouteId = dto.RouteId,
                Latitude = dto.Lat,
                Longitude = dto.Lng,
                Timestamp = DateTime.UtcNow
            };

            _db.RouteTrackings.Add(track);
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}
