using Microsoft.EntityFrameworkCore;

namespace Weaver.Core;

// Writable store for boards (created via EnsureCreated). Distinct from
// WeaverDbContext, which is read-only over observed telemetry.
public sealed class BoardsDbContext : DbContext
{
    public BoardsDbContext(DbContextOptions<BoardsDbContext> options) : base(options) { }

    public DbSet<BoardEntity> Boards => Set<BoardEntity>();
    public DbSet<BoardNodeEntity> BoardNodes => Set<BoardNodeEntity>();
    public DbSet<EvidenceEntity> Evidence => Set<EvidenceEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BoardEntity>().HasKey(x => x.Id);
        b.Entity<BoardNodeEntity>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.BoardId, x.ServiceId }).IsUnique(); });
        b.Entity<EvidenceEntity>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.BoardId, x.ServiceId }); });
    }
}
