using Microsoft.EntityFrameworkCore;

namespace Backend.api.Database
{
    public class WarehouseDbContext : DbContext
    {
        public WarehouseDbContext() { }
        public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }
    }
}