using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace PgFreshCache.TestApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var pgConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new Exception("DefaultConnection not configured in connection strings");

            builder.Services.AddDbContext<StoreDbContext>(options =>
            {
                options.UseNpgsql(pgConnectionString);
            });

            builder.Services.AddPgFreshCache<StoreDbContext>("cache", pgConnectionString, "cache_publication");

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
