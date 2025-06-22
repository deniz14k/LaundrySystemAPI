using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ApiSpalatorie.Models;


namespace ApiSpalatorie.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
           : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<Item> Items { get; set; } = null!;

        public DbSet<DeliveryRoute> DeliveryRoutes { get; set; }
        public DbSet<DeliveryRouteOrder> DeliveryRouteOrders { get; set; }


        public DbSet<OtpEntry> OtpEntries => Set<OtpEntry>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // one to many relationship between Order and Item
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
    
}

