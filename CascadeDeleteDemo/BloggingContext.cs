using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CascadeDeleteDemo
{
    public sealed class BloggingContext : DbContext
    {
        public BloggingContext(DeleteBehavior deleteBehavior, bool requiredRelationship)
        {
            DeleteBehavior = deleteBehavior;
            RequiredRelationship = requiredRelationship;

            if (LogMessages == null)
            {
                LogMessages = new List<string>();
                this.GetService<ILoggerFactory>().AddProvider(new MyLoggerProvider());
            }
        }

        public DeleteBehavior DeleteBehavior { get; }
        public bool RequiredRelationship { get; }

        public DbSet<Blog> Blogs { get; set; }
        public DbSet<Post> Posts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .ReplaceService<IModelCacheKeyFactory, DeleteBehaviorCacheKeyFactory>()
                .EnableSensitiveDataLogging()
                .UseMySql("Server=localhost;port=3306;database=demo_cascade_delete_demo;uid=root;pwd=root;Convert Zero Datetime=True;");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder
                .Entity<Blog>()
                .HasMany(e => e.Posts)
                .WithOne(e => e.Blog)
                .OnDelete(DeleteBehavior)
                .IsRequired(RequiredRelationship);

        public override int SaveChanges()
        {
            LogMessages.Clear();

            return base.SaveChanges();
        }

        public class DeleteBehaviorCacheKeyFactory : IModelCacheKeyFactory
        {
            public virtual object Create(DbContext context)
            {
                var bloggingContext = (BloggingContext)context;

                return (bloggingContext.DeleteBehavior, bloggingContext.RequiredRelationship);
            }
        }

        public static IList<string> LogMessages;

        private class MyLoggerProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new SampleLogger();

            public void Dispose() { }

            private class SampleLogger : ILogger
            {
                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                    Func<TState, Exception, string> formatter)
                {
                    if (eventId.Id == RelationalEventId.CommandExecuting.Id)
                    {
                        var message = formatter(state, exception);
                        var commandIndex = Math.Max(message.IndexOf("UPDATE"), message.IndexOf("DELETE"));
                        if (commandIndex >= 0)
                        {
                            var truncatedMessage = message.Substring(commandIndex, message.IndexOf(";", commandIndex) - commandIndex).Replace(Environment.NewLine, " ");

                            for (var i = 0; i < 4; i++)
                            {
                                var paramIndex = message.IndexOf($"@p{i}='");
                                if (paramIndex >= 0)
                                {
                                    var paramValue = message.Substring(paramIndex + 5, 1);
                                    if (paramValue == "'")
                                    {
                                        paramValue = "NULL";
                                    }

                                    truncatedMessage = truncatedMessage.Replace($"@p{i}", paramValue);
                                }
                            }

                            LogMessages.Add(truncatedMessage);
                        }
                    }
                }

                public IDisposable BeginScope<TState>(TState state) => null;
            }
        }
    }
}
