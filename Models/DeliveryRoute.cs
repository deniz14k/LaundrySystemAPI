// Models/DeliveryRoute.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ApiSpalatorie.Models
{
    public class DeliveryRoute
    {
        public int Id { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string? CreatedBy { get; set; }

        public string? DriverName { get; set; }
       
        public bool IsStarted { get; set; }


        public ICollection<DeliveryRouteOrder> Orders { get; set; } = new List<DeliveryRouteOrder>();
    }
}
