namespace ApiSpalatorie.Models

{
    public class OtpEntry
    {
        public int Id { get; set; }
        public string Phone { get; set; } = "";
        public string Code { get; set; } = "";
        public DateTime Expires { get; set; }
    }
}
