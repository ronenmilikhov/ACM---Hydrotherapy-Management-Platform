using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using ReactServerSide.DAL;
using System.Text;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Allow large request bodies for base64-encoded image uploads (up to 15 MB).
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 15 * 1024 * 1024;
});

string jwtIssuer = (builder.Configuration["Jwt:Issuer"] ?? "ReactServerSide").Trim();
string jwtAudience = (builder.Configuration["Jwt:Audience"] ?? "ReactProjectClient").Trim();
string jwtSecret = (builder.Configuration["Jwt:SecretKey"] ?? builder.Configuration["JWT_SECRET_KEY"] ?? string.Empty).Trim();

if (jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSecret = "development-secret-key-1234567890abc";
    }
    else
    {
        throw new InvalidOperationException("Jwt:SecretKey must be configured and at least 32 characters long.");
    }
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

// Setup Firebase Admin SDK credentials and Google Application Credentials
Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "acm-application-38298");
Environment.SetEnvironmentVariable("FIREBASE_PROJECT_ID", "acm-application-38298");

string credentialPath = Path.Combine(builder.Environment.ContentRootPath, "gcp-key.json");
if (File.Exists(credentialPath))
{
    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
    FirebaseApp.Create(new AppOptions()
    {
        Credential = GoogleCredential.FromFile(credentialPath),
        ProjectId = "acm-application-38298"
    });
}
else
{
    try
    {
        FirebaseApp.Create(new AppOptions()
        {
            Credential = GoogleCredential.GetApplicationDefault(),
            ProjectId = "acm-application-38298"
        });
    }
    catch (Exception)
    {
        if (builder.Environment.IsDevelopment())
        {
            // Local development can continue without Firebase credentials; the app will use the
            // other services that do not require initialization at boot.
        }
        else
        {
            throw;
        }
    }
}

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
    try
    {
        DBServices db = scope.ServiceProvider.GetRequiredService<DBServices>();
        db.EnsureChildrenSchemaReady();
    }
    catch (Exception)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("ExpoCors");

// Disable HttpsRedirection since the deployment uses a raw HTTP public IP without SSL.
// if (!app.Environment.IsDevelopment())
// {
//     app.UseHttpsRedirection();
// }

app.MapGet("/", () => Results.Ok(new { status = "healthy", message = "React Server is running" }));

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
