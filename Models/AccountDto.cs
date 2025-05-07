namespace ApiSpalatorie.Models
{
    public class AccountDto
    {


        public record RegisterDto(string Email, string Password, string? Role);
        public record LoginDto(string Email, string Password);

    }
}
