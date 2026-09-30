using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;
using System.Net.Http;
using FirebaseAdmin.Auth;

namespace ReactServerSide.DAL
{
    public class DBServices
    {
        private readonly string _connectionString;
        private readonly AmazonDynamoDBClient _dynamoDbClient;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private const string PasswordHashPrefix = "pbkdf2";
        private const int PasswordHashIterations = 120000;
        private const int PasswordSaltSizeBytes = 16;
        private const int PasswordKeySizeBytes = 32;
        private static readonly Dictionary<string, string[]> GroupRecommendationConceptKeywords = new Dictionary<string, string[]>
        {
            ["motor"] = new[] { "motor", "coordination", "balance", "strength", "מוטור", "קואורדינציה", "שיווי", "יציבה", "כוח" },
            ["sensory"] = new[] { "sensory", "regulation", "touch", "noise", "חושי", "ויסות", "מגע", "רעש" },
            ["communication"] = new[] { "communication", "social", "verbal", "language", "תקשורת", "חברתי", "שפה", "דיבור" },
            ["confidence"] = new[] { "confidence", "anxiety", "fear", "self", "ביטחון", "חרדה", "פחד", "עצמי" },
            ["beginner"] = new[] { "beginner", "basic", "starter", "מתחיל", "בסיס", "ראשוני" },
            ["advanced"] = new[] { "advanced", "independent", "progress", "מתקדם", "עצמאי", "התקדמות" }
        };

        private static readonly Dictionary<string, string> GroupRecommendationConceptLabels = new Dictionary<string, string>
        {
            ["motor"] = "מוטוריקה",
            ["sensory"] = "ויסות חושי",
            ["communication"] = "תקשורת",
            ["confidence"] = "ביטחון רגשי",
            ["beginner"] = "רמת התחלה",
            ["advanced"] = "רמה מתקדמת"
        };

        private static readonly string[] PreferredAiMetricOrder =
        {
            "שליטה בנשימות (הכנסת ראש למים)",
            "יציבה וציפה",
            "הסתגלות וביטחון במים",
            "תנועתיות וקואורדינציה",
            "תקשורת במים (ושיתוף פעולה)",
            "התמדה ומאמץ"
        };

        private static readonly Dictionary<string, string> AiMetricAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["שליטה בנשימות"] = "שליטה בנשימות (הכנסת ראש למים)",
            ["שליטה בנשימות (הכנסת ראש למים)(הכנסת ראש למים)"] = "שליטה בנשימות (הכנסת ראש למים)",
            ["שליטה בנשימות (הכנסת ראש למים) (הכנסת ראש למים)"] = "שליטה בנשימות (הכנסת ראש למים)",
            ["ציפה על הבטן"] = "הסתגלות וביטחון במים",
            ["ציפה על הגב"] = "יציבה וציפה",
            ["תנועות ידיים"] = "תנועתיות וקואורדינציה",
            ["תנועות רגליים"] = "תקשורת במים (ושיתוף פעולה)",
            ["breathingControl"] = "שליטה בנשימות (הכנסת ראש למים)",
            ["backFloat"] = "יציבה וציפה",
            ["frontFloat"] = "הסתגלות וביטחון במים",
            ["armMovement"] = "תנועתיות וקואורדינציה",
            ["legMovement"] = "תקשורת במים (ושיתוף פעולה)",
            ["socialCommunication"] = "תקשורת במים (ושיתוף פעולה)",
            ["waterConfidence"] = "הסתגלות וביטחון במים",
            ["perseveranceAndEffort"] = "התמדה ומאמץ",
            ["persistenceEffort"] = "התמדה ומאמץ"
        };

        public DBServices(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _connectionString = configuration.GetConnectionString("myProjDB") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;";
            }

