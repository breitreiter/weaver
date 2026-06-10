using Microsoft.EntityFrameworkCore;

namespace Weaver.Core;

// Writable store for boards (created via EnsureCreated). Distinct from
// WeaverDbContext, which is read-only over observed telemetry.
public sealed class BoardsDbContext : DbContext
{
    public BoardsDbContext(DbContextOptions<BoardsDbContext> options) : base(options) { }

    public DbSet<BoardEntity> Boards => Set<BoardEntity>();
    public DbSet<BoardItemEntity> BoardItems => Set<BoardItemEntity>();
    public DbSet<BoardEdgeEntity> BoardEdges => Set<BoardEdgeEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BoardEntity>().HasKey(x => x.Id);
        b.Entity<BoardItemEntity>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.BoardId); });
        b.Entity<BoardEdgeEntity>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.BoardId); });
    }
}
