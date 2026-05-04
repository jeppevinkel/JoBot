using JoBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JoBot.Data.Context;

public class JoBotDbContext : DbContext
{
    public JoBotDbContext(DbContextOptions<JoBotDbContext> options) : base(options) { }

    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<GuildSettingsEntity> GuildSettings => Set<GuildSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GuildId);
            entity.HasIndex(e => e.Timestamp);

            entity.Property(e => e.Timestamp)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        modelBuilder.Entity<GuildSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.GuildId);
        });
    }
}