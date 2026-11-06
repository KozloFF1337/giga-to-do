using Microsoft.EntityFrameworkCore;

namespace GigaTodo;

public sealed class DesignFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<Database>
{
    public Database CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<Database>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=giga_todo;Username=giga_todo").Options);
}

public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<WorkItem> Tasks => Set<WorkItem>();
    public DbSet<AgentRun> Runs => Set<AgentRun>();
    public DbSet<TaskEvent> Events => Set<TaskEvent>();
    public DbSet<Recurrence> Schedules => Set<Recurrence>();
    public DbSet<ScheduleSlot> Slots => Set<ScheduleSlot>();
    public DbSet<ResourceLease> Resources => Set<ResourceLease>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Board>().ToTable("boards");
        model.Entity<WorkItem>().ToTable("tasks");
        model.Entity<AgentRun>().ToTable("runs");
        model.Entity<TaskEvent>().ToTable("events");
        model.Entity<Recurrence>().ToTable("schedules");
        model.Entity<ScheduleSlot>().ToTable("slots");
        model.Entity<ResourceLease>().ToTable("resources").HasKey(r => r.Name);
        model.Entity<Attachment>().ToTable("attachments");
        model.Entity<WorkItem>().HasIndex(t => t.DedupeKey).IsUnique();
        model.Entity<WorkItem>().HasIndex(t => new { t.BoardId, t.Stage, t.Position });
        model.Entity<AgentRun>().HasIndex(r => r.TokenHash).IsUnique();
        model.Entity<AgentRun>().HasIndex(r => new { r.TaskId, r.State });
        model.Entity<TaskEvent>().HasIndex(e => new { e.TaskId, e.Id });
        model.Entity<ScheduleSlot>().HasIndex(s => new { s.ScheduleId, s.At }).IsUnique();
        model.Entity<WorkItem>().HasOne<Board>().WithMany().HasForeignKey(t => t.BoardId);
        model.Entity<Recurrence>().HasOne<Board>().WithMany().HasForeignKey(t => t.BoardId);
        model.Entity<AgentRun>().HasOne<WorkItem>().WithMany().HasForeignKey(r => r.TaskId);
        model.Entity<TaskEvent>().HasOne<WorkItem>().WithMany().HasForeignKey(e => e.TaskId);
        model.Entity<ScheduleSlot>().HasOne<Recurrence>().WithMany().HasForeignKey(s => s.ScheduleId);
        model.Entity<WorkItem>().Property(t => t.Version).IsConcurrencyToken();
        model.Entity<Board>().Property(b => b.Version).IsConcurrencyToken();
        model.Entity<Recurrence>().Property(s => s.Version).IsConcurrencyToken();
    }
}
