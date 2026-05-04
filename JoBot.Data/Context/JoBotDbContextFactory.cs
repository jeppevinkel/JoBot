using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JoBot.Data.Context;

public class JoBotDbContextFactory : IDesignTimeDbContextFactory<JoBotDbContext>
{
    public JoBotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JoBotDbContext>()
            .UseSqlite("Data Source=jobot_design.db")
            .Options;

        return new JoBotDbContext(options);
    }
}