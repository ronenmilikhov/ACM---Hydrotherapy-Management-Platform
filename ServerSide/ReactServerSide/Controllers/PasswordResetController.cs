using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PasswordResetController : ControllerBase
    {
        private const string PasswordHashPrefix = "pbkdf2";
        private const int PasswordHashIterations = 120000;
        private const int PasswordSaltSizeBytes = 16;
        private const int PasswordKeySizeBytes = 32;

        private readonly AmazonDynamoDBClient _dynamoDbClient;
        private readonly FirestoreDb _firestoreDb;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public PasswordResetController(Microsoft.Extensions.Configuration.IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _dynamoDbClient = new AmazonDynamoDBClient();
            _firestoreDb = FirestoreDb.Create();
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestReset([FromBody] ResetRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Email))
                return BadRequest(new { message = "יש להזין כתובת אימייל." });

            string email = System.Text.RegularExpressions.Regex.Replace(dto.Email, @"[\u200B-\u200D\u200E\u200F\uFEFF]", "").Trim().ToLower();

            bool emailExists = await CheckEmailExistsInDb(email);

            if (emailExists)
            {
                try
                {
                    await EnsureFirebaseUserExists(email);

                    // Request password reset email from Firebase Auth
                    string apiKey = Environment.GetEnvironmentVariable("FIREBASE_API_KEY") ?? _configuration["Firebase:ApiKey"] ?? "";
                    string url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={apiKey}";

                    var payload = new
                    {
                        requestType = "PASSWORD_RESET",
                        email = email
                    };

                    using var client = _httpClientFactory.CreateClient();
                    // Add locale header to send email in Hebrew
                    client.DefaultRequestHeaders.Add("X-Firebase-Locale", "he");

                    var json = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await client.PostAsync(url, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[PasswordReset] Firebase sendOobCode failed for {email}: {response.StatusCode} - {errorContent}");
                        return BadRequest(new { message = "שגיאה בשליחת אימייל לאיפוס סיסמה דרך Firebase. אנא נסו שוב." });
                    }

                    Console.WriteLine($"[PasswordReset] Successfully triggered Firebase password reset for {email}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PasswordReset] Firebase error for {email}: {ex.Message}");
                    return BadRequest(new { message = "שגיאה באיפוס הסיסמה. אנא נסו שוב." });
                }
            }

            return Ok(new { message = "אם האימייל קיים במערכת, אימייל לאיפוס סיסמה נשלח אליו בהצלחה." });
        }

        [HttpPost("confirm")]
        public IActionResult ConfirmReset([FromBody] ConfirmResetDto dto)
        {
            return BadRequest(new { message = "איפוס סיסמה מתבצע כעת ישירות דרך הקישור שנשלח לאימייל שלך. אין צורך להזין קוד באפליקציה." });
        }

        private async Task<bool> CheckEmailExistsInDb(string email)
        {
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };

            var instructorResponse = await _dynamoDbClient.GetItemAsync("Instructors", key);
            if (instructorResponse.Item != null && instructorResponse.Item.Count > 0)
            {
                bool isActive = instructorResponse.Item.ContainsKey("IsActive") && instructorResponse.Item["IsActive"].BOOL == true;
                if (isActive) return true;
            }

            var parentResponse = await _dynamoDbClient.GetItemAsync("Parents", key);
            if (parentResponse.Item != null && parentResponse.Item.Count > 0)
            {
                bool isActive = parentResponse.Item.ContainsKey("IsActive") && parentResponse.Item["IsActive"].BOOL == true;
                if (isActive) return true;
            }

            return false;
        }

        private async Task EnsureFirebaseUserExists(string email)
        {
            try
            {
                await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email);
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
            {
                var args = new UserRecordArgs
                {
                    Email = email,
                    EmailVerified = true,
                    Password = "TempPass1!"
                };
                await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
            }
        }

        private async Task UpdatePasswordInDb(string email, string newPassword)
        {
            string hashedPassword = HashPassword(newPassword);
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["PasswordHash"] = new AttributeValueUpdate
                {
                    Action = AttributeAction.PUT,
                    Value = new AttributeValue { S = hashedPassword }
                }
            };

            var instructorResponse = await _dynamoDbClient.GetItemAsync("Instructors", key);
            if (instructorResponse.Item != null && instructorResponse.Item.Count > 0)
            {
                await _dynamoDbClient.UpdateItemAsync("Instructors", key, updates);
            }

            var parentResponse = await _dynamoDbClient.GetItemAsync("Parents", key);
            if (parentResponse.Item != null && parentResponse.Item.Count > 0)
            {
                await _dynamoDbClient.UpdateItemAsync("Parents", key, updates);
            }
        }

        private static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(PasswordSaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                PasswordHashIterations,
                HashAlgorithmName.SHA256,
                PasswordKeySizeBytes);

            return $"{PasswordHashPrefix}${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }
    }

    public class ResetRequestDto
    {
        public string? Email { get; set; }
    }

    public class ConfirmResetDto
    {
        public string? Email { get; set; }
        public string? Code { get; set; }
        public string? NewPassword { get; set; }
    }
}
