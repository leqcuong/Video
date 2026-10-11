using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ThesisDemo.Infrastructure.Persistence;

public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteThesisDemoDbContext>
{
    public SqliteThesisDemoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteThesisDemoDbContext>().UseSqlite("Data Source=thesis-demo.design.db").Options);
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerThesisDemoDbContext>
{
    public SqlServerThesisDemoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqlServerThesisDemoDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ThesisDemoDesign;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
