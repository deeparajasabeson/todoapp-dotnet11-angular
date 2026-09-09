using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("TodoDb")
    ?? throw new InvalidOperationException(
        "Connection string 'TodoDb' is missing. Set ConnectionStrings:TodoDb in appsettings.json or user secrets.");

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)));

// Statuses and priorities travel as names ("InProgress", "High"), not ints.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// The Angular dev server proxies /api to this app, so CORS is not needed for the
// normal workflow - this is here for when the SPA is served from its own origin.
const string SpaCors = "spa";
builder.Services.AddCors(options => options.AddPolicy(SpaCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "Todo API",
        Version = "v1",
        Description = "To-do items with a status and a priority. "
            + "Statuses: Pending, InProgress, Completed, Cancelled. "
            + "Priorities: Low, Medium, High, Critical."
    };
    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI only - the OpenAPI document itself comes from Microsoft.AspNetCore.OpenApi
    // above, so there is no second document generator in the app.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Todo API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Todo API";
        options.DisplayRequestDuration();
        // The point of this page is trying calls out, so skip the extra click per operation.
        options.EnableTryItOutByDefault();
    });

    // Convenience for local work only. Elsewhere apply migrations deliberately with
    // `dotnet ef database update` (or a deployment step) rather than on startup.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
    await TodoSeeder.SeedAsync(db);
}

app.UseHttpsRedirection();
app.UseCors(SpaCors);

// Land on Swagger UI in Development; elsewhere it does not exist, so point at health.
app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health"))
    .ExcludeFromDescription();
app.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" })).WithTags("System");

app.MapTodoEndpoints();

app.Run();
