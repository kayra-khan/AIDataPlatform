using System.Text.Json;
using System.Text.Json.Serialization;
using AIDataPlatform.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using static AIDataPlatform.Models.DataModel;
using AIDataPlatform.Services.Database;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.SemanticKernel.ChatCompletion;
using Syncfusion.Blazor.InteractiveChat;
using ChatMessage = Syncfusion.Blazor.InteractiveChat.ChatMessage;

namespace AIDataPlatform.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        // DbSet for tenants table
        public DbSet<DataModel.Tenant> Tenants { get; set; }
        
        // DbSet for invitations table
        public DbSet<DataModel.Invitation> Invitations { get; set; }
        
        // DbSet for chathistories table
        public DbSet<DataModel.UserChatHistory> ChatHistories { get; set; }
        
        // DbSet for session containers
        public DbSet<DataModel.SessionContainer> SessionContainers { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure the relationship between Tenant and ApplicationUser
            modelBuilder.Entity<DataModel.Tenant>()
                .HasMany(t => t.Users)
                .WithOne()
                .HasForeignKey(u => u.TenantId)
                .IsRequired();
            
            // Configure the relationship between UserChatHistory and ApplicationUser
            modelBuilder.Entity<DataModel.UserChatHistory>(entity =>
            {
                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(u => u.UserId)
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.Prompts)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, JsonSerializerContext.Default.ListAssistViewPrompt),
                        v => JsonSerializer.Deserialize(v, JsonSerializerContext.Default.ListAssistViewPrompt) 
                             ?? new List<AssistViewPrompt>()
                    )
                    .Metadata.SetValueComparer(
                        new ValueComparer<List<AssistViewPrompt>>(
                            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                            c => c.ToList()));

                entity.Property(e => e.SerializedChatHistory)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { 
                            WriteIndented = false,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        }),
                        v => JsonSerializer.Deserialize<ChatHistory>(v, new JsonSerializerOptions {
                            WriteIndented = false,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        })
                    )
                    .Metadata.SetValueComparer(
                        new ValueComparer<ChatHistory>(
                            (c1, c2) => c1 != null && c2 != null && 
                                JsonSerializer.Serialize(c1, new JsonSerializerOptions { WriteIndented = false }) == 
                                JsonSerializer.Serialize(c2, new JsonSerializerOptions { WriteIndented = false }),
                            c => c != null ? JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = false }).GetHashCode() : 0,
                            c => c != null ? JsonSerializer.Deserialize<ChatHistory>(
                                JsonSerializer.Serialize(c, new JsonSerializerOptions { 
                                    WriteIndented = false,
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                                }),
                                new JsonSerializerOptions {
                                    WriteIndented = false,
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                                }) : null
                        ));

                entity.Property(e => e.Id)
                    .HasColumnType("uuid");
            });
            
            // Configure the relationship between SessionContainer and ApplicationUser
            modelBuilder.Entity<DataModel.SessionContainer>(entity =>
            {
                entity.HasOne(sc => sc.User)
                    .WithMany()
                    .HasForeignKey(sc => sc.UserId)
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(sc => new { sc.UserId, sc.SessionId })
                    .IsUnique();

                entity.HasIndex(sc => sc.ContainerName)
                    .IsUnique();

                entity.Property(e => e.Id)
                    .HasColumnType("uuid");
            });
            
            base.OnModelCreating(modelBuilder);
        }
    }
    
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(List<AssistViewPrompt>))]
    public partial class JsonSerializerContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}