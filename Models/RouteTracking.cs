// Models/RouteTracking.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiSpalatorie.Models
{
    public class RouteTracking
    {
        [Key] public int Id { get; set; }

        [ForeignKey(nameof(DeliveryRoute))]
        public int DeliveryRouteId { get; set; }
        public DeliveryRoute DeliveryRoute { get; set; } = null!;

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
