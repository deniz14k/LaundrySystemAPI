// DTOs/CreateRouteRequest.cs
namespace ApiSpalatorie.Models.DTOs
{
    public class CreateRouteRequest
    {
        public string DriverName { get; set; } = string.Empty;
        public List<int> OrderIds { get; set; } = new();
    }
}
