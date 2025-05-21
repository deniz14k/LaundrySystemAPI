using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using AplicatieSpalatorie.Data;
using AplicatieSpalatorie.Models;
using ApiSpalatorie.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vonage;
using Vonage.Request;
using Vonage.Messaging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ApiSpalatorie.Models;

[ApiController]
[Route("api/[controller]")]
public class OtpController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly VonageSettings _vonage;
    private readonly IWebHostEnvironment _env;

    public OtpController(
        ApplicationDbContext db,
        IConfiguration config,
        IOptions<VonageSettings> vonageOptions,
        IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _vonage = vonageOptions.Value;
        _env = env;
    }

    // 1) POST /api/otp/send
    [HttpPost("send")]
    public async Task<IActionResult> SendOtp([FromBody] PhoneDto dto)
    {
        // generate code + save to DB
        var code = new Random().Next(100000, 999999).ToString();
        var entry = new OtpEntry
        {
            Phone = dto.Phone,
            Code = code,
            Expires = DateTime.UtcNow.AddMinutes(5)
        };
        _db.OtpEntries.Add(entry);
        await _db.SaveChangesAsync();

        /* In development, return the code for testing instead of sending SMS
        if (_env.IsDevelopment())
        {
            return Ok(new
            {
                message = "OTP (dev) generated.",
                sentTo = dto.Phone,
                code
            });
        }
        */

        // Production: send via Vonage
        var creds = Credentials.FromApiKeyAndSecret(_vonage.ApiKey, _vonage.ApiSecret);
        var client = new VonageClient(creds);
        var response = await client.SmsClient.SendAnSmsAsync(new SendSmsRequest
        {
            To = dto.Phone,
            From = _vonage.From,
            Text = $"Your code is {code}"
        });

        // grab the first message result
        var msg = response.Messages.FirstOrDefault();
        if (msg == null)
        {
            return StatusCode(500, "SMS gateway error: no response");
        }

        // return status info
        return Ok(new
        {
            sentTo = dto.Phone,
            from = _vonage.From,
            messageId = msg.MessageId,
            status = msg.Status,
            errorText = msg.ErrorText
        });
    }

    // 2) POST /api/otp/verify
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyDto dto)
    {
        var now = DateTime.UtcNow;
        var entry = await _db.OtpEntries
            .Where(x => x.Phone == dto.Phone && x.Code == dto.Code && x.Expires > now)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (entry == null)
            return BadRequest(new { error = "Invalid or expired code." });

        // remove used entry
        _db.OtpEntries.Remove(entry);
        await _db.SaveChangesAsync();

        // Issue JWT
        // Issue JWT, now including the Name claim (not just the phone)
        var claims = new List<Claim>
{
    // Set NameIdentifier to phone so you can still look it up:
    new Claim(ClaimTypes.NameIdentifier, dto.Phone),

    // Use dto.Name if provided, otherwise fallback to phone:
    new Claim(ClaimTypes.Name,
        !string.IsNullOrWhiteSpace(dto.Name)
           ? dto.Name
           : dto.Phone),

    new Claim(ClaimTypes.Role, "Customer")
};

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: new SigningCredentials(
                                   new SymmetricSecurityKey(
                                       Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)),
                                   SecurityAlgorithms.HmacSha256)
        );

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }


    private async Task SendSmsAsync(string toPhone, string text)
    {
        // Ensure E.164 format for Romania if no '+' present
        var normalized = toPhone.StartsWith("+")
           ? toPhone
           : $"+40{toPhone}";

        var creds = Credentials.FromApiKeyAndSecret(_vonage.ApiKey, _vonage.ApiSecret);
        var client = new VonageClient(creds);

        var response = await client.SmsClient.SendAnSmsAsync(new SendSmsRequest
        {
            To = normalized,
            From = _vonage.From,
            Text = text
        });

        var msg = response.Messages.FirstOrDefault();
        if (msg == null || msg.Status != "0")
            throw new InvalidOperationException($"SMS failed to {normalized}: {msg?.ErrorText}");

        
    }

}
