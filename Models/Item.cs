using AplicatieSpalatorie.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;


public class Item
{
    public int Id { get; set; }

    public string Type { get; set; } = "Carpet";

    public double Price { get; set; }

    public double? Length { get; set; }  //can be null for blankets
    public double? Width { get; set; }  // this too

    public int OrderId { get; set; }

    [JsonIgnore] public Order? Order { get; set; } = null!;
}
