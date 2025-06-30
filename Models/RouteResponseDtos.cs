namespace ApiSpalatorie.Models
{
    public class RouteResponse
    {
        public List<Route>? routes { get; set; }
        public object? geocodingResults { get; set; }
    }

    public class Route
    {
        public List<Leg>? legs { get; set; }
        public int distanceMeters { get; set; }
        public string? duration { get; set; }
        public string? staticDuration { get; set; }
        public RoutePolyline? polyline { get; set; }
        public string? description { get; set; }
        public Viewport? viewport { get; set; }
        public TravelAdvisory? travelAdvisory { get; set; }
        public LocalizedValues? localizedValues { get; set; }
        public string? routeToken { get; set; }
        public List<string>? routeLabels { get; set; }
        public PolylineDetails? polylineDetails { get; set; }

        public List<int>? optimizedIntermediateWaypointIndex { get; set; }
        
    }

    public class Leg
    {
        public int distanceMeters { get; set; }
        public string? duration { get; set; }
        public string? staticDuration { get; set; }
        public RoutePolyline? polyline { get; set; }
        public StepLocation? startLocation { get; set; }
        public StepLocation? endLocation { get; set; }
        public List<Step>? steps { get; set; }
        public LocalizedValues? localizedValues { get; set; }
    }

    public class Step
    {
        public int distanceMeters { get; set; }
        public string? staticDuration { get; set; }
        public RoutePolyline? polyline { get; set; }
        public StepLocation? startLocation { get; set; }
        public StepLocation? endLocation { get; set; }
        public NavigationInstruction? navigationInstruction { get; set; }
        public LocalizedValues? localizedValues { get; set; }
        public string? travelMode { get; set; }
    }

    public class RoutePolyline
    {
        public string? encodedPolyline { get; set; }
    }

    public class StepLocation
    {
        public LatLng? latLng { get; set; }
    }

    public class LatLng
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
    }

    public class NavigationInstruction
    {
        public string? maneuver { get; set; }
        public string? instructions { get; set; }
    }

    public class LocalizedValues
    {
        public LocalizedText? distance { get; set; }
        public LocalizedText? duration { get; set; }
        public LocalizedText? staticDuration { get; set; }
    }

    public class LocalizedText
    {
        public string? text { get; set; }
    }

    public class Viewport
    {
        public LatLng? low { get; set; }
        public LatLng? high { get; set; }
    }

    public class TravelAdvisory { }

    public class PolylineDetails { }
}
