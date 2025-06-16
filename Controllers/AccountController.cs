using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiSpalatorie.Data;
using static ApiSpalatorie.Models.AccountDto;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly UserManager<IdentityUser> _users;
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly IConfiguration _cfg;

    public AccountController(
      UserManager<IdentityUser> u,
      SignInManager<IdentityUser> s,
      IConfiguration cfg)
      => (_users, _signIn, _cfg) = (u, s, cfg);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // create user
        var user = new IdentityUser(dto.Email) { Email = dto.Email, UserName = dto.Email };
        var res = await _users.CreateAsync(user, dto.Password);
        if (!res.Succeeded) return BadRequest(res.Errors);

        // role: Customer by default
        var role = string.IsNullOrWhiteSpace(dto.Role) ? "Customer" : dto.Role;
        await _users.AddToRoleAsync(user, role);

        // build JWT exactly like Login
        var roles = await _users.GetRolesAsync(user);
        var token = BuildJwt(user, roles);

        return Ok(new { token });
    }


    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user is null) return Unauthorized();

        var signInRes = await _signIn.PasswordSignInAsync(user, dto.Password, false, false);
        if (!signInRes.Succeeded) return Unauthorized();

        // claims
        var roles = await _users.GetRolesAsync(user);
        var claims = new List<Claim> {
      new Claim(ClaimTypes.Name, user.UserName!),
      new Claim(ClaimTypes.NameIdentifier, user.Id)
    };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
                       issuer: _cfg["Jwt:Issuer"],
                       audience: _cfg["Jwt:Issuer"],
                       claims: claims,
                       expires: DateTime.UtcNow.AddHours(2),
                       signingCredentials: creds);

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    private string BuildJwt(IdentityUser user, IEnumerable<string> roles)
    {
        var claims = new List<Claim> {
        new Claim(ClaimTypes.Name, user.UserName!),
        new Claim(ClaimTypes.NameIdentifier, user.Id)
    };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
                     issuer: _cfg["Jwt:Issuer"],
                     audience: _cfg["Jwt:Issuer"],
                     claims: claims,
                     expires: DateTime.UtcNow.AddHours(2),
                     signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }



}
