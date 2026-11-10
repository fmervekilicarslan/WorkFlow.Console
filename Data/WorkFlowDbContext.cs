using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Data;

public class WorkFlowDbContext(DbContextOptions<WorkFlowDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("TaskCodeSequence").StartsAt(1).IncrementsBy(1);
        modelBuilder.Entity<TaskItem>(task =>
        {
            task.HasKey(item => item.Id);
            task.Ignore(item => item.Code);
            task.Property(item => item.CodeNumber).HasDefaultValueSql("nextval('\"TaskCodeSequence\"')").ValueGeneratedOnAdd();
            task.HasIndex(item => item.CodeNumber).IsUnique();
            task.Property(item => item.Id).ValueGeneratedNever();
            task.Property(item => item.Title).IsRequired().HasMaxLength(120);
            task.Property(item => item.Description).IsRequired().HasMaxLength(1000);
            task.Property(item => item.Priority).HasConversion<int>().IsRequired();
            task.Property(item => item.Status).HasConversion<int>().IsRequired();
            task.Property(item => item.CreatedDate).HasColumnType("timestamp with time zone").IsRequired();
            task.Property(item => item.DueDate).HasColumnType("timestamp with time zone");
        });
    }
}
