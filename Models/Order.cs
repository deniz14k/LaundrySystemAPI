using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;


namespace ApiSpalatorie.Models
{
    public class Order
    {
        
        public int Id { get; set; }

        public string CustomerId { get; set; } = "";

        public string TelephoneNumber { get; set; } = "";

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        public string? ServiceType { get; set; }

        public string? Observation { get; set; }


        public string? DeliveryAddress { get; set; }
        public List<Item> Items { get; set; } = new List<Item>();

        [Required]
        public string Status { get; set; } = "Pending";

        [NotMapped]
        public double TotalPrice => Items.Sum(i => i.Price);
        public DateTime? CompletedDate { get; set; }

        public double? DeliveryLatitude { get; set; }
        public double? DeliveryLongitude { get; set; }

        public bool IsAssignedToRoute { get; set; } = false;



    }

}
