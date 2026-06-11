using SignalRLab.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy =>
    {
        policy.WithOrigins("http://localhost:5221", "https://localhost:7064", "http://localhost:5221")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseCors("dev");

app.MapHub<ChatHub>("/hubs/chat");

app.MapGet("/", () => "Hello World!");

app.Run();