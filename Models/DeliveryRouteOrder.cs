    using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiSpalatorie.Models
{
    public class DeliveryRouteOrder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public Order Order { get; set; } = null!;

        [Required]
        public int DeliveryRouteId { get; set; }

        [ForeignKey("DeliveryRouteId")]
        public DeliveryRoute DeliveryRoute { get; set; } = null!;

        public int StopIndex { get; set; }

        public bool IsCompleted { get; set; } = false;
    }
}
