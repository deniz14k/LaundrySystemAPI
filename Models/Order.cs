using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;


namespace AplicatieSpalatorie.Models
{
    public class Order
    {
        public int Id { get; set; }

        public string CustomerId { get; set; } = "";

        public string TelephoneNumber { get; set; } = "";

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        public string? ServiceType { get; set; } 

        public List<Item> Items { get; set; } = new List<Item>();

        [Required]
        public string Status { get; set; } = "Pending";

        [NotMapped]
        public double TotalPrice => Items.Sum(i => i.Price);
        public DateTime? CompletedDate { get; set; }



    }

}
