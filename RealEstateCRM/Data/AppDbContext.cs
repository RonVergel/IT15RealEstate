using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Models;

namespace RealEstateCRM.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }
    }
}
