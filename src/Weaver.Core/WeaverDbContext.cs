using Microsoft.EntityFrameworkCore;

namespace Weaver.Core;

public sealed class WeaverDbContext : DbContext
{
    public WeaverDbContext(DbContextOptions<WeaverDbContext> options) : base(options)
        => ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

    public DbSet<ServiceEntity> Services => Set<ServiceEntity>();
    public DbSet<DependencyEntity> Dependencies => Set<DependencyEntity>();
    public DbSet<RequestTypeEntity> RequestTypes => Set<RequestTypeEntity>();
    public DbSet<MetricSampleEntity> MetricSamples => Set<MetricSampleEntity>();
    public DbSet<LogEventEntity> Logs => Set<LogEventEntity>();
    public DbSet<TraceEntity> Traces => Set<TraceEntity>();
    public DbSet<SpanEntity> Spans => Set<SpanEntity>();
    public DbSet<ChangeEventEntity> ChangeEvents => Set<ChangeEventEntity>();
    public DbSet<KnowledgeSnippetEntity> Knowledge => Set<KnowledgeSnippetEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ServiceEntity>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Subsystem).HasColumnName("subsystem");
            e.Property(x => x.OwnerTeam).HasColumnName("owner_team");
        });

        b.Entity<DependencyEntity>(e =>
        {
            e.ToTable("dependencies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FromService).HasColumnName("from_service");
            e.Property(x => x.ToService).HasColumnName("to_service");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Critical).HasColumnName("critical");
        });

        b.Entity<RequestTypeEntity>(e =>
        {
            e.ToTable("request_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Weight).HasColumnName("weight");
            e.Property(x => x.Path).HasColumnName("path");
        });

        b.Entity<MetricSampleEntity>(e =>
        {
            e.HasNoKey();
            e.ToTable("metric_samples");
            e.Property(x => x.SubjectKind).HasColumnName("subject_kind");
            e.Property(x => x.SubjectId).HasColumnName("subject_id");
            e.Property(x => x.Ts).HasColumnName("ts");
            e.Property(x => x.Metric).HasColumnName("metric");
            e.Property(x => x.Value).HasColumnName("value");
        });

        b.Entity<LogEventEntity>(e =>
        {
            e.ToTable("log_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.Ts).HasColumnName("ts");
            e.Property(x => x.Level).HasColumnName("level");
            e.Property(x => x.TemplateId).HasColumnName("template_id");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.Fields).HasColumnName("fields");
            e.Property(x => x.TraceId).HasColumnName("trace_id");
            e.Property(x => x.SpanId).HasColumnName("span_id");
        });

        b.Entity<TraceEntity>(e =>
        {
            e.ToTable("traces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RequestTypeId).HasColumnName("request_type_id");
            e.Property(x => x.RootServiceId).HasColumnName("root_service_id");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.DurationMs).HasColumnName("duration_ms");
            e.Property(x => x.Status).HasColumnName("status");
        });

        b.Entity<SpanEntity>(e =>
        {
            e.ToTable("spans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TraceId).HasColumnName("trace_id");
            e.Property(x => x.ParentSpanId).HasColumnName("parent_span_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.EdgeId).HasColumnName("edge_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.StartOffsetMs).HasColumnName("start_offset_ms");
            e.Property(x => x.DurationMs).HasColumnName("duration_ms");
            e.Property(x => x.SelfMs).HasColumnName("self_ms");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Attributes).HasColumnName("attributes");
        });

        b.Entity<ChangeEventEntity>(e =>
        {
            e.ToTable("change_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Ts).HasColumnName("ts");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Fields).HasColumnName("fields");
        });

        b.Entity<KnowledgeSnippetEntity>(e =>
        {
            e.ToTable("knowledge_snippets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.SourceRef).HasColumnName("source_ref");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.DocRef).HasColumnName("doc_ref");
            e.Property(x => x.Seq).HasColumnName("seq");
        });
    }
}
