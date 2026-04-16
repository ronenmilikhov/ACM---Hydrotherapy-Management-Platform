using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using ReactServerSide.DAL;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Allow large request bodies for base64-encoded image uploads (up to 15 MB).
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 15 * 1024 * 1024;
});

string jwtIssuer = (builder.Configuration["Jwt:Issuer"] ?? "ReactServerSide").Trim();
string jwtAudience = (builder.Configuration["Jwt:Audience"] ?? "ReactProjectClient").Trim();
string jwtSecret = (builder.Configuration["Jwt:SecretKey"] ?? string.Empty).Trim();

if (jwtSecret.Length < 32)
{
    throw new InvalidOperationException("Jwt:SecretKey must be configured and at least 32 characters long.");
}

byte[] jwtKeyBytes = Encoding.UTF8.GetBytes(jwtSecret);
SymmetricSecurityKey jwtSigningKey = new SymmetricSecurityKey(jwtKeyBytes);

// Add services to the container.

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<DBServices>();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExpoCors", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    DBServices db = scope.ServiceProvider.GetRequiredService<DBServices>();
    db.EnsureChildrenSchemaReady();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ExpoCors");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

string chatAttachmentsDirectory = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "chat-attachments");
Directory.CreateDirectory(chatAttachmentsDirectory);

// Serve chat-attachment images with explicit CORS headers and no-cache so that
// clients on other machines / networks always fetch a fresh copy directly from
// this server instead of relying on a stale or failed cached response.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(chatAttachmentsDirectory),
    RequestPath = "/chat-attachments",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        ctx.Context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
    }
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
