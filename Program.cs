using AplicatieSpalatorie.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add EF Core DbContext
//    - Make sure you have "DefaultConnection" set in appsettings.json.
//    - Also ensure you installed the EF Core packages, e.g.:
//       dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 8.*
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 2. Add controllers
builder.Services.AddControllers();

// 3. Add Swagger (OpenAPI)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 4. Configure the middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// 5. Map controller endpoints
app.MapControllers();

// 6. Run the app
app.Run();