            _dynamoDbClient = new AmazonDynamoDBClient(RegionEndpoint.EUNorth1);
        }

        public AmazonDynamoDBClient DynamoDbClient => _dynamoDbClient;

        public SqlConnection OpenConnection()
        {
            SqlConnection con = new SqlConnection(_connectionString);
            con.Open();
            return con;
        }

        private Dictionary<string, AttributeValue>? GetParentByNumericId(int parentId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "Parents",
                FilterExpression = "Id = :val",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { N = parentId.ToString() }
                }
            };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            return res.Items.FirstOrDefault();
        }

        private Dictionary<string, AttributeValue>? GetInstructorByNumericId(int instructorId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "Instructors",
                FilterExpression = "Id = :val",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { N = instructorId.ToString() }
                }
            };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            return res.Items.FirstOrDefault();
        }

        private HashSet<int> GetInstructorIdsForChild(int parentId, int childId)
        {
            var instructorIds = new HashSet<int>();

            // 1. Group path
            var groupChildrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :cid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var activeGroupIds = new HashSet<int>();
            foreach (var item in groupChildrenScan.Items)
            {
                activeGroupIds.Add(int.Parse(item["GroupId"].N));
            }

            if (activeGroupIds.Count > 0)
            {
                var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
                var trulyActiveGroupIds = new HashSet<int>();
                foreach (var g in groupsScan.Items)
                {
                    int gid = int.Parse(g["Id"].N);
                    bool gActive = g.ContainsKey("IsActive") && g["IsActive"].BOOL == true;
                    if (gActive && activeGroupIds.Contains(gid))
                    {
                        trulyActiveGroupIds.Add(gid);
                    }
                }

                if (trulyActiveGroupIds.Count > 0)
                {
                    var instructorGroupsScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "InstructorGroups",
                        FilterExpression = "IsActive = :active",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true }
                        }
                    }).GetAwaiter().GetResult();

                    foreach (var ig in instructorGroupsScan.Items)
                    {
                        int gid = int.Parse(ig["GroupId"].N);
                        int instId = int.Parse(ig["InstructorId"].N);
                        if (trulyActiveGroupIds.Contains(gid))
                        {
                            instructorIds.Add(instId);
                        }
                    }
                }
            }

            // 2. Private training sessions path
            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "ChildId = :cid AND #stat <> :canc",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#stat"] = "Status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":canc"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var ts in tsScan.Items)
            {
                instructorIds.Add(int.Parse(ts["InstructorId"].N));
            }

            return instructorIds;
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

        private static bool IsPasswordHashFormat(string storedPassword)
        {
            return storedPassword.StartsWith($"{PasswordHashPrefix}$", StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyPassword(string rawPassword, string storedPassword)
        {
            if (string.IsNullOrEmpty(storedPassword))
            {
                return false;
            }

            if (!IsPasswordHashFormat(storedPassword))
            {
                // Backward compatibility for legacy plaintext rows.
                return string.Equals(storedPassword, rawPassword, StringComparison.Ordinal);
            }

            string[] parts = storedPassword.Split('$');
            if (parts.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out int iterations) || iterations < 10000)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    rawPassword,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void UpgradeInstructorPasswordHash(string email, string rawPassword)
        {
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["PasswordHash"] = new AttributeValueUpdate
                {
                    Action = AttributeAction.PUT,
                    Value = new AttributeValue { S = HashPassword(rawPassword) }
                }
            };
            _dynamoDbClient.UpdateItemAsync("Instructors", key, updates).GetAwaiter().GetResult();
        }

        private void UpgradeParentPasswordHash(string email, string rawPassword)
        {
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["PasswordHash"] = new AttributeValueUpdate
                {
                    Action = AttributeAction.PUT,
                    Value = new AttributeValue { S = HashPassword(rawPassword) }
                }
            };
            _dynamoDbClient.UpdateItemAsync("Parents", key, updates).GetAwaiter().GetResult();
        }

        private string AuthenticateWithFirebase(string email, string password)
        {
            try
            {
                email = System.Text.RegularExpressions.Regex.Replace(email, @"[\u200B-\u200D\u200E\u200F\uFEFF]", "").Trim().ToLowerInvariant();
                string apiKey = Environment.GetEnvironmentVariable("FIREBASE_API_KEY") ?? _configuration["Firebase:ApiKey"] ?? "";
                string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}";
                
                var payload = new
                {
                    email = email,
                    password = password,
                    returnSecureToken = true
                };

                using var client = _httpClientFactory.CreateClient();
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = client.PostAsync(url, content).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return "SUCCESS";
                }
                
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Console.WriteLine($"[Firebase Auth] SignIn failed for {email}: {response.StatusCode} - {errorContent}");
                
                if (!string.IsNullOrEmpty(errorContent))
                {
                    using var doc = JsonDocument.Parse(errorContent);
                    if (doc.RootElement.TryGetProperty("error", out var errorEl) &&
                        errorEl.TryGetProperty("message", out var msgEl))
                    {
                        string msg = msgEl.GetString() ?? "";
                        if (msg.Contains("EMAIL_NOT_FOUND") || msg.Contains("USER_NOT_FOUND"))
                        {
                            return "USER_NOT_FOUND";
                        }
                        if (msg.Contains("INVALID_PASSWORD"))
                        {
                            return "INVALID_PASSWORD";
                        }
                    }
                }
                
                return "ERROR";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase Auth] Exception during SignIn for {email}: {ex.Message}");
                return "ERROR";
            }
        }

        public AuthenticatedUser? PostAuthenticateUser(string email, string password)
        {
            // Clean hidden RLM, zero-width spaces and control chars from email
            email = System.Text.RegularExpressions.Regex.Replace(email, @"[\u200B-\u200D\u200E\u200F\uFEFF]", "").Trim().ToLowerInvariant();

            bool existsInFirebase = false;
            try
            {
                var userRecord = FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email).GetAwaiter().GetResult();
                existsInFirebase = true;
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
            {
                existsInFirebase = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase Auth] GetUserByEmail failed for {email}: {ex.Message}");
                existsInFirebase = false; // Fall back to local DB check in case of outage
            }

            if (existsInFirebase)
            {
                // User exists in Firebase, so they MUST authenticate via Firebase
                string fbResult = AuthenticateWithFirebase(email, password);
                if (fbResult == "SUCCESS")
                {
                    // Authenticated in Firebase. Load details from DynamoDB.
                    var instructorKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
                    try
                    {
                        var instructorResponse = _dynamoDbClient.GetItemAsync("Instructors", instructorKey).GetAwaiter().GetResult();
                        if (instructorResponse.Item != null && instructorResponse.Item.Count > 0)
                        {
                            var item = instructorResponse.Item;
                            bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                            if (isActive)
                            {
                                int id = int.Parse(item["Id"].N);
                                string role = item.ContainsKey("Role") ? item["Role"].S : "";
                                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                                
                                string storedPassword = item.ContainsKey("PasswordHash") ? item["PasswordHash"].S : "";
                                if (!VerifyPassword(password, storedPassword))
                                {
                                    // User updated password in Firebase (e.g. via reset link). Sync back to local DB.
                                    UpgradeInstructorPasswordHash(email, password);
                                }

                                return new AuthenticatedUser
                                {
                                    Id = id,
                                    Email = email,
                                    Role = role,
                                    UserType = "Instructor",
                                    FullName = $"{firstName} {lastName}".Trim()
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Login] Error loading instructor {email}: {ex.Message}");
                    }

                    var parentKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
                    try
                    {
                        var parentResponse = _dynamoDbClient.GetItemAsync("Parents", parentKey).GetAwaiter().GetResult();
                        if (parentResponse.Item != null && parentResponse.Item.Count > 0)
                        {
                            var item = parentResponse.Item;
                            bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                            if (isActive)
                            {
                                int id = int.Parse(item["Id"].N);
                                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                                string storedPassword = item.ContainsKey("PasswordHash") ? item["PasswordHash"].S : "";
                                if (!VerifyPassword(password, storedPassword))
                                {
                                    // User updated password in Firebase. Sync back to local DB.
                                    UpgradeParentPasswordHash(email, password);
                                }

                                return new AuthenticatedUser
                                {
                                    Id = id,
                                    Email = email,
                                    Role = "Parent",
                                    UserType = "Parent",
                                    FullName = $"{firstName} {lastName}".Trim()
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Login] Error loading parent {email}: {ex.Message}");
                    }
                }
                
                // If they exist in Firebase but Firebase authentication failed, reject login
                return null;
            }

            // Fallback for unmigrated users (doesn't exist in Firebase yet)
            // Look up in DynamoDB and verify local password hash
            var fallbackInstructorKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            try
            {
                var instructorResponse = _dynamoDbClient.GetItemAsync("Instructors", fallbackInstructorKey).GetAwaiter().GetResult();
                if (instructorResponse.Item != null && instructorResponse.Item.Count > 0)
                {
                    var item = instructorResponse.Item;
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isActive)
                    {
                        string storedPassword = item.ContainsKey("PasswordHash") ? item["PasswordHash"].S : "";
                        if (VerifyPassword(password, storedPassword))
                        {
                            int id = int.Parse(item["Id"].N);
                            string role = item.ContainsKey("Role") ? item["Role"].S : "";
                            string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                            string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                            if (!IsPasswordHashFormat(storedPassword))
                            {
                                UpgradeInstructorPasswordHash(email, password);
                            }

                            // Automatically register/migrate user to Firebase Auth
                            try
                            {
                                var args = new UserRecordArgs
                                {
                                    Email = email,
                                    EmailVerified = true,
                                    Password = password
                                };
                                FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
                                Console.WriteLine($"[Firebase Sync] Migrated instructor {email} to Firebase Auth.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Firebase Sync] Error migrating instructor {email}: {ex.Message}");
                            }

                            return new AuthenticatedUser
                            {
                                Id = id,
                                Email = email,
                                Role = role,
                                UserType = "Instructor",
                                FullName = $"{firstName} {lastName}".Trim()
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Login] Fallback instructor error for {email}: {ex.Message}");
            }

            var fallbackParentKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            try
            {
                var parentResponse = _dynamoDbClient.GetItemAsync("Parents", fallbackParentKey).GetAwaiter().GetResult();
                if (parentResponse.Item != null && parentResponse.Item.Count > 0)
                {
                    var item = parentResponse.Item;
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isActive)
                    {
                        string storedPassword = item.ContainsKey("PasswordHash") ? item["PasswordHash"].S : "";
                        if (VerifyPassword(password, storedPassword))
                        {
                            int id = int.Parse(item["Id"].N);
                            string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                            string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                            if (!IsPasswordHashFormat(storedPassword))
                            {
                                UpgradeParentPasswordHash(email, password);
                            }

                            // Automatically register/migrate user to Firebase Auth
                            try
                            {
                                var args = new UserRecordArgs
                                {
                                    Email = email,
                                    EmailVerified = true,
                                    Password = password
                                };
                                FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
                                Console.WriteLine($"[Firebase Sync] Migrated parent {email} to Firebase Auth.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Firebase Sync] Error migrating parent {email}: {ex.Message}");
                            }

                            return new AuthenticatedUser
                            {
                                Id = id,
                                Email = email,
                                Role = "Parent",
                                UserType = "Parent",
                                FullName = $"{firstName} {lastName}".Trim()
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Login] Fallback parent error for {email}: {ex.Message}");
            }

            return null;
        }

        public bool PostIsManagerCredentialsValid(string email, string password)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                ["Email"] = new AttributeValue { S = email }
            };

            try
            {
                var response = _dynamoDbClient.GetItemAsync("Instructors", key).GetAwaiter().GetResult();
                if (response.Item != null && response.Item.Count > 0)
                {
                    var item = response.Item;
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    string role = item.ContainsKey("Role") ? item["Role"].S : "";
                    if (isActive && role == "Manager")
                    {
                        string storedPassword = item.ContainsKey("PasswordHash") ? item["PasswordHash"].S : "";
                        bool isValid = VerifyPassword(password, storedPassword);
                        if (isValid)
                        {
                            if (!IsPasswordHashFormat(storedPassword))
                            {
                                UpgradeInstructorPasswordHash(email, password);
                            }
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fail
            }
            return false;
        }

        public bool PostEmailExists(string email)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                ["Email"] = new AttributeValue { S = email }
            };

            try
            {
                var instructorResponse = _dynamoDbClient.GetItemAsync("Instructors", key).GetAwaiter().GetResult();
                if (instructorResponse.Item != null && instructorResponse.Item.Count > 0)
                {
                    return true;
                }

                var parentResponse = _dynamoDbClient.GetItemAsync("Parents", key).GetAwaiter().GetResult();
                if (parentResponse.Item != null && parentResponse.Item.Count > 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore
            }

            return false;
        }

        public void EnsureChildrenSchemaReady()
        {
            // DynamoDB is schemaless, no schema updates required at startup.
        }

        private static void EnsureReportChildrenTableReady(SqlConnection con)
        {
            // Check if the new schema column (ExerciseKey) exists.
            // If not, drop the old table and recreate with the new schema.
            const string checkColumnSql = @"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'ReportChildren'
                  AND COLUMN_NAME = 'ExerciseKey';";

            using (SqlCommand checkCmd = new SqlCommand(checkColumnSql, con))
            {
                int columnExists = Convert.ToInt32(checkCmd.ExecuteScalar());
                if (columnExists > 0)
                {
                    return; // New schema already in place.
                }
            }

            const string dropAndRecreateSql = @"
                IF OBJECT_ID('dbo.ReportChildren', 'U') IS NOT NULL
                    DROP TABLE dbo.ReportChildren;

                CREATE TABLE dbo.ReportChildren (
                    ReportId        INT IDENTITY(1,1) PRIMARY KEY,
                    ChildId         INT NOT NULL,
                    InstructorId    INT NOT NULL,
                    GroupId         INT NULL,
                    ReportDate      DATE NOT NULL,
                    ExerciseKey     NVARCHAR(50) NOT NULL,
                    ExerciseTitle   NVARCHAR(100) NOT NULL,
                    IsPresent       BIT NOT NULL DEFAULT 1,
                    Comment         NVARCHAR(MAX) NULL,
                    Metrics         NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );";

            using (SqlCommand createCmd = new SqlCommand(dropAndRecreateSql, con))
            {
                createCmd.ExecuteNonQuery();
            }
        }

        public int PostCreateInstructor(string email, string password, string firstName, string lastName)
        {
            string normalizedEmail = email.Trim().ToLowerInvariant();
            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            string hashedPassword = HashPassword(password);

            // 1. Check if email already exists in Parents
            var parentKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = normalizedEmail } };
            var parentRes = _dynamoDbClient.GetItemAsync("Parents", parentKey).GetAwaiter().GetResult();
            if (parentRes.Item != null && parentRes.Item.Count > 0)
            {
                throw new InvalidOperationException("Email already exists.");
            }

            // 2. Check if email already exists in Instructors
            var instructorKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = normalizedEmail } };
            var instructorRes = _dynamoDbClient.GetItemAsync("Instructors", instructorKey).GetAwaiter().GetResult();

            if (instructorRes.Item != null && instructorRes.Item.Count > 0)
            {
                var item = instructorRes.Item;
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                string role = item.ContainsKey("Role") ? item["Role"].S : "";
                
                if (!string.Equals(role, "Instructor", StringComparison.OrdinalIgnoreCase) || isActive)
                {
                    throw new InvalidOperationException("Email already exists.");
                }

                // Reactivate inactive instructor
                int existingId = int.Parse(item["Id"].N);
                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["PasswordHash"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = hashedPassword } },
                    ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedFirstName } },
                    ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedLastName } },
                    ["Role"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Instructor" } },
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } }
                };
                _dynamoDbClient.UpdateItemAsync("Instructors", instructorKey, updates).GetAwaiter().GetResult();

                // Sync reactivated user to Firebase Auth
                try
                {
                    UserRecord userRecord;
                    try
                    {
                        userRecord = FirebaseAuth.DefaultInstance.GetUserByEmailAsync(normalizedEmail).GetAwaiter().GetResult();
                        var updateArgs = new UserRecordArgs
                        {
                            Uid = userRecord.Uid,
                            Password = password
                        };
                        FirebaseAuth.DefaultInstance.UpdateUserAsync(updateArgs).GetAwaiter().GetResult();
                    }
                    catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
                    {
                        var args = new UserRecordArgs
                        {
                            Email = normalizedEmail,
                            EmailVerified = true,
                            Password = password
                        };
                        FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Firebase Sync] Error syncing reactivated instructor {normalizedEmail}: {ex.Message}");
                }

                return existingId;
            }

            // 3. Find max ID in Instructors to generate a new ID
            int maxId = 0;
            var scanRequest = new ScanRequest { TableName = "Instructors", ProjectionExpression = "Id" };
            var scanResponse = _dynamoDbClient.ScanAsync(scanRequest).GetAwaiter().GetResult();
            foreach (var item in scanResponse.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int id))
                {
                    if (id > maxId) maxId = id;
                }
            }
            int newId = maxId + 1;

            // 4. Insert new Instructor item
            var newInstructor = new Dictionary<string, AttributeValue>
            {
                ["Email"] = new AttributeValue { S = normalizedEmail },
                ["PasswordHash"] = new AttributeValue { S = hashedPassword },
                ["Role"] = new AttributeValue { S = "Instructor" },
                ["IsActive"] = new AttributeValue { BOOL = true },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["FirstName"] = new AttributeValue { S = normalizedFirstName },
                ["LastName"] = new AttributeValue { S = normalizedLastName },
                ["Id"] = new AttributeValue { N = newId.ToString() }
            };
            _dynamoDbClient.PutItemAsync("Instructors", newInstructor).GetAwaiter().GetResult();

            // Sync newly created instructor to Firebase Auth
            try
            {
                var args = new UserRecordArgs
                {
                    Email = normalizedEmail,
                    EmailVerified = true,
                    Password = password
                };
                FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase Sync] Error creating instructor {normalizedEmail} in Firebase Auth: {ex.Message}");
            }

            return newId;
        }

        public int PostCreateParent(string email, string password, string firstName, string lastName, string? phone)
        {
            string normalizedEmail = email.Trim().ToLowerInvariant();
            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            string normalizedPhone = string.IsNullOrWhiteSpace(phone) ? "" : phone.Trim();
            string hashedPassword = HashPassword(password);

            // 1. Check if email already exists in Instructors
            var instructorKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = normalizedEmail } };
            var instructorRes = _dynamoDbClient.GetItemAsync("Instructors", instructorKey).GetAwaiter().GetResult();
            if (instructorRes.Item != null && instructorRes.Item.Count > 0)
            {
                throw new InvalidOperationException("Email already exists.");
            }

            // 2. Check if email already exists in Parents
            var parentKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = normalizedEmail } };
            var parentRes = _dynamoDbClient.GetItemAsync("Parents", parentKey).GetAwaiter().GetResult();

            if (parentRes.Item != null && parentRes.Item.Count > 0)
            {
                var item = parentRes.Item;
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                
                if (isActive)
                {
                    throw new InvalidOperationException("Email already exists.");
                }

                // Reactivate inactive parent
                int existingId = int.Parse(item["Id"].N);
                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["PasswordHash"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = hashedPassword } },
                    ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedFirstName } },
                    ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedLastName } },
                    ["Phone"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedPhone } },
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } }
                };
                _dynamoDbClient.UpdateItemAsync("Parents", parentKey, updates).GetAwaiter().GetResult();

                // Sync reactivated parent to Firebase Auth
                try
                {
                    UserRecord userRecord;
                    try
                    {
                        userRecord = FirebaseAuth.DefaultInstance.GetUserByEmailAsync(normalizedEmail).GetAwaiter().GetResult();
                        var updateArgs = new UserRecordArgs
                        {
                            Uid = userRecord.Uid,
                            Password = password
                        };
                        FirebaseAuth.DefaultInstance.UpdateUserAsync(updateArgs).GetAwaiter().GetResult();
                    }
                    catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
                    {
                        var args = new UserRecordArgs
                        {
                            Email = normalizedEmail,
                            EmailVerified = true,
                            Password = password
                        };
                        FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Firebase Sync] Error syncing reactivated parent {normalizedEmail}: {ex.Message}");
                }

                return existingId;
            }

            // 3. Find max ID in Parents to generate a new ID
            int maxId = 0;
            var scanRequest = new ScanRequest { TableName = "Parents", ProjectionExpression = "Id" };
            var scanResponse = _dynamoDbClient.ScanAsync(scanRequest).GetAwaiter().GetResult();
            foreach (var item in scanResponse.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int id))
                {
                    if (id > maxId) maxId = id;
                }
            }
            int newId = maxId + 1;

            // 4. Insert new Parent item
            var newParent = new Dictionary<string, AttributeValue>
            {
                ["Email"] = new AttributeValue { S = normalizedEmail },
                ["PasswordHash"] = new AttributeValue { S = hashedPassword },
                ["FirstName"] = new AttributeValue { S = normalizedFirstName },
                ["LastName"] = new AttributeValue { S = normalizedLastName },
                ["Phone"] = new AttributeValue { S = normalizedPhone },
                ["IsActive"] = new AttributeValue { BOOL = true },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["Id"] = new AttributeValue { N = newId.ToString() }
            };
            _dynamoDbClient.PutItemAsync("Parents", newParent).GetAwaiter().GetResult();

            // Sync newly created parent to Firebase Auth
            try
            {
                var args = new UserRecordArgs
                {
                    Email = normalizedEmail,
                    EmailVerified = true,
                    Password = password
                };
                FirebaseAuth.DefaultInstance.CreateUserAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase Sync] Error creating parent {normalizedEmail} in Firebase Auth: {ex.Message}");
            }

            return newId;
        }

        public List<ManagerParentRecord> GetParentsForManager()
        {
            var parents = new List<ManagerParentRecord>();
            var parentsById = new Dictionary<int, ManagerParentRecord>();

            // 1. Scan Parents
            var scanParentsReq = new ScanRequest { TableName = "Parents" };
            var scanParentsRes = _dynamoDbClient.ScanAsync(scanParentsReq).GetAwaiter().GetResult();
            foreach (var item in scanParentsRes.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (!isActive) continue;

                int id = int.Parse(item["Id"].N);
                string email = item.ContainsKey("Email") ? item["Email"].S : "";
                string phone = item.ContainsKey("Phone") ? item["Phone"].S : "";
                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                string fullName = $"{firstName} {lastName}".Trim();

                var parent = new ManagerParentRecord
                {
                    ParentId = id,
                    Email = email,
                    Phone = phone,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? email : fullName,
                    ChildNames = new List<string>()
                };

                parents.Add(parent);
                parentsById[id] = parent;
            }

            if (parents.Count == 0)
            {
                return parents;
            }

            // 2. Scan Children to populate ChildNames
            var scanChildrenReq = new ScanRequest { TableName = "Children" };
            var scanChildrenRes = _dynamoDbClient.ScanAsync(scanChildrenReq).GetAwaiter().GetResult();
            foreach (var item in scanChildrenRes.Items)
            {
                bool childIsActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (!childIsActive) continue;

                int parentId = int.Parse(item["ParentId"].N);
                if (!parentsById.TryGetValue(parentId, out var parent))
                {
                    continue;
                }

                string childFirstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string childLastName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                string childName = $"{childFirstName} {childLastName}".Trim();

                if (string.IsNullOrWhiteSpace(childName))
                {
                    childName = $"Child {item["Id"].N}";
                }

                parent.ChildNames.Add(childName);
            }

            // Sort parents by FirstName, LastName, ParentId (matching the SQL query ORDER BY)
            return parents
                .OrderBy(p => p.FirstName, StringComparer.Ordinal)
                .ThenBy(p => p.LastName, StringComparer.Ordinal)
                .ThenBy(p => p.ParentId)
                .ToList();
        }

        public bool PutUpdateParentNames(int parentId, string firstName, string lastName)
        {
            var parent = GetParentByNumericId(parentId);
            if (parent == null || !parent.ContainsKey("IsActive") || parent["IsActive"].BOOL != true)
            {
                return false;
            }
            string email = parent["Email"].S;
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = firstName.Trim() } },
                ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = lastName.Trim() } }
            };
            try
            {
                _dynamoDbClient.UpdateItemAsync("Parents", key, updates).GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteDeactivateParentForManager(int parentId)
        {
            var parent = GetParentByNumericId(parentId);
            if (parent == null || !parent.ContainsKey("IsActive") || parent["IsActive"].BOOL != true)
            {
                return false;
            }

            string parentEmail = parent["Email"].S;
            
            // 1. Deactivate parent
            var parentKey = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = parentEmail } };
            var parentUpdates = new Dictionary<string, AttributeValueUpdate>
            {
                ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
            };
            _dynamoDbClient.UpdateItemAsync("Parents", parentKey, parentUpdates).GetAwaiter().GetResult();

            // 2. Deactivate children
            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "ParentId = :pid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var child in childrenScan.Items)
            {
                int childId = int.Parse(child["Id"].N);
                var childKey = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = childId.ToString() } };
                var childUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("Children", childKey, childUpdates).GetAwaiter().GetResult();

                // Deactivate in GroupChildren
                var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "GroupChildren",
                    FilterExpression = "ChildId = :cid AND IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = childId.ToString() },
                        [":active"] = new AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                foreach (var gc in gcScan.Items)
                {
                    var gcKey = new Dictionary<string, AttributeValue>
                    {
                        ["GroupId"] = gc["GroupId"],
                        ["ChildId"] = gc["ChildId"]
                    };
                    var gcUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                    };
                    _dynamoDbClient.UpdateItemAsync("GroupChildren", gcKey, gcUpdates).GetAwaiter().GetResult();
                }

                // Deactivate Conversations for child
                var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Conversations",
                    FilterExpression = "ChildId = :cid AND IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = childId.ToString() },
                        [":active"] = new AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                foreach (var conv in convScan.Items)
                {
                    var convKey = new Dictionary<string, AttributeValue> { ["Id"] = conv["Id"] };
                    var convUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                    };
                    _dynamoDbClient.UpdateItemAsync("Conversations", convKey, convUpdates).GetAwaiter().GetResult();
                }
            }

            // 3. Deactivate parent's Conversations (just in case they weren't covered by child check)
            var parentConvScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Conversations",
                FilterExpression = "ParentId = :pid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var conv in parentConvScan.Items)
            {
                var convKey = new Dictionary<string, AttributeValue> { ["Id"] = conv["Id"] };
                var convUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("Conversations", convKey, convUpdates).GetAwaiter().GetResult();
            }

            // 4. Deactivate PushDeviceTokens
            var tokenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "PushDeviceTokens",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = "Parent" },
                    [":uid"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var token in tokenScan.Items)
            {
                var tokenKey = new Dictionary<string, AttributeValue> { ["ExpoPushToken"] = token["ExpoPushToken"] };
                var tokenUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("PushDeviceTokens", tokenKey, tokenUpdates).GetAwaiter().GetResult();
            }

            return true;
        }

        public List<ManagerInstructorRecord> GetInstructorsForManager()
        {
            var instructors = new List<ManagerInstructorRecord>();
            var instructorsById = new Dictionary<int, ManagerInstructorRecord>();

            // 1. Scan Instructors
            var scanIRes = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Instructors",
                FilterExpression = "IsActive = :active AND #r = :role",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#r"] = "Role" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true },
                    [":role"] = new AttributeValue { S = "Instructor" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in scanIRes.Items)
            {
                int id = int.Parse(item["Id"].N);
                string email = item.ContainsKey("Email") ? item["Email"].S : "";
                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                string fullName = $"{firstName} {lastName}".Trim();

                var instructor = new ManagerInstructorRecord
                {
                    InstructorId = id,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? email : fullName,
                    GroupNames = new List<string>()
                };

                instructors.Add(instructor);
                instructorsById[id] = instructor;
            }

            if (instructors.Count == 0)
            {
                return instructors;
            }

            // 2. Scan Groups and filter active ones
            var scanGRes = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Groups",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var groupsMap = new Dictionary<int, string>();
            foreach (var g in scanGRes.Items)
            {
                int gid = int.Parse(g["Id"].N);
                string name = g.ContainsKey("Name") ? g["Name"].S : "";
                groupsMap[gid] = name;
            }

            // 3. Scan InstructorGroups and populate GroupNames
            var scanIgRes = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var instructorGroupRelations = scanIgRes.Items
                .Select(item => new
                {
                    InstructorId = int.Parse(item["InstructorId"].N),
                    GroupId = int.Parse(item["GroupId"].N)
                })
                .OrderBy(ig => ig.InstructorId)
                .ToList();

            foreach (var relation in instructorGroupRelations)
            {
                if (instructorsById.TryGetValue(relation.InstructorId, out var instructor))
                {
                    if (groupsMap.TryGetValue(relation.GroupId, out string groupName))
                    {
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            instructor.GroupNames.Add(groupName);
                        }
                    }
                }
            }

            // Order instructors by FirstName, LastName, InstructorId
            return instructors
                .OrderBy(i => i.FirstName, StringComparer.Ordinal)
                .ThenBy(i => i.LastName, StringComparer.Ordinal)
                .ThenBy(i => i.InstructorId)
                .ToList();
        }

        public bool PutUpdateInstructorNames(int instructorId, string firstName, string lastName)
        {
            var instructor = GetInstructorByNumericId(instructorId);
            if (instructor == null || !instructor.ContainsKey("IsActive") || instructor["IsActive"].BOOL != true)
            {
                return false;
            }
            string email = instructor["Email"].S;
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = firstName.Trim() } },
                ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = lastName.Trim() } }
            };
            try
            {
                _dynamoDbClient.UpdateItemAsync("Instructors", key, updates).GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteDeactivateInstructorForManager(int instructorId)
        {
            var instructor = GetInstructorByNumericId(instructorId);
            if (instructor == null || !instructor.ContainsKey("IsActive") || instructor["IsActive"].BOOL != true)
            {
                return false;
            }

            string email = instructor["Email"].S;

            // 1. Deactivate Instructor
            var key = new Dictionary<string, AttributeValue> { ["Email"] = new AttributeValue { S = email } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
            };
            _dynamoDbClient.UpdateItemAsync("Instructors", key, updates).GetAwaiter().GetResult();

            // 2. Deactivate InstructorGroups
            var igScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "InstructorId = :instId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var ig in igScan.Items)
            {
                var igKey = new Dictionary<string, AttributeValue> { ["Id"] = ig["Id"] };
                var igUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("InstructorGroups", igKey, igUpdates).GetAwaiter().GetResult();
            }

            // 3. Deactivate Conversations
            var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Conversations",
                FilterExpression = "InstructorId = :instId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var conv in convScan.Items)
            {
                var convKey = new Dictionary<string, AttributeValue> { ["Id"] = conv["Id"] };
                var convUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("Conversations", convKey, convUpdates).GetAwaiter().GetResult();
            }

            // 4. Deactivate PushDeviceTokens
            var tokenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "PushDeviceTokens",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = "Instructor" },
                    [":uid"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var token in tokenScan.Items)
            {
                var tokenKey = new Dictionary<string, AttributeValue> { ["ExpoPushToken"] = token["ExpoPushToken"] };
                var tokenUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("PushDeviceTokens", tokenKey, tokenUpdates).GetAwaiter().GetResult();
            }

            return true;
        }

        public bool GetInstructorExists(int instructorId)
        {
            var instructor = GetInstructorByNumericId(instructorId);
            if (instructor == null) return false;
            bool isActive = instructor.ContainsKey("IsActive") && instructor["IsActive"].BOOL == true;
            string role = instructor.ContainsKey("Role") ? instructor["Role"].S : "";
            return isActive && string.Equals(role, "Instructor", StringComparison.OrdinalIgnoreCase);
        }

        public bool GetManagerExists(int managerId)
        {
            var manager = GetInstructorByNumericId(managerId);
            if (manager == null) return false;
            bool isActive = manager.ContainsKey("IsActive") && manager["IsActive"].BOOL == true;
            string role = manager.ContainsKey("Role") ? manager["Role"].S : "";
            return isActive && string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
        }

        public string GetInstructorFullName(int instructorId)
        {
            var instructor = GetInstructorByNumericId(instructorId);
            if (instructor != null)
            {
                string firstName = instructor.ContainsKey("FirstName") ? instructor["FirstName"].S : "";
                string lastName = instructor.ContainsKey("LastName") ? instructor["LastName"].S : "";
                string fullName = $"{firstName} {lastName}".Trim();
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    return fullName;
                }
            }
            return "המדריך";
        }

        public bool GetChildAssignedToInstructor(int instructorId, int childId, int? groupId = null)
        {
            // 1. Get child's active groups
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :cid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var childGroupIds = gcScan.Items
                .Select(item => int.Parse(item["GroupId"].N))
                .ToHashSet();

            if (groupId.HasValue)
            {
                if (!childGroupIds.Contains(groupId.Value))
                {
                    return false;
                }
                childGroupIds = new HashSet<int> { groupId.Value };
            }

            if (childGroupIds.Count == 0)
            {
                return false;
            }

            // 2. Check if instructor is assigned to any of these groups
            var igScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "InstructorId = :instId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in igScan.Items)
            {
                int gId = int.Parse(item["GroupId"].N);
                if (childGroupIds.Contains(gId))
                {
                    return true;
                }
            }

            return false;
        }

        public bool GetChildHadLessonOnDate(int instructorId, int childId, DateTime reportDate, int? groupId = null)
        {
            string dateStr = reportDate.ToString("yyyy-MM-dd");

            // 1. Get invitations
            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "InstructorId = :instId AND MeetingDate = :mdate AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":mdate"] = new AttributeValue { S = dateStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var invitationIds = liScan.Items
                .Select(item => int.Parse(item["InvitationId"].N))
                .ToHashSet();

            if (invitationIds.Count == 0)
            {
                return false;
            }

            // 2. Check recipients
            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "ChildId = :cid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in lirScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                if (invitationIds.Contains(invId))
                {
                    return true;
                }
            }

            return false;
        }

        public List<DateTime> GetLessonDatesForChild(int instructorId, int childId)
        {
            string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // 1. Get candidate invitations
            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "InstructorId = :instId AND LessonType = :privType AND MeetingDate <= :today AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":privType"] = new AttributeValue { S = "Private" },
                    [":today"] = new AttributeValue { S = todayStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var invitationMap = new Dictionary<int, string>(); // InvitationId -> MeetingDate
            foreach (var item in liScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                string mDate = item["MeetingDate"].S;
                invitationMap[invId] = mDate;
            }

            var dates = new List<DateTime>();
            if (invitationMap.Count == 0)
            {
                return dates;
            }

            // 2. Get recipients
            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "ChildId = :cid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var uniqueDates = new HashSet<string>();
            foreach (var item in lirScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                if (invitationMap.TryGetValue(invId, out string? mdate))
                {
                    uniqueDates.Add(mdate);
                }
            }

            foreach (var dateStr in uniqueDates)
            {
                if (DateTime.TryParse(dateStr, out DateTime dt))
                {
                    dates.Add(dt.Date);
                }
            }

            return dates.OrderByDescending(d => d).ToList();
        }

        private List<Dictionary<string, AttributeValue>> GetActiveGroupsForInstructor(int instructorId)
        {
            var activeGroups = new List<Dictionary<string, AttributeValue>>();

            // 1. Scan InstructorGroups
            var scanIg = new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "InstructorId = :instId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var igRes = _dynamoDbClient.ScanAsync(scanIg).GetAwaiter().GetResult();
            var activeGroupIds = igRes.Items.Select(ig => int.Parse(ig["GroupId"].N)).ToHashSet();

            if (activeGroupIds.Count == 0) return activeGroups;

            // 2. Scan Groups and filter active
            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            foreach (var g in groupsScan.Items)
            {
                int gid = int.Parse(g["Id"].N);
                bool isActive = g.ContainsKey("IsActive") && g["IsActive"].BOOL == true;
                if (isActive && activeGroupIds.Contains(gid))
                {
                    activeGroups.Add(g);
                }
            }

            return activeGroups;
        }

        public List<InstructorGroupRecord> GetGroupsForInstructor(int instructorId)
        {
            var groups = new List<InstructorGroupRecord>();

            var activeGroups = GetActiveGroupsForInstructor(instructorId);
            if (activeGroups.Count == 0) return groups;

            var activeGroupIds = activeGroups.Select(g => int.Parse(g["Id"].N)).ToHashSet();

            var groupChildrenCounts = new Dictionary<int, int>();
            var scanGc = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var activeChildrenIds = new HashSet<int>();
            var scanC = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();
            foreach (var c in scanC.Items)
            {
                activeChildrenIds.Add(int.Parse(c["Id"].N));
            }

            foreach (var gc in scanGc.Items)
            {
                int gid = int.Parse(gc["GroupId"].N);
                int cid = int.Parse(gc["ChildId"].N);
                if (activeGroupIds.Contains(gid) && activeChildrenIds.Contains(cid))
                {
                    if (!groupChildrenCounts.ContainsKey(gid)) groupChildrenCounts[gid] = 0;
                    groupChildrenCounts[gid]++;
                }
            }

            foreach (var g in activeGroups)
            {
                int gid = int.Parse(g["Id"].N);
                string name = g.ContainsKey("Name") ? g["Name"].S : "";
                string desc = g.ContainsKey("Description") ? g["Description"].S : "";
                int childCount = groupChildrenCounts.ContainsKey(gid) ? groupChildrenCounts[gid] : 0;

                groups.Add(new InstructorGroupRecord
                {
                    GroupId = gid,
                    Name = name,
                    Description = desc,
                    ActiveChildrenCount = childCount
                });
            }

            return groups.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.GroupId).ToList();
        }

        public InstructorGroupRecord? GetInstructorGroupById(int instructorId, int groupId)
        {
            var groups = GetGroupsForInstructor(instructorId);
            return groups.FirstOrDefault(g => g.GroupId == groupId);
        }

        public int? GetAssignedInstructorIdForGroup(int groupId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "GroupId = :gid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":gid"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            
            var activeInstructorIds = new HashSet<int>();
            var scanI = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Instructors",
                FilterExpression = "IsActive = :active AND #role = :role",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#role"] = "Role"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true },
                    [":role"] = new AttributeValue { S = "Instructor" }
                }
            }).GetAwaiter().GetResult();
            foreach (var item in scanI.Items)
            {
                activeInstructorIds.Add(int.Parse(item["Id"].N));
            }

            var matches = res.Items
                .Select(ig => int.Parse(ig["InstructorId"].N))
                .Where(id => activeInstructorIds.Contains(id))
                .OrderBy(id => id)
                .ToList();

            return matches.Count > 0 ? (int?)matches[0] : null;
        }

        public List<InstructorChildRecord> GetChildrenForInstructorGroup(int instructorId, int groupId)
        {
            var childrenList = new List<InstructorChildRecord>();

            var scanIg = new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "InstructorId = :instId AND GroupId = :gid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":gid"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var igRes = _dynamoDbClient.ScanAsync(scanIg).GetAwaiter().GetResult();
            if (igRes.Items.Count == 0) return childrenList;

            var groupKey = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = groupId.ToString() } };
            var groupRes = _dynamoDbClient.GetItemAsync("Groups", groupKey).GetAwaiter().GetResult();
            if (groupRes.Item == null || !groupRes.Item.ContainsKey("IsActive") || groupRes.Item["IsActive"].BOOL != true)
            {
                return childrenList;
            }

            var scanGc = new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "GroupId = :gid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":gid"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var gcRes = _dynamoDbClient.ScanAsync(scanGc).GetAwaiter().GetResult();
            var childIds = gcRes.Items.Select(gc => int.Parse(gc["ChildId"].N)).ToHashSet();

            if (childIds.Count == 0) return childrenList;

            var parentsById = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanParents = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
            foreach (var p in scanParents.Items)
            {
                if (p.ContainsKey("Id") && int.TryParse(p["Id"].N, out int pid))
                {
                    parentsById[pid] = p;
                }
            }

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var c in childrenScan.Items)
            {
                int cid = int.Parse(c["Id"].N);
                if (childIds.Contains(cid))
                {
                    int parentId = int.Parse(c["ParentId"].N);
                    string firstName = c.ContainsKey("FirstName") ? c["FirstName"].S : "";
                    string lastName = c.ContainsKey("LastName") ? c["LastName"].S : "";
                    string parentEmail = "";
                    string parentFirstName = "";
                    string parentLastName = "";

                    if (parentsById.TryGetValue(parentId, out var p))
                    {
                        parentEmail = p.ContainsKey("Email") ? p["Email"].S : "";
                        parentFirstName = p.ContainsKey("FirstName") ? p["FirstName"].S : "";
                        parentLastName = p.ContainsKey("LastName") ? p["LastName"].S : "";
                    }

                    childrenList.Add(new InstructorChildRecord
                    {
                        ChildId = cid,
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = $"{firstName} {lastName}".Trim(),
                        ParentId = parentId,
                        ParentEmail = parentEmail,
                        ParentFullName = $"{parentFirstName} {parentLastName}".Trim()
                    });
                }
            }

            return childrenList.OrderBy(c => c.FirstName, StringComparer.Ordinal)
                               .ThenBy(c => c.LastName, StringComparer.Ordinal)
                               .ThenBy(c => c.ChildId)
                               .ToList();
        }

        public List<InstructorGroupWithChildrenRecord> GetChildrenByGroupsForInstructor(int instructorId)
        {
            var groupsWithChildren = new List<InstructorGroupWithChildrenRecord>();

            var activeGroups = GetActiveGroupsForInstructor(instructorId);
            if (activeGroups.Count == 0) return groupsWithChildren;

            foreach (var g in activeGroups)
            {
                int gid = int.Parse(g["Id"].N);
                string gname = g.ContainsKey("Name") ? g["Name"].S : "";
                string gdesc = g.ContainsKey("Description") ? g["Description"].S : "";

                var children = GetChildrenForInstructorGroup(instructorId, gid);

                groupsWithChildren.Add(new InstructorGroupWithChildrenRecord
                {
                    GroupId = gid,
                    GroupName = gname,
                    GroupDescription = gdesc,
                    Children = children
                });
            }

            return groupsWithChildren.OrderBy(g => g.GroupName, StringComparer.Ordinal)
                                     .ThenBy(g => g.GroupId)
                                     .ToList();
        }

        public ChildReportRecord PostCreateChildReport(
            int childId,
            int instructorId,
            int? groupId,
            DateTime reportDate,
            string exerciseKey,
            string exerciseTitle,
            bool isPresent,
            string comment,
            string metricsJson)
        {
            // Find max ReportId
            int maxId = 0;
            var scanAll = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "ReportChildren", ProjectionExpression = "ReportId" }).GetAwaiter().GetResult();
            foreach (var item in scanAll.Items)
            {
                if (item.ContainsKey("ReportId") && int.TryParse(item["ReportId"].N, out int rid))
                {
                    if (rid > maxId) maxId = rid;
                }
            }
            int newReportId = maxId + 1;

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["ReportId"] = new AttributeValue { N = newReportId.ToString() },
                ["ChildId"] = new AttributeValue { N = childId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["GroupId"] = new AttributeValue { N = groupId.HasValue ? groupId.Value.ToString() : "0" },
                ["ReportDate"] = new AttributeValue { S = reportDate.Date.ToString("yyyy-MM-dd") },
                ["ExerciseKey"] = new AttributeValue { S = exerciseKey.Trim() },
                ["ExerciseTitle"] = new AttributeValue { S = exerciseTitle.Trim() },
                ["IsPresent"] = new AttributeValue { BOOL = isPresent },
                ["Comment"] = new AttributeValue { S = string.IsNullOrWhiteSpace(comment) ? "" : comment.Trim() },
                ["Metrics"] = new AttributeValue { S = string.IsNullOrWhiteSpace(metricsJson) ? "[]" : metricsJson.Trim() },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };

            _dynamoDbClient.PutItemAsync("ReportChildren", newItem).GetAwaiter().GetResult();

            return new ChildReportRecord
            {
                ReportId = newReportId,
                ChildId = childId,
                InstructorId = instructorId,
                GroupId = groupId,
                ReportDate = reportDate.Date,
                ExerciseKey = exerciseKey.Trim(),
                ExerciseTitle = exerciseTitle.Trim(),
                IsPresent = isPresent,
                Comment = comment,
                Metrics = string.IsNullOrWhiteSpace(metricsJson) ? "[]" : metricsJson.Trim()
            };
        }

        public ChildReportRecord? GetChildReportByDateForInstructor(int childId, int instructorId, DateTime reportDate)
        {
            string dateStr = reportDate.Date.ToString("yyyy-MM-dd");
            var scanReq = new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "ChildId = :cid AND InstructorId = :iid AND ReportDate = :rdate",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":iid"] = new AttributeValue { N = instructorId.ToString() },
                    [":rdate"] = new AttributeValue { S = dateStr }
                }
            };

            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            var latest = res.Items
                .OrderByDescending(item => int.Parse(item["ReportId"].N))
                .FirstOrDefault();

            if (latest == null) return null;
            return MapReportChildrenItem(latest);
        }

        public ChildReportRecord? GetChildReportByDateAndExercise(int childId, int instructorId, DateTime reportDate, string exerciseKey)
        {
            string dateStr = reportDate.Date.ToString("yyyy-MM-dd");
            var scanReq = new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "ChildId = :cid AND InstructorId = :iid AND ReportDate = :rdate AND ExerciseKey = :exkey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":iid"] = new AttributeValue { N = instructorId.ToString() },
                    [":rdate"] = new AttributeValue { S = dateStr },
                    [":exkey"] = new AttributeValue { S = exerciseKey.Trim() }
                }
            };

            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            var latest = res.Items
                .OrderByDescending(item => int.Parse(item["ReportId"].N))
                .FirstOrDefault();

            if (latest == null) return null;
            return MapReportChildrenItem(latest);
        }

        public ChildReportRecord UpdateChildReportForInstructor(
            int reportId,
            int childId,
            int instructorId,
            int? groupId,
            DateTime reportDate,
            string exerciseKey,
            string exerciseTitle,
            bool isPresent,
            string comment,
            string metricsJson)
        {
            var key = new Dictionary<string, AttributeValue> { ["ReportId"] = new AttributeValue { N = reportId.ToString() } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["GroupId"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { N = groupId.HasValue ? groupId.Value.ToString() : "0" } },
                ["ReportDate"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = reportDate.Date.ToString("yyyy-MM-dd") } },
                ["ExerciseKey"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = exerciseKey.Trim() } },
                ["ExerciseTitle"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = exerciseTitle.Trim() } },
                ["IsPresent"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = isPresent } },
                ["Comment"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(comment) ? "" : comment.Trim() } },
                ["Metrics"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(metricsJson) ? "[]" : metricsJson.Trim() } }
            };

            _dynamoDbClient.UpdateItemAsync("ReportChildren", key, updates).GetAwaiter().GetResult();

            return new ChildReportRecord
            {
                ReportId = reportId,
                ChildId = childId,
                InstructorId = instructorId,
                GroupId = groupId,
                ReportDate = reportDate.Date,
                ExerciseKey = exerciseKey.Trim(),
                ExerciseTitle = exerciseTitle.Trim(),
                IsPresent = isPresent,
                Comment = comment,
                Metrics = string.IsNullOrWhiteSpace(metricsJson) ? "[]" : metricsJson.Trim()
            };
        }

        public List<ChildReportRecord> GetChildReportsForMetrics(int childId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "ChildId = :cid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() }
                }
            };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            return res.Items
                .Select(MapReportChildrenItem)
                .OrderBy(r => r.ReportDate)
                .ThenBy(r => r.ReportId)
                .ToList();
        }

        public int GetScheduledLessonsCountForChild(int childId)
        {
            string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // 1. Get child's active group ids
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :cid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var groupIds = gcScan.Items
                .Select(item => int.Parse(item["GroupId"].N))
                .ToHashSet();

            if (groupIds.Count == 0)
            {
                return 0;
            }

            // 2. Scan LessonInvitations
            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "MeetingDate <= :today AND Status <> :cancelled AND Status <> :cancelledByInst",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":today"] = new AttributeValue { S = todayStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" },
                    [":cancelledByInst"] = new AttributeValue { S = "CancelledByInstructor" }
                }
            }).GetAwaiter().GetResult();

            int count = 0;
            foreach (var item in liScan.Items)
            {
                int gId = 0;
                if (item.TryGetValue("GroupId", out var gIdVal))
                {
                    string gIdStr = gIdVal.S ?? gIdVal.N ?? "";
                    int.TryParse(gIdStr, out gId);
                }
                if (groupIds.Contains(gId))
                {
                    count++;
                }
            }

            return count;
        }

        public List<ChildReportRecord> GetChildReportsForChildren(IEnumerable<int> childIds)
        {
            var normalizedChildIds = (childIds ?? Enumerable.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            if (normalizedChildIds.Count == 0)
            {
                return new List<ChildReportRecord>();
            }

            var scanRes = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "ReportChildren" }).GetAwaiter().GetResult();
            var reports = new List<ChildReportRecord>();

            foreach (var item in scanRes.Items)
            {
                int childId = 0;
                if (item.ContainsKey("ChildId"))
                {
                    var val = item["ChildId"];
                    string raw = val.N ?? val.S ?? "";
                    int.TryParse(raw, out childId);
                }

                if (childId > 0 && normalizedChildIds.Contains(childId))
                {
                    try
                    {
                        reports.Add(MapReportChildrenItem(item));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DBServices Warning] Failed to map report child item childId={childId}: {ex.Message}");
                    }
                }
            }

            return reports
                .OrderBy(r => r.ChildId)
                .ThenBy(r => r.ReportDate)
                .ThenBy(r => r.ReportId)
                .ToList();
        }

        private static ChildReportRecord MapReportChildrenItem(Dictionary<string, AttributeValue> item)
        {
            int reportId = 0;
            if (item.ContainsKey("ReportId"))
            {
                var val = item["ReportId"];
                string raw = val.N ?? val.S ?? "";
                int.TryParse(raw, out reportId);
            }

            int childId = 0;
            if (item.ContainsKey("ChildId"))
            {
                var val = item["ChildId"];
                string raw = val.N ?? val.S ?? "";
                int.TryParse(raw, out childId);
            }

            int instructorId = 0;
            if (item.ContainsKey("InstructorId"))
            {
                var val = item["InstructorId"];
                string raw = val.N ?? val.S ?? "";
                int.TryParse(raw, out instructorId);
            }

            int? groupId = null;
            if (item.ContainsKey("GroupId"))
            {
                var val = item["GroupId"];
                string raw = val.N ?? val.S ?? "";
                if (int.TryParse(raw, out int gidVal) && gidVal > 0)
                {
                    groupId = gidVal;
                }
            }

            DateTime reportDate = DateTime.Today;
            if (item.ContainsKey("ReportDate") && !string.IsNullOrWhiteSpace(item["ReportDate"].S))
            {
                DateTime.TryParse(item["ReportDate"].S, out reportDate);
            }

            string exerciseKey = item.ContainsKey("ExerciseKey") ? (item["ExerciseKey"].S ?? "") : "";
            string exerciseTitle = item.ContainsKey("ExerciseTitle") ? (item["ExerciseTitle"].S ?? "") : "";
            bool isPresent = item.ContainsKey("IsPresent") && item["IsPresent"].BOOL == true;
            string comment = item.ContainsKey("Comment") ? (item["Comment"].S ?? "") : "";
            string metrics = item.ContainsKey("Metrics") ? (item["Metrics"].S ?? "[]") : "[]";

            return new ChildReportRecord
            {
                ReportId = reportId,
                ChildId = childId,
                InstructorId = instructorId,
                GroupId = groupId,
                ReportDate = reportDate,
                ExerciseKey = exerciseKey,
                ExerciseTitle = exerciseTitle,
                IsPresent = isPresent,
                Comment = comment,
                Metrics = metrics
            };
        }

        public List<ChildRecord> GetChildren(bool includeInactive = false)
        {
            // 1. Scan Parents and index by Id
            var parentsById = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanParentsRes = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
            foreach (var p in scanParentsRes.Items)
            {
                if (p.ContainsKey("Id") && int.TryParse(p["Id"].N, out int pid))
                {
                    parentsById[pid] = p;
                }
            }

            // 2. Scan Children
            var children = new List<ChildRecord>();
            var scanChildrenRes = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            foreach (var c in scanChildrenRes.Items)
            {
                bool isActive = c.ContainsKey("IsActive") && c["IsActive"].BOOL == true;
                if (!includeInactive && !isActive) continue;

                int id = int.Parse(c["Id"].N);
                int parentId = int.Parse(c["ParentId"].N);
                string firstName = c.ContainsKey("FirstName") ? c["FirstName"].S : "";
                string lastName = c.ContainsKey("LastName") ? c["LastName"].S : "";
                DateTime? birthDate = c.ContainsKey("BirthDate") ? DateTime.Parse(c["BirthDate"].S) : (DateTime?)null;
                DateTime createdAt = c.ContainsKey("CreatedAt") ? DateTime.Parse(c["CreatedAt"].S) : DateTime.UtcNow;
                string description = c.ContainsKey("Description") ? c["Description"].S : "";
                string strengths = c.ContainsKey("Strengths") ? c["Strengths"].S : "";
                string weaknesses = c.ContainsKey("Weaknesses") ? c["Weaknesses"].S : "";
                string personalGoals = c.ContainsKey("PersonalGoals") ? c["PersonalGoals"].S : "";

                string parentEmail = "";
                string parentFirstName = "";
                string parentLastName = "";

                if (parentsById.TryGetValue(parentId, out var pItem))
                {
                    parentEmail = pItem.ContainsKey("Email") ? pItem["Email"].S : "";
                    parentFirstName = pItem.ContainsKey("FirstName") ? pItem["FirstName"].S : "";
                    parentLastName = pItem.ContainsKey("LastName") ? pItem["LastName"].S : "";
                }

                children.Add(new ChildRecord
                {
                    Id = id,
                    ParentId = parentId,
                    FirstName = firstName,
                    LastName = lastName,
                    BirthDate = birthDate,
                    IsActive = isActive,
                    CreatedAt = createdAt,
                    ParentEmail = parentEmail,
                    ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                    ChildDescription = description,
                    Strengths = strengths,
                    Weaknesses = weaknesses,
                    PersonalGoals = personalGoals
                });
            }

            return children.OrderByDescending(c => c.Id).ToList();
        }

        public ChildRecord? GetChildById(int id)
        {
            var key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = id.ToString() } };
            var res = _dynamoDbClient.GetItemAsync("Children", key).GetAwaiter().GetResult();
            if (res.Item == null || res.Item.Count == 0) return null;

            var c = res.Item;
            int parentId = int.Parse(c["ParentId"].N);
            bool isActive = c.ContainsKey("IsActive") && c["IsActive"].BOOL == true;
            string firstName = c.ContainsKey("FirstName") ? c["FirstName"].S : "";
            string lastName = c.ContainsKey("LastName") ? c["LastName"].S : "";
            DateTime? birthDate = c.ContainsKey("BirthDate") ? DateTime.Parse(c["BirthDate"].S) : (DateTime?)null;
            DateTime createdAt = c.ContainsKey("CreatedAt") ? DateTime.Parse(c["CreatedAt"].S) : DateTime.UtcNow;
            string description = c.ContainsKey("Description") ? c["Description"].S : "";
            string strengths = c.ContainsKey("Strengths") ? c["Strengths"].S : "";
            string weaknesses = c.ContainsKey("Weaknesses") ? c["Weaknesses"].S : "";
            string personalGoals = c.ContainsKey("PersonalGoals") ? c["PersonalGoals"].S : "";

            string parentEmail = "";
            string parentFirstName = "";
            string parentLastName = "";

            var parent = GetParentByNumericId(parentId);
            if (parent != null)
            {
                parentEmail = parent.ContainsKey("Email") ? parent["Email"].S : "";
                parentFirstName = parent.ContainsKey("FirstName") ? parent["FirstName"].S : "";
                parentLastName = parent.ContainsKey("LastName") ? parent["LastName"].S : "";
            }

            return new ChildRecord
            {
                Id = id,
                ParentId = parentId,
                FirstName = firstName,
                LastName = lastName,
                BirthDate = birthDate,
                IsActive = isActive,
                CreatedAt = createdAt,
                ParentEmail = parentEmail,
                ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                ChildDescription = description,
                Strengths = strengths,
                Weaknesses = weaknesses,
                PersonalGoals = personalGoals
            };
        }

        public bool GetParentExists(int parentId)
        {
            var parent = GetParentByNumericId(parentId);
            if (parent != null)
            {
                return parent.ContainsKey("IsActive") && parent["IsActive"].BOOL == true;
            }
            return false;
        }

        public List<ParentEmailOption> GetActiveParentEmailOptions()
        {
            var parents = new List<ParentEmailOption>();
            var scanReq = new ScanRequest { TableName = "Parents" };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            foreach (var item in res.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive)
                {
                    parents.Add(new ParentEmailOption
                    {
                        Id = int.Parse(item["Id"].N),
                        Email = item.ContainsKey("Email") ? item["Email"].S : ""
                    });
                }
            }
            return parents.OrderBy(p => p.Email, StringComparer.Ordinal).ToList();
        }

        public List<GroupChildOptionRecord> GetChildrenForGroupManagement(bool includeInactive = false)
        {
            // 1. Scan Parents
            var parentsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
            var parentsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in parentsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int pId))
                {
                    parentsMap[pId] = item;
                }
            }

            // 2. Scan Groups
            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            var groupsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in groupsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int gId))
                {
                    groupsMap[gId] = item;
                }
            }

            // 3. Scan GroupChildren for active group assignments
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
            var activeGroupMap = new Dictionary<int, int>(); // ChildId -> GroupId
            foreach (var item in gcScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive)
                {
                    if (item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cId) &&
                        item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId))
                    {
                        activeGroupMap[cId] = gId;
                    }
                }
            }

            // 4. Scan Children
            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            var children = new List<GroupChildOptionRecord>();

            foreach (var item in childrenScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (!includeInactive && !isActive)
                {
                    continue;
                }

                int childId = int.Parse(item["Id"].N);
                int parentId = int.Parse(item["ParentId"].N);

                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                string parentEmail = "";
                string parentFirstName = "";
                string parentLastName = "";
                if (parentsMap.TryGetValue(parentId, out var parentItem))
                {
                    parentEmail = parentItem.ContainsKey("Email") ? parentItem["Email"].S : "";
                    parentFirstName = parentItem.ContainsKey("FirstName") ? parentItem["FirstName"].S : "";
                    parentLastName = parentItem.ContainsKey("LastName") ? parentItem["LastName"].S : "";
                }

                int? activeGroupId = null;
                string activeGroupName = null;
                if (activeGroupMap.TryGetValue(childId, out int gId))
                {
                    activeGroupId = gId;
                    if (groupsMap.TryGetValue(gId, out var groupItem))
                    {
                        activeGroupName = groupItem.ContainsKey("Name") ? groupItem["Name"].S : "";
                    }
                }

                children.Add(new GroupChildOptionRecord
                {
                    ChildId = childId,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    ParentEmail = parentEmail,
                    ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                    ActiveGroupId = activeGroupId,
                    ActiveGroupName = activeGroupName
                });
            }

            return children
                .OrderBy(c => c.FirstName)
                .ThenBy(c => c.LastName)
                .ThenBy(c => c.ChildId)
                .ToList();
        }

        public List<GroupInstructorOptionRecord> GetInstructorsForGroupManagement()
        {
            var scanRes = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Instructors" }).GetAwaiter().GetResult();
            var instructors = new List<GroupInstructorOptionRecord>();

            foreach (var item in scanRes.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                string role = item.ContainsKey("Role") ? item["Role"].S : "";

                if (isActive && string.Equals(role, "Instructor", StringComparison.OrdinalIgnoreCase))
                {
                    string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                    string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                    instructors.Add(new GroupInstructorOptionRecord
                    {
                        InstructorId = int.Parse(item["Id"].N),
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = $"{firstName} {lastName}".Trim(),
                        Email = item.ContainsKey("Email") ? item["Email"].S : ""
                    });
                }
            }

            return instructors
                .OrderBy(i => i.FirstName)
                .ThenBy(i => i.LastName)
                .ThenBy(i => i.InstructorId)
                .ToList();
        }

        public List<GroupSummaryRecord> GetGroupsForManagement(bool includeInactive = false)
        {
            // 1. Scan Groups
            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            
            // 2. Scan InstructorGroups (only active ones)
            var igScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "InstructorGroups" }).GetAwaiter().GetResult();
            var activeInstructorMap = new Dictionary<int, int>(); // GroupId -> InstructorId
            foreach (var item in igScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive)
                {
                    if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId) &&
                        item.ContainsKey("InstructorId") && int.TryParse(item["InstructorId"].N, out int iId))
                    {
                        activeInstructorMap[gId] = iId;
                    }
                }
            }

            // 3. Scan Instructors
            var instructorsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Instructors" }).GetAwaiter().GetResult();
            var instructorsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in instructorsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int iId))
                {
                    instructorsMap[iId] = item;
                }
            }

            // 4. Scan GroupChildren (only active ones)
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
            var activeChildrenCounts = new Dictionary<int, HashSet<int>>(); // GroupId -> Set of ChildIds
            foreach (var item in gcScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive)
                {
                    if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId) &&
                        item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cId))
                    {
                        if (!activeChildrenCounts.ContainsKey(gId))
                        {
                            activeChildrenCounts[gId] = new HashSet<int>();
                        }
                        activeChildrenCounts[gId].Add(cId);
                    }
                }
            }

            var groups = new List<GroupSummaryRecord>();
            foreach (var item in groupsScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (!includeInactive && !isActive)
                {
                    continue;
                }

                int groupId = int.Parse(item["Id"].N);
                string name = item.ContainsKey("Name") ? item["Name"].S : "";
                string description = item.ContainsKey("Description") ? item["Description"].S : "";

                int? instructorId = null;
                string instructorFullName = "";
                string instructorEmail = "";

                if (activeInstructorMap.TryGetValue(groupId, out int iId))
                {
                    if (instructorsMap.TryGetValue(iId, out var instItem))
                    {
                        bool isInstActive = instItem.ContainsKey("IsActive") && instItem["IsActive"].BOOL == true;
                        if (isInstActive)
                        {
                            instructorId = iId;
                            string firstName = instItem.ContainsKey("FirstName") ? instItem["FirstName"].S : "";
                            string lastName = instItem.ContainsKey("LastName") ? instItem["LastName"].S : "";
                            instructorFullName = $"{firstName} {lastName}".Trim();
                            instructorEmail = instItem.ContainsKey("Email") ? instItem["Email"].S : "";
                        }
                    }
                }

                int activeChildrenCount = 0;
                if (activeChildrenCounts.TryGetValue(groupId, out var childrenSet))
                {
                    activeChildrenCount = childrenSet.Count;
                }

                groups.Add(new GroupSummaryRecord
                {
                    GroupId = groupId,
                    Name = name,
                    Description = description,
                    IsActive = isActive,
                    InstructorId = instructorId,
                    InstructorFullName = instructorFullName,
                    InstructorEmail = instructorEmail,
                    ActiveChildrenCount = activeChildrenCount
                });
            }

            return groups
                .OrderBy(g => g.Name)
                .ThenBy(g => g.GroupId)
                .ToList();
        }

        public GroupSummaryRecord PostCreateGroupWithInstructor(string groupName, string? description, int instructorId)
        {
            string normalizedGroupName = groupName.Trim();
            string normalizedDescription = string.IsNullOrWhiteSpace(description)
                ? ""
                : description.Trim();

            EnsureInstructorCanTeach(instructorId);

            // Check if group with same name exists
            var scanReq = new ScanRequest
            {
                TableName = "Groups",
                FilterExpression = "Name = :name",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":name"] = new AttributeValue { S = normalizedGroupName }
                }
            };
            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            var existingGroup = scanRes.Items.FirstOrDefault();

            int groupId;
            if (existingGroup != null)
            {
                int existingGroupId = int.Parse(existingGroup["Id"].N);
                bool existingGroupIsActive = existingGroup.ContainsKey("IsActive") && existingGroup["IsActive"].BOOL == true;

                if (existingGroupIsActive)
                {
                    throw new InvalidOperationException("A group with this name already exists.");
                }

                // Reactivate group
                var updateReq = new UpdateItemRequest
                {
                    TableName = "Groups",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = new AttributeValue { N = existingGroupId.ToString() }
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["Description"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { S = normalizedDescription }
                        },
                        ["IsActive"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { BOOL = true }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();

                groupId = existingGroupId;
            }
            else
            {
                // Find max Id in Groups table
                var allGroupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
                int maxId = 0;
                foreach (var gItem in allGroupsScan.Items)
                {
                    if (gItem.ContainsKey("Id") && int.TryParse(gItem["Id"].N, out int idVal))
                    {
                        if (idVal > maxId) maxId = idVal;
                    }
                }
                groupId = maxId + 1;

                var newItem = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = groupId.ToString() },
                    ["Name"] = new AttributeValue { S = normalizedGroupName },
                    ["Description"] = new AttributeValue { S = normalizedDescription },
                    ["IsActive"] = new AttributeValue { BOOL = true },
                    ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                };
                _dynamoDbClient.PutItemAsync("Groups", newItem).GetAwaiter().GetResult();
            }

            UpsertActiveInstructorGroup(groupId, instructorId);

            GroupSummaryRecord? createdGroup = GetGroupByIdForManagement(groupId);
            if (createdGroup == null)
            {
                throw new InvalidOperationException("Group was created but could not be loaded.");
            }

            return createdGroup;
        }

        public GroupSummaryRecord PutSetGroupInstructor(int groupId, int instructorId)
        {
            if (!GetGroupExistsAndActive(groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            EnsureInstructorCanTeach(instructorId);
            UpsertActiveInstructorGroup(groupId, instructorId);

            GroupSummaryRecord? updatedGroup = GetGroupByIdForManagement(groupId);
            if (updatedGroup == null)
            {
                throw new InvalidOperationException("Group was updated but could not be loaded.");
            }

            return updatedGroup;
        }

        public GroupSummaryRecord PutUpdateGroupForManagement(int groupId, string groupName, string? description, int instructorId)
        {
            if (!GetGroupExistsAndActive(groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            EnsureInstructorCanTeach(instructorId);

            var updateReq = new UpdateItemRequest
            {
                TableName = "Groups",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = groupId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["Name"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = groupName.Trim() }
                    },
                    ["Description"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = string.IsNullOrWhiteSpace(description) ? "" : description.Trim() }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();

            UpsertActiveInstructorGroup(groupId, instructorId);

            GroupSummaryRecord? updatedGroup = GetGroupByIdForManagement(groupId);
            if (updatedGroup == null)
            {
                throw new InvalidOperationException("Group was updated but could not be loaded.");
            }

            return updatedGroup;
        }

        public bool DeleteDeactivateGroupForManagement(int groupId)
        {
            if (!GetGroupExistsAndActive(groupId))
            {
                return false;
            }

            var updateGroupReq = new UpdateItemRequest
            {
                TableName = "Groups",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = groupId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { BOOL = false }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(updateGroupReq).GetAwaiter().GetResult();

            var igScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "GroupId = :groupId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":groupId"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in igScan.Items)
            {
                var updateIgReq = new UpdateItemRequest
                {
                    TableName = "InstructorGroups",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = item["Id"]
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { BOOL = false }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(updateIgReq).GetAwaiter().GetResult();
            }

            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "GroupId = :groupId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":groupId"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in gcScan.Items)
            {
                var updateGcReq = new UpdateItemRequest
                {
                    TableName = "GroupChildren",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["GroupId"] = item["GroupId"],
                        ["ChildId"] = item["ChildId"]
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { BOOL = false }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(updateGcReq).GetAwaiter().GetResult();
            }

            return true;
        }

        public GroupChildOptionRecord PutAssignChildToGroup(int childId, int groupId)
        {
            if (!GetGroupExistsAndActive(groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            if (!GetChildExistsAndActive(childId))
            {
                throw new InvalidOperationException("Child does not exist or is inactive.");
            }

            EnsureGroupHasSingleActiveInstructor(groupId);

            var scanReq = new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :childId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":childId"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var activeAssignments = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();

            foreach (var item in activeAssignments.Items)
            {
                int itemGroupId = int.Parse(item["GroupId"].N);
                if (itemGroupId != groupId)
                {
                    var updateReq = new UpdateItemRequest
                    {
                        TableName = "GroupChildren",
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["GroupId"] = item["GroupId"],
                            ["ChildId"] = item["ChildId"]
                        },
                        AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                        {
                            ["IsActive"] = new AttributeValueUpdate
                            {
                                Action = AttributeAction.PUT,
                                Value = new AttributeValue { BOOL = false }
                            }
                        }
                    };
                    _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();
                }
            }

            var getReq = new GetItemRequest
            {
                TableName = "GroupChildren",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                    ["ChildId"] = new AttributeValue { N = childId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();

            if (getRes.Item != null && getRes.Item.Count > 0)
            {
                var updateReq = new UpdateItemRequest
                {
                    TableName = "GroupChildren",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                        ["ChildId"] = new AttributeValue { N = childId.ToString() }
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { BOOL = true }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();
            }
            else
            {
                var newItem = new Dictionary<string, AttributeValue>
                {
                    ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                    ["ChildId"] = new AttributeValue { N = childId.ToString() },
                    ["IsActive"] = new AttributeValue { BOOL = true },
                    ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                };
                _dynamoDbClient.PutItemAsync("GroupChildren", newItem).GetAwaiter().GetResult();
            }

            GroupChildOptionRecord? assignedChild = GetGroupChildOptionByChildId(childId);
            if (assignedChild == null)
            {
                throw new InvalidOperationException("Child assignment was saved but could not be loaded.");
            }

            return assignedChild;
        }

        public GroupChildOptionRecord PutUnassignChildFromGroup(int childId)
        {
            if (!GetChildExistsAndActive(childId))
            {
                throw new InvalidOperationException("Child does not exist or is inactive.");
            }

            var scanReq = new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :childId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":childId"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var activeAssignments = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();

            foreach (var item in activeAssignments.Items)
            {
                var updateReq = new UpdateItemRequest
                {
                    TableName = "GroupChildren",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["GroupId"] = item["GroupId"],
                        ["ChildId"] = item["ChildId"]
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { BOOL = false }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();
            }

            GroupChildOptionRecord? child = GetGroupChildOptionByChildId(childId);
            if (child == null)
            {
                throw new InvalidOperationException("Child update was saved but could not be loaded.");
            }

            return child;
        }

        public GroupRecommendationResultRecord PostRecommendGroupForChild(int childId, string childDescription)
        {
            if (childId <= 0)
            {
                throw new InvalidOperationException("ChildId is required.");
            }

            string normalizedDescription = NormalizeRecommendationText(childDescription);
            if (string.IsNullOrWhiteSpace(normalizedDescription))
            {
                throw new InvalidOperationException("Child description is required.");
            }

            GroupChildOptionRecord? child = GetGroupChildOptionByChildId(childId);
            if (child == null)
            {
                throw new InvalidOperationException("Child does not exist.");
            }

            List<GroupSummaryRecord> candidateGroups = GetGroupsForManagement(false)
                .Where((group) => group.IsActive && group.InstructorId.HasValue)
                .ToList();

            if (candidateGroups.Count == 0)
            {
                throw new InvalidOperationException("לא נמצאו קבוצות פעילות עם מדריך משויך.");
            }

            HashSet<string> childTokens = TokenizeRecommendationText(normalizedDescription);
            if (childTokens.Count == 0)
            {
                throw new InvalidOperationException("Child description must include meaningful text.");
            }

            HashSet<string> childConcepts = ExtractRecommendationConcepts(childTokens);

            GroupRecommendationEvaluation? bestMatch = null;

            foreach (GroupSummaryRecord group in candidateGroups)
            {
                string groupText = NormalizeRecommendationText($"{group.Name} {group.Description}");
                HashSet<string> groupTokens = TokenizeRecommendationText(groupText);
                HashSet<string> groupConcepts = ExtractRecommendationConcepts(groupTokens);

                List<string> sharedTokens = childTokens
                    .Where((token) => groupTokens.Contains(token))
                    .OrderBy((token) => token)
                    .Take(6)
                    .ToList();

                List<string> sharedConceptKeys = childConcepts
                    .Where((concept) => groupConcepts.Contains(concept))
                    .OrderBy((concept) => concept)
                    .ToList();

                List<string> sharedConceptLabels = sharedConceptKeys
                    .Select((concept) => GroupRecommendationConceptLabels.TryGetValue(concept, out string? label) ? label : concept)
                    .ToList();

                double score = 0;
                score += sharedTokens.Count * 2.2;
                score += sharedConceptLabels.Count * 4.5;
                score += Math.Max(0, 12 - group.ActiveChildrenCount) * 0.15;

                if (sharedTokens.Count == 0 && sharedConceptLabels.Count == 0)
                {
                    score -= 1;
                }

                GroupRecommendationEvaluation currentEvaluation = new GroupRecommendationEvaluation
                {
                    Group = group,
                    Score = score,
                    SharedTokens = sharedTokens,
                    SharedConceptLabels = sharedConceptLabels
                };

                if (bestMatch == null ||
                    currentEvaluation.Score > bestMatch.Score + 0.0001 ||
                    (Math.Abs(currentEvaluation.Score - bestMatch.Score) <= 0.0001
                     && currentEvaluation.Group.ActiveChildrenCount < bestMatch.Group.ActiveChildrenCount))
                {
                    bestMatch = currentEvaluation;
                }
            }

            if (bestMatch == null)
            {
                throw new InvalidOperationException("Group recommendation could not be computed.");
            }

            bool usedFallback = bestMatch.SharedTokens.Count == 0 && bestMatch.SharedConceptLabels.Count == 0;

            return new GroupRecommendationResultRecord
            {
                ChildId = child.ChildId,
                ChildFullName = child.FullName,
                RecommendedGroupId = bestMatch.Group.GroupId,
                RecommendedGroupName = bestMatch.Group.Name,
                RecommendedInstructorFullName = bestMatch.Group.InstructorFullName,
                Score = Math.Round(bestMatch.Score, 2),
                UsedFallback = usedFallback,
                Reasoning = BuildRecommendationReasoning(bestMatch, usedFallback),
                SharedTerms = bestMatch.SharedTokens,
                SharedConcepts = bestMatch.SharedConceptLabels
            };
        }

        public GroupSummaryRecord? GetGroupByIdForManagement(int groupId)
        {
            if (groupId <= 0)
            {
                return null;
            }
            return GetGroupsForManagement(true).FirstOrDefault(g => g.GroupId == groupId);
        }

        public GroupChildOptionRecord? GetGroupChildOptionByChildId(int childId)
        {
            if (childId <= 0)
            {
                return null;
            }
            return GetChildrenForGroupManagement(true).FirstOrDefault(c => c.ChildId == childId);
        }

        public ManagerCenterSettingsRecord GetManagerCenterSettings()
        {
            var getReq = new GetItemRequest
            {
                TableName = "CenterSettings",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = "1" }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item != null && getRes.Item.Count > 0)
            {
                var item = getRes.Item;
                return new ManagerCenterSettingsRecord
                {
                    CenterName = item.ContainsKey("CenterName") ? item["CenterName"].S : "",
                    CenterAddress = item.ContainsKey("CenterAddress") ? item["CenterAddress"].S : "",
                    SendAutoReports = item.ContainsKey("SendAutoReports") && item["SendAutoReports"].BOOL == true,
                    ReceiveAlerts = item.ContainsKey("ReceiveAlerts") && item["ReceiveAlerts"].BOOL == true,
                    ShowKidsAdvanced = item.ContainsKey("ShowKidsAdvanced") && item["ShowKidsAdvanced"].BOOL == true,
                    UpdatedAtUtc = item.ContainsKey("UpdatedAt") && DateTime.TryParse(item["UpdatedAt"].S, out DateTime dt) ? dt : DateTime.UtcNow
                };
            }

            DateTime updatedAtUtc = DateTime.UtcNow;

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = "1" },
                ["CenterName"] = new AttributeValue { S = "" },
                ["CenterAddress"] = new AttributeValue { S = "" },
                ["SendAutoReports"] = new AttributeValue { BOOL = true },
                ["ReceiveAlerts"] = new AttributeValue { BOOL = true },
                ["ShowKidsAdvanced"] = new AttributeValue { BOOL = false },
                ["UpdatedAt"] = new AttributeValue { S = updatedAtUtc.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("CenterSettings", newItem).GetAwaiter().GetResult();

            return new ManagerCenterSettingsRecord
            {
                CenterName = string.Empty,
                CenterAddress = string.Empty,
                SendAutoReports = true,
                ReceiveAlerts = true,
                ShowKidsAdvanced = false,
                UpdatedAtUtc = updatedAtUtc
            };
        }

        public ManagerCenterSettingsRecord PutManagerCenterSettings(
            string centerName,
            string centerAddress,
            bool sendAutoReports,
            bool receiveAlerts,
            bool showKidsAdvanced)
        {
            string normalizedCenterName = (centerName ?? string.Empty).Trim();
            string normalizedCenterAddress = (centerAddress ?? string.Empty).Trim();
            DateTime updatedAtUtc = DateTime.UtcNow;

            var item = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = "1" },
                ["CenterName"] = new AttributeValue { S = normalizedCenterName },
                ["CenterAddress"] = new AttributeValue { S = normalizedCenterAddress },
                ["SendAutoReports"] = new AttributeValue { BOOL = sendAutoReports },
                ["ReceiveAlerts"] = new AttributeValue { BOOL = receiveAlerts },
                ["ShowKidsAdvanced"] = new AttributeValue { BOOL = showKidsAdvanced },
                ["UpdatedAt"] = new AttributeValue { S = updatedAtUtc.ToString("o") }
            };

            _dynamoDbClient.PutItemAsync("CenterSettings", item).GetAwaiter().GetResult();

            return new ManagerCenterSettingsRecord
            {
                CenterName = normalizedCenterName,
                CenterAddress = normalizedCenterAddress,
                SendAutoReports = sendAutoReports,
                ReceiveAlerts = receiveAlerts,
                ShowKidsAdvanced = showKidsAdvanced,
                UpdatedAtUtc = updatedAtUtc
            };
        }

        public List<ManagerAttendanceReportRowRecord> GetManagerAttendanceReportRows(DateTime startDate, DateTime endDate)
        {
            DateTime normalizedStartDate = startDate.Date;
            DateTime normalizedEndDate = endDate.Date;

            if (normalizedStartDate > normalizedEndDate)
            {
                throw new InvalidOperationException("Start date must be on or before end date.");
            }

            string startStr = normalizedStartDate.ToString("yyyy-MM-dd");
            string endStr = normalizedEndDate.ToString("yyyy-MM-dd");

            // 1. Fetch children map
            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            var childrenMap = new Dictionary<int, string>();
            foreach (var item in childrenScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    string fName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                    string lName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                    childrenMap[idVal] = $"{fName} {lName}".Trim();
                }
            }

            // 2. Fetch instructors map
            var instructorsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Instructors" }).GetAwaiter().GetResult();
            var instructorsMap = new Dictionary<int, string>();
            foreach (var item in instructorsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    string fName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                    string lName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                    instructorsMap[idVal] = $"{fName} {lName}".Trim();
                }
            }

            // 3. Fetch groups map
            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            var groupsMap = new Dictionary<int, string>();
            foreach (var item in groupsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    groupsMap[idVal] = item.ContainsKey("Name") ? item["Name"].S : "";
                }
            }

            var rows = new List<ManagerAttendanceReportRowRecord>();

            // 4. Fetch TrainingSessions
            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "MeetingDate >= :start AND MeetingDate <= :end",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in tsScan.Items)
            {
                int sessionId = int.Parse(item["SessionId"].N);
                DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                int instructorId = int.Parse(item["InstructorId"].N);

                int? childId = null;
                if (item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cid))
                {
                    childId = cid;
                }

                string childFullName = "";
                if (childId.HasValue)
                {
                    childrenMap.TryGetValue(childId.Value, out childFullName);
                }

                instructorsMap.TryGetValue(instructorId, out string instructorFullName);

                rows.Add(new ManagerAttendanceReportRowRecord
                {
                    SessionId = sessionId,
                    LessonType = "Private",
                    MeetingDate = meetingDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = status,
                    AttendanceCategory = MapAttendanceCategory(status),
                    ChildId = childId,
                    ChildFullName = childFullName,
                    InstructorId = instructorId,
                    InstructorFullName = instructorFullName,
                    GroupId = null,
                    GroupName = null
                });
            }

            // 5. Fetch GroupTrainingSessions
            var gtsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupTrainingSessions",
                FilterExpression = "MeetingDate >= :start AND MeetingDate <= :end",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in gtsScan.Items)
            {
                int sessionId = int.Parse(item["SessionId"].N);
                DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                int instructorId = int.Parse(item["InstructorId"].N);

                int? groupId = null;
                if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid))
                {
                    groupId = gid;
                }

                string groupName = "";
                if (groupId.HasValue)
                {
                    groupsMap.TryGetValue(groupId.Value, out groupName);
                }

                instructorsMap.TryGetValue(instructorId, out string instructorFullName);

                rows.Add(new ManagerAttendanceReportRowRecord
                {
                    SessionId = sessionId,
                    LessonType = "Group",
                    MeetingDate = meetingDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = status,
                    AttendanceCategory = MapAttendanceCategory(status),
                    ChildId = null,
                    ChildFullName = null,
                    InstructorId = instructorId,
                    InstructorFullName = instructorFullName,
                    GroupId = groupId,
                    GroupName = groupName
                });
            }

            return rows
                .OrderByDescending(r => r.MeetingDate)
                .ThenByDescending(r => r.StartTime)
                .ThenByDescending(r => r.SessionId)
                .ToList();
        }

        public ManagerSystemReportsOverviewRecord GetManagerSystemReportsOverview(DateTime startDate, DateTime endDate)
        {
            DateTime normalizedStartDate = startDate.Date;
            DateTime normalizedEndDate = endDate.Date;

            if (normalizedStartDate > normalizedEndDate)
            {
                throw new InvalidOperationException("Start date must be on or before end date.");
            }

            List<ManagerAttendanceReportRowRecord> attendanceRows = GetManagerAttendanceReportRows(normalizedStartDate, normalizedEndDate);

            // Active Groups Count
            var groupsScanForActive = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Groups",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();
            int activeGroupsCount = groupsScanForActive.Items.Count;

            // Active Instructors Count
            var instructorsScanForActive = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Instructors",
                FilterExpression = "IsActive = :active AND #role = :role",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#role"] = "Role"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true },
                    [":role"] = new AttributeValue { S = "Instructor" }
                }
            }).GetAwaiter().GetResult();
            int activeInstructorsCount = instructorsScanForActive.Items.Count;

            // Active Children Count
            var childrenScanForActive = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();
            int activeChildrenCount = childrenScanForActive.Items.Count;

            // Reports within date range
            var reportsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "ReportDate >= :start AND ReportDate <= :end",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = normalizedStartDate.ToString("yyyy-MM-dd") },
                    [":end"] = new AttributeValue { S = normalizedEndDate.ToString("yyyy-MM-dd") }
                }
            }).GetAwaiter().GetResult();

            int reportsCount = reportsScan.Items.Count;

            var distinctChildIds = new HashSet<int>();
            foreach (var rItem in reportsScan.Items)
            {
                if (rItem.ContainsKey("ChildId") && int.TryParse(rItem["ChildId"].N, out int cid))
                {
                    distinctChildIds.Add(cid);
                }
            }
            int childrenWithReportsCount = distinctChildIds.Count;

            return new ManagerSystemReportsOverviewRecord
            {
                RangeStartDate = normalizedStartDate,
                RangeEndDate = normalizedEndDate,
                GeneratedAtUtc = DateTime.UtcNow,
                ActiveGroupsCount = activeGroupsCount,
                ActiveInstructorsCount = activeInstructorsCount,
                ActiveChildrenCount = activeChildrenCount,
                ReportsCount = reportsCount,
                ChildrenWithReportsCount = childrenWithReportsCount,
                AttendanceRowsCount = attendanceRows.Count,
                PresentCount = attendanceRows.Count((row) => row.AttendanceCategory == "Present"),
                AbsentCount = attendanceRows.Count((row) => row.AttendanceCategory == "Absent"),
                LateCount = attendanceRows.Count((row) => row.AttendanceCategory == "Late"),
                ScheduledCount = attendanceRows.Count((row) => row.AttendanceCategory == "Scheduled"),
                CancelledCount = attendanceRows.Count((row) => row.AttendanceCategory == "Cancelled")
            };
        }

        public ManagerGeneratedSystemReportRecord GenerateManagerSystemReport(string reportType, DateTime startDate, DateTime endDate)
        {
            string normalizedReportType = (reportType ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedReportType))
            {
                throw new InvalidOperationException("Report type is required.");
            }

            DateTime normalizedStartDate = startDate.Date;
            DateTime normalizedEndDate = endDate.Date;

            if (normalizedStartDate > normalizedEndDate)
            {
                throw new InvalidOperationException("Start date must be on or before end date.");
            }

            DateTime generatedAtUtc = DateTime.UtcNow;

            switch (normalizedReportType)
            {
                case "attendance":
                {
                    List<ManagerAttendanceReportRowRecord> attendanceRows = GetManagerAttendanceReportRows(normalizedStartDate, normalizedEndDate);
                    int presentCount = attendanceRows.Count((row) => row.AttendanceCategory == "Present");
                    int absentCount = attendanceRows.Count((row) => row.AttendanceCategory == "Absent");
                    int lateCount = attendanceRows.Count((row) => row.AttendanceCategory == "Late");
                    int scheduledCount = attendanceRows.Count((row) => row.AttendanceCategory == "Scheduled");
                    int cancelledCount = attendanceRows.Count((row) => row.AttendanceCategory == "Cancelled");

                    return new ManagerGeneratedSystemReportRecord
                    {
                        ReportType = normalizedReportType,
                        Title = "דו\"ח נוכחות",
                        RangeStartDate = normalizedStartDate,
                        RangeEndDate = normalizedEndDate,
                        GeneratedAtUtc = generatedAtUtc,
                        RecordsCount = attendanceRows.Count,
                        SummaryLines = new List<string>
                        {
                            $"סה\"כ רשומות נוכחות: {attendanceRows.Count}",
                            $"נוכחים: {presentCount}",
                            $"חיסורים: {absentCount}",
                            $"איחורים: {lateCount}",
                            $"מתוזמנים: {scheduledCount}",
                            $"בוטלו: {cancelledCount}"
                        }
                    };
                }

                case "children":
                {
                    var childActiveScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "Children",
                        FilterExpression = "IsActive = :active",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true }
                        }
                    }).GetAwaiter().GetResult();
                    int activeChildrenCount = childActiveScan.Items.Count;

                    var childReportsScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "ReportChildren",
                        FilterExpression = "ReportDate >= :start AND ReportDate <= :end",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":start"] = new AttributeValue { S = normalizedStartDate.ToString("yyyy-MM-dd") },
                            [":end"] = new AttributeValue { S = normalizedEndDate.ToString("yyyy-MM-dd") }
                        }
                    }).GetAwaiter().GetResult();

                    var distinctChildIds = new HashSet<int>();
                    foreach (var rItem in childReportsScan.Items)
                    {
                        if (rItem.ContainsKey("ChildId") && int.TryParse(rItem["ChildId"].N, out int cid))
                        {
                            distinctChildIds.Add(cid);
                        }
                    }
                    int childrenWithReportsCount = distinctChildIds.Count;

                    return new ManagerGeneratedSystemReportRecord
                    {
                        ReportType = normalizedReportType,
                        Title = "דו\"ח ילדים",
                        RangeStartDate = normalizedStartDate,
                        RangeEndDate = normalizedEndDate,
                        GeneratedAtUtc = generatedAtUtc,
                        RecordsCount = activeChildrenCount,
                        SummaryLines = new List<string>
                        {
                            $"ילדים פעילים במערכת: {activeChildrenCount}",
                            $"ילדים עם דו\"חות בטווח: {childrenWithReportsCount}"
                        }
                    };
                }

                case "instructors":
                {
                    var instActiveScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "Instructors",
                        FilterExpression = "IsActive = :active AND #role = :role",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#role"] = "Role"
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true },
                            [":role"] = new AttributeValue { S = "Instructor" }
                        }
                    }).GetAwaiter().GetResult();
                    int activeInstructorsCount = instActiveScan.Items.Count;

                    var reportsScanInst = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "ReportChildren",
                        FilterExpression = "ReportDate >= :start AND ReportDate <= :end",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":start"] = new AttributeValue { S = normalizedStartDate.ToString("yyyy-MM-dd") },
                            [":end"] = new AttributeValue { S = normalizedEndDate.ToString("yyyy-MM-dd") }
                        }
                    }).GetAwaiter().GetResult();

                    var distinctInstructorIds = new HashSet<int>();
                    foreach (var rItem in reportsScanInst.Items)
                    {
                        if (rItem.ContainsKey("InstructorId") && int.TryParse(rItem["InstructorId"].N, out int iid))
                        {
                            distinctInstructorIds.Add(iid);
                        }
                    }
                    int instructorsWithReportsCount = distinctInstructorIds.Count;

                    return new ManagerGeneratedSystemReportRecord
                    {
                        ReportType = normalizedReportType,
                        Title = "דו\"ח מדריכים",
                        RangeStartDate = normalizedStartDate,
                        RangeEndDate = normalizedEndDate,
                        GeneratedAtUtc = generatedAtUtc,
                        RecordsCount = activeInstructorsCount,
                        SummaryLines = new List<string>
                        {
                            $"מדריכים פעילים במערכת: {activeInstructorsCount}",
                            $"מדריכים שהגישו דו\"חות בטווח: {instructorsWithReportsCount}"
                        }
                    };
                }

                case "groups":
                {
                    var groupsScanForActive = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "Groups",
                        FilterExpression = "IsActive = :active",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true }
                        }
                    }).GetAwaiter().GetResult();
                    int activeGroupsCount = groupsScanForActive.Items.Count;

                    var activeGroupIds = new HashSet<int>();
                    foreach (var item in groupsScanForActive.Items)
                    {
                        if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int gid))
                        {
                            activeGroupIds.Add(gid);
                        }
                    }

                    var igScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "InstructorGroups",
                        FilterExpression = "IsActive = :active",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true }
                        }
                    }).GetAwaiter().GetResult();

                    var groupsWithInstructorSet = new HashSet<int>();
                    foreach (var item in igScan.Items)
                    {
                        if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid))
                        {
                            if (activeGroupIds.Contains(gid))
                            {
                                groupsWithInstructorSet.Add(gid);
                            }
                        }
                    }
                    int groupsWithInstructorCount = groupsWithInstructorSet.Count;

                    var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
                    {
                        TableName = "GroupChildren",
                        FilterExpression = "IsActive = :active",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":active"] = new AttributeValue { BOOL = true }
                        }
                    }).GetAwaiter().GetResult();

                    var groupsWithChildrenSet = new HashSet<int>();
                    foreach (var item in gcScan.Items)
                    {
                        if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid))
                        {
                            if (activeGroupIds.Contains(gid))
                            {
                                groupsWithChildrenSet.Add(gid);
                            }
                        }
                    }
                    int groupsWithChildrenCount = groupsWithChildrenSet.Count;

                    return new ManagerGeneratedSystemReportRecord
                    {
                        ReportType = normalizedReportType,
                        Title = "דו\"ח קבוצות",
                        RangeStartDate = normalizedStartDate,
                        RangeEndDate = normalizedEndDate,
                        GeneratedAtUtc = generatedAtUtc,
                        RecordsCount = activeGroupsCount,
                        SummaryLines = new List<string>
                        {
                            $"קבוצות פעילות: {activeGroupsCount}",
                            $"קבוצות עם מדריך פעיל: {groupsWithInstructorCount}",
                            $"קבוצות עם ילדים פעילים: {groupsWithChildrenCount}"
                        }
                    };
                }

                default:
                    throw new InvalidOperationException("Unsupported report type.");
            }
        }

        public int PostCreateChild(int parentId, string firstName, string lastName, DateTime? birthDate, string? childDescription, string? strengths, string? weaknesses, string? personalGoals)
        {
            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            string normalizedBirthDate = birthDate.HasValue ? birthDate.Value.ToString("yyyy-MM-dd") : "";
            string normalizedDescription = string.IsNullOrWhiteSpace(childDescription) ? "" : childDescription.Trim();
            string normalizedStrengths = string.IsNullOrWhiteSpace(strengths) ? "" : strengths.Trim();
            string normalizedWeaknesses = string.IsNullOrWhiteSpace(weaknesses) ? "" : weaknesses.Trim();
            string normalizedGoals = string.IsNullOrWhiteSpace(personalGoals) ? "" : personalGoals.Trim();

            // Scan Children for ParentId and check in-memory for case-insensitivity
            var scanReq = new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "ParentId = :pid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() }
                }
            };
            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            
            Dictionary<string, AttributeValue>? existingChild = null;
            foreach (var item in scanRes.Items)
            {
                string fname = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string lname = item.ContainsKey("LastName") ? item["LastName"].S : "";
                if (string.Equals(fname, normalizedFirstName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(lname, normalizedLastName, StringComparison.OrdinalIgnoreCase))
                {
                    existingChild = item;
                    break;
                }
            }

            if (existingChild != null)
            {
                int existingId = int.Parse(existingChild["Id"].N);
                bool isActive = existingChild.ContainsKey("IsActive") && existingChild["IsActive"].BOOL == true;
                if (isActive)
                {
                    throw new InvalidOperationException("Child already exists for this parent.");
                }

                // Reactivate inactive child
                var key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = existingId.ToString() } };
                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedFirstName } },
                    ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedLastName } },
                    ["BirthDate"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedBirthDate } },
                    ["Description"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedDescription } },
                    ["Strengths"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedStrengths } },
                    ["Weaknesses"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedWeaknesses } },
                    ["PersonalGoals"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = normalizedGoals } },
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } }
                };
                _dynamoDbClient.UpdateItemAsync("Children", key, updates).GetAwaiter().GetResult();
                return existingId;
            }

            // Find max ID in Children
            int maxId = 0;
            var scanAll = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children", ProjectionExpression = "Id" }).GetAwaiter().GetResult();
            foreach (var item in scanAll.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int cid))
                {
                    if (cid > maxId) maxId = cid;
                }
            }
            int newChildId = maxId + 1;

            // Put new child item
            var newChild = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = newChildId.ToString() },
                ["ParentId"] = new AttributeValue { N = parentId.ToString() },
                ["FirstName"] = new AttributeValue { S = normalizedFirstName },
                ["LastName"] = new AttributeValue { S = normalizedLastName },
                ["BirthDate"] = new AttributeValue { S = normalizedBirthDate },
                ["Description"] = new AttributeValue { S = normalizedDescription },
                ["Strengths"] = new AttributeValue { S = normalizedStrengths },
                ["Weaknesses"] = new AttributeValue { S = normalizedWeaknesses },
                ["PersonalGoals"] = new AttributeValue { S = normalizedGoals },
                ["IsActive"] = new AttributeValue { BOOL = true },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("Children", newChild).GetAwaiter().GetResult();
            return newChildId;
        }

        public bool PutUpdateChild(int id, int parentId, string firstName, string lastName, DateTime? birthDate, string? childDescription, bool isActive, string? strengths, string? weaknesses, string? personalGoals)
        {
            var key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = id.ToString() } };
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["ParentId"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { N = parentId.ToString() } },
                ["FirstName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = firstName.Trim() } },
                ["LastName"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = lastName.Trim() } },
                ["BirthDate"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = birthDate.HasValue ? birthDate.Value.ToString("yyyy-MM-dd") : "" } },
                ["Description"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(childDescription) ? "" : childDescription.Trim() } },
                ["Strengths"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(strengths) ? "" : strengths.Trim() } },
                ["Weaknesses"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(weaknesses) ? "" : weaknesses.Trim() } },
                ["PersonalGoals"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = string.IsNullOrWhiteSpace(personalGoals) ? "" : personalGoals.Trim() } },
                ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = isActive } }
            };
            
            try
            {
                _dynamoDbClient.UpdateItemAsync("Children", key, updates).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool DeleteDeactivateChild(int id)
        {
            var key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = id.ToString() } };
            
            try
            {
                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                };
                _dynamoDbClient.UpdateItemAsync("Children", key, updates).GetAwaiter().GetResult();

                // Deactivate related GroupChildren
                var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "GroupChildren",
                    FilterExpression = "ChildId = :cid AND IsActive = :act",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = id.ToString() },
                        [":act"] = new AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                foreach (var gc in gcScan.Items)
                {
                    var gcKey = new Dictionary<string, AttributeValue>
                    {
                        ["GroupId"] = gc["GroupId"],
                        ["ChildId"] = gc["ChildId"]
                    };
                    var gcUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                    };
                    _dynamoDbClient.UpdateItemAsync("GroupChildren", gcKey, gcUpdates).GetAwaiter().GetResult();
                }

                // Deactivate related Conversations
                var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Conversations",
                    FilterExpression = "ChildId = :cid AND IsActive = :act",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = id.ToString() },
                        [":act"] = new AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                foreach (var conv in convScan.Items)
                {
                    var convKey = new Dictionary<string, AttributeValue> { ["Id"] = conv["Id"] };
                    var convUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } }
                    };
                    _dynamoDbClient.UpdateItemAsync("Conversations", convKey, convUpdates).GetAwaiter().GetResult();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<ChatChildRecord> GetParentChildrenForChat(int parentId)
        {
            var children = new List<ChatChildRecord>();

            var scanReq = new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "ParentId = :pid AND IsActive = :act",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() },
                    [":act"] = new AttributeValue { BOOL = true }
                }
            };
            var res = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();

            foreach (var item in res.Items)
            {
                int childId = int.Parse(item["Id"].N);
                string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                var instIds = GetInstructorIdsForChild(parentId, childId);

                children.Add(new ChatChildRecord
                {
                    Id = childId,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    InstructorsCount = instIds.Count
                });
            }

            return children.OrderBy(c => c.FirstName, StringComparer.Ordinal)
                           .ThenBy(c => c.LastName, StringComparer.Ordinal)
                           .ThenBy(c => c.Id)
                           .ToList();
        }

        public bool GetChildBelongsToParent(int parentId, int childId)
        {
            var key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = childId.ToString() } };
            try
            {
                var res = _dynamoDbClient.GetItemAsync("Children", key).GetAwaiter().GetResult();
                if (res.Item != null && res.Item.Count > 0)
                {
                    var item = res.Item;
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    int pId = int.Parse(item["ParentId"].N);
                    return isActive && pId == parentId;
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return false;
        }

        public List<ChatInstructorRecord> GetInstructorsForParentChild(int parentId, int childId)
        {
            var instructors = new List<ChatInstructorRecord>();
            var instructorIds = GetInstructorIdsForChild(parentId, childId);

            if (instructorIds.Count == 0)
            {
                return instructors;
            }

            var instructorsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Instructors" }).GetAwaiter().GetResult();
            foreach (var item in instructorsScan.Items)
            {
                int id = int.Parse(item["Id"].N);
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive && instructorIds.Contains(id))
                {
                    string firstName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                    string lastName = item.ContainsKey("LastName") ? item["LastName"].S : "";

                    instructors.Add(new ChatInstructorRecord
                    {
                        Id = id,
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = $"{firstName} {lastName}".Trim(),
                        Email = item.ContainsKey("Email") ? item["Email"].S : ""
                    });
                }
            }

            return instructors.OrderBy(i => i.FirstName, StringComparer.Ordinal)
                              .ThenBy(i => i.LastName, StringComparer.Ordinal)
                              .ThenBy(i => i.Id)
                              .ToList();
        }

        public bool GetInstructorAssignedToParentChild(int parentId, int childId, int instructorId)
        {
            var instructorIds = GetInstructorIdsForChild(parentId, childId);
            return instructorIds.Contains(instructorId);
        }

        public bool GetIsTrainingSessionTimeOccupied(int instructorId, int childId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime)
        {
            string dateStr = meetingDate.ToString("yyyy-MM-dd");

            // 1. Scan/Query TrainingSessions on this date
            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "MeetingDate = :mdate AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":mdate"] = new AttributeValue { S = dateStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in tsScan.Items)
            {
                int instId = int.Parse(item["InstructorId"].N);
                int cId = int.Parse(item["ChildId"].N);
                TimeSpan sessionStart = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan sessionEnd = TimeSpan.Parse(item["EndTime"].S);

                if (instId == instructorId || cId == childId)
                {
                    if (sessionStart < endTime && sessionEnd > startTime)
                    {
                        return true;
                    }
                }
            }

            // 2. Scan/Query GroupTrainingSessions on this date
            var gtsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupTrainingSessions",
                FilterExpression = "MeetingDate = :mdate AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":mdate"] = new AttributeValue { S = dateStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            // Check if child is in any group
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "ChildId = :childId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":childId"] = new AttributeValue { N = childId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var childGroupIds = new HashSet<int>();
            foreach (var item in gcScan.Items)
            {
                childGroupIds.Add(int.Parse(item["GroupId"].N));
            }

            foreach (var item in gtsScan.Items)
            {
                int instId = int.Parse(item["InstructorId"].N);
                int gId = int.Parse(item["GroupId"].N);
                TimeSpan sessionStart = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan sessionEnd = TimeSpan.Parse(item["EndTime"].S);

                if (instId == instructorId || childGroupIds.Contains(gId))
                {
                    if (sessionStart < endTime && sessionEnd > startTime)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public TrainingSessionRecord PostCreateTrainingSession(
            int parentId,
            int childId,
            int instructorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string notes,
            string? targetMetric)
        {
            var allScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "TrainingSessions" }).GetAwaiter().GetResult();
            int maxId = 0;
            foreach (var item in allScan.Items)
            {
                if (item.ContainsKey("SessionId") && int.TryParse(item["SessionId"].N, out int idVal))
                {
                    if (idVal > maxId) maxId = idVal;
                }
            }
            int newSessionId = maxId + 1;

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["SessionId"] = new AttributeValue { N = newSessionId.ToString() },
                ["ParentId"] = new AttributeValue { N = parentId.ToString() },
                ["ChildId"] = new AttributeValue { N = childId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["MeetingDate"] = new AttributeValue { S = meetingDate.ToString("yyyy-MM-dd") },
                ["StartTime"] = new AttributeValue { S = startTime.ToString("c") },
                ["EndTime"] = new AttributeValue { S = endTime.ToString("c") },
                ["Notes"] = new AttributeValue { S = string.IsNullOrWhiteSpace(notes) ? "" : notes.Trim() },
                ["Status"] = new AttributeValue { S = "Scheduled" },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["TargetMetric"] = new AttributeValue { S = string.IsNullOrWhiteSpace(targetMetric) ? "" : targetMetric.Trim() }
            };

            _dynamoDbClient.PutItemAsync("TrainingSessions", newItem).GetAwaiter().GetResult();

            return new TrainingSessionRecord
            {
                SessionId = newSessionId,
                ParentId = parentId,
                ChildId = childId,
                InstructorId = instructorId,
                MeetingDate = meetingDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                Notes = string.IsNullOrWhiteSpace(notes) ? "" : notes.Trim(),
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow,
                TargetMetric = string.IsNullOrWhiteSpace(targetMetric) ? "" : targetMetric.Trim()
            };
        }

        public bool PostCancelTrainingSessionForInstructor(int instructorId, int sessionId, string cancelReason)
        {
            var getReq = new GetItemRequest
            {
                TableName = "TrainingSessions",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["SessionId"] = new AttributeValue { N = sessionId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item == null || getRes.Item.Count == 0)
            {
                return false;
            }

            var item = getRes.Item;
            int itemInstructorId = int.Parse(item["InstructorId"].N);
            if (itemInstructorId != instructorId)
            {
                return false;
            }

            string status = item.ContainsKey("Status") ? item["Status"].S : "";
            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int parentId = int.Parse(item["ParentId"].N);
            int childId = int.Parse(item["ChildId"].N);
            DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
            TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
            TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);

            DateTime sessionStart = meetingDate.Date.Add(startTime);
            if (sessionStart <= DateTime.Now)
            {
                throw new InvalidOperationException("לא ניתן לבטל אימון שכבר התחיל או הסתיים.");
            }

            var updateReq = new UpdateItemRequest
            {
                TableName = "TrainingSessions",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["SessionId"] = new AttributeValue { N = sessionId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["Status"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = "Cancelled" }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();

            string childFullName = $"ילד #{childId}";
            var childRes = _dynamoDbClient.GetItemAsync(new GetItemRequest
            {
                TableName = "Children",
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = childId.ToString() } }
            }).GetAwaiter().GetResult();
            if (childRes.Item != null && childRes.Item.Count > 0)
            {
                string fName = childRes.Item.ContainsKey("FirstName") ? childRes.Item["FirstName"].S : "";
                string lName = childRes.Item.ContainsKey("LastName") ? childRes.Item["LastName"].S : "";
                childFullName = $"{fName} {lName}".Trim();
            }

            if (parentId > 0)
            {
                string normalizedChildName = childFullName;
                string reasonText = string.IsNullOrWhiteSpace(cancelReason)
                    ? string.Empty
                    : $" סיבה: {cancelReason.Trim()}";

                string notificationTitle = "שיעור קרוב בוטל";
                string notificationBody =
                    $"האימון של {normalizedChildName} בתאריך {meetingDate:yyyy-MM-dd} בשעה {startTime:hh\\:mm} בוטל על ידי המדריך.{reasonText}";

                string payloadJson = JsonSerializer.Serialize(new
                {
                    type = "upcoming_lesson_cancelled",
                    sessionId,
                    parentId,
                    childId,
                    instructorId,
                    childName = normalizedChildName,
                    meetingDate = meetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    startTime = startTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    endTime = endTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    cancelledBy = "Instructor"
                });

                InsertLessonNotification(
                    "Parent",
                    parentId,
                    "TrainingSessionCancelled",
                    notificationTitle,
                    notificationBody,
                    null,
                    null,
                    payloadJson);
            }

            return true;
        }

        public List<InstructorTrainingSessionViewRecord> GetInstructorTrainingSessions(
            int instructorId,
            DateTime startDate,
            DateTime endDate)
        {
            var sessions = new List<InstructorTrainingSessionViewRecord>();
            string startStr = startDate.ToString("yyyy-MM-dd");
            string endStr = endDate.ToString("yyyy-MM-dd");

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            var childrenMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in childrenScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    childrenMap[idVal] = item;
                }
            }

            var parentsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
            var parentsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in parentsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    parentsMap[idVal] = item;
                }
            }

            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "InstructorId = :instructorId AND MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() },
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var existingSessionsKeySet = new HashSet<string>();

            foreach (var item in tsScan.Items)
            {
                int sessionId = int.Parse(item["SessionId"].N);
                int pId = item.ContainsKey("ParentId") ? int.Parse(item["ParentId"].N) : 0;
                int childId = int.Parse(item["ChildId"].N);
                DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                string targetMetric = item.ContainsKey("TargetMetric") ? item["TargetMetric"].S : "";

                string childFullName = "";
                if (childrenMap.TryGetValue(childId, out var childItem))
                {
                    string fName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                    string lName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                    childFullName = $"{fName} {lName}".Trim();
                }

                string parentFullName = "";
                if (parentsMap.TryGetValue(pId, out var parentItem))
                {
                    string fName = parentItem.ContainsKey("FirstName") ? parentItem["FirstName"].S : "";
                    string lName = parentItem.ContainsKey("LastName") ? parentItem["LastName"].S : "";
                    parentFullName = $"{fName} {lName}".Trim();
                }

                sessions.Add(new InstructorTrainingSessionViewRecord
                {
                    SessionId = sessionId,
                    InstructorId = instructorId,
                    ParentId = pId,
                    ChildId = childId,
                    ChildFullName = childFullName,
                    ParentFullName = parentFullName,
                    MeetingDate = meetingDate.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = status,
                    TargetMetric = targetMetric
                });

                existingSessionsKeySet.Add($"{childId}_{item["MeetingDate"].S}_{item["StartTime"].S}_{item["EndTime"].S}");
            }

            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "InstructorId = :instructorId AND LessonType = :privateType AND MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() },
                    [":privateType"] = new AttributeValue { S = "Private" },
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "IsActive = :active AND ResponseStatus = :approved",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true },
                    [":approved"] = new AttributeValue { S = "Approved" }
                }
            }).GetAwaiter().GetResult();

            var recipientsMap = new Dictionary<int, List<Dictionary<string, AttributeValue>>>();
            foreach (var item in lirScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                if (!recipientsMap.ContainsKey(invId))
                {
                    recipientsMap[invId] = new List<Dictionary<string, AttributeValue>>();
                }
                recipientsMap[invId].Add(item);
            }

            foreach (var liItem in liScan.Items)
            {
                int invitationId = int.Parse(liItem["InvitationId"].N);
                string meetingDateStr = liItem["MeetingDate"].S;
                string startTimeStr = liItem["StartTime"].S;
                string endTimeStr = liItem["EndTime"].S;
                DateTime meetingDate = DateTime.Parse(meetingDateStr);
                TimeSpan startTime = TimeSpan.Parse(startTimeStr);
                TimeSpan endTime = TimeSpan.Parse(endTimeStr);
                string status = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
                string targetMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S : "";

                if (recipientsMap.TryGetValue(invitationId, out var recipients))
                {
                    foreach (var lirItem in recipients)
                    {
                        int childId = int.Parse(lirItem["ChildId"].N);
                        int pId = int.Parse(lirItem["ParentId"].N);

                        string key = $"{childId}_{meetingDateStr}_{startTimeStr}_{endTimeStr}";
                        if (existingSessionsKeySet.Contains(key))
                        {
                            continue;
                        }

                        string childFullName = "";
                        if (childrenMap.TryGetValue(childId, out var childItem))
                        {
                            string fName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                            string lName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                            childFullName = $"{fName} {lName}".Trim();
                        }

                        string parentFullName = "";
                        if (parentsMap.TryGetValue(pId, out var parentItem))
                        {
                            string fName = parentItem.ContainsKey("FirstName") ? parentItem["FirstName"].S : "";
                            string lName = parentItem.ContainsKey("LastName") ? parentItem["LastName"].S : "";
                            parentFullName = $"{fName} {lName}".Trim();
                        }

                        sessions.Add(new InstructorTrainingSessionViewRecord
                        {
                            SessionId = -invitationId,
                            InstructorId = instructorId,
                            ParentId = pId,
                            ChildId = childId,
                            ChildFullName = childFullName,
                            ParentFullName = parentFullName,
                            MeetingDate = meetingDate.Date,
                            StartTime = startTime,
                            EndTime = endTime,
                            Status = status,
                            TargetMetric = targetMetric
                        });
                    }
                }
            }

            return sessions
                .OrderBy(s => s.MeetingDate)
                .ThenBy(s => s.StartTime)
                .ThenBy(s => s.EndTime)
                .ThenBy(s => s.SessionId)
                .ToList();
        }

        public bool GetIsGroupTrainingSessionTimeOccupied(int instructorId, int groupId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime)
        {
            string dateStr = meetingDate.ToString("yyyy-MM-dd");

            // 1. Scan/Query TrainingSessions
            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "MeetingDate = :mdate AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":mdate"] = new AttributeValue { S = dateStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            // Check if there are active child group associations for the groupId
            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "GroupId = :groupId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":groupId"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var groupChildIds = new HashSet<int>();
            foreach (var item in gcScan.Items)
            {
                groupChildIds.Add(int.Parse(item["ChildId"].N));
            }

            foreach (var item in tsScan.Items)
            {
                int instId = int.Parse(item["InstructorId"].N);
                int childId = int.Parse(item["ChildId"].N);
                TimeSpan sessionStart = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan sessionEnd = TimeSpan.Parse(item["EndTime"].S);

                if (instId == instructorId || groupChildIds.Contains(childId))
                {
                    if (sessionStart < endTime && sessionEnd > startTime)
                    {
                        return true;
                    }
                }
            }

            // 2. Scan/Query GroupTrainingSessions
            var gtsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupTrainingSessions",
                FilterExpression = "MeetingDate = :mdate AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":mdate"] = new AttributeValue { S = dateStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in gtsScan.Items)
            {
                int instId = int.Parse(item["InstructorId"].N);
                int gId = int.Parse(item["GroupId"].N);
                TimeSpan sessionStart = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan sessionEnd = TimeSpan.Parse(item["EndTime"].S);

                if (instId == instructorId || gId == groupId)
                {
                    if (sessionStart < endTime && sessionEnd > startTime)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public GroupTrainingSessionRecord PostCreateGroupTrainingSession(
            int groupId,
            int instructorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string notes,
            string targetMetric = "שיעור קבוצתי")
        {
            var allScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupTrainingSessions" }).GetAwaiter().GetResult();
            int maxId = 0;
            foreach (var item in allScan.Items)
            {
                if (item.ContainsKey("SessionId") && int.TryParse(item["SessionId"].N, out int idVal))
                {
                    if (idVal > maxId) maxId = idVal;
                }
            }
            int newSessionId = maxId + 1;

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["SessionId"] = new AttributeValue { N = newSessionId.ToString() },
                ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["MeetingDate"] = new AttributeValue { S = meetingDate.ToString("yyyy-MM-dd") },
                ["StartTime"] = new AttributeValue { S = startTime.ToString("c") },
                ["EndTime"] = new AttributeValue { S = endTime.ToString("c") },
                ["Notes"] = new AttributeValue { S = string.IsNullOrWhiteSpace(notes) ? "" : notes.Trim() },
                ["Status"] = new AttributeValue { S = "Scheduled" },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["TargetMetric"] = new AttributeValue { S = string.IsNullOrWhiteSpace(targetMetric) ? "שיעור קבוצתי" : targetMetric.Trim() }
            };

            _dynamoDbClient.PutItemAsync("GroupTrainingSessions", newItem).GetAwaiter().GetResult();

            return new GroupTrainingSessionRecord
            {
                SessionId = newSessionId,
                GroupId = groupId,
                InstructorId = instructorId,
                MeetingDate = meetingDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                Notes = string.IsNullOrWhiteSpace(notes) ? "" : notes.Trim(),
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow,
                TargetMetric = string.IsNullOrWhiteSpace(targetMetric) ? "שיעור קבוצתי" : targetMetric.Trim()
            };
        }

        public List<InstructorGroupTrainingSessionViewRecord> GetInstructorGroupTrainingSessions(
            int instructorId,
            DateTime startDate,
            DateTime endDate)
        {
            var sessions = new List<InstructorGroupTrainingSessionViewRecord>();
            string startStr = startDate.ToString("yyyy-MM-dd");
            string endStr = endDate.ToString("yyyy-MM-dd");

            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            var groupsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in groupsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    groupsMap[idVal] = item;
                }
            }

            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
            var activeChildrenCounts = new Dictionary<int, int>();
            foreach (var item in gcScan.Items)
            {
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                if (isActive)
                {
                    int gId = int.Parse(item["GroupId"].N);
                    if (!activeChildrenCounts.ContainsKey(gId))
                    {
                        activeChildrenCounts[gId] = 0;
                    }
                    activeChildrenCounts[gId]++;
                }
            }

            var existingKeys = new HashSet<string>();

            // 1. Scan GroupTrainingSessions
            var gtsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupTrainingSessions",
                FilterExpression = "InstructorId = :instructorId AND MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() },
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in gtsScan.Items)
            {
                int sessionId = int.Parse(item["SessionId"].N);
                int gId = int.Parse(item["GroupId"].N);
                DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                string notes = item.ContainsKey("Notes") ? item["Notes"].S : "";
                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                string targetMetric = item.ContainsKey("TargetMetric") ? item["TargetMetric"].S : "שיעור קבוצתי";

                string groupName = "";
                if (groupsMap.TryGetValue(gId, out var groupItem))
                {
                    bool isGroupActive = groupItem.ContainsKey("IsActive") && groupItem["IsActive"].BOOL == true;
                    if (!isGroupActive)
                    {
                        continue;
                    }
                    groupName = groupItem.ContainsKey("Name") ? groupItem["Name"].S : "";
                }
                else
                {
                    continue;
                }

                int activeChildrenCount = 0;
                activeChildrenCounts.TryGetValue(gId, out activeChildrenCount);

                sessions.Add(new InstructorGroupTrainingSessionViewRecord
                {
                    SessionId = sessionId,
                    GroupId = gId,
                    GroupName = groupName,
                    InstructorId = instructorId,
                    MeetingDate = meetingDate.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Notes = notes,
                    Status = status,
                    ActiveChildrenCount = activeChildrenCount,
                    TargetMetric = targetMetric
                });

                existingKeys.Add($"{gId}_{item["MeetingDate"].S}_{item["StartTime"].S}_{item["EndTime"].S}");
            }

            // 2. Scan LessonInvitations where LessonType = "Group" and Status <> "Cancelled"
            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "InstructorId = :instructorId AND LessonType = :groupType AND MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() },
                    [":groupType"] = new AttributeValue { S = "Group" },
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in liScan.Items)
            {
                int invitationId = int.Parse(item["InvitationId"].N);
                int gId = item.ContainsKey("GroupId") ? int.Parse(item["GroupId"].N) : 0;
                if (gId <= 0) continue;

                string meetingDateStr = item["MeetingDate"].S;
                string startTimeStr = item["StartTime"].S;
                string endTimeStr = item["EndTime"].S;

                string key = $"{gId}_{meetingDateStr}_{startTimeStr}_{endTimeStr}";
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                DateTime meetingDate = DateTime.Parse(meetingDateStr);
                TimeSpan startTime = TimeSpan.Parse(startTimeStr);
                TimeSpan endTime = TimeSpan.Parse(endTimeStr);
                string note = item.ContainsKey("GeneralNote") ? item["GeneralNote"].S : "";
                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                string targetMetric = item.ContainsKey("TargetMetric") ? item["TargetMetric"].S : "שיעור קבוצתי";

                string groupName = "";
                if (groupsMap.TryGetValue(gId, out var groupItem))
                {
                    bool isGroupActive = groupItem.ContainsKey("IsActive") && groupItem["IsActive"].BOOL == true;
                    if (!isGroupActive)
                    {
                        continue;
                    }
                    groupName = groupItem.ContainsKey("Name") ? groupItem["Name"].S : "";
                }
                else
                {
                    continue;
                }

                int activeChildrenCount = 0;
                activeChildrenCounts.TryGetValue(gId, out activeChildrenCount);

                sessions.Add(new InstructorGroupTrainingSessionViewRecord
                {
                    SessionId = -invitationId,
                    GroupId = gId,
                    GroupName = groupName,
                    InstructorId = instructorId,
                    MeetingDate = meetingDate.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Notes = note,
                    Status = status,
                    ActiveChildrenCount = activeChildrenCount,
                    TargetMetric = targetMetric
                });
            }

            return sessions
                .OrderBy(s => s.MeetingDate)
                .ThenBy(s => s.StartTime)
                .ThenBy(s => s.EndTime)
                .ThenBy(s => s.SessionId)
                .ToList();
        }

        public List<ParentScheduledLessonRecord> GetParentScheduledLessons(int parentId, DateTime startDate, DateTime endDate)
        {
            var lessons = new List<ParentScheduledLessonRecord>();
            string startStr = startDate.ToString("yyyy-MM-dd");
            string endStr = endDate.ToString("yyyy-MM-dd");

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "ParentId = :parentId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":parentId"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var parentChildrenMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in childrenScan.Items)
            {
                int childId = int.Parse(item["Id"].N);
                parentChildrenMap[childId] = item;
            }

            if (parentChildrenMap.Count == 0)
            {
                return lessons;
            }

            var instructorsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Instructors" }).GetAwaiter().GetResult();
            var instructorsMap = new Dictionary<int, string>();
            foreach (var item in instructorsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    string fName = item.ContainsKey("FirstName") ? item["FirstName"].S : "";
                    string lName = item.ContainsKey("LastName") ? item["LastName"].S : "";
                    instructorsMap[idVal] = $"{fName} {lName}".Trim();
                }
            }

            var groupsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
            var groupsMap = new Dictionary<int, string>();
            foreach (var item in groupsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    bool isGroupActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isGroupActive)
                    {
                        groupsMap[idVal] = item.ContainsKey("Name") ? item["Name"].S : "";
                    }
                }
            }

            var tsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "TrainingSessions",
                FilterExpression = "MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            foreach (var item in tsScan.Items)
            {
                int childId = int.Parse(item["ChildId"].N);
                if (parentChildrenMap.TryGetValue(childId, out var childItem))
                {
                    int sessionId = int.Parse(item["SessionId"].N);
                    int instructorId = int.Parse(item["InstructorId"].N);
                    DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                    TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                    TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                    string status = item.ContainsKey("Status") ? item["Status"].S : "";
                    string notes = item.ContainsKey("Notes") ? item["Notes"].S : "";
                    string targetMetric = item.ContainsKey("TargetMetric") ? item["TargetMetric"].S : "";

                    string childFirstName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                    string childLastName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                    string childFullName = $"{childFirstName} {childLastName}".Trim();

                    instructorsMap.TryGetValue(instructorId, out string instructorFullName);

                    lessons.Add(new ParentScheduledLessonRecord
                    {
                        SessionId = sessionId,
                        LessonType = "Private",
                        ChildId = childId,
                        ChildFullName = childFullName,
                        InstructorId = instructorId,
                        InstructorFullName = instructorFullName ?? "",
                        GroupId = null,
                        GroupName = null,
                        MeetingDate = meetingDate.Date,
                        StartTime = startTime,
                        EndTime = endTime,
                        Status = status,
                        Notes = notes,
                        TargetMetric = targetMetric
                    });
                }
            }

            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var groupInvitationTargetMetric = new Dictionary<string, string>();
            foreach (var liItem in liScan.Items)
            {
                if (liItem.TryGetValue("GroupId", out var gIdVal))
                {
                    string gIdStr = gIdVal.S ?? gIdVal.N ?? "";
                    if (int.TryParse(gIdStr, out int gId) && gId > 0 &&
                        liItem.TryGetValue("MeetingDate", out var dateVal) && dateVal.S != null)
                    {
                        string tMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S ?? "" : "";
                        groupInvitationTargetMetric[$"{gId}_{dateVal.S}"] = tMetric;
                    }
                }
            }

            var gtsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupTrainingSessions",
                FilterExpression = "MeetingDate >= :start AND MeetingDate <= :end AND Status <> :cancelled",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startStr },
                    [":end"] = new AttributeValue { S = endStr },
                    [":cancelled"] = new AttributeValue { S = "Cancelled" }
                }
            }).GetAwaiter().GetResult();

            var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "GroupChildren",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var groupChildrenLookup = new Dictionary<int, List<int>>();
            foreach (var item in gcScan.Items)
            {
                int gId = int.Parse(item["GroupId"].N);
                int cId = int.Parse(item["ChildId"].N);
                if (!groupChildrenLookup.ContainsKey(gId))
                {
                    groupChildrenLookup[gId] = new List<int>();
                }
                groupChildrenLookup[gId].Add(cId);
            }

            foreach (var item in gtsScan.Items)
            {
                int groupId = int.Parse(item["GroupId"].N);
                if (groupsMap.TryGetValue(groupId, out string groupName))
                {
                    if (groupChildrenLookup.TryGetValue(groupId, out var childIds))
                    {
                        foreach (var childId in childIds)
                        {
                            if (parentChildrenMap.TryGetValue(childId, out var childItem))
                            {
                                int sessionId = int.Parse(item["SessionId"].N);
                                int instructorId = int.Parse(item["InstructorId"].N);
                                DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                                TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
                                TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);
                                string status = item.ContainsKey("Status") ? item["Status"].S : "";
                                string notes = item.ContainsKey("Notes") ? item["Notes"].S : "";

                                string childFirstName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                                string childLastName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                                string childFullName = $"{childFirstName} {childLastName}".Trim();

                                instructorsMap.TryGetValue(instructorId, out string instructorFullName);

                                string targetMetric = "";
                                if (item.ContainsKey("TargetMetric") && item["TargetMetric"].S != null)
                                {
                                    targetMetric = item["TargetMetric"].S;
                                }
                                else
                                {
                                    string key = $"{groupId}_{item["MeetingDate"].S}";
                                    groupInvitationTargetMetric.TryGetValue(key, out targetMetric);
                                }

                                lessons.Add(new ParentScheduledLessonRecord
                                {
                                    SessionId = sessionId,
                                    LessonType = "Group",
                                    ChildId = childId,
                                    ChildFullName = childFullName,
                                    InstructorId = instructorId,
                                    InstructorFullName = instructorFullName ?? "",
                                    GroupId = groupId,
                                    GroupName = groupName,
                                    MeetingDate = meetingDate.Date,
                                    StartTime = startTime,
                                    EndTime = endTime,
                                    Status = status,
                                    Notes = notes,
                                    TargetMetric = targetMetric ?? ""
                                });
                            }
                        }
                    }
                }
            }

            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "ParentId = :parentId AND IsActive = :active AND ResponseStatus = :approved",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":parentId"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true },
                    [":approved"] = new AttributeValue { S = "Approved" }
                }
            }).GetAwaiter().GetResult();

            var invitationRecipientsLookup = new Dictionary<int, List<Dictionary<string, AttributeValue>>>();
            foreach (var item in lirScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                if (!invitationRecipientsLookup.ContainsKey(invId))
                {
                    invitationRecipientsLookup[invId] = new List<Dictionary<string, AttributeValue>>();
                }
                invitationRecipientsLookup[invId].Add(item);
            }

            foreach (var liItem in liScan.Items)
            {
                int invitationId = int.Parse(liItem["InvitationId"].N);
                if (invitationRecipientsLookup.TryGetValue(invitationId, out var recipients))
                {
                    int instructorId = int.Parse(liItem["InstructorId"].N);
                    DateTime meetingDate = DateTime.Parse(liItem["MeetingDate"].S);
                    TimeSpan startTime = TimeSpan.Parse(liItem["StartTime"].S);
                    TimeSpan endTime = TimeSpan.Parse(liItem["EndTime"].S);
                    string status = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
                    string lessonType = liItem.ContainsKey("LessonType") ? liItem["LessonType"].S : "";
                    string notes = liItem.ContainsKey("GeneralNote") ? liItem["GeneralNote"].S : "";
                    string targetMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S : "";
                    int? groupId = null;
                    if (liItem.TryGetValue("GroupId", out var gIdValAttr))
                    {
                        string gIdStrVal = gIdValAttr.S ?? gIdValAttr.N ?? "";
                        if (int.TryParse(gIdStrVal, out int gIdVal) && gIdVal > 0)
                        {
                            groupId = gIdVal;
                        }
                    }

                    string groupName = null;
                    if (groupId.HasValue)
                    {
                        groupsMap.TryGetValue(groupId.Value, out groupName);
                    }

                    foreach (var lirItem in recipients)
                    {
                        int childId = int.Parse(lirItem["ChildId"].N);
                        if (parentChildrenMap.TryGetValue(childId, out var childItem))
                        {
                            bool alreadyExists = lessons.Any(l =>
                                l.ChildId == childId &&
                                l.MeetingDate.Date == meetingDate.Date &&
                                l.StartTime == startTime &&
                                l.EndTime == endTime &&
                                l.LessonType.Equals(lessonType, StringComparison.OrdinalIgnoreCase));

                            if (alreadyExists)
                            {
                                continue;
                            }

                            string childFirstName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                            string childLastName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                            string childFullName = $"{childFirstName} {childLastName}".Trim();

                            instructorsMap.TryGetValue(instructorId, out string instructorFullName);

                            lessons.Add(new ParentScheduledLessonRecord
                            {
                                SessionId = -invitationId,
                                LessonType = lessonType,
                                ChildId = childId,
                                ChildFullName = childFullName,
                                InstructorId = instructorId,
                                InstructorFullName = instructorFullName ?? "",
                                GroupId = groupId,
                                GroupName = groupName,
                                MeetingDate = meetingDate.Date,
                                StartTime = startTime,
                                EndTime = endTime,
                                Status = status,
                                Notes = notes,
                                TargetMetric = targetMetric
                            });
                        }
                    }
                }
            }

            return lessons
                .OrderBy(l => l.MeetingDate)
                .ThenBy(l => l.StartTime)
                .ThenBy(l => l.EndTime)
                .ThenBy(l => l.ChildFullName)
                .ThenBy(l => l.LessonType)
                .ToList();
        }

        private ConversationRecord BuildConversationRecord(Dictionary<string, AttributeValue> convItem)
        {
            int convId = int.Parse(convItem["Id"].N);
            int parentId = int.Parse(convItem["ParentId"].N);
            int childId = int.Parse(convItem["ChildId"].N);
            int instructorId = int.Parse(convItem["InstructorId"].N);
            bool isActive = convItem.ContainsKey("IsActive") && convItem["IsActive"].BOOL == true;
            DateTime createdAt = DateTime.Parse(convItem["CreatedAt"].S);
            DateTime? lastMessageAt = null;
            if (convItem.ContainsKey("LastMessageAt") && !string.IsNullOrWhiteSpace(convItem["LastMessageAt"].S))
            {
                lastMessageAt = DateTime.Parse(convItem["LastMessageAt"].S);
            }

            var pScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Parents",
                FilterExpression = "Id = :pid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() }
                }
            }).GetAwaiter().GetResult();
            string pFirstName = "";
            string pLastName = "";
            if (pScan.Items.Count > 0)
            {
                pFirstName = pScan.Items[0].ContainsKey("FirstName") ? pScan.Items[0]["FirstName"].S : "";
                pLastName = pScan.Items[0].ContainsKey("LastName") ? pScan.Items[0]["LastName"].S : "";
            }

            var cReq = new GetItemRequest
            {
                TableName = "Children",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = childId.ToString() }
                }
            };
            var cRes = _dynamoDbClient.GetItemAsync(cReq).GetAwaiter().GetResult();
            string cFirstName = "";
            string cLastName = "";
            if (cRes.Item != null && cRes.Item.Count > 0)
            {
                cFirstName = cRes.Item.ContainsKey("FirstName") ? cRes.Item["FirstName"].S : "";
                cLastName = cRes.Item.ContainsKey("LastName") ? cRes.Item["LastName"].S : "";
            }

            var iScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Instructors",
                FilterExpression = "Id = :iid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":iid"] = new AttributeValue { N = instructorId.ToString() }
                }
            }).GetAwaiter().GetResult();
            string iFirstName = "";
            string iLastName = "";
            if (iScan.Items.Count > 0)
            {
                iFirstName = iScan.Items[0].ContainsKey("FirstName") ? iScan.Items[0]["FirstName"].S : "";
                iLastName = iScan.Items[0].ContainsKey("LastName") ? iScan.Items[0]["LastName"].S : "";
            }

            return new ConversationRecord
            {
                Id = convId,
                ParentId = parentId,
                ChildId = childId,
                InstructorId = instructorId,
                IsActive = isActive,
                CreatedAt = createdAt,
                LastMessageAt = lastMessageAt,
                ParentFullName = $"{pFirstName} {pLastName}".Trim(),
                ChildFullName = $"{cFirstName} {cLastName}".Trim(),
                InstructorFullName = $"{iFirstName} {iLastName}".Trim()
            };
        }

        public ConversationRecord PostGetOrCreateConversation(int parentId, int childId, int instructorId)
        {
            var childGetReq = new GetItemRequest
            {
                TableName = "Children",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = childId.ToString() }
                }
            };
            var childGetRes = _dynamoDbClient.GetItemAsync(childGetReq).GetAwaiter().GetResult();
            if (childGetRes.Item == null || childGetRes.Item.Count == 0 || childGetRes.Item["ParentId"].N != parentId.ToString() || (childGetRes.Item.ContainsKey("IsActive") && childGetRes.Item["IsActive"].BOOL == false))
            {
                throw new InvalidOperationException("Selected child does not belong to this parent.");
            }

            if (!GetInstructorAssignedToParentChild(parentId, childId, instructorId))
            {
                throw new InvalidOperationException("Selected instructor is not assigned to this child.");
            }

            var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Conversations",
                FilterExpression = "ParentId = :pid AND ChildId = :cid AND InstructorId = :iid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() },
                    [":cid"] = new AttributeValue { N = childId.ToString() },
                    [":iid"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            if (convScan.Items.Count > 0)
            {
                var existingItem = convScan.Items[0];
                return BuildConversationRecord(existingItem);
            }

            var allConvScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Conversations" }).GetAwaiter().GetResult();
            int maxId = 0;
            foreach (var item in allConvScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    if (idVal > maxId) maxId = idVal;
                }
            }
            int newId = maxId + 1;
            string createdAt = DateTime.UtcNow.ToString("o");

            var newConv = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = newId.ToString() },
                ["ParentId"] = new AttributeValue { N = parentId.ToString() },
                ["ChildId"] = new AttributeValue { N = childId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["IsActive"] = new AttributeValue { BOOL = true },
                ["CreatedAt"] = new AttributeValue { S = createdAt },
                ["LastMessageAt"] = new AttributeValue { S = "" }
            };

            _dynamoDbClient.PutItemAsync("Conversations", newConv).GetAwaiter().GetResult();
            return BuildConversationRecord(newConv);
        }

        public ConversationRecord? GetConversationById(int conversationId)
        {
            var getReq = new GetItemRequest
            {
                TableName = "Conversations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = conversationId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item == null || getRes.Item.Count == 0)
            {
                return null;
            }
            return BuildConversationRecord(getRes.Item);
        }

        public List<ChatMessageRecord> GetConversationMessages(int conversationId)
        {
            var messagesScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Messages",
                FilterExpression = "ConversationId = :cid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":cid"] = new AttributeValue { N = conversationId.ToString() }
                }
            }).GetAwaiter().GetResult();

            var conversation = GetConversationById(conversationId);
            string parentFullName = conversation != null ? conversation.ParentFullName : "";
            string instructorFullName = conversation != null ? conversation.InstructorFullName : "";

            var messages = new List<ChatMessageRecord>();
            foreach (var item in messagesScan.Items)
            {
                string senderType = item.ContainsKey("SenderType") ? item["SenderType"].S : "";
                string senderName = senderType;
                if (string.Equals(senderType, "Parent", StringComparison.OrdinalIgnoreCase))
                {
                    senderName = parentFullName;
                }
                else if (string.Equals(senderType, "Instructor", StringComparison.OrdinalIgnoreCase))
                {
                    senderName = instructorFullName;
                }

                messages.Add(new ChatMessageRecord
                {
                    Id = int.Parse(item["Id"].N),
                    ConversationId = int.Parse(item["ConversationId"].N),
                    SenderType = senderType,
                    MessageText = item.ContainsKey("MessageText") ? item["MessageText"].S : "",
                    SentAt = DateTime.Parse(item["SentAt"].S),
                    SenderName = senderName
                });
            }

            return messages
                .OrderBy(m => m.SentAt)
                .ThenBy(m => m.Id)
                .ToList();
        }

        public ChatMessageRecord PostCreateConversationMessage(int conversationId, string senderType, int senderId, string messageText)
        {
            var conversation = GetConversationById(conversationId);
            if (conversation == null || !conversation.IsActive)
            {
                throw new InvalidOperationException("Conversation was not found.");
            }

            string normalizedSenderType = NormalizeSenderType(senderType);

            if (normalizedSenderType == "Parent" && senderId != conversation.ParentId)
            {
                throw new UnauthorizedAccessException("Sender id does not match the conversation parent.");
            }

            if (normalizedSenderType == "Instructor" && senderId != conversation.InstructorId)
            {
                throw new UnauthorizedAccessException("Sender id does not match the conversation instructor.");
            }

            var allMsgsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Messages" }).GetAwaiter().GetResult();
            int maxId = 0;
            foreach (var item in allMsgsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                {
                    if (idVal > maxId) maxId = idVal;
                }
            }
            int newMsgId = maxId + 1;
            DateTime sentAt = DateTime.UtcNow;

            var newMsg = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = newMsgId.ToString() },
                ["ParentId"] = new AttributeValue { N = conversation.ParentId.ToString() },
                ["InstructorId"] = new AttributeValue { N = conversation.InstructorId.ToString() },
                ["SenderType"] = new AttributeValue { S = normalizedSenderType },
                ["MessageText"] = new AttributeValue { S = messageText.Trim() },
                ["SentAt"] = new AttributeValue { S = sentAt.ToString("o") },
                ["ConversationId"] = new AttributeValue { N = conversationId.ToString() }
            };

            _dynamoDbClient.PutItemAsync("Messages", newMsg).GetAwaiter().GetResult();

            var convKey = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new AttributeValue { N = conversationId.ToString() }
            };
            var convUpdates = new Dictionary<string, AttributeValueUpdate>
            {
                ["LastMessageAt"] = new AttributeValueUpdate
                {
                    Action = AttributeAction.PUT,
                    Value = new AttributeValue { S = sentAt.ToString("o") }
                }
            };
            _dynamoDbClient.UpdateItemAsync("Conversations", convKey, convUpdates).GetAwaiter().GetResult();

            return new ChatMessageRecord
            {
                Id = newMsgId,
                ConversationId = conversationId,
                SenderType = normalizedSenderType,
                MessageText = messageText.Trim(),
                SentAt = sentAt,
                SenderName = normalizedSenderType == "Parent" ? conversation.ParentFullName : conversation.InstructorFullName
            };
        }

        public void UpsertPushDeviceToken(string userType, int userId, string pushToken, string platform, string? deviceId)
        {
            string normalizedUserType = NormalizeNotificationUserType(userType);
            string normalizedPushToken = string.IsNullOrWhiteSpace(pushToken) ? string.Empty : pushToken.Trim();
            string normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? string.Empty : platform.Trim();
            string normalizedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId.Trim();

            if (userId <= 0)
            {
                throw new InvalidOperationException("User id must be a positive number.");
            }

            if (string.IsNullOrWhiteSpace(normalizedPushToken))
            {
                throw new InvalidOperationException("Push token is required.");
            }

            bool looksLikeExpoToken = normalizedPushToken.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
                || normalizedPushToken.StartsWith("ExpoPushToken[", StringComparison.Ordinal);

            if (!looksLikeExpoToken)
            {
                throw new InvalidOperationException("Push token is not a valid Expo token.");
            }

            if (normalizedUserType == "Parent")
            {
                var parent = GetParentByNumericId(userId);
                if (parent == null || !parent.ContainsKey("IsActive") || parent["IsActive"].BOOL == false)
                {
                    throw new InvalidOperationException("User was not found or is inactive.");
                }
            }
            else if (normalizedUserType == "Instructor")
            {
                var inst = GetInstructorByNumericId(userId);
                if (inst == null || !inst.ContainsKey("IsActive") || inst["IsActive"].BOOL == false)
                {
                    throw new InvalidOperationException("User was not found or is inactive.");
                }
            }
            else
            {
                throw new InvalidOperationException("User was not found or is inactive.");
            }

            var tokenKey = new Dictionary<string, AttributeValue> { ["ExpoPushToken"] = new AttributeValue { S = normalizedPushToken } };
            var getRes = _dynamoDbClient.GetItemAsync("PushDeviceTokens", tokenKey).GetAwaiter().GetResult();

            int tokenId;
            string createdAt;
            if (getRes.Item != null && getRes.Item.Count > 0)
            {
                tokenId = int.Parse(getRes.Item["Id"].N);
                createdAt = getRes.Item.ContainsKey("CreatedAt") ? getRes.Item["CreatedAt"].S : DateTime.UtcNow.ToString("o");
            }
            else
            {
                int maxId = 0;
                var scanRes = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "PushDeviceTokens", ProjectionExpression = "Id" }).GetAwaiter().GetResult();
                foreach (var item in scanRes.Items)
                {
                    if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                    {
                        if (idVal > maxId) maxId = idVal;
                    }
                }
                tokenId = maxId + 1;
                createdAt = DateTime.UtcNow.ToString("o");
            }

            var updatedItem = new Dictionary<string, AttributeValue>
            {
                ["ExpoPushToken"] = new AttributeValue { S = normalizedPushToken },
                ["Id"] = new AttributeValue { N = tokenId.ToString() },
                ["UserType"] = new AttributeValue { S = normalizedUserType },
                ["UserId"] = new AttributeValue { N = userId.ToString() },
                ["Platform"] = new AttributeValue { S = normalizedPlatform },
                ["DeviceId"] = new AttributeValue { S = normalizedDeviceId },
                ["IsActive"] = new AttributeValue { BOOL = true },
                ["CreatedAt"] = new AttributeValue { S = createdAt },
                ["UpdatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["LastRegisteredAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("PushDeviceTokens", updatedItem).GetAwaiter().GetResult();
        }

        public void DeactivatePushDeviceToken(string userType, int userId, string pushToken)
        {
            string normalizedUserType = NormalizeNotificationUserType(userType);
            string normalizedPushToken = string.IsNullOrWhiteSpace(pushToken) ? string.Empty : pushToken.Trim();

            if (userId <= 0 || string.IsNullOrWhiteSpace(normalizedPushToken))
            {
                return;
            }

            var tokenKey = new Dictionary<string, AttributeValue> { ["ExpoPushToken"] = new AttributeValue { S = normalizedPushToken } };
            var getRes = _dynamoDbClient.GetItemAsync("PushDeviceTokens", tokenKey).GetAwaiter().GetResult();
            if (getRes.Item != null && getRes.Item.Count > 0)
            {
                var item = getRes.Item;
                string itemUserType = item.ContainsKey("UserType") ? item["UserType"].S : "";
                int itemUserId = item.ContainsKey("UserId") ? int.Parse(item["UserId"].N) : 0;

                if (string.Equals(itemUserType, normalizedUserType, StringComparison.OrdinalIgnoreCase) && itemUserId == userId)
                {
                    var updates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsActive"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = false } },
                        ["UpdatedAt"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                    };
                    _dynamoDbClient.UpdateItemAsync("PushDeviceTokens", tokenKey, updates).GetAwaiter().GetResult();
                }
            }
        }

        public List<string> GetConversationRecipientPushTokens(int conversationId, string senderType, int senderId)
        {
            string normalizedSenderType = NormalizeSenderType(senderType);
            if (senderId <= 0)
            {
                return new List<string>();
            }

            var conversation = GetConversationById(conversationId);
            if (conversation == null || !conversation.IsActive)
            {
                return new List<string>();
            }

            string recipientType;
            int recipientId;

            if (normalizedSenderType == "Parent")
            {
                if (senderId != conversation.ParentId)
                {
                    return new List<string>();
                }

                recipientType = "Instructor";
                recipientId = conversation.InstructorId;
            }
            else
            {
                if (senderId != conversation.InstructorId)
                {
                    return new List<string>();
                }

                recipientType = "Parent";
                recipientId = conversation.ParentId;
            }

            var scanReq = new ScanRequest
            {
                TableName = "PushDeviceTokens",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = recipientType },
                    [":uid"] = new AttributeValue { N = recipientId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };

            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();

            var sortedItems = scanRes.Items
                .Select(item => new
                {
                    Token = item.ContainsKey("ExpoPushToken") ? item["ExpoPushToken"].S.Trim() : "",
                    UpdatedAt = item.ContainsKey("UpdatedAt") ? item["UpdatedAt"].S : "",
                    Id = item.ContainsKey("Id") ? int.Parse(item["Id"].N) : 0
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            var tokens = new List<string>();
            var uniqueTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in sortedItems)
            {
                if (uniqueTokens.Add(item.Token))
                {
                    tokens.Add(item.Token);
                }
            }

            return tokens;
        }

        public List<string> GetActivePushTokensForUser(string userType, int userId)
        {
            string normalizedUserType = NormalizeNotificationUserType(userType);
            if (userId <= 0)
            {
                return new List<string>();
            }

            var scanReq = new ScanRequest
            {
                TableName = "PushDeviceTokens",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = normalizedUserType },
                    [":uid"] = new AttributeValue { N = userId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };

            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();

            var sortedItems = scanRes.Items
                .Select(item => new
                {
                    Token = item.ContainsKey("ExpoPushToken") ? item["ExpoPushToken"].S.Trim() : "",
                    UpdatedAt = item.ContainsKey("UpdatedAt") ? item["UpdatedAt"].S : "",
                    Id = item.ContainsKey("Id") ? int.Parse(item["Id"].N) : 0
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            var tokens = new List<string>();
            var uniqueTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in sortedItems)
            {
                if (uniqueTokens.Add(item.Token))
                {
                    tokens.Add(item.Token);
                }
            }

            return tokens;
        }

        public List<ConversationInboxRecord> GetInstructorConversationInbox(int instructorId)
        {
            var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Conversations",
                FilterExpression = "InstructorId = :iid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":iid"] = new AttributeValue { N = instructorId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            List<ConversationInboxRecord> inboxItems = new List<ConversationInboxRecord>();
            foreach (var conv in convScan.Items)
            {
                if (!conv.ContainsKey("LastMessageAt") || string.IsNullOrWhiteSpace(conv["LastMessageAt"].S))
                {
                    continue;
                }

                var conversationRecord = BuildConversationRecord(conv);

                var msgScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Messages",
                    FilterExpression = "ConversationId = :cid",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = conversationRecord.Id.ToString() }
                    }
                }).GetAwaiter().GetResult();

                string lastMessageText = "";
                string lastMessageSenderType = "";
                DateTime? lastMsgAt = null;

                var sortedMsgs = msgScan.Items
                    .Select(m => new {
                        Text = m.ContainsKey("MessageText") ? m["MessageText"].S : "",
                        SenderType = m.ContainsKey("SenderType") ? m["SenderType"].S : "",
                        SentAt = DateTime.Parse(m["SentAt"].S),
                        Id = int.Parse(m["Id"].N)
                    })
                    .OrderByDescending(m => m.SentAt)
                    .ThenByDescending(m => m.Id)
                    .FirstOrDefault();

                if (sortedMsgs != null)
                {
                    lastMessageText = sortedMsgs.Text;
                    lastMessageSenderType = sortedMsgs.SenderType;
                    lastMsgAt = sortedMsgs.SentAt;
                }

                inboxItems.Add(new ConversationInboxRecord
                {
                    ConversationId = conversationRecord.Id,
                    ParentId = conversationRecord.ParentId,
                    ChildId = conversationRecord.ChildId,
                    InstructorId = conversationRecord.InstructorId,
                    ParentFullName = conversationRecord.ParentFullName,
                    ChildFullName = conversationRecord.ChildFullName,
                    LastMessageAt = lastMsgAt,
                    LastMessageText = lastMessageText,
                    LastMessageSenderType = lastMessageSenderType
                });
            }

            return inboxItems
                .OrderByDescending(i => i.LastMessageAt)
                .ThenByDescending(i => i.ConversationId)
                .ToList();
        }

        public List<ConversationInboxRecord> GetParentConversationInbox(int parentId)
        {
            var convScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Conversations",
                FilterExpression = "ParentId = :pid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pid"] = new AttributeValue { N = parentId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            List<ConversationInboxRecord> inboxItems = new List<ConversationInboxRecord>();
            foreach (var conv in convScan.Items)
            {
                if (!conv.ContainsKey("LastMessageAt") || string.IsNullOrWhiteSpace(conv["LastMessageAt"].S))
                {
                    continue;
                }

                var conversationRecord = BuildConversationRecord(conv);

                var msgScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Messages",
                    FilterExpression = "ConversationId = :cid",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":cid"] = new AttributeValue { N = conversationRecord.Id.ToString() }
                    }
                }).GetAwaiter().GetResult();

                string lastMessageText = "";
                string lastMessageSenderType = "";
                DateTime? lastMsgAt = null;

                var sortedMsgs = msgScan.Items
                    .Select(m => new {
                        Text = m.ContainsKey("MessageText") ? m["MessageText"].S : "",
                        SenderType = m.ContainsKey("SenderType") ? m["SenderType"].S : "",
                        SentAt = DateTime.Parse(m["SentAt"].S),
                        Id = int.Parse(m["Id"].N)
                    })
                    .OrderByDescending(m => m.SentAt)
                    .ThenByDescending(m => m.Id)
                    .FirstOrDefault();

                if (sortedMsgs != null)
                {
                    lastMessageText = sortedMsgs.Text;
                    lastMessageSenderType = sortedMsgs.SenderType;
                    lastMsgAt = sortedMsgs.SentAt;
                }

                inboxItems.Add(new ConversationInboxRecord
                {
                    ConversationId = conversationRecord.Id,
                    ParentId = conversationRecord.ParentId,
                    ChildId = conversationRecord.ChildId,
                    InstructorId = conversationRecord.InstructorId,
                    ParentFullName = conversationRecord.ParentFullName,
                    ChildFullName = conversationRecord.ChildFullName,
                    InstructorFullName = conversationRecord.InstructorFullName,
                    LastMessageAt = lastMsgAt,
                    LastMessageText = lastMessageText,
                    LastMessageSenderType = lastMessageSenderType
                });
            }

            return inboxItems
                .OrderByDescending(i => i.LastMessageAt)
                .ThenByDescending(i => i.ConversationId)
                .ToList();
        }

        public void PostCreateChatMessageNotification(
            int conversationId,
            string senderType,
            int senderId,
            int messageId,
            string messageText)
        {
            if (conversationId <= 0 || messageId <= 0 || senderId <= 0)
            {
                return;
            }

            string normalizedSenderType;
            try
            {
                normalizedSenderType = NormalizeSenderType(senderType);
            }
            catch
            {
                return;
            }

            var conversation = GetConversationById(conversationId);
            if (conversation == null || !conversation.IsActive)
            {
                return;
            }

            string recipientUserType;
            int recipientUserId;
            string senderDisplayName;

            string parentFullName = conversation.ParentFullName;
            string instructorFullName = conversation.InstructorFullName;
            string childFullName = conversation.ChildFullName;

            if (string.Equals(normalizedSenderType, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                if (senderId != conversation.ParentId)
                {
                    return;
                }

                recipientUserType = "Instructor";
                recipientUserId = conversation.InstructorId;
                senderDisplayName = string.IsNullOrWhiteSpace(parentFullName) ? "הורה" : parentFullName;
            }
            else
            {
                if (senderId != conversation.InstructorId)
                {
                    return;
                }

                recipientUserType = "Parent";
                recipientUserId = conversation.ParentId;
                senderDisplayName = string.IsNullOrWhiteSpace(instructorFullName) ? "מדריך" : instructorFullName;
            }

            string messagePreview = string.IsNullOrWhiteSpace(messageText)
                ? string.Empty
                : messageText.Trim();

            if (messagePreview.Length > 160)
            {
                messagePreview = messagePreview[..157] + "...";
            }

            string title = $"הודעה חדשה מ-{senderDisplayName}";
            string body = string.IsNullOrWhiteSpace(messagePreview)
                ? "נשלחה הודעה חדשה בצ׳אט."
                : messagePreview;

            string payloadJson = JsonSerializer.Serialize(new
            {
                type = "chat_message",
                conversationId,
                messageId,
                parentId = conversation.ParentId,
                childId = conversation.ChildId,
                childName = childFullName,
                instructorId = conversation.InstructorId,
                instructorName = instructorFullName,
                senderType = normalizedSenderType,
                senderId
            });

            InsertLessonNotification(
                recipientUserType,
                recipientUserId,
                "ChatMessage",
                title,
                body,
                null,
                null,
                payloadJson);
        }

        public HashSet<int> GetActiveChildIdsInGroup(int groupId)
        {
            var childIds = new HashSet<int>();
            try
            {
                var gcScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "GroupChildren",
                    FilterExpression = "GroupId = :gid AND IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":gid"] = new AttributeValue { N = groupId.ToString() },
                        [":active"] = new AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                foreach (var item in gcScan.Items)
                {
                    childIds.Add(int.Parse(item["ChildId"].N));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetActiveChildIdsInGroup: {ex.Message}");
            }
            return childIds;
        }

        public List<int> GetInstructorGroupRecipientParents(int instructorId, int groupId)
        {
            var parentIds = new List<int>();

            // 1. Verify that this group is actively assigned to the instructor
            var instructorGroupsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "InstructorId = :instId AND GroupId = :gid AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() },
                    [":gid"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            if (instructorGroupsScan.Items.Count == 0)
            {
                return parentIds;
            }

            // 2. Get active children in this group
            var childIds = GetActiveChildIdsInGroup(groupId);
            if (childIds.Count == 0)
            {
                return parentIds;
            }

            // 3. Scan Children to find ParentIds for these children
            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            foreach (var childItem in childrenScan.Items)
            {
                int childId = int.Parse(childItem["Id"].N);
                if (childIds.Contains(childId))
                {
                    int parentId = int.Parse(childItem["ParentId"].N);
                    parentIds.Add(parentId);
                }
            }

            return parentIds.Distinct().ToList();
        }

        public List<LessonInvitationRecipientOptionRecord> GetInstructorInvitationRecipientOptions(int instructorId)
        {
            var options = new List<LessonInvitationRecipientOptionRecord>();

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "Children",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var parentsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
            var parentsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            foreach (var item in parentsScan.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int pId))
                {
                    bool isParentActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isParentActive)
                    {
                        parentsMap[pId] = item;
                    }
                }
            }

            foreach (var childItem in childrenScan.Items)
            {
                int childId = int.Parse(childItem["Id"].N);
                int parentId = int.Parse(childItem["ParentId"].N);

                if (parentsMap.TryGetValue(parentId, out var parentItem))
                {
                    var instructorIds = GetInstructorIdsForChild(parentId, childId);
                    if (instructorIds.Contains(instructorId))
                    {
                        string childFirstName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                        string childLastName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                        string childFullName = $"{childFirstName} {childLastName}".Trim();

                        string parentFirstName = parentItem.ContainsKey("FirstName") ? parentItem["FirstName"].S : "";
                        string parentLastName = parentItem.ContainsKey("LastName") ? parentItem["LastName"].S : "";
                        string parentFullName = $"{parentFirstName} {parentLastName}".Trim();
                        string parentEmail = parentItem.ContainsKey("Email") ? parentItem["Email"].S : "";

                        options.Add(new LessonInvitationRecipientOptionRecord
                        {
                            ParentId = parentId,
                            ChildId = childId,
                            ParentFullName = parentFullName,
                            ParentEmail = parentEmail,
                            ChildFullName = childFullName
                        });
                    }
                }
            }

            return options
                .OrderBy(o => o.ParentFullName)
                .ThenBy(o => o.ChildFullName)
                .ToList();
        }

        public LessonInvitationRecord PostCreateLessonInvitation(
            int instructorId,
            string lessonType,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int? groupId,
            string generalNote,
            string targetMetric,
            int capacity,
            int minRegistrations,
            List<LessonInvitationRecipientSelectionRecord> recipients)
        {
            string normalizedLessonType = NormalizeLessonType(lessonType);
            TimeSpan duration = endTime - startTime;
            if (duration.TotalMinutes < 45 || duration.TotalMinutes > 90)
            {
                throw new InvalidOperationException("Lesson duration must be about one hour (45-90 minutes).");
            }

            int normalizedCapacity = Math.Clamp(capacity <= 0 ? 20 : capacity, 1, 20);
            int normalizedMinRegistrations = normalizedLessonType == "Private"
                ? 1
                : Math.Clamp(minRegistrations <= 0 ? 2 : minRegistrations, 2, normalizedCapacity);

            List<LessonInvitationRecipientSelectionRecord> normalizedRecipients = (recipients ?? new List<LessonInvitationRecipientSelectionRecord>())
                .Where((item) => item.ParentId > 0 && item.ChildId > 0)
                .GroupBy((item) => $"{item.ParentId}:{item.ChildId}")
                .Select((group) => group.First())
                .ToList();

            if (normalizedLessonType == "Private" && normalizedRecipients.Count != 1)
            {
                throw new InvalidOperationException("Private lesson invitation must include exactly one parent-child pair.");
            }

            if (normalizedRecipients.Count == 0)
            {
                throw new InvalidOperationException("At least one recipient is required.");
            }

            if (normalizedRecipients.Count > normalizedCapacity)
            {
                throw new InvalidOperationException("Number of selected recipients exceeds lesson capacity.");
            }

            string normalizedNote = string.IsNullOrWhiteSpace(generalNote) ? string.Empty : generalNote.Trim();
            string normalizedTargetMetric = string.IsNullOrWhiteSpace(targetMetric) ? string.Empty : targetMetric.Trim();

            List<(int ParentId, int ChildId, string ParentFullName, string ChildFullName)> recipientDetails = new List<(int, int, string, string)>();

            foreach (LessonInvitationRecipientSelectionRecord recipient in normalizedRecipients)
            {
                var instructorIds = GetInstructorIdsForChild(recipient.ParentId, recipient.ChildId);
                if (!instructorIds.Contains(instructorId))
                {
                    throw new InvalidOperationException("One or more selected parent-child pairs are not assigned to the instructor.");
                }

                string childFullName = "";
                var childRes = _dynamoDbClient.GetItemAsync(new GetItemRequest
                {
                    TableName = "Children",
                    Key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = recipient.ChildId.ToString() } }
                }).GetAwaiter().GetResult();
                if (childRes.Item != null && childRes.Item.Count > 0)
                {
                    string fName = childRes.Item.ContainsKey("FirstName") ? childRes.Item["FirstName"].S : "";
                    string lName = childRes.Item.ContainsKey("LastName") ? childRes.Item["LastName"].S : "";
                    childFullName = $"{fName} {lName}".Trim();
                }

                string parentFullName = "";
                var parentRes = GetParentByNumericId(recipient.ParentId);
                if (parentRes != null)
                {
                    string fName = parentRes.ContainsKey("FirstName") ? parentRes["FirstName"].S : "";
                    string lName = parentRes.ContainsKey("LastName") ? parentRes["LastName"].S : "";
                    parentFullName = $"{fName} {lName}".Trim();
                }

                recipientDetails.Add((
                    recipient.ParentId,
                    recipient.ChildId,
                    parentFullName,
                    childFullName));
            }

            var allInvitationsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitations" }).GetAwaiter().GetResult();
            int maxInvitationId = 0;
            foreach (var item in allInvitationsScan.Items)
            {
                if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int idVal))
                {
                    if (idVal > maxInvitationId) maxInvitationId = idVal;
                }
            }
            int invitationId = maxInvitationId + 1;

            var newInvitation = new Dictionary<string, AttributeValue>
            {
                ["InvitationId"] = new AttributeValue { N = invitationId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["LessonType"] = new AttributeValue { S = normalizedLessonType },
                ["GroupId"] = new AttributeValue { N = groupId.HasValue && groupId.Value > 0 ? groupId.Value.ToString() : "0" },
                ["MeetingDate"] = new AttributeValue { S = meetingDate.ToString("yyyy-MM-dd") },
                ["StartTime"] = new AttributeValue { S = startTime.ToString("c") },
                ["EndTime"] = new AttributeValue { S = endTime.ToString("c") },
                ["Capacity"] = new AttributeValue { N = normalizedCapacity.ToString() },
                ["MinRegistrations"] = new AttributeValue { N = normalizedMinRegistrations.ToString() },
                ["GeneralNote"] = new AttributeValue { S = normalizedNote },
                ["Status"] = new AttributeValue { S = "Pending" },
                ["TargetMetric"] = new AttributeValue { S = normalizedTargetMetric },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["UpdatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("LessonInvitations", newInvitation).GetAwaiter().GetResult();

            var allRecipientsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
            int maxRecipientId = 0;
            foreach (var item in allRecipientsScan.Items)
            {
                if (item.ContainsKey("RecipientId") && int.TryParse(item["RecipientId"].N, out int idVal))
                {
                    if (idVal > maxRecipientId) maxRecipientId = idVal;
                }
            }

            foreach ((int ParentId, int ChildId, string ParentFullName, string ChildFullName) detail in recipientDetails)
            {
                maxRecipientId++;
                int recipientId = maxRecipientId;

                var newRecipient = new Dictionary<string, AttributeValue>
                {
                    ["RecipientId"] = new AttributeValue { N = recipientId.ToString() },
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() },
                    ["ParentId"] = new AttributeValue { N = detail.ParentId.ToString() },
                    ["ChildId"] = new AttributeValue { N = detail.ChildId.ToString() },
                    ["ResponseStatus"] = new AttributeValue { S = "Pending" },
                    ["RespondedAt"] = new AttributeValue { S = "" },
                    ["IsActive"] = new AttributeValue { BOOL = true }
                };
                _dynamoDbClient.PutItemAsync("LessonInvitationRecipients", newRecipient).GetAwaiter().GetResult();

                string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(detail.ChildFullName)
                    ? "הילד/ה"
                    : detail.ChildFullName.Trim();

                string title = normalizedLessonType == "Group"
                    ? "הזמנה לשיעור קבוצתי"
                    : "הזמנה לשיעור פרטי";

                string body = normalizedLessonType == "Group"
                    ? $"נפתחה עבורך הזמנה לשיעור קבוצתי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}. אנא אשרו או דחו את ההשתתפות דרך האפליקציה."
                    : $"נפתחה עבורך הזמנה לשיעור פרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}. אנא אשרו או דחו את ההזמנה דרך האפליקציה.";

                if (!string.IsNullOrWhiteSpace(normalizedTargetMetric))
                {
                    body = body + " \nמדד מטרה: " + normalizedTargetMetric;
                }

                if (!string.IsNullOrWhiteSpace(normalizedNote))
                {
                    body = body + " \nהערת מדריך/ה: " + normalizedNote;
                }

                InsertLessonNotification(
                    "Parent",
                    detail.ParentId,
                    "LessonInvite",
                    title,
                    body,
                    invitationId,
                    recipientId,
                    string.Empty);
            }

            InsertLessonNotification(
                "Instructor",
                instructorId,
                "LessonCreated",
                "הזמנות לשיעור נשלחו בהצלחה",
                $"נשלחו {recipientDetails.Count} הזמנות לשיעור {(normalizedLessonType == "Group" ? "קבוצתי" : "פרטי")} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}.",
                invitationId,
                null,
                string.Empty);

            return new LessonInvitationRecord
            {
                InvitationId = invitationId,
                InstructorId = instructorId,
                LessonType = normalizedLessonType,
                GroupId = groupId,
                MeetingDate = meetingDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                Capacity = normalizedCapacity,
                MinRegistrations = normalizedMinRegistrations,
                GeneralNote = normalizedNote,
                Status = "Pending",
                RecipientsCount = recipientDetails.Count,
                ApprovedCount = 0,
                RejectedCount = 0,
                PendingCount = recipientDetails.Count,
                CreatedAtUtc = DateTime.UtcNow,
                TargetMetric = normalizedTargetMetric
            };
        }

        public List<LessonInvitationRecord> GetInstructorLessonInvitations(int instructorId)
        {
            AutoCancelAllExpiredInvitations();
            var invitations = new List<LessonInvitationRecord>();

            var liScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitations",
                FilterExpression = "InstructorId = :instructorId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() }
                }
            }).GetAwaiter().GetResult();

            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var recipientsMap = new Dictionary<int, List<Dictionary<string, AttributeValue>>>();
            foreach (var item in lirScan.Items)
            {
                int invId = int.Parse(item["InvitationId"].N);
                if (!recipientsMap.ContainsKey(invId))
                {
                    recipientsMap[invId] = new List<Dictionary<string, AttributeValue>>();
                }
                recipientsMap[invId].Add(item);
            }

            var reportScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "InstructorId = :instructorId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instructorId"] = new AttributeValue { N = instructorId.ToString() }
                }
            }).GetAwaiter().GetResult();

            foreach (var liItem in liScan.Items)
            {
                int invitationId = int.Parse(liItem["InvitationId"].N);
                string lessonType = liItem.ContainsKey("LessonType") ? liItem["LessonType"].S : "";
                int? groupId = null;
                if (liItem.TryGetValue("GroupId", out var gIdValAttr))
                {
                    string gIdStrVal = gIdValAttr.S ?? gIdValAttr.N ?? "";
                    if (int.TryParse(gIdStrVal, out int gIdVal) && gIdVal > 0)
                    {
                        groupId = gIdVal;
                    }
                }
                DateTime meetingDate = DateTime.Parse(liItem["MeetingDate"].S);
                TimeSpan startTime = TimeSpan.Parse(liItem["StartTime"].S);
                TimeSpan endTime = TimeSpan.Parse(liItem["EndTime"].S);
                int capacity = liItem.ContainsKey("Capacity") ? int.Parse(liItem["Capacity"].N) : 20;
                int minRegistrations = liItem.ContainsKey("MinRegistrations") ? int.Parse(liItem["MinRegistrations"].N) : 2;
                string generalNote = liItem.ContainsKey("GeneralNote") ? liItem["GeneralNote"].S : "";
                string status = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
                DateTime createdAtUtc = liItem.ContainsKey("CreatedAt") ? DateTime.Parse(liItem["CreatedAt"].S) : DateTime.UtcNow;
                string targetMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S : "";

                int recipientsCount = 0;
                int approvedCount = 0;
                int rejectedCount = 0;
                int pendingCount = 0;

                if (recipientsMap.TryGetValue(invitationId, out var recipients))
                {
                    recipientsCount = recipients.Count;
                    foreach (var lir in recipients)
                    {
                        string rStatus = lir.ContainsKey("ResponseStatus") ? lir["ResponseStatus"].S : "";
                        if (string.Equals(rStatus, "Approved", StringComparison.OrdinalIgnoreCase)) approvedCount++;
                        else if (string.Equals(rStatus, "Rejected", StringComparison.OrdinalIgnoreCase)) rejectedCount++;
                        else pendingCount++;
                    }
                }

                bool hasReport = false;
                string meetingDateStr = liItem["MeetingDate"].S;
                foreach (var rc in reportScan.Items)
                {
                    string rcReportDate = rc.ContainsKey("ReportDate") ? rc["ReportDate"].S : "";
                    if (rcReportDate == meetingDateStr)
                    {
                        int? rcGroupId = rc.ContainsKey("GroupId") && int.TryParse(rc["GroupId"].N, out int rcGId) && rcGId > 0 ? (int?)rcGId : null;
                        if (rcGroupId == groupId)
                        {
                            hasReport = true;
                            break;
                        }
                    }
                }

                invitations.Add(new LessonInvitationRecord
                {
                    InvitationId = invitationId,
                    InstructorId = instructorId,
                    LessonType = lessonType,
                    GroupId = groupId,
                    MeetingDate = meetingDate.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Capacity = capacity,
                    MinRegistrations = minRegistrations,
                    GeneralNote = generalNote,
                    Status = status,
                    RecipientsCount = recipientsCount,
                    ApprovedCount = approvedCount,
                    RejectedCount = rejectedCount,
                    PendingCount = pendingCount,
                    CreatedAtUtc = createdAtUtc,
                    TargetMetric = targetMetric,
                    HasReport = hasReport
                });
            }

            return invitations
                .OrderByDescending(li => li.MeetingDate)
                .ThenByDescending(li => li.StartTime)
                .ThenByDescending(li => li.InvitationId)
                .ToList();
        }

        public void AutoCancelAllExpiredInvitations()
        {
            try
            {
                var scanRes = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "LessonInvitations",
                    FilterExpression = "#status = :pending",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":pending"] = new AttributeValue { S = "Pending" }
                    }
                }).GetAwaiter().GetResult();

                foreach (var item in scanRes.Items)
                {
                    int invitationId = int.Parse(item["InvitationId"].N);
                    int instructorId = int.Parse(item["InstructorId"].N);
                    DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
                    TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);

                    if (DateTime.Now >= meetingDate.Date.Add(startTime))
                    {
                        PostCancelLessonInvitation(instructorId, invitationId, "בוטל אוטומטית מאחר שלא אושר בזמן.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AutoCancelAllExpiredInvitations: {ex.Message}");
            }
        }

        public List<LessonNotificationRecord> GetUserLessonNotifications(string userType, int userId)
        {
            AutoCancelAllExpiredInvitations();
            string normalizedUserType = NormalizeNotificationUserType(userType);

            // 1. Scan/Fetch matching notifications
            var scanNotificationsReq = new ScanRequest
            {
                TableName = "LessonNotifications",
                FilterExpression = "UserType = :utype AND UserId = :uid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = normalizedUserType },
                    [":uid"] = new AttributeValue { N = userId.ToString() }
                }
            };
            var notificationsRes = _dynamoDbClient.ScanAsync(scanNotificationsReq).GetAwaiter().GetResult();
            var notificationItems = notificationsRes.Items;

            var recipientsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanRecipients = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
            foreach (var item in scanRecipients.Items)
            {
                if (item.ContainsKey("RecipientId") && int.TryParse(item["RecipientId"].N, out int rId))
                {
                    recipientsMap[rId] = item;
                }
            }

            var invitationsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanInvitations = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitations" }).GetAwaiter().GetResult();
            foreach (var item in scanInvitations.Items)
            {
                if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int invId))
                {
                    invitationsMap[invId] = item;
                }
            }

            List<LessonNotificationRecord> notifications = new List<LessonNotificationRecord>();

            foreach (var n in notificationItems)
            {
                int notificationId = int.Parse(n["NotificationId"].N);
                string nType = n.ContainsKey("NotificationType") ? n["NotificationType"].S : "";
                string title = n.ContainsKey("Title") ? n["Title"].S : "";
                string body = n.ContainsKey("Body") ? n["Body"].S : "";
                
                int? relatedInvitationId = n.ContainsKey("RelatedInvitationId") && int.TryParse(n["RelatedInvitationId"].N, out int relatedInvId) && relatedInvId > 0 ? (int?)relatedInvId : null;
                int? relatedRecipientId = n.ContainsKey("RelatedRecipientId") && int.TryParse(n["RelatedRecipientId"].N, out int relatedRecId) && relatedRecId > 0 ? (int?)relatedRecId : null;
                
                bool isRead = n.ContainsKey("IsRead") && n["IsRead"].BOOL == true;
                DateTime createdAtUtc = n.ContainsKey("CreatedAt") ? DateTime.Parse(n["CreatedAt"].S) : DateTime.UtcNow;
                DateTime? readAtUtc = n.ContainsKey("ReadAt") && !string.IsNullOrWhiteSpace(n["ReadAt"].S) ? (DateTime?)DateTime.Parse(n["ReadAt"].S) : null;
                string payloadJson = n.ContainsKey("PayloadJson") ? n["PayloadJson"].S : "";

                string lessonType = "";
                DateTime? meetingDate = null;
                TimeSpan? startTime = null;
                TimeSpan? endTime = null;
                string invitationStatus = "";
                string recipientResponseStatus = "";
                string targetMetric = "";

                Dictionary<string, AttributeValue>? lirItem = null;
                if (relatedRecipientId.HasValue && recipientsMap.TryGetValue(relatedRecipientId.Value, out var lir))
                {
                    lirItem = lir;
                    recipientResponseStatus = lirItem.ContainsKey("ResponseStatus") ? lirItem["ResponseStatus"].S : "";
                }

                int? targetInvitationId = relatedInvitationId;
                if (!targetInvitationId.HasValue && lirItem != null && lirItem.ContainsKey("InvitationId") && int.TryParse(lirItem["InvitationId"].N, out int lirInvId))
                {
                    targetInvitationId = lirInvId;
                }

                if (targetInvitationId.HasValue && invitationsMap.TryGetValue(targetInvitationId.Value, out var liItem))
                {
                    lessonType = liItem.ContainsKey("LessonType") ? liItem["LessonType"].S : "";
                    meetingDate = liItem.ContainsKey("MeetingDate") ? (DateTime?)DateTime.Parse(liItem["MeetingDate"].S) : null;
                    startTime = liItem.ContainsKey("StartTime") ? (TimeSpan?)TimeSpan.Parse(liItem["StartTime"].S) : null;
                    endTime = liItem.ContainsKey("EndTime") ? (TimeSpan?)TimeSpan.Parse(liItem["EndTime"].S) : null;
                    invitationStatus = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
                    targetMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S : "";
                }

                notifications.Add(new LessonNotificationRecord
                {
                    NotificationId = notificationId,
                    UserType = normalizedUserType,
                    UserId = userId,
                    NotificationType = nType,
                    Title = title,
                    Body = body,
                    RelatedInvitationId = relatedInvitationId,
                    RelatedRecipientId = relatedRecipientId,
                    IsRead = isRead,
                    CreatedAtUtc = createdAtUtc,
                    ReadAtUtc = readAtUtc,
                    PayloadJson = payloadJson,
                    LessonType = lessonType,
                    MeetingDate = meetingDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    InvitationStatus = invitationStatus,
                    RecipientResponseStatus = recipientResponseStatus,
                    TargetMetric = targetMetric
                });
            }

            return notifications
                .OrderByDescending(n => n.CreatedAtUtc)
                .ThenByDescending(n => n.NotificationId)
                .ToList();
        }

        public List<LessonNotificationRecord> GetLessonNotificationsCreatedSince(DateTime sinceUtc, int? relatedInvitationId = null)
        {
            string sinceStr = sinceUtc.ToString("o");
            var scanReq = new ScanRequest
            {
                TableName = "LessonNotifications"
            };

            if (relatedInvitationId.HasValue)
            {
                scanReq.FilterExpression = "CreatedAt >= :since AND RelatedInvitationId = :invId";
                scanReq.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":since"] = new AttributeValue { S = sinceStr },
                    [":invId"] = new AttributeValue { N = relatedInvitationId.Value.ToString() }
                };
            }
            else
            {
                scanReq.FilterExpression = "CreatedAt >= :since";
                scanReq.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":since"] = new AttributeValue { S = sinceStr }
                };
            }

            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            var notificationItems = scanRes.Items;

            var recipientsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanRecipients = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
            foreach (var item in scanRecipients.Items)
            {
                if (item.ContainsKey("RecipientId") && int.TryParse(item["RecipientId"].N, out int rId))
                {
                    recipientsMap[rId] = item;
                }
            }

            var invitationsMap = new Dictionary<int, Dictionary<string, AttributeValue>>();
            var scanInvitations = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitations" }).GetAwaiter().GetResult();
            foreach (var item in scanInvitations.Items)
            {
                if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int invId))
                {
                    invitationsMap[invId] = item;
                }
            }

            List<LessonNotificationRecord> notifications = new List<LessonNotificationRecord>();

            foreach (var n in notificationItems)
            {
                int notificationId = int.Parse(n["NotificationId"].N);
                string userType = n.ContainsKey("UserType") ? n["UserType"].S : "";
                int userId = n.ContainsKey("UserId") ? int.Parse(n["UserId"].N) : 0;
                string nType = n.ContainsKey("NotificationType") ? n["NotificationType"].S : "";
                string title = n.ContainsKey("Title") ? n["Title"].S : "";
                string body = n.ContainsKey("Body") ? n["Body"].S : "";
                
                int? relatedInvitationIdVal = n.ContainsKey("RelatedInvitationId") && int.TryParse(n["RelatedInvitationId"].N, out int relatedInvId) && relatedInvId > 0 ? (int?)relatedInvId : null;
                int? relatedRecipientId = n.ContainsKey("RelatedRecipientId") && int.TryParse(n["RelatedRecipientId"].N, out int relatedRecId) && relatedRecId > 0 ? (int?)relatedRecId : null;
                
                bool isRead = n.ContainsKey("IsRead") && n["IsRead"].BOOL == true;
                DateTime createdAtUtc = n.ContainsKey("CreatedAt") ? DateTime.Parse(n["CreatedAt"].S) : DateTime.UtcNow;
                DateTime? readAtUtc = n.ContainsKey("ReadAt") && !string.IsNullOrWhiteSpace(n["ReadAt"].S) ? (DateTime?)DateTime.Parse(n["ReadAt"].S) : null;
                string payloadJson = n.ContainsKey("PayloadJson") ? n["PayloadJson"].S : "";

                string lessonType = "";
                DateTime? meetingDate = null;
                TimeSpan? startTime = null;
                TimeSpan? endTime = null;
                string invitationStatus = "";
                string recipientResponseStatus = "";
                string targetMetric = "";

                Dictionary<string, AttributeValue>? lirItem = null;
                if (relatedRecipientId.HasValue && recipientsMap.TryGetValue(relatedRecipientId.Value, out var lir))
                {
                    lirItem = lir;
                    recipientResponseStatus = lirItem.ContainsKey("ResponseStatus") ? lirItem["ResponseStatus"].S : "";
                }

                int? targetInvitationId = relatedInvitationIdVal;
                if (!targetInvitationId.HasValue && lirItem != null && lirItem.ContainsKey("InvitationId") && int.TryParse(lirItem["InvitationId"].N, out int lirInvId))
                {
                    targetInvitationId = lirInvId;
                }

                if (targetInvitationId.HasValue && invitationsMap.TryGetValue(targetInvitationId.Value, out var liItem))
                {
                    lessonType = liItem.ContainsKey("LessonType") ? liItem["LessonType"].S : "";
                    meetingDate = liItem.ContainsKey("MeetingDate") ? (DateTime?)DateTime.Parse(liItem["MeetingDate"].S) : null;
                    startTime = liItem.ContainsKey("StartTime") ? (TimeSpan?)TimeSpan.Parse(liItem["StartTime"].S) : null;
                    endTime = liItem.ContainsKey("EndTime") ? (TimeSpan?)TimeSpan.Parse(liItem["EndTime"].S) : null;
                    invitationStatus = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
                    targetMetric = liItem.ContainsKey("TargetMetric") ? liItem["TargetMetric"].S : "";
                }

                notifications.Add(new LessonNotificationRecord
                {
                    NotificationId = notificationId,
                    UserType = userType,
                    UserId = userId,
                    NotificationType = nType,
                    Title = title,
                    Body = body,
                    RelatedInvitationId = relatedInvitationIdVal,
                    RelatedRecipientId = relatedRecipientId,
                    IsRead = isRead,
                    CreatedAtUtc = createdAtUtc,
                    ReadAtUtc = readAtUtc,
                    PayloadJson = payloadJson,
                    LessonType = lessonType,
                    MeetingDate = meetingDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    InvitationStatus = invitationStatus,
                    RecipientResponseStatus = recipientResponseStatus,
                    TargetMetric = targetMetric
                });
            }

            return notifications
                .OrderBy(n => n.NotificationId)
                .ToList();
        }

        public void PostCreateUserLessonNotification(
            string userType,
            int userId,
            string notificationType,
            string title,
            string body,
            int? relatedInvitationId,
            int? relatedRecipientId,
            string payloadJson)
        {
            InsertLessonNotification(
                userType,
                userId,
                notificationType,
                title,
                body,
                relatedInvitationId,
                relatedRecipientId,
                payloadJson);
        }

        public int PostMarkUserLessonNotificationsAsRead(string userType, int userId)
        {
            string normalizedUserType = NormalizeNotificationUserType(userType);

            var scanReq = new ScanRequest
            {
                TableName = "LessonNotifications",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsRead = :unread",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = normalizedUserType },
                    [":uid"] = new AttributeValue { N = userId.ToString() },
                    [":unread"] = new AttributeValue { BOOL = false }
                }
            };

            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            int updatedCount = 0;

            foreach (var item in scanRes.Items)
            {
                int notificationId = int.Parse(item["NotificationId"].N);
                var key = new Dictionary<string, AttributeValue>
                {
                    ["NotificationId"] = new AttributeValue { N = notificationId.ToString() }
                };

                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["IsRead"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } },
                    ["ReadAt"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                };

                _dynamoDbClient.UpdateItemAsync("LessonNotifications", key, updates).GetAwaiter().GetResult();
                updatedCount++;
            }

            return updatedCount;
        }

        public int PostMarkUserLessonNotificationsAsReadByIds(string userType, int userId, IEnumerable<int> notificationIds)
        {
            string normalizedUserType = NormalizeNotificationUserType(userType);
            var normalizedIds = (notificationIds ?? Enumerable.Empty<int>())
                .Where((id) => id > 0)
                .Distinct()
                .ToHashSet();

            if (normalizedIds.Count == 0)
            {
                return 0;
            }

            var scanReq = new ScanRequest
            {
                TableName = "LessonNotifications",
                FilterExpression = "UserType = :utype AND UserId = :uid AND IsRead = :unread",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":utype"] = new AttributeValue { S = normalizedUserType },
                    [":uid"] = new AttributeValue { N = userId.ToString() },
                    [":unread"] = new AttributeValue { BOOL = false }
                }
            };

            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            int updatedCount = 0;

            foreach (var item in scanRes.Items)
            {
                int notificationId = int.Parse(item["NotificationId"].N);
                if (normalizedIds.Contains(notificationId))
                {
                    var key = new Dictionary<string, AttributeValue>
                    {
                        ["NotificationId"] = new AttributeValue { N = notificationId.ToString() }
                    };

                    var updates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["IsRead"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } },
                        ["ReadAt"] = new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                    };

                    _dynamoDbClient.UpdateItemAsync("LessonNotifications", key, updates).GetAwaiter().GetResult();
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        public bool PostCancelLessonInvitation(int instructorId, int invitationId, string cancelReason)
        {
            var getReq = new GetItemRequest
            {
                TableName = "LessonInvitations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item == null || getRes.Item.Count == 0)
            {
                return false;
            }

            var item = getRes.Item;
            int itemInstructorId = int.Parse(item["InstructorId"].N);
            if (itemInstructorId != instructorId)
            {
                return false;
            }

            string status = item.ContainsKey("Status") ? item["Status"].S : "";
            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string lessonType = item.ContainsKey("LessonType") ? item["LessonType"].S : "";
            DateTime meetingDate = DateTime.Parse(item["MeetingDate"].S);
            TimeSpan startTime = TimeSpan.Parse(item["StartTime"].S);
            TimeSpan endTime = TimeSpan.Parse(item["EndTime"].S);

            var updateReq = new UpdateItemRequest
            {
                TableName = "LessonInvitations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["Status"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = "Cancelled" }
                    },
                    ["UpdatedAt"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();

            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "InvitationId = :invitationId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":invitationId"] = new AttributeValue { N = invitationId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            var childrenMap = new Dictionary<int, string>();
            foreach (var childItem in childrenScan.Items)
            {
                if (childItem.ContainsKey("Id") && int.TryParse(childItem["Id"].N, out int idVal))
                {
                    string fName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                    string lName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                    childrenMap[idVal] = $"{fName} {lName}".Trim();
                }
            }

            string reasonText = string.IsNullOrWhiteSpace(cancelReason)
                ? ""
                : $" סיבה: {cancelReason.Trim()}";

            string lessonTypeLabel = string.Equals(lessonType, "Group", StringComparison.OrdinalIgnoreCase)
                ? "שיעור קבוצתי"
                : "שיעור פרטי";

            foreach (var lir in lirScan.Items)
            {
                int parentId = int.Parse(lir["ParentId"].N);
                int childId = int.Parse(lir["ChildId"].N);
                int recipientId = int.Parse(lir["RecipientId"].N);

                childrenMap.TryGetValue(childId, out string childFullName);
                string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(childFullName)
                    ? "הילד/ה"
                    : childFullName.Trim();

                InsertLessonNotification(
                    "Parent",
                    parentId,
                    "LessonCancelled",
                    "שיעור בוטל",
                    $"{lessonTypeLabel} עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm} בוטל.{reasonText}",
                    invitationId,
                    recipientId,
                    string.Empty);
            }

            InsertLessonNotification(
                "Instructor",
                instructorId,
                "LessonCancelled",
                "ביטול שיעור הושלם",
                $"ההזמנה מספר {invitationId} בוטלה ונשלחו עדכונים לכל ההורים.",
                invitationId,
                null,
                string.Empty);

            return true;
        }

        public LessonInvitationActionResult PostRespondToLessonInvitation(int parentId, int recipientId, string responseAction)
        {
            string normalizedResponse = string.Equals((responseAction ?? string.Empty).Trim(), "approve", StringComparison.OrdinalIgnoreCase)
                || string.Equals((responseAction ?? string.Empty).Trim(), "approved", StringComparison.OrdinalIgnoreCase)
                ? "Approved"
                : "Rejected";

            var getReq = new GetItemRequest
            {
                TableName = "LessonInvitationRecipients",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["RecipientId"] = new AttributeValue { N = recipientId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item == null || getRes.Item.Count == 0)
            {
                throw new InvalidOperationException("Invitation recipient was not found for this parent.");
            }

            var lirItem = getRes.Item;
            int lirParentId = int.Parse(lirItem["ParentId"].N);
            bool lirIsActive = lirItem.ContainsKey("IsActive") && lirItem["IsActive"].BOOL == true;
            if (lirParentId != parentId || !lirIsActive)
            {
                throw new InvalidOperationException("Invitation recipient was not found for this parent.");
            }

            int invitationId = int.Parse(lirItem["InvitationId"].N);
            int childId = int.Parse(lirItem["ChildId"].N);
            string currentResponseStatus = lirItem.ContainsKey("ResponseStatus") ? lirItem["ResponseStatus"].S : "";

            if (!string.Equals(currentResponseStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This invitation was already answered.");
            }

            var liGetReq = new GetItemRequest
            {
                TableName = "LessonInvitations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                }
            };
            var liGetRes = _dynamoDbClient.GetItemAsync(liGetReq).GetAwaiter().GetResult();
            if (liGetRes.Item == null || liGetRes.Item.Count == 0)
            {
                throw new InvalidOperationException("Invitation was not found.");
            }

            var liItem = liGetRes.Item;
            string invitationStatus = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
            if (string.Equals(invitationStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This invitation was already cancelled.");
            }

            int instructorId = int.Parse(liItem["InstructorId"].N);
            string lessonType = liItem.ContainsKey("LessonType") ? liItem["LessonType"].S : "";
            DateTime meetingDate = DateTime.Parse(liItem["MeetingDate"].S);
            TimeSpan meetingStartTime = TimeSpan.Parse(liItem["StartTime"].S);
            TimeSpan meetingEndTime = TimeSpan.Parse(liItem["EndTime"].S);

            if (string.Equals(normalizedResponse, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                DateTime meetingStartDateTime = meetingDate.Date.Add(meetingStartTime);
                DateTime latestApprovalDateTime = meetingStartDateTime.AddHours(-1);
                if (DateTime.Now > latestApprovalDateTime)
                {
                    throw new InvalidOperationException("לא ניתן לאשר את ההזמנה פחות משעה לפני תחילת השיעור.");
                }
            }

            var lirUpdateReq = new UpdateItemRequest
            {
                TableName = "LessonInvitationRecipients",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["RecipientId"] = new AttributeValue { N = recipientId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["ResponseStatus"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = normalizedResponse }
                    },
                    ["RespondedAt"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(lirUpdateReq).GetAwaiter().GetResult();

            string childFullName = $"ילד #{childId}";
            var childRes = _dynamoDbClient.GetItemAsync(new GetItemRequest
            {
                TableName = "Children",
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new AttributeValue { N = childId.ToString() } }
            }).GetAwaiter().GetResult();
            if (childRes.Item != null && childRes.Item.Count > 0)
            {
                string fName = childRes.Item.ContainsKey("FirstName") ? childRes.Item["FirstName"].S : "";
                string lName = childRes.Item.ContainsKey("LastName") ? childRes.Item["LastName"].S : "";
                childFullName = $"{fName} {lName}".Trim();
            }

            string parentFullName = "ההורה";
            var parentRes = GetParentByNumericId(parentId);
            if (parentRes != null)
            {
                string fName = parentRes.ContainsKey("FirstName") ? parentRes["FirstName"].S : "";
                string lName = parentRes.ContainsKey("LastName") ? parentRes["LastName"].S : "";
                parentFullName = $"{fName} {lName}".Trim();
            }

            string responseLabel = string.Equals(normalizedResponse, "Approved", StringComparison.OrdinalIgnoreCase)
                ? "אישור"
                : "דחייה";

            string normalizedChildNameForNotification = childFullName;
            string normalizedParentNameForNotification = parentFullName;

            InsertLessonNotification(
                "Instructor",
                instructorId,
                "LessonResponse",
                "התקבלה תגובת הורה להזמנה",
                $"התקבלה תגובת הורה ({responseLabel}) להזמנה עבור {normalizedChildNameForNotification}. ההורה: {normalizedParentNameForNotification}. מועד השיעור: {meetingDate:yyyy-MM-dd} {meetingStartTime:hh\\:mm}-{meetingEndTime:hh\\:mm}.",
                invitationId,
                recipientId,
                string.Empty);

            string finalizedStatus = invitationStatus;

            if (string.Equals(lessonType, "Private", StringComparison.OrdinalIgnoreCase))
            {
                finalizedStatus = string.Equals(normalizedResponse, "Approved", StringComparison.OrdinalIgnoreCase)
                    ? "Confirmed"
                    : "Cancelled";

                var liUpdateStatusReq = new UpdateItemRequest
                {
                    TableName = "LessonInvitations",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                    },
                    AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                    {
                        ["Status"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { S = finalizedStatus }
                        },
                        ["UpdatedAt"] = new AttributeValueUpdate
                        {
                            Action = AttributeAction.PUT,
                            Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                        }
                    }
                };
                _dynamoDbClient.UpdateItemAsync(liUpdateStatusReq).GetAwaiter().GetResult();

                string parentTitle = string.Equals(finalizedStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? "התגובה להזמנה נקלטה: שיעור פרטי אושר"
                    : "התגובה להזמנה נקלטה: שיעור פרטי בוטל";

                string parentBody = string.Equals(finalizedStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? $"הזמנת השיעור הפרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {meetingStartTime:hh\\:mm}-{meetingEndTime:hh\\:mm} אושרה, והמדריך עודכן בהתאם."
                    : $"הזמנת השיעור הפרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {meetingStartTime:hh\\:mm}-{meetingEndTime:hh\\:mm} נדחתה, והמדריך עודכן בהתאם.";

                InsertLessonNotification(
                    "Parent",
                    parentId,
                    "LessonUpdate",
                    parentTitle,
                    parentBody,
                    invitationId,
                    recipientId,
                    string.Empty);
            }
            else
            {
                finalizedStatus = EvaluateAndFinalizeGroupInvitation(invitationId, instructorId);
            }

            return new LessonInvitationActionResult
            {
                InvitationId = invitationId,
                RecipientId = recipientId,
                ResponseStatus = normalizedResponse,
                InvitationStatus = finalizedStatus
            };
        }

        private string EvaluateAndFinalizeGroupInvitation(int invitationId, int instructorId)
        {
            var getReq = new GetItemRequest
            {
                TableName = "LessonInvitations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                }
            };
            var getRes = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (getRes.Item == null || getRes.Item.Count == 0)
            {
                throw new InvalidOperationException("Invitation was not found.");
            }

            var liItem = getRes.Item;
            string currentStatus = liItem.ContainsKey("Status") ? liItem["Status"].S : "";
            int minRegistrations = liItem.ContainsKey("MinRegistrations") ? int.Parse(liItem["MinRegistrations"].N) : 5;

            if (!string.Equals(currentStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                return currentStatus;
            }

            var lirScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "LessonInvitationRecipients",
                FilterExpression = "InvitationId = :invitationId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":invitationId"] = new AttributeValue { N = invitationId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            int approvedCount = 0;
            int pendingCount = 0;
            foreach (var lir in lirScan.Items)
            {
                string rStatus = lir.ContainsKey("ResponseStatus") ? lir["ResponseStatus"].S : "";
                if (string.Equals(rStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    approvedCount++;
                }
                else if (string.Equals(rStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    pendingCount++;
                }
            }

            if (pendingCount > 0)
            {
                return "Pending";
            }

            string nextStatus = approvedCount >= minRegistrations ? "Confirmed" : "Cancelled";

            var liUpdateReq = new UpdateItemRequest
            {
                TableName = "LessonInvitations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["InvitationId"] = new AttributeValue { N = invitationId.ToString() }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["Status"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = nextStatus }
                    },
                    ["UpdatedAt"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                }
            };
            _dynamoDbClient.UpdateItemAsync(liUpdateReq).GetAwaiter().GetResult();

            var childrenScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "Children" }).GetAwaiter().GetResult();
            var childrenMap = new Dictionary<int, string>();
            foreach (var childItem in childrenScan.Items)
            {
                if (childItem.ContainsKey("Id") && int.TryParse(childItem["Id"].N, out int idVal))
                {
                    string fName = childItem.ContainsKey("FirstName") ? childItem["FirstName"].S : "";
                    string lName = childItem.ContainsKey("LastName") ? childItem["LastName"].S : "";
                    childrenMap[idVal] = $"{fName} {lName}".Trim();
                }
            }

            foreach (var lir in lirScan.Items)
            {
                int parentId = int.Parse(lir["ParentId"].N);
                int childId = int.Parse(lir["ChildId"].N);
                int recipientId = int.Parse(lir["RecipientId"].N);

                childrenMap.TryGetValue(childId, out string childFullName);
                string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(childFullName)
                    ? "הילד/ה"
                    : childFullName.Trim();

                string title = string.Equals(nextStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? "השיעור הקבוצתי אושר"
                    : "השיעור הקבוצתי בוטל";

                string body = string.Equals(nextStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? $"השיעור הקבוצתי אושר ומתקיים כמתוכנן. הילד/ה: {normalizedChildNameForNotification}."
                    : $"השיעור הקבוצתי בוטל עקב פחות מ-{minRegistrations} אישורים. הילד/ה: {normalizedChildNameForNotification}.";

                InsertLessonNotification(
                    "Parent",
                    parentId,
                    "LessonUpdate",
                    title,
                    body,
                    invitationId,
                    recipientId,
                    string.Empty);
            }

            string instructorBody = string.Equals(nextStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                ? $"הזמנה מספר {invitationId} אושרה עם {approvedCount} נרשמים."
                : $"הזמנה מספר {invitationId} בוטלה אוטומטית: רק {approvedCount} אישורים (נדרש לפחות {minRegistrations}).";

            InsertLessonNotification(
                "Instructor",
                instructorId,
                "LessonUpdate",
                "עדכון הזמנה קבוצתית",
                instructorBody,
                invitationId,
                null,
                string.Empty);

            return nextStatus;
        }

        private void InsertLessonNotification(
            string userType,
            int userId,
            string notificationType,
            string title,
            string body,
            int? relatedInvitationId,
            int? relatedRecipientId,
            string payloadJson)
        {
            var allScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonNotifications" }).GetAwaiter().GetResult();
            int maxId = 0;
            foreach (var item in allScan.Items)
            {
                if (item.ContainsKey("NotificationId") && int.TryParse(item["NotificationId"].N, out int idVal))
                {
                    if (idVal > maxId) maxId = idVal;
                }
            }
            int newNotificationId = maxId + 1;

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["NotificationId"] = new AttributeValue { N = newNotificationId.ToString() },
                ["UserType"] = new AttributeValue { S = NormalizeNotificationUserType(userType) },
                ["UserId"] = new AttributeValue { N = userId.ToString() },
                ["NotificationType"] = new AttributeValue { S = string.IsNullOrWhiteSpace(notificationType) ? "General" : notificationType.Trim() },
                ["Title"] = new AttributeValue { S = string.IsNullOrWhiteSpace(title) ? "עדכון מערכת" : title.Trim() },
                ["Body"] = new AttributeValue { S = string.IsNullOrWhiteSpace(body) ? "" : body.Trim() },
                ["RelatedInvitationId"] = new AttributeValue { N = relatedInvitationId.HasValue ? relatedInvitationId.Value.ToString() : "0" },
                ["RelatedRecipientId"] = new AttributeValue { N = relatedRecipientId.HasValue ? relatedRecipientId.Value.ToString() : "0" },
                ["IsRead"] = new AttributeValue { BOOL = false },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["ReadAt"] = new AttributeValue { S = "" },
                ["PayloadJson"] = new AttributeValue { S = string.IsNullOrWhiteSpace(payloadJson) ? "" : payloadJson.Trim() }
            };

            _dynamoDbClient.PutItemAsync("LessonNotifications", newItem).GetAwaiter().GetResult();
        }

        private static string NormalizeLessonType(string lessonType)
        {
            string normalized = (lessonType ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "private")
            {
                return "Private";
            }

            if (normalized == "group")
            {
                return "Group";
            }

            throw new InvalidOperationException("LessonType must be either Private or Group.");
        }

        private static string NormalizeNotificationUserType(string userType)
        {
            string normalized = (userType ?? string.Empty).Trim();
            if (string.Equals(normalized, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                return "Parent";
            }

            if (string.Equals(normalized, "Instructor", StringComparison.OrdinalIgnoreCase))
            {
                return "Instructor";
            }

            throw new InvalidOperationException("UserType must be Parent or Instructor.");
        }

        private static ChildRecord MapChild(SqlDataReader reader)
        {
            string parentFirstName = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
            string parentLastName = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);

            return new ChildRecord
            {
                Id = reader.GetInt32(0),
                ParentId = reader.GetInt32(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                BirthDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                IsActive = reader.GetBoolean(5),
                CreatedAt = reader.GetDateTime(6),
                ParentEmail = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                ChildDescription = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                Strengths = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                Weaknesses = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                PersonalGoals = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
            };
        }

        private static void EnsureChildrenDescriptionColumnExists(SqlConnection con)
        {
            const string sql = @"
                IF COL_LENGTH('dbo.Children', 'Description') IS NULL
                BEGIN
                    ALTER TABLE dbo.Children
                    ADD [Description] NVARCHAR(MAX) NULL;
                END;

                IF COL_LENGTH('dbo.Children', 'ChildDescription') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE dbo.Children
                          SET [Description] = ChildDescription
                          WHERE [Description] IS NULL
                            AND ChildDescription IS NOT NULL;');

                    DECLARE @defaultConstraintName sysname;
                    SELECT @defaultConstraintName = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID('dbo.Children')
                      AND c.name = 'ChildDescription';

                    IF @defaultConstraintName IS NOT NULL
                    BEGIN
                        EXEC(N'ALTER TABLE dbo.Children DROP CONSTRAINT [' + @defaultConstraintName + ']');
                    END;

                    ALTER TABLE dbo.Children
                    DROP COLUMN ChildDescription;
                END;

                IF COL_LENGTH('dbo.Children', 'Strengths') IS NULL
                BEGIN
                    ALTER TABLE dbo.Children
                    ADD Strengths NVARCHAR(MAX) NULL;
                END;

                IF COL_LENGTH('dbo.Children', 'Weaknesses') IS NULL
                BEGIN
                    ALTER TABLE dbo.Children
                    ADD Weaknesses NVARCHAR(MAX) NULL;
                END;

                IF COL_LENGTH('dbo.Children', 'PersonalGoals') IS NULL
                BEGIN
                    ALTER TABLE dbo.Children
                    ADD PersonalGoals NVARCHAR(MAX) NULL;
                END;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.ExecuteNonQuery();
        }

        private static void EnsureLessonSchedulingTablesExists(SqlConnection con, SqlTransaction? tx = null)
        {
            const string sql = @"
                IF OBJECT_ID('dbo.LessonInvitations', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LessonInvitations
                    (
                        InvitationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LessonInvitations PRIMARY KEY,
                        InstructorId INT NOT NULL,
                        LessonType NVARCHAR(20) NOT NULL,
                        GroupId INT NULL,
                        MeetingDate DATE NOT NULL,
                        StartTime TIME NOT NULL,
                        EndTime TIME NOT NULL,
                        Capacity INT NOT NULL CONSTRAINT DF_LessonInvitations_Capacity DEFAULT (20),
                        MinRegistrations INT NOT NULL CONSTRAINT DF_LessonInvitations_MinRegistrations DEFAULT (5),
                        GeneralNote NVARCHAR(1000) NULL,
                        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_LessonInvitations_Status DEFAULT (N'Pending'),
                        TargetMetric NVARCHAR(100) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LessonInvitations_CreatedAt DEFAULT (SYSUTCDATETIME()),
                        UpdatedAt DATETIME2 NULL,
                        CONSTRAINT FK_LessonInvitations_Instructor FOREIGN KEY (InstructorId) REFERENCES dbo.Instructors(Id)
                    );

                    CREATE INDEX IX_LessonInvitations_Instructor_MeetingDate
                        ON dbo.LessonInvitations (InstructorId, MeetingDate DESC, StartTime DESC);
                END;

                IF OBJECT_ID('dbo.LessonInvitations', 'U') IS NOT NULL AND COL_LENGTH('dbo.LessonInvitations', 'TargetMetric') IS NULL
                BEGIN
                    ALTER TABLE dbo.LessonInvitations ADD TargetMetric NVARCHAR(100) NULL;
                END;

                IF OBJECT_ID('dbo.TrainingSessions', 'U') IS NOT NULL AND COL_LENGTH('dbo.TrainingSessions', 'TargetMetric') IS NULL
                BEGIN
                    ALTER TABLE dbo.TrainingSessions ADD TargetMetric NVARCHAR(100) NULL;
                END;

                IF OBJECT_ID('dbo.LessonInvitationRecipients', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LessonInvitationRecipients
                    (
                        RecipientId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LessonInvitationRecipients PRIMARY KEY,
                        InvitationId INT NOT NULL,
                        ParentId INT NOT NULL,
                        ChildId INT NOT NULL,
                        ResponseStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_LessonInvitationRecipients_ResponseStatus DEFAULT (N'Pending'),
                        RespondedAt DATETIME2 NULL,
                        IsActive BIT NOT NULL CONSTRAINT DF_LessonInvitationRecipients_IsActive DEFAULT (1),
                        CONSTRAINT FK_LessonInvitationRecipients_Invitation FOREIGN KEY (InvitationId) REFERENCES dbo.LessonInvitations(InvitationId),
                        CONSTRAINT FK_LessonInvitationRecipients_Parent FOREIGN KEY (ParentId) REFERENCES dbo.Parents(Id),
                        CONSTRAINT FK_LessonInvitationRecipients_Child FOREIGN KEY (ChildId) REFERENCES dbo.Children(Id)
                    );

                    CREATE INDEX IX_LessonInvitationRecipients_Invitation
                        ON dbo.LessonInvitationRecipients (InvitationId, ResponseStatus, IsActive);

                    CREATE INDEX IX_LessonInvitationRecipients_Parent
                        ON dbo.LessonInvitationRecipients (ParentId, IsActive);
                END;

                IF OBJECT_ID('dbo.LessonNotifications', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LessonNotifications
                    (
                        NotificationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LessonNotifications PRIMARY KEY,
                        UserType NVARCHAR(20) NOT NULL,
                        UserId INT NOT NULL,
                        NotificationType NVARCHAR(40) NOT NULL,
                        Title NVARCHAR(200) NOT NULL,
                        Body NVARCHAR(MAX) NOT NULL,
                        RelatedInvitationId INT NULL,
                        RelatedRecipientId INT NULL,
                        IsRead BIT NOT NULL CONSTRAINT DF_LessonNotifications_IsRead DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LessonNotifications_CreatedAt DEFAULT (SYSUTCDATETIME()),
                        ReadAt DATETIME2 NULL,
                        PayloadJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_LessonNotifications_PayloadJson DEFAULT (N'')
                    );

                    CREATE INDEX IX_LessonNotifications_User
                        ON dbo.LessonNotifications (UserType, UserId, CreatedAt DESC, NotificationId DESC);
                END;";

            using SqlCommand cmd = tx == null
                ? new SqlCommand(sql, con)
                : new SqlCommand(sql, con, tx);
            cmd.ExecuteNonQuery();
        }

        private static void EnsurePushDeviceTokensTableExists(SqlConnection con, SqlTransaction? tx = null)
        {
            const string sql = @"
                IF OBJECT_ID('dbo.PushDeviceTokens', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.PushDeviceTokens
                    (
                        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PushDeviceTokens PRIMARY KEY,
                        UserType NVARCHAR(20) NOT NULL,
                        UserId INT NOT NULL,
                        ExpoPushToken NVARCHAR(255) NOT NULL,
                        Platform NVARCHAR(30) NULL,
                        DeviceId NVARCHAR(200) NULL,
                        IsActive BIT NOT NULL CONSTRAINT DF_PushDeviceTokens_IsActive DEFAULT (1),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PushDeviceTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_PushDeviceTokens_UpdatedAt DEFAULT (SYSUTCDATETIME()),
                        LastRegisteredAt DATETIME2 NOT NULL CONSTRAINT DF_PushDeviceTokens_LastRegisteredAt DEFAULT (SYSUTCDATETIME())
                    );

                    CREATE UNIQUE INDEX UX_PushDeviceTokens_ExpoPushToken
                        ON dbo.PushDeviceTokens (ExpoPushToken);

                    CREATE INDEX IX_PushDeviceTokens_User
                        ON dbo.PushDeviceTokens (UserType, UserId, IsActive, UpdatedAt DESC, Id DESC);
                END;";

            using SqlCommand cmd = tx == null
                ? new SqlCommand(sql, con)
                : new SqlCommand(sql, con, tx);
            cmd.ExecuteNonQuery();
        }

        private static bool GetUserTableExists(SqlConnection con, SqlTransaction? tx, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return false;
            }

            const string sql = @"
                SELECT CASE
                    WHEN OBJECT_ID(@FullTableName, 'U') IS NULL THEN 0
                    ELSE 1
                END;";

            string fullTableName = $"dbo.{tableName.Trim()}";
            using SqlCommand cmd = tx == null
                ? new SqlCommand(sql, con)
                : new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@FullTableName", fullTableName);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        private static bool GetGroupTrainingSessionsTableExists(SqlConnection con, SqlTransaction? tx = null)
        {
            return GetUserTableExists(con, tx, "GroupTrainingSessions");
        }

        private bool GetGroupExistsAndActive(int groupId)
        {
            var getReq = new GetItemRequest
            {
                TableName = "Groups",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = groupId.ToString() }
                }
            };
            var res = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (res.Item == null || res.Item.Count == 0) return false;
            return res.Item.ContainsKey("IsActive") && res.Item["IsActive"].BOOL == true;
        }

        private bool GetChildExistsAndActive(int childId)
        {
            var getReq = new GetItemRequest
            {
                TableName = "Children",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = childId.ToString() }
                }
            };
            var res = _dynamoDbClient.GetItemAsync(getReq).GetAwaiter().GetResult();
            if (res.Item == null || res.Item.Count == 0) return false;
            return res.Item.ContainsKey("IsActive") && res.Item["IsActive"].BOOL == true;
        }

        private void EnsureInstructorCanTeach(int instructorId)
        {
            var instructor = GetInstructorByNumericId(instructorId);
            if (instructor == null)
            {
                throw new InvalidOperationException("Instructor does not exist, is inactive, or is not in Instructor role.");
            }
            bool isActive = instructor.ContainsKey("IsActive") && instructor["IsActive"].BOOL == true;
            string role = instructor.ContainsKey("Role") ? instructor["Role"].S : "";
            if (!isActive || !string.Equals(role, "Instructor", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Instructor does not exist, is inactive, or is not in Instructor role.");
            }
        }

        private void EnsureGroupHasSingleActiveInstructor(int groupId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "GroupId = :groupId AND IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":groupId"] = new AttributeValue { N = groupId.ToString() },
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            int count = 0;
            foreach (var item in scanRes.Items)
            {
                int instructorId = int.Parse(item["InstructorId"].N);
                var instructor = GetInstructorByNumericId(instructorId);
                if (instructor != null)
                {
                    bool isInstActive = instructor.ContainsKey("IsActive") && instructor["IsActive"].BOOL == true;
                    string role = instructor.ContainsKey("Role") ? instructor["Role"].S : "";
                    if (isInstActive && string.Equals(role, "Instructor", StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
            if (count != 1)
            {
                throw new InvalidOperationException("Group must have exactly one active instructor before assigning children.");
            }
        }

        private void UpsertActiveInstructorGroup(int groupId, int instructorId)
        {
            var scanReq = new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "GroupId = :groupId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":groupId"] = new AttributeValue { N = groupId.ToString() }
                }
            };
            var scanRes = _dynamoDbClient.ScanAsync(scanReq).GetAwaiter().GetResult();
            
            bool foundTarget = false;
            foreach (var item in scanRes.Items)
            {
                int itemInstructorId = int.Parse(item["InstructorId"].N);
                bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                
                if (itemInstructorId == instructorId)
                {
                    foundTarget = true;
                    if (!isActive)
                    {
                        var updateReq = new UpdateItemRequest
                        {
                            TableName = "InstructorGroups",
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["Id"] = item["Id"]
                            },
                            AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                            {
                                ["IsActive"] = new AttributeValueUpdate
                                {
                                    Action = AttributeAction.PUT,
                                    Value = new AttributeValue { BOOL = true }
                                }
                            }
                        };
                        _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();
                    }
                }
                else
                {
                    if (isActive)
                    {
                        var updateReq = new UpdateItemRequest
                        {
                            TableName = "InstructorGroups",
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["Id"] = item["Id"]
                            },
                            AttributeUpdates = new Dictionary<string, AttributeValueUpdate>
                            {
                                ["IsActive"] = new AttributeValueUpdate
                                {
                                    Action = AttributeAction.PUT,
                                    Value = new AttributeValue { BOOL = false }
                                }
                            }
                        };
                        _dynamoDbClient.UpdateItemAsync(updateReq).GetAwaiter().GetResult();
                    }
                }
            }
            
            if (!foundTarget)
            {
                var allScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "InstructorGroups" }).GetAwaiter().GetResult();
                int maxId = 0;
                foreach (var item in allScan.Items)
                {
                    if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal))
                    {
                        if (idVal > maxId) maxId = idVal;
                    }
                }
                int newId = maxId + 1;
                
                var newItem = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { N = newId.ToString() },
                    ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                    ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                    ["IsActive"] = new AttributeValue { BOOL = true },
                    ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                };
                _dynamoDbClient.PutItemAsync("InstructorGroups", newItem).GetAwaiter().GetResult();
            }
        }

        private static string NormalizeRecommendationText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string lowered = value.Trim().ToLowerInvariant();
            StringBuilder builder = new StringBuilder(lowered.Length);

            foreach (char c in lowered)
            {
                builder.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ');
            }

            return builder.ToString();
        }

        private static HashSet<string> TokenizeRecommendationText(string normalizedText)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string token in normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length >= 2)
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        private static HashSet<string> ExtractRecommendationConcepts(HashSet<string> tokens)
        {
            HashSet<string> concepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string concept, string[] keywords) in GroupRecommendationConceptKeywords)
            {
                bool found = keywords.Any((keyword) => tokens.Any((token) => IsTokenMatchingKeyword(token, keyword)));
                if (found)
                {
                    concepts.Add(concept);
                }
            }

            return concepts;
        }

        private static bool IsTokenMatchingKeyword(string token, string keyword)
        {
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (token.Length >= 4 && token.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (keyword.Length >= 4 && keyword.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string BuildRecommendationReasoning(GroupRecommendationEvaluation evaluation, bool usedFallback)
        {
            if (usedFallback)
            {
                return "לא נמצאה התאמה חזקה לפי הטקסט, ולכן נבחרה הקבוצה עם העומס הנמוך ביותר.";
            }

            List<string> parts = new List<string>();

            if (evaluation.SharedConceptLabels.Count > 0)
            {
                parts.Add($"תחומי התאמה: {string.Join(", ", evaluation.SharedConceptLabels)}");
            }

            if (evaluation.SharedTokens.Count > 0)
            {
                parts.Add($"מונחים משותפים: {string.Join(", ", evaluation.SharedTokens)}");
            }

            parts.Add($"ילדים פעילים בקבוצה: {evaluation.Group.ActiveChildrenCount}");

            return string.Join(" | ", parts);
        }

        private sealed class GroupRecommendationEvaluation
        {
            public GroupSummaryRecord Group { get; set; } = new GroupSummaryRecord();
            public double Score { get; set; }
            public List<string> SharedTokens { get; set; } = new List<string>();
            public List<string> SharedConceptLabels { get; set; } = new List<string>();
        }

        private static ConversationRecord MapConversation(SqlDataReader reader)
        {
            string parentFirstName = GetNullableString(reader, 7);
            string parentLastName = GetNullableString(reader, 8);
            string childFirstName = GetNullableString(reader, 9);
            string childLastName = GetNullableString(reader, 10);
            string instructorFirstName = GetNullableString(reader, 11);
            string instructorLastName = GetNullableString(reader, 12);

            return new ConversationRecord
            {
                Id = reader.GetInt32(0),
                ParentId = reader.GetInt32(1),
                ChildId = reader.GetInt32(2),
                InstructorId = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4),
                CreatedAt = reader.GetDateTime(5),
                LastMessageAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                ChildFullName = $"{childFirstName} {childLastName}".Trim(),
                InstructorFullName = $"{instructorFirstName} {instructorLastName}".Trim()
            };
        }

        private static ChatMessageRecord MapChatMessage(SqlDataReader reader)
        {
            return new ChatMessageRecord
            {
                Id = reader.GetInt32(0),
                ConversationId = reader.GetInt32(1),
                SenderType = reader.GetString(2),
                MessageText = reader.GetString(3),
                SentAt = reader.GetDateTime(4),
                SenderName = reader.FieldCount > 5 ? GetNullableString(reader, 5) : string.Empty
            };
        }

        private static string NormalizeSenderType(string senderType)
        {
            if (string.Equals(senderType, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                return "Parent";
            }

            if (string.Equals(senderType, "Instructor", StringComparison.OrdinalIgnoreCase))
            {
                return "Instructor";
            }

            throw new InvalidOperationException("SenderType must be Parent or Instructor.");
        }

        private static string MapAttendanceCategory(string status)
        {
            string normalizedStatus = (status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
            {
                return "Scheduled";
            }

            if (ContainsAny(normalizedStatus, "late", "איחור"))
            {
                return "Late";
            }

            if (ContainsAny(normalizedStatus, "absent", "no show", "noshow", "חיסור", "נעדר"))
            {
                return "Absent";
            }

            if (ContainsAny(normalizedStatus, "cancel", "בוטל"))
            {
                return "Cancelled";
            }

            if (ContainsAny(normalizedStatus, "complete", "completed", "done", "present", "נוכח", "בוצע"))
            {
                return "Present";
            }

            return "Scheduled";
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(source) || values == null || values.Length == 0)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeAiMetricLabel(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
            {
                return string.Empty;
            }

            string normalized = Regex.Replace(rawLabel, @"\s+", " ").Trim();
            normalized = normalized.Replace("(הכנסת ראש למים)(הכנסת ראש למים)", "(הכנסת ראש למים)");
            normalized = normalized.Replace("(הכנסת ראש למים) (הכנסת ראש למים)", "(הכנסת ראש למים)");

            if (AiMetricAliasMap.TryGetValue(normalized, out string? canonicalLabel))
            {
                return canonicalLabel;
            }

            return normalized;
        }

        private static Dictionary<string, double> OrderAiMetrics(Dictionary<string, double> metrics)
        {
            Dictionary<string, double> ordered = new Dictionary<string, double>();

            foreach (string preferredLabel in PreferredAiMetricOrder)
            {
                if (metrics.TryGetValue(preferredLabel, out double value))
                {
                    ordered[preferredLabel] = value;
                }
            }

            foreach (var metric in metrics)
            {
                if (!ordered.ContainsKey(metric.Key))
                {
                    ordered[metric.Key] = metric.Value;
                }
            }

            return ordered;
        }

        private static void EnsureCenterSettingsTableExists(SqlConnection con)
        {
            const string sql = @"
                IF OBJECT_ID('dbo.CenterSettings', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.CenterSettings
                    (
                        Id INT NOT NULL CONSTRAINT PK_CenterSettings PRIMARY KEY,
                        CenterName NVARCHAR(200) NOT NULL CONSTRAINT DF_CenterSettings_CenterName DEFAULT (N''),
                        CenterAddress NVARCHAR(500) NOT NULL CONSTRAINT DF_CenterSettings_CenterAddress DEFAULT (N''),
                        SendAutoReports BIT NOT NULL CONSTRAINT DF_CenterSettings_SendAutoReports DEFAULT ((1)),
                        ReceiveAlerts BIT NOT NULL CONSTRAINT DF_CenterSettings_ReceiveAlerts DEFAULT ((1)),
                        ShowKidsAdvanced BIT NOT NULL CONSTRAINT DF_CenterSettings_ShowKidsAdvanced DEFAULT ((0)),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_CenterSettings_UpdatedAt DEFAULT (SYSUTCDATETIME())
                    );
                END;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.ExecuteNonQuery();
        }

        private static string GetNullableString(SqlDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }

        public List<GroupAiStatisticRecord> GetGroupAiStatistics(DateTime startDate, DateTime endDate)
        {
            List<GroupAiStatisticRecord> result = new List<GroupAiStatisticRecord>();

            // 1. Scan Groups where IsActive = true
            var groupsScanReq = new ScanRequest
            {
                TableName = "Groups",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            };
            var groupsScanRes = _dynamoDbClient.ScanAsync(groupsScanReq).GetAwaiter().GetResult();

            Dictionary<int, GroupAiStatisticRecord> dict = new Dictionary<int, GroupAiStatisticRecord>();
            foreach (var item in groupsScanRes.Items)
            {
                if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int groupId))
                {
                    string groupName = item.ContainsKey("Name") ? item["Name"].S : "";
                    var stat = new GroupAiStatisticRecord
                    {
                        GroupId = groupId,
                        GroupName = groupName,
                        MedianMetrics = new Dictionary<string, double>()
                    };
                    dict[groupId] = stat;
                    result.Add(stat);
                }
            }

            // 2. Scan ReportChildren table to find reports within date range, having GroupId, and IsPresent = true
            var reportsScanReq = new ScanRequest
            {
                TableName = "ReportChildren",
                FilterExpression = "ReportDate >= :start AND ReportDate <= :end AND IsPresent = :present AND attribute_exists(GroupId)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":start"] = new AttributeValue { S = startDate.ToString("yyyy-MM-dd") },
                    [":end"] = new AttributeValue { S = endDate.ToString("yyyy-MM-dd") },
                    [":present"] = new AttributeValue { BOOL = true }
                }
            };
            var reportsScanRes = _dynamoDbClient.ScanAsync(reportsScanReq).GetAwaiter().GetResult();

            Dictionary<int, Dictionary<string, List<double>>> rawMetrics = new Dictionary<int, Dictionary<string, List<double>>>();

            foreach (var item in reportsScanRes.Items)
            {
                if (!item.ContainsKey("GroupId") || item["GroupId"].NULL == true)
                {
                    continue;
                }

                if (!int.TryParse(item["GroupId"].N, out int groupId))
                {
                    continue;
                }

                // Only consider reports for groups that are active (present in dict)
                if (!dict.ContainsKey(groupId))
                {
                    continue;
                }

                string metricsJson = item.ContainsKey("Metrics") ? item["Metrics"].S : "";
                if (string.IsNullOrWhiteSpace(metricsJson)) continue;

                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(metricsJson))
                    {
                        var metricsEl = doc.RootElement;

                        if (!rawMetrics.ContainsKey(groupId))
                            rawMetrics[groupId] = new Dictionary<string, List<double>>();

                        if (metricsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var metricObj in metricsEl.EnumerateArray())
                            {
                                if (metricObj.TryGetProperty("label", out var labelEl) && metricObj.TryGetProperty("value", out var valueEl))
                                {
                                    string label = NormalizeAiMetricLabel(labelEl.GetString() ?? string.Empty);
                                    double val;
                                    if (!string.IsNullOrWhiteSpace(label) && valueEl.TryGetDouble(out val))
                                    {
                                        if (!rawMetrics[groupId].ContainsKey(label))
                                            rawMetrics[groupId][label] = new List<double>();
                                        rawMetrics[groupId][label].Add(val);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var kvp in rawMetrics)
            {
                if (dict.TryGetValue(kvp.Key, out var stat))
                {
                    foreach (var m in kvp.Value)
                    {
                        if (m.Value.Count > 0)
                        {
                            m.Value.Sort();
                            int count = m.Value.Count;
                            double median;
                            if (count % 2 == 0)
                            {
                                median = (m.Value[count / 2 - 1] + m.Value[count / 2]) / 2.0;
                            }
                            else
                            {
                                median = m.Value[count / 2];
                            }
                            stat.MedianMetrics[m.Key] = Math.Round(median, 2);
                        }
                    }

                    stat.MedianMetrics = OrderAiMetrics(stat.MedianMetrics);
                }
            }

            return result;
        }

        public void SaveAIChatMessage(int instructorId, string senderType, string messageText)
        {
            int newId = 1;
            var queryReq = new QueryRequest
            {
                TableName = "InstructorAIChatMessages",
                KeyConditionExpression = "InstructorId = :instId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() }
                },
                ScanIndexForward = false,
                Limit = 1
            };
            var queryRes = _dynamoDbClient.QueryAsync(queryReq).GetAwaiter().GetResult();
            if (queryRes.Items.Count > 0)
            {
                if (queryRes.Items[0].ContainsKey("Id") && int.TryParse(queryRes.Items[0]["Id"].N, out int lastId))
                {
                    newId = lastId + 1;
                }
            }

            var newItem = new Dictionary<string, AttributeValue>
            {
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["Id"] = new AttributeValue { N = newId.ToString() },
                ["SenderType"] = new AttributeValue { S = senderType },
                ["MessageText"] = new AttributeValue { S = messageText },
                ["SentAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("InstructorAIChatMessages", newItem).GetAwaiter().GetResult();
        }

        public List<AIChatMessageRecord> GetAIChatMessages(int instructorId)
        {
            List<AIChatMessageRecord> list = new List<AIChatMessageRecord>();
            
            var queryReq = new QueryRequest
            {
                TableName = "InstructorAIChatMessages",
                KeyConditionExpression = "InstructorId = :instId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() }
                },
                ScanIndexForward = true
            };
            
            var queryRes = _dynamoDbClient.QueryAsync(queryReq).GetAwaiter().GetResult();
            foreach (var item in queryRes.Items)
            {
                string senderType = item.ContainsKey("SenderType") ? item["SenderType"].S : "";
                string messageText = item.ContainsKey("MessageText") ? item["MessageText"].S : "";
                
                DateTime sentAt = DateTime.UtcNow;
                if (item.ContainsKey("SentAt"))
                {
                    DateTime.TryParse(item["SentAt"].S, out sentAt);
                }
                
                list.Add(new AIChatMessageRecord
                {
                    SenderType = senderType,
                    MessageText = messageText,
                    SentAt = sentAt
                });
            }
            return list;
        }

        public void ClearAIChatMessages(int instructorId)
        {
            var queryReq = new QueryRequest
            {
                TableName = "InstructorAIChatMessages",
                KeyConditionExpression = "InstructorId = :instId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":instId"] = new AttributeValue { N = instructorId.ToString() }
                },
                ProjectionExpression = "InstructorId, Id"
            };
            
            var queryRes = _dynamoDbClient.QueryAsync(queryReq).GetAwaiter().GetResult();
            foreach (var item in queryRes.Items)
            {
                var deleteReq = new DeleteItemRequest
                {
                    TableName = "InstructorAIChatMessages",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["InstructorId"] = item["InstructorId"],
                        ["Id"] = item["Id"]
                    }
                };
                _dynamoDbClient.DeleteItemAsync(deleteReq).GetAwaiter().GetResult();
            }
        }

        public void CreateTempCompletedLessonInvitation(out int instructorId, out int groupId, out string meetingDateStr, out string targetMetric)
        {
            instructorId = 0;
            groupId = 0;

            var instGroupsScan = _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = "InstructorGroups",
                FilterExpression = "IsActive = :active",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":active"] = new AttributeValue { BOOL = true }
                }
            }).GetAwaiter().GetResult();

            if (instGroupsScan.Items.Count > 0)
            {
                var first = instGroupsScan.Items[0];
                instructorId = int.Parse(first["InstructorId"].N);
                groupId = int.Parse(first["GroupId"].N);
            }

            if (instructorId == 0 || groupId == 0)
            {
                var instScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Instructors",
                    FilterExpression = "IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":active"] = new AttributeValue { BOOL = true }
                    },
                    Limit = 1
                }).GetAwaiter().GetResult();
                if (instScan.Items.Count > 0)
                {
                    instructorId = int.Parse(instScan.Items[0]["Id"].N);
                }

                var grpScan = _dynamoDbClient.ScanAsync(new ScanRequest
                {
                    TableName = "Groups",
                    FilterExpression = "IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":active"] = new AttributeValue { BOOL = true }
                    },
                    Limit = 1
                }).GetAwaiter().GetResult();
                if (grpScan.Items.Count > 0)
                {
                    groupId = int.Parse(grpScan.Items[0]["Id"].N);
                }
            }

            if (instructorId == 0) instructorId = 1;
            if (groupId == 0) groupId = 1;

            targetMetric = "בעיטות בטן";
            DateTime meetingDate = DateTime.Today.AddDays(-2);
            meetingDateStr = meetingDate.ToString("yyyy-MM-dd");
            TimeSpan startTime = new TimeSpan(10, 30, 0);
            TimeSpan endTime = new TimeSpan(11, 30, 0);

            var allInvitationsScan = _dynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitations" }).GetAwaiter().GetResult();
            int maxInvitationId = 0;
            foreach (var item in allInvitationsScan.Items)
            {
                if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int idVal))
                {
                    if (idVal > maxInvitationId) maxInvitationId = idVal;
                }
            }
            int invitationId = maxInvitationId + 1;

            var newInvitation = new Dictionary<string, AttributeValue>
            {
                ["InvitationId"] = new AttributeValue { N = invitationId.ToString() },
                ["InstructorId"] = new AttributeValue { N = instructorId.ToString() },
                ["LessonType"] = new AttributeValue { S = "Group" },
                ["GroupId"] = new AttributeValue { N = groupId.ToString() },
                ["MeetingDate"] = new AttributeValue { S = meetingDateStr },
                ["StartTime"] = new AttributeValue { S = startTime.ToString("c") },
                ["EndTime"] = new AttributeValue { S = endTime.ToString("c") },
                ["Capacity"] = new AttributeValue { N = "20" },
                ["MinRegistrations"] = new AttributeValue { N = "5" },
                ["GeneralNote"] = new AttributeValue { S = "שיעור לדוגמה שהסתיים בעבר" },
                ["Status"] = new AttributeValue { S = "Scheduled" },
                ["TargetMetric"] = new AttributeValue { S = targetMetric },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                ["UpdatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
            };
            _dynamoDbClient.PutItemAsync("LessonInvitations", newInvitation).GetAwaiter().GetResult();
        }

        public async System.Threading.Tasks.Task<string> MigrateSqlToDynamoDbAsync()
        {
            var log = new StringBuilder();
            try
            {
                using SqlConnection con = OpenConnection();

                // 1. Migrate Instructors
                log.AppendLine("Starting Instructors migration...");
                const string sqlInstructors = "SELECT Id, Email, PasswordHash, Role, IsActive, CreatedAt, FirstName, LastName FROM dbo.Instructors";
                using (SqlCommand cmd = new SqlCommand(sqlInstructors, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var email = reader.GetString(1);
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Email"] = new AttributeValue { S = email },
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["PasswordHash"] = new AttributeValue { S = GetNullableString(reader, 2) ?? "" },
                            ["Role"] = new AttributeValue { S = GetNullableString(reader, 3) ?? "" },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(4) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(5).ToString("o") },
                            ["FirstName"] = new AttributeValue { S = GetNullableString(reader, 6) ?? "" },
                            ["LastName"] = new AttributeValue { S = GetNullableString(reader, 7) ?? "" }
                        };
                        await _dynamoDbClient.PutItemAsync("Instructors", item);
                    }
                }
                log.AppendLine("Instructors migrated successfully.");

                // 2. Migrate Parents
                log.AppendLine("Starting Parents migration...");
                const string sqlParents = "SELECT Id, Email, PasswordHash, FirstName, LastName, Phone, IsActive, CreatedAt FROM dbo.Parents";
                using (SqlCommand cmd = new SqlCommand(sqlParents, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var email = reader.GetString(1);
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Email"] = new AttributeValue { S = email },
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["PasswordHash"] = new AttributeValue { S = GetNullableString(reader, 2) ?? "" },
                            ["FirstName"] = new AttributeValue { S = GetNullableString(reader, 3) ?? "" },
                            ["LastName"] = new AttributeValue { S = GetNullableString(reader, 4) ?? "" },
                            ["Phone"] = new AttributeValue { S = GetNullableString(reader, 5) ?? "" },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(6) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(7).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("Parents", item);
                    }
                }
                log.AppendLine("Parents migrated successfully.");

                // 3. Migrate Children
                log.AppendLine("Starting Children migration...");
                const string sqlChildren = "SELECT Id, ParentId, FirstName, LastName, BirthDate, Description, IsActive, CreatedAt FROM dbo.Children";
                using (SqlCommand cmd = new SqlCommand(sqlChildren, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ParentId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["FirstName"] = new AttributeValue { S = reader.GetString(2) },
                            ["LastName"] = new AttributeValue { S = reader.GetString(3) },
                            ["BirthDate"] = new AttributeValue { S = reader.GetDateTime(4).ToString("o") },
                            ["Description"] = new AttributeValue { S = GetNullableString(reader, 5) ?? "" },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(6) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(7).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("Children", item);
                    }
                }
                log.AppendLine("Children migrated successfully.");

                // 4. Migrate GroupChildren
                log.AppendLine("Starting GroupChildren migration...");
                const string sqlGroupChildren = "SELECT GroupId, ChildId, IsActive, CreatedAt FROM dbo.GroupChildren";
                using (SqlCommand cmd = new SqlCommand(sqlGroupChildren, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["GroupId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ChildId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(2) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(3).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("GroupChildren", item);
                    }
                }
                log.AppendLine("GroupChildren migrated successfully.");

                // 5. Migrate Conversations
                log.AppendLine("Starting Conversations migration...");
                const string sqlConversations = "SELECT Id, ParentId, ChildId, InstructorId, IsActive, CreatedAt FROM dbo.Conversations";
                using (SqlCommand cmd = new SqlCommand(sqlConversations, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ParentId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["ChildId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(3).ToString() },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(4) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(5).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("Conversations", item);
                    }
                }
                log.AppendLine("Conversations migrated successfully.");

                // 6. Migrate ReportChildren
                log.AppendLine("Starting ReportChildren migration...");
                const string sqlReportChildren = "SELECT ReportId, ChildId, InstructorId, GroupId, ReportDate, ExerciseKey, ExerciseTitle, IsPresent, Comment, Metrics, CreatedAt FROM dbo.ReportChildren";
                using (SqlCommand cmd = new SqlCommand(sqlReportChildren, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["ReportId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ChildId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["GroupId"] = new AttributeValue { N = reader.IsDBNull(3) ? "0" : reader.GetInt32(3).ToString() },
                            ["ReportDate"] = new AttributeValue { S = reader.GetDateTime(4).ToString("yyyy-MM-dd") },
                            ["ExerciseKey"] = new AttributeValue { S = reader.GetString(5) },
                            ["ExerciseTitle"] = new AttributeValue { S = reader.GetString(6) },
                            ["IsPresent"] = new AttributeValue { BOOL = reader.GetBoolean(7) },
                            ["Comment"] = new AttributeValue { S = GetNullableString(reader, 8) ?? "" },
                            ["Metrics"] = new AttributeValue { S = reader.GetString(9) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(10).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("ReportChildren", item);
                    }
                }
                log.AppendLine("ReportChildren migrated successfully.");

                // 7. Migrate InstructorAIChatMessages
                log.AppendLine("Starting InstructorAIChatMessages migration...");
                const string sqlChat = "SELECT Id, InstructorId, SenderType, MessageText, SentAt FROM dbo.InstructorAIChatMessages";
                using (SqlCommand cmd = new SqlCommand(sqlChat, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["SenderType"] = new AttributeValue { S = reader.GetString(2) },
                            ["MessageText"] = new AttributeValue { S = reader.GetString(3) },
                            ["SentAt"] = new AttributeValue { S = reader.GetDateTime(4).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("InstructorAIChatMessages", item);
                    }
                }
                log.AppendLine("InstructorAIChatMessages migrated successfully.");

                // 8. Migrate PushDeviceTokens
                log.AppendLine("Starting PushDeviceTokens migration...");
                const string sqlPush = "SELECT Id, UserType, UserId, ExpoPushToken, Platform, DeviceId, IsActive, CreatedAt, UpdatedAt, LastRegisteredAt FROM dbo.PushDeviceTokens";
                using (SqlCommand cmd = new SqlCommand(sqlPush, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["ExpoPushToken"] = new AttributeValue { S = reader.GetString(3) },
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["UserType"] = new AttributeValue { S = reader.GetString(1) },
                            ["UserId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["Platform"] = new AttributeValue { S = GetNullableString(reader, 4) ?? "" },
                            ["DeviceId"] = new AttributeValue { S = GetNullableString(reader, 5) ?? "" },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(6) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(7).ToString("o") },
                            ["UpdatedAt"] = new AttributeValue { S = reader.GetDateTime(8).ToString("o") },
                            ["LastRegisteredAt"] = new AttributeValue { S = reader.GetDateTime(9).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("PushDeviceTokens", item);
                    }
                }
                log.AppendLine("PushDeviceTokens migrated successfully.");

                // 9. Migrate Groups
                log.AppendLine("Starting Groups migration...");
                const string sqlGroups = "SELECT Id, Name, Description, IsActive, CreatedAt FROM dbo.Groups";
                using (SqlCommand cmd = new SqlCommand(sqlGroups, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["Name"] = new AttributeValue { S = reader.GetString(1) },
                            ["Description"] = new AttributeValue { S = GetNullableString(reader, 2) ?? "" },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(3) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(4).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("Groups", item);
                    }
                }
                log.AppendLine("Groups migrated successfully.");

                // 10. Migrate InstructorGroups
                log.AppendLine("Starting InstructorGroups migration...");
                const string sqlInstructorGroups = "SELECT Id, InstructorId, GroupId, IsActive, CreatedAt FROM dbo.InstructorGroups";
                using (SqlCommand cmd = new SqlCommand(sqlInstructorGroups, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["GroupId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(3) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(4).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("InstructorGroups", item);
                    }
                }
                log.AppendLine("InstructorGroups migrated successfully.");

                // 11. Migrate LessonInvitations
                log.AppendLine("Starting LessonInvitations migration...");
                const string sqlLessonInvitations = "SELECT InvitationId, InstructorId, LessonType, GroupId, MeetingDate, StartTime, EndTime, Capacity, MinRegistrations, GeneralNote, Status, CreatedAt, UpdatedAt, TargetMetric FROM dbo.LessonInvitations";
                using (SqlCommand cmd = new SqlCommand(sqlLessonInvitations, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["InvitationId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["LessonType"] = new AttributeValue { S = reader.GetString(2) },
                            ["GroupId"] = new AttributeValue { N = reader.IsDBNull(3) ? "0" : reader.GetInt32(3).ToString() },
                            ["MeetingDate"] = new AttributeValue { S = reader.GetDateTime(4).ToString("yyyy-MM-dd") },
                            ["StartTime"] = new AttributeValue { S = reader.GetTimeSpan(5).ToString("c") },
                            ["EndTime"] = new AttributeValue { S = reader.GetTimeSpan(6).ToString("c") },
                            ["Capacity"] = new AttributeValue { N = reader.GetInt32(7).ToString() },
                            ["MinRegistrations"] = new AttributeValue { N = reader.GetInt32(8).ToString() },
                            ["GeneralNote"] = new AttributeValue { S = GetNullableString(reader, 9) ?? "" },
                            ["Status"] = new AttributeValue { S = reader.GetString(10) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(11).ToString("o") },
                            ["UpdatedAt"] = new AttributeValue { S = reader.IsDBNull(12) ? "" : reader.GetDateTime(12).ToString("o") },
                            ["TargetMetric"] = new AttributeValue { S = GetNullableString(reader, 13) ?? "" }
                        };
                        await _dynamoDbClient.PutItemAsync("LessonInvitations", item);
                    }
                }
                log.AppendLine("LessonInvitations migrated successfully.");

                // 12. Migrate LessonInvitationRecipients
                log.AppendLine("Starting LessonInvitationRecipients migration...");
                const string sqlLessonInvitationRecipients = "SELECT RecipientId, InvitationId, ParentId, ChildId, ResponseStatus, RespondedAt, IsActive FROM dbo.LessonInvitationRecipients";
                using (SqlCommand cmd = new SqlCommand(sqlLessonInvitationRecipients, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["RecipientId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["InvitationId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["ParentId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["ChildId"] = new AttributeValue { N = reader.GetInt32(3).ToString() },
                            ["ResponseStatus"] = new AttributeValue { S = reader.GetString(4) },
                            ["RespondedAt"] = new AttributeValue { S = reader.IsDBNull(5) ? "" : reader.GetDateTime(5).ToString("o") },
                            ["IsActive"] = new AttributeValue { BOOL = reader.GetBoolean(6) }
                        };
                        await _dynamoDbClient.PutItemAsync("LessonInvitationRecipients", item);
                    }
                }
                log.AppendLine("LessonInvitationRecipients migrated successfully.");

                // 13. Migrate Messages
                log.AppendLine("Starting Messages migration...");
                const string sqlMessages = "SELECT Id, ParentId, InstructorId, SenderType, MessageText, SentAt, ConversationId FROM dbo.Messages";
                using (SqlCommand cmd = new SqlCommand(sqlMessages, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ParentId"] = new AttributeValue { N = reader.IsDBNull(1) ? "0" : reader.GetInt32(1).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.IsDBNull(2) ? "0" : reader.GetInt32(2).ToString() },
                            ["SenderType"] = new AttributeValue { S = reader.GetString(3) },
                            ["MessageText"] = new AttributeValue { S = reader.GetString(4) },
                            ["SentAt"] = new AttributeValue { S = reader.GetDateTime(5).ToString("o") },
                            ["ConversationId"] = new AttributeValue { N = reader.GetInt32(6).ToString() }
                        };
                        await _dynamoDbClient.PutItemAsync("Messages", item);
                    }
                }
                log.AppendLine("Messages migrated successfully.");

                // 14. Migrate LessonNotifications
                log.AppendLine("Starting LessonNotifications migration...");
                const string sqlLessonNotifications = "SELECT NotificationId, UserType, UserId, NotificationType, Title, Body, RelatedInvitationId, RelatedRecipientId, IsRead, CreatedAt, ReadAt, PayloadJson FROM dbo.LessonNotifications";
                using (SqlCommand cmd = new SqlCommand(sqlLessonNotifications, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["NotificationId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["UserType"] = new AttributeValue { S = reader.GetString(1) },
                            ["UserId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["NotificationType"] = new AttributeValue { S = reader.GetString(3) },
                            ["Title"] = new AttributeValue { S = reader.GetString(4) },
                            ["Body"] = new AttributeValue { S = reader.GetString(5) },
                            ["RelatedInvitationId"] = new AttributeValue { N = reader.IsDBNull(6) ? "0" : reader.GetInt32(6).ToString() },
                            ["RelatedRecipientId"] = new AttributeValue { N = reader.IsDBNull(7) ? "0" : reader.GetInt32(7).ToString() },
                            ["IsRead"] = new AttributeValue { BOOL = reader.GetBoolean(8) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(9).ToString("o") },
                            ["ReadAt"] = new AttributeValue { S = reader.IsDBNull(10) ? "" : reader.GetDateTime(10).ToString("o") },
                            ["PayloadJson"] = new AttributeValue { S = GetNullableString(reader, 11) ?? "" }
                        };
                        await _dynamoDbClient.PutItemAsync("LessonNotifications", item);
                    }
                }
                log.AppendLine("LessonNotifications migrated successfully.");

                // 15. Migrate GroupTrainingSessions
                log.AppendLine("Starting GroupTrainingSessions migration...");
                const string sqlGroupTrainingSessions = "SELECT SessionId, GroupId, InstructorId, MeetingDate, StartTime, EndTime, Notes, Status, CreatedAt FROM dbo.GroupTrainingSessions";
                using (SqlCommand cmd = new SqlCommand(sqlGroupTrainingSessions, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["SessionId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["GroupId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["MeetingDate"] = new AttributeValue { S = reader.GetDateTime(3).ToString("yyyy-MM-dd") },
                            ["StartTime"] = new AttributeValue { S = reader.GetTimeSpan(4).ToString("c") },
                            ["EndTime"] = new AttributeValue { S = reader.GetTimeSpan(5).ToString("c") },
                            ["Notes"] = new AttributeValue { S = GetNullableString(reader, 6) ?? "" },
                            ["Status"] = new AttributeValue { S = reader.GetString(7) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(8).ToString("o") }
                        };
                        await _dynamoDbClient.PutItemAsync("GroupTrainingSessions", item);
                    }
                }
                log.AppendLine("GroupTrainingSessions migrated successfully.");

                // 16. Migrate TrainingSessions
                log.AppendLine("Starting TrainingSessions migration...");
                const string sqlTrainingSessions = "SELECT SessionId, ParentId, ChildId, InstructorId, MeetingDate, StartTime, EndTime, Notes, Status, CreatedAt, TargetMetric FROM dbo.TrainingSessions";
                using (SqlCommand cmd = new SqlCommand(sqlTrainingSessions, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var item = new Dictionary<string, AttributeValue>
                        {
                            ["SessionId"] = new AttributeValue { N = reader.GetInt32(0).ToString() },
                            ["ParentId"] = new AttributeValue { N = reader.GetInt32(1).ToString() },
                            ["ChildId"] = new AttributeValue { N = reader.GetInt32(2).ToString() },
                            ["InstructorId"] = new AttributeValue { N = reader.GetInt32(3).ToString() },
                            ["MeetingDate"] = new AttributeValue { S = reader.GetDateTime(4).ToString("yyyy-MM-dd") },
                            ["StartTime"] = new AttributeValue { S = reader.GetTimeSpan(5).ToString("c") },
                            ["EndTime"] = new AttributeValue { S = reader.GetTimeSpan(6).ToString("c") },
                            ["Notes"] = new AttributeValue { S = GetNullableString(reader, 7) ?? "" },
                            ["Status"] = new AttributeValue { S = reader.GetString(8) },
                            ["CreatedAt"] = new AttributeValue { S = reader.GetDateTime(9).ToString("o") },
                            ["TargetMetric"] = new AttributeValue { S = GetNullableString(reader, 10) ?? "" }
                        };
                        await _dynamoDbClient.PutItemAsync("TrainingSessions", item);
                    }
                }
                log.AppendLine("TrainingSessions migrated successfully.");

                log.AppendLine("Database Migration completed successfully!");
            }
            catch (Exception ex)
            {
                log.AppendLine("Error occurred during migration: " + ex.Message + "\n" + ex.StackTrace);
            }
            return log.ToString();
        }
    }

    public class AIChatMessageRecord
    {
        public string SenderType { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    public class AuthenticatedUser
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class ChildRecord
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string ChildDescription { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ParentEmail { get; set; } = string.Empty;
        public string ParentFullName { get; set; } = string.Empty;
        public string Strengths { get; set; } = string.Empty;
        public string Weaknesses { get; set; } = string.Empty;
        public string PersonalGoals { get; set; } = string.Empty;
    }

    public class ParentEmailOption
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public class ManagerParentRecord
    {
        public int ParentId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> ChildNames { get; set; } = new List<string>();
    }

    public class ManagerInstructorRecord
    {
        public int InstructorId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> GroupNames { get; set; } = new List<string>();
    }

    public class InstructorGroupRecord
    {
        public int GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int ActiveChildrenCount { get; set; }
    }

    public class InstructorChildRecord
    {
        public int ChildId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public string ParentEmail { get; set; } = string.Empty;
        public string ParentFullName { get; set; } = string.Empty;
    }

    public class InstructorGroupWithChildrenRecord
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string GroupDescription { get; set; } = string.Empty;
        public List<InstructorChildRecord> Children { get; set; } = new List<InstructorChildRecord>();
    }

    public class GroupChildOptionRecord
    {
        public int ChildId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string ParentEmail { get; set; } = string.Empty;
        public string ParentFullName { get; set; } = string.Empty;
        public int? ActiveGroupId { get; set; }
        public string ActiveGroupName { get; set; } = string.Empty;
    }

    public class GroupInstructorOptionRecord
    {
        public int InstructorId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class GroupSummaryRecord
    {
        public int GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? InstructorId { get; set; }
        public string InstructorFullName { get; set; } = string.Empty;
        public string InstructorEmail { get; set; } = string.Empty;
        public int ActiveChildrenCount { get; set; }
    }

    public class ManagerCenterSettingsRecord
    {
        public string CenterName { get; set; } = string.Empty;
        public string CenterAddress { get; set; } = string.Empty;
        public bool SendAutoReports { get; set; }
        public bool ReceiveAlerts { get; set; }
        public bool ShowKidsAdvanced { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class ManagerAttendanceReportRowRecord
    {
        public int SessionId { get; set; }
        public string LessonType { get; set; } = string.Empty;
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string AttendanceCategory { get; set; } = string.Empty;
        public int? ChildId { get; set; }
        public string ChildFullName { get; set; } = string.Empty;
        public int InstructorId { get; set; }
        public string InstructorFullName { get; set; } = string.Empty;
        public int? GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
    }

    public class ManagerSystemReportsOverviewRecord
    {
        public DateTime RangeStartDate { get; set; }
        public DateTime RangeEndDate { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public int ActiveGroupsCount { get; set; }
        public int ActiveInstructorsCount { get; set; }
        public int ActiveChildrenCount { get; set; }
        public int ReportsCount { get; set; }
        public int ChildrenWithReportsCount { get; set; }
        public int AttendanceRowsCount { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int ScheduledCount { get; set; }
        public int CancelledCount { get; set; }
    }

    public class ManagerGeneratedSystemReportRecord
    {
        public string ReportType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime RangeStartDate { get; set; }
        public DateTime RangeEndDate { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public int RecordsCount { get; set; }
        public List<string> SummaryLines { get; set; } = new List<string>();
    }

    public class GroupRecommendationResultRecord
    {
        public int ChildId { get; set; }
        public string ChildFullName { get; set; } = string.Empty;
        public int RecommendedGroupId { get; set; }
        public string RecommendedGroupName { get; set; } = string.Empty;
        public string RecommendedInstructorFullName { get; set; } = string.Empty;
        public double Score { get; set; }
        public bool UsedFallback { get; set; }
        public string Reasoning { get; set; } = string.Empty;
        public List<string> SharedTerms { get; set; } = new List<string>();
        public List<string> SharedConcepts { get; set; } = new List<string>();
    }

    public class ChatChildRecord
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int InstructorsCount { get; set; }
    }

    public class ChatInstructorRecord
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ConversationRecord
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public int ChildId { get; set; }
        public int InstructorId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string ParentFullName { get; set; } = string.Empty;
        public string ChildFullName { get; set; } = string.Empty;
        public string InstructorFullName { get; set; } = string.Empty;
    }

    public class ConversationInboxRecord
    {
        public int ConversationId { get; set; }
        public int ParentId { get; set; }
        public int ChildId { get; set; }
        public int InstructorId { get; set; }
        public string ParentFullName { get; set; } = string.Empty;
        public string ChildFullName { get; set; } = string.Empty;
        public string InstructorFullName { get; set; } = string.Empty;
        public DateTime? LastMessageAt { get; set; }
        public string LastMessageText { get; set; } = string.Empty;
        public string LastMessageSenderType { get; set; } = string.Empty;
    }

    public class ChatMessageRecord
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public string SenderType { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    public class ChildReportRecord
    {
        public int ReportId { get; set; }
        public int ChildId { get; set; }
        public int InstructorId { get; set; }
        public int? GroupId { get; set; }
        public DateTime ReportDate { get; set; }
        public string ExerciseKey { get; set; } = string.Empty;
        public string ExerciseTitle { get; set; } = string.Empty;
        public bool IsPresent { get; set; } = true;
        public string Comment { get; set; } = string.Empty;
        public string Metrics { get; set; } = "[]";

        /// <summary>
        /// Backward-compatible property: returns the old Notes-style JSON
        /// so existing code that reads Notes continues to work during migration.
        /// </summary>
        public string Notes
        {
            get
            {
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    isPresent = IsPresent,
                    exerciseKey = ExerciseKey,
                    exerciseTitle = ExerciseTitle,
                    comment = Comment,
                    metrics = System.Text.Json.JsonSerializer.Deserialize<object>(
                        string.IsNullOrWhiteSpace(Metrics) ? "[]" : Metrics) ?? new object[0]
                });
            }
        }
    }

    public class TrainingSessionRecord
    {
        public int SessionId { get; set; }
        public int ParentId { get; set; }
        public int ChildId { get; set; }
        public int InstructorId { get; set; }
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class GroupTrainingSessionRecord
    {
        public int SessionId { get; set; }
        public int GroupId { get; set; }
        public int InstructorId { get; set; }
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class InstructorTrainingSessionViewRecord
    {
        public int SessionId { get; set; }
        public int InstructorId { get; set; }
        public int ParentId { get; set; }
        public int ChildId { get; set; }
        public string ChildFullName { get; set; } = string.Empty;
        public string ParentFullName { get; set; } = string.Empty;
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class InstructorGroupTrainingSessionViewRecord
    {
        public int SessionId { get; set; }
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public int InstructorId { get; set; }
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ActiveChildrenCount { get; set; }
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class ParentScheduledLessonRecord
    {
        public int SessionId { get; set; }
        public string LessonType { get; set; } = string.Empty;
        public int ChildId { get; set; }
        public string ChildFullName { get; set; } = string.Empty;
        public int InstructorId { get; set; }
        public string InstructorFullName { get; set; } = string.Empty;
        public int? GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class LessonInvitationRecipientSelectionRecord
    {
        public int ParentId { get; set; }
        public int ChildId { get; set; }
    }

    public class LessonInvitationRecipientOptionRecord
    {
        public int ParentId { get; set; }
        public int ChildId { get; set; }
        public string ParentFullName { get; set; } = string.Empty;
        public string ParentEmail { get; set; } = string.Empty;
        public string ChildFullName { get; set; } = string.Empty;
    }

    public class LessonInvitationRecord
    {
        public int InvitationId { get; set; }
        public int InstructorId { get; set; }
        public string LessonType { get; set; } = string.Empty;
        public int? GroupId { get; set; }
        public DateTime MeetingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int Capacity { get; set; }
        public int MinRegistrations { get; set; }
        public string GeneralNote { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int RecipientsCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int PendingCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string TargetMetric { get; set; } = string.Empty;
        public bool HasReport { get; set; }
    }

    public class LessonNotificationRecord
    {
        public int NotificationId { get; set; }
        public string UserType { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public int? RelatedInvitationId { get; set; }
        public int? RelatedRecipientId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public string LessonType { get; set; } = string.Empty;
        public DateTime? MeetingDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string InvitationStatus { get; set; } = string.Empty;
        public string RecipientResponseStatus { get; set; } = string.Empty;
        public string TargetMetric { get; set; } = string.Empty;
    }

    public class LessonInvitationActionResult
    {
        public int InvitationId { get; set; }
        public int RecipientId { get; set; }
        public string ResponseStatus { get; set; } = string.Empty;
        public string InvitationStatus { get; set; } = string.Empty;
    }

    public class GroupAiStatisticRecord
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public Dictionary<string, double> MedianMetrics { get; set; } = new Dictionary<string, double>();
        public string AiNotes { get; set; } = string.Empty;
    }
}

