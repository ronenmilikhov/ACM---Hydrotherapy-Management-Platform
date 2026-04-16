using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ReactServerSide.DAL
{
    public class DBServices
    {
        private readonly string _connectionString;
        private const string PasswordHashPrefix = "pbkdf2";
        private const int PasswordHashIterations = 120000;
        private const int PasswordSaltSizeBytes = 16;
        private const int PasswordKeySizeBytes = 32;
        private static readonly Dictionary<string, string[]> GroupRecommendationConceptKeywords = new Dictionary<string, string[]>
        {
            ["motor"] = new[] { "motor", "coordination", "balance", "strength", "מוטור", "קואורדינציה", "שיווי", "יציבה", "כוח" },
            ["sensory"] = new[] { "sensory", "regulation", "touch", "noise", "חושי", "ויסות", "רגישות", "מגע", "רעש" },
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

        public DBServices(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("myProjDB")
                ?? throw new InvalidOperationException("Connection string 'myProjDB' was not found.");
        }

        private SqlConnection OpenConnection()
        {
            SqlConnection con = new SqlConnection(_connectionString);
            con.Open();
            return con;
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

        private static void UpgradeInstructorPasswordHash(SqlConnection con, int instructorId, string rawPassword)
        {
            const string sql = @"
                UPDATE dbo.Instructors
                SET PasswordHash = @PasswordHash
                WHERE Id = @Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", instructorId);
            cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(rawPassword));
            cmd.ExecuteNonQuery();
        }

        private static void UpgradeParentPasswordHash(SqlConnection con, int parentId, string rawPassword)
        {
            const string sql = @"
                UPDATE dbo.Parents
                SET PasswordHash = @PasswordHash
                WHERE Id = @Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", parentId);
            cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(rawPassword));
            cmd.ExecuteNonQuery();
        }

        public AuthenticatedUser? PostAuthenticateUser(string email, string password)
        {
            using SqlConnection con = OpenConnection();

            AuthenticatedUser? authenticatedInstructor = null;
            bool shouldUpgradeInstructorHash = false;
            int instructorIdToUpgrade = 0;

            const string instructorSql = @"
                SELECT TOP 1 Id, Email, Role, FirstName, LastName, PasswordHash
                FROM dbo.Instructors
                WHERE LOWER(Email) = @Email AND IsActive = 1;";

            using (SqlCommand instructorCmd = new SqlCommand(instructorSql, con))
            {
                instructorCmd.Parameters.AddWithValue("@Email", email);

                using SqlDataReader reader = instructorCmd.ExecuteReader();
                if (reader.Read())
                {
                    int instructorId = reader.GetInt32(0);
                    string storedPassword = GetNullableString(reader, 5);
                    if (VerifyPassword(password, storedPassword))
                    {
                        shouldUpgradeInstructorHash = !IsPasswordHashFormat(storedPassword);
                        instructorIdToUpgrade = instructorId;
                        authenticatedInstructor = new AuthenticatedUser
                        {
                            Id = instructorId,
                            Email = reader.GetString(1),
                            Role = reader.GetString(2),
                            UserType = "Instructor",
                            FullName = $"{GetNullableString(reader, 3)} {GetNullableString(reader, 4)}".Trim()
                        };
                    }
                }
            }

            if (authenticatedInstructor != null)
            {
                if (shouldUpgradeInstructorHash)
                {
                    UpgradeInstructorPasswordHash(con, instructorIdToUpgrade, password);
                }

                return authenticatedInstructor;
            }

            AuthenticatedUser? authenticatedParent = null;
            bool shouldUpgradeParentHash = false;
            int parentIdToUpgrade = 0;

            const string parentSql = @"
                SELECT TOP 1 Id, Email, FirstName, LastName, PasswordHash
                FROM dbo.Parents
                WHERE LOWER(Email) = @Email AND IsActive = 1;";

            using (SqlCommand parentCmd = new SqlCommand(parentSql, con))
            {
                parentCmd.Parameters.AddWithValue("@Email", email);

                using SqlDataReader reader = parentCmd.ExecuteReader();
                if (reader.Read())
                {
                    int parentId = reader.GetInt32(0);
                    string storedPassword = GetNullableString(reader, 4);
                    if (VerifyPassword(password, storedPassword))
                    {
                        shouldUpgradeParentHash = !IsPasswordHashFormat(storedPassword);
                        parentIdToUpgrade = parentId;
                        authenticatedParent = new AuthenticatedUser
                        {
                            Id = parentId,
                            Email = reader.GetString(1),
                            Role = "Parent",
                            UserType = "Parent",
                            FullName = $"{GetNullableString(reader, 2)} {GetNullableString(reader, 3)}".Trim()
                        };
                    }
                }
            }

            if (authenticatedParent != null)
            {
                if (shouldUpgradeParentHash)
                {
                    UpgradeParentPasswordHash(con, parentIdToUpgrade, password);
                }

                return authenticatedParent;
            }

            return null;
        }

        public bool PostIsManagerCredentialsValid(string email, string password)
        {
            using SqlConnection con = OpenConnection();

            const string managerSql = @"
                SELECT TOP 1 Id, PasswordHash
                FROM dbo.Instructors
                WHERE LOWER(Email) = @Email AND Role = 'Manager' AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(managerSql, con);
            cmd.Parameters.AddWithValue("@Email", email);

            int managerId;
            string storedPassword;

            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return false;
                }

                managerId = reader.GetInt32(0);
                storedPassword = GetNullableString(reader, 1);
            }

            bool isValid = VerifyPassword(password, storedPassword);

            if (!isValid)
            {
                return false;
            }

            if (!IsPasswordHashFormat(storedPassword))
            {
                UpgradeInstructorPasswordHash(con, managerId, password);
            }

            return true;
        }

        public bool PostEmailExists(string email)
        {
            using SqlConnection con = OpenConnection();

            const string existsSql = @"
                SELECT CASE
                    WHEN EXISTS (SELECT 1 FROM dbo.Instructors WHERE LOWER(Email) = @Email)
                      OR EXISTS (SELECT 1 FROM dbo.Parents WHERE LOWER(Email) = @Email)
                    THEN 1 ELSE 0 END;";

            using SqlCommand cmd = new SqlCommand(existsSql, con);
            cmd.Parameters.AddWithValue("@Email", email);

            int exists = Convert.ToInt32(cmd.ExecuteScalar());
            return exists == 1;
        }

        public void EnsureChildrenSchemaReady()
        {
            using SqlConnection con = OpenConnection();
            EnsureChildrenDescriptionColumnExists(con);
            EnsurePushDeviceTokensTableExists(con);
        }

        public int PostCreateInstructor(string email, string password, string firstName, string lastName)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            string normalizedEmail = email.Trim().ToLowerInvariant();
            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            string hashedPassword = HashPassword(password);

            const string parentExistsSql = @"
                SELECT COUNT(1)
                FROM dbo.Parents
                WHERE LOWER(Email) = @Email;";

            using (SqlCommand parentExistsCmd = new SqlCommand(parentExistsSql, con, tx))
            {
                parentExistsCmd.Parameters.AddWithValue("@Email", normalizedEmail);
                if (Convert.ToInt32(parentExistsCmd.ExecuteScalar()) > 0)
                {
                    throw new InvalidOperationException("Email already exists.");
                }
            }

            const string existingInstructorSql = @"
                SELECT TOP 1 Id, Role, IsActive
                FROM dbo.Instructors
                WHERE LOWER(Email) = @Email
                ORDER BY Id;";

            int existingInstructorId = 0;
            string existingRole = string.Empty;
            bool existingIsActive = false;

            using (SqlCommand existingInstructorCmd = new SqlCommand(existingInstructorSql, con, tx))
            {
                existingInstructorCmd.Parameters.AddWithValue("@Email", normalizedEmail);
                using SqlDataReader reader = existingInstructorCmd.ExecuteReader();
                if (reader.Read())
                {
                    existingInstructorId = reader.GetInt32(0);
                    existingRole = GetNullableString(reader, 1);
                    existingIsActive = reader.GetBoolean(2);
                }
            }

            if (existingInstructorId > 0)
            {
                if (!string.Equals(existingRole, "Instructor", StringComparison.OrdinalIgnoreCase) || existingIsActive)
                {
                    throw new InvalidOperationException("Email already exists.");
                }

                const string reactivateSql = @"
                    UPDATE dbo.Instructors
                    SET
                        PasswordHash = @PasswordHash,
                        FirstName = @FirstName,
                        LastName = @LastName,
                        Role = 'Instructor',
                        IsActive = 1
                    WHERE Id = @InstructorId
                      AND IsActive = 0;";

                int affectedRows;
                using (SqlCommand reactivateCmd = new SqlCommand(reactivateSql, con, tx))
                {
                    reactivateCmd.Parameters.AddWithValue("@InstructorId", existingInstructorId);
                    reactivateCmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                    reactivateCmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                    reactivateCmd.Parameters.AddWithValue("@LastName", normalizedLastName);
                    affectedRows = reactivateCmd.ExecuteNonQuery();
                }

                if (affectedRows == 0)
                {
                    throw new InvalidOperationException("Instructor exists but could not be reactivated.");
                }

                tx.Commit();
                return existingInstructorId;
            }

            const string insertSql = @"
                INSERT INTO dbo.Instructors (Email, PasswordHash, Role, IsActive, CreatedAt, FirstName, LastName)
                VALUES (@Email, @PasswordHash, 'Instructor', 1, SYSUTCDATETIME(), @FirstName, @LastName);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using (SqlCommand cmd = new SqlCommand(insertSql, con, tx))
            {
                cmd.Parameters.AddWithValue("@Email", normalizedEmail);
                cmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                cmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                cmd.Parameters.AddWithValue("@LastName", normalizedLastName);

                try
                {
                    int newId = Convert.ToInt32(cmd.ExecuteScalar());
                    tx.Commit();
                    return newId;
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    throw new InvalidOperationException("Email already exists.");
                }
            }
        }

        public int PostCreateParent(string email, string password, string firstName, string lastName, string? phone)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            string normalizedEmail = email.Trim().ToLowerInvariant();
            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            object normalizedPhone = string.IsNullOrWhiteSpace(phone)
                ? DBNull.Value
                : phone.Trim();
            string hashedPassword = HashPassword(password);

            const string instructorExistsSql = @"
                SELECT COUNT(1)
                FROM dbo.Instructors
                WHERE LOWER(Email) = @Email;";

            using (SqlCommand instructorExistsCmd = new SqlCommand(instructorExistsSql, con, tx))
            {
                instructorExistsCmd.Parameters.AddWithValue("@Email", normalizedEmail);
                if (Convert.ToInt32(instructorExistsCmd.ExecuteScalar()) > 0)
                {
                    throw new InvalidOperationException("Email already exists.");
                }
            }

            const string existingParentSql = @"
                SELECT TOP 1 Id, IsActive
                FROM dbo.Parents
                WHERE LOWER(Email) = @Email
                ORDER BY Id;";

            int existingParentId = 0;
            bool existingIsActive = false;

            using (SqlCommand existingParentCmd = new SqlCommand(existingParentSql, con, tx))
            {
                existingParentCmd.Parameters.AddWithValue("@Email", normalizedEmail);
                using SqlDataReader reader = existingParentCmd.ExecuteReader();
                if (reader.Read())
                {
                    existingParentId = reader.GetInt32(0);
                    existingIsActive = reader.GetBoolean(1);
                }
            }

            if (existingParentId > 0)
            {
                if (existingIsActive)
                {
                    throw new InvalidOperationException("Email already exists.");
                }

                const string reactivateSql = @"
                    UPDATE dbo.Parents
                    SET
                        PasswordHash = @PasswordHash,
                        FirstName = @FirstName,
                        LastName = @LastName,
                        Phone = @Phone,
                        IsActive = 1
                    WHERE Id = @ParentId
                      AND IsActive = 0;";

                int affectedRows;
                using (SqlCommand reactivateCmd = new SqlCommand(reactivateSql, con, tx))
                {
                    reactivateCmd.Parameters.AddWithValue("@ParentId", existingParentId);
                    reactivateCmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                    reactivateCmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                    reactivateCmd.Parameters.AddWithValue("@LastName", normalizedLastName);
                    reactivateCmd.Parameters.Add("@Phone", SqlDbType.NVarChar).Value = normalizedPhone;
                    affectedRows = reactivateCmd.ExecuteNonQuery();
                }

                if (affectedRows == 0)
                {
                    throw new InvalidOperationException("Parent exists but could not be reactivated.");
                }

                tx.Commit();
                return existingParentId;
            }

            const string insertSql = @"
                INSERT INTO dbo.Parents (Email, PasswordHash, FirstName, LastName, Phone, IsActive, CreatedAt)
                VALUES (@Email, @PasswordHash, @FirstName, @LastName, @Phone, 1, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using (SqlCommand cmd = new SqlCommand(insertSql, con, tx))
            {
                cmd.Parameters.AddWithValue("@Email", normalizedEmail);
                cmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                cmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                cmd.Parameters.AddWithValue("@LastName", normalizedLastName);
                cmd.Parameters.Add("@Phone", SqlDbType.NVarChar).Value = normalizedPhone;

                try
                {
                    int newId = Convert.ToInt32(cmd.ExecuteScalar());
                    tx.Commit();
                    return newId;
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    throw new InvalidOperationException("Email already exists.");
                }
            }
        }

        public List<ManagerParentRecord> GetParentsForManager()
        {
            using SqlConnection con = OpenConnection();

            const string parentsSql = @"
                SELECT
                    Id,
                    Email,
                    Phone,
                    FirstName,
                    LastName
                FROM dbo.Parents
                WHERE IsActive = 1
                ORDER BY FirstName, LastName, Id;";

            List<ManagerParentRecord> parents = new List<ManagerParentRecord>();
            Dictionary<int, ManagerParentRecord> parentsById = new Dictionary<int, ManagerParentRecord>();

            using (SqlCommand parentsCmd = new SqlCommand(parentsSql, con))
            {
                using SqlDataReader reader = parentsCmd.ExecuteReader();
                while (reader.Read())
                {
                    string firstName = GetNullableString(reader, 3);
                    string lastName = GetNullableString(reader, 4);
                    string fullName = $"{firstName} {lastName}".Trim();

                    ManagerParentRecord parent = new ManagerParentRecord
                    {
                        ParentId = reader.GetInt32(0),
                        Email = GetNullableString(reader, 1),
                        Phone = GetNullableString(reader, 2),
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = string.IsNullOrWhiteSpace(fullName) ? GetNullableString(reader, 1) : fullName,
                        ChildNames = new List<string>()
                    };

                    parents.Add(parent);
                    parentsById[parent.ParentId] = parent;
                }
            }

            if (parents.Count == 0)
            {
                return parents;
            }

            const string childrenSql = @"
                SELECT
                    c.ParentId,
                    c.FirstName,
                    c.LastName,
                    c.Id
                FROM dbo.Children c
                INNER JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                   AND p.IsActive = 1
                WHERE c.IsActive = 1
                ORDER BY c.ParentId, c.FirstName, c.LastName, c.Id;";

            using (SqlCommand childrenCmd = new SqlCommand(childrenSql, con))
            {
                using SqlDataReader reader = childrenCmd.ExecuteReader();
                while (reader.Read())
                {
                    int parentId = reader.GetInt32(0);
                    if (!parentsById.TryGetValue(parentId, out ManagerParentRecord? parent))
                    {
                        continue;
                    }

                    string childName = $"{GetNullableString(reader, 1)} {GetNullableString(reader, 2)}".Trim();
                    if (string.IsNullOrWhiteSpace(childName))
                    {
                        childName = $"Child {reader.GetInt32(3)}";
                    }

                    parent.ChildNames.Add(childName);
                }
            }

            return parents;
        }

        public bool PutUpdateParentNames(int parentId, string firstName, string lastName)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                UPDATE dbo.Parents
                SET
                    FirstName = @FirstName,
                    LastName = @LastName
                WHERE Id = @ParentId
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", lastName);

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteDeactivateParentForManager(int parentId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            bool hasChildren = GetUserTableExists(con, tx, "Children");
            bool hasGroupChildren = GetUserTableExists(con, tx, "GroupChildren");
            bool hasConversations = GetUserTableExists(con, tx, "Conversations");
            bool hasPushTokens = GetUserTableExists(con, tx, "PushDeviceTokens");

            const string deactivateParentSql = @"
                UPDATE dbo.Parents
                SET IsActive = 0
                WHERE Id = @ParentId
                  AND IsActive = 1;";

            int affectedRows;
            using (SqlCommand deactivateParentCmd = new SqlCommand(deactivateParentSql, con, tx))
            {
                deactivateParentCmd.Parameters.AddWithValue("@ParentId", parentId);
                affectedRows = deactivateParentCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                tx.Rollback();
                return false;
            }

            if (hasChildren)
            {
                const string deactivateChildrenSql = @"
                    UPDATE dbo.Children
                    SET IsActive = 0
                    WHERE ParentId = @ParentId
                      AND IsActive = 1;";

                using SqlCommand deactivateChildrenCmd = new SqlCommand(deactivateChildrenSql, con, tx);
                deactivateChildrenCmd.Parameters.AddWithValue("@ParentId", parentId);
                deactivateChildrenCmd.ExecuteNonQuery();
            }

            if (hasGroupChildren && hasChildren)
            {
                const string deactivateGroupChildrenSql = @"
                    UPDATE dbo.GroupChildren
                    SET IsActive = 0
                    WHERE IsActive = 1
                      AND ChildId IN (
                          SELECT c.Id
                          FROM dbo.Children c
                          WHERE c.ParentId = @ParentId
                      );";

                using SqlCommand deactivateGroupChildrenCmd = new SqlCommand(deactivateGroupChildrenSql, con, tx);
                deactivateGroupChildrenCmd.Parameters.AddWithValue("@ParentId", parentId);
                deactivateGroupChildrenCmd.ExecuteNonQuery();
            }

            if (hasConversations)
            {
                const string deactivateConversationsSql = @"
                    UPDATE dbo.Conversations
                    SET IsActive = 0
                    WHERE ParentId = @ParentId
                      AND IsActive = 1;";

                using SqlCommand deactivateConversationsCmd = new SqlCommand(deactivateConversationsSql, con, tx);
                deactivateConversationsCmd.Parameters.AddWithValue("@ParentId", parentId);
                deactivateConversationsCmd.ExecuteNonQuery();
            }

            if (hasPushTokens)
            {
                const string deactivateTokensSql = @"
                    UPDATE dbo.PushDeviceTokens
                    SET IsActive = 0
                    WHERE UserType = 'Parent'
                      AND UserId = @ParentId
                      AND IsActive = 1;";

                using SqlCommand deactivateTokensCmd = new SqlCommand(deactivateTokensSql, con, tx);
                deactivateTokensCmd.Parameters.AddWithValue("@ParentId", parentId);
                deactivateTokensCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        public List<ManagerInstructorRecord> GetInstructorsForManager()
        {
            using SqlConnection con = OpenConnection();

            const string instructorsSql = @"
                SELECT
                    Id,
                    Email,
                    FirstName,
                    LastName
                FROM dbo.Instructors
                WHERE IsActive = 1
                  AND Role = 'Instructor'
                ORDER BY FirstName, LastName, Id;";

            List<ManagerInstructorRecord> instructors = new List<ManagerInstructorRecord>();
            Dictionary<int, ManagerInstructorRecord> instructorsById = new Dictionary<int, ManagerInstructorRecord>();

            using (SqlCommand instructorsCmd = new SqlCommand(instructorsSql, con))
            {
                using SqlDataReader reader = instructorsCmd.ExecuteReader();
                while (reader.Read())
                {
                    string firstName = GetNullableString(reader, 2);
                    string lastName = GetNullableString(reader, 3);
                    string fullName = $"{firstName} {lastName}".Trim();

                    ManagerInstructorRecord instructor = new ManagerInstructorRecord
                    {
                        InstructorId = reader.GetInt32(0),
                        Email = GetNullableString(reader, 1),
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = string.IsNullOrWhiteSpace(fullName) ? GetNullableString(reader, 1) : fullName,
                        GroupNames = new List<string>()
                    };

                    instructors.Add(instructor);
                    instructorsById[instructor.InstructorId] = instructor;
                }
            }

            if (instructors.Count == 0)
            {
                return instructors;
            }

            const string groupsSql = @"
                SELECT
                    ig.InstructorId,
                    g.Name
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                WHERE ig.IsActive = 1
                ORDER BY ig.InstructorId, g.Name;";

            using (SqlCommand groupsCmd = new SqlCommand(groupsSql, con))
            {
                using SqlDataReader reader = groupsCmd.ExecuteReader();
                while (reader.Read())
                {
                    int instructorId = reader.GetInt32(0);
                    if (!instructorsById.TryGetValue(instructorId, out ManagerInstructorRecord? instructor))
                    {
                        continue;
                    }

                    string groupName = GetNullableString(reader, 1).Trim();
                    if (!string.IsNullOrWhiteSpace(groupName))
                    {
                        instructor.GroupNames.Add(groupName);
                    }
                }
            }

            return instructors;
        }

        public bool PutUpdateInstructorNames(int instructorId, string firstName, string lastName)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                UPDATE dbo.Instructors
                SET
                    FirstName = @FirstName,
                    LastName = @LastName
                WHERE Id = @InstructorId
                  AND Role = 'Instructor'
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", lastName);

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteDeactivateInstructorForManager(int instructorId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            bool hasConversations = GetUserTableExists(con, tx, "Conversations");
            bool hasInstructorGroups = GetUserTableExists(con, tx, "InstructorGroups");
            bool hasPushTokens = GetUserTableExists(con, tx, "PushDeviceTokens");

            const string deactivateInstructorSql = @"
                UPDATE dbo.Instructors
                SET IsActive = 0
                WHERE Id = @InstructorId
                  AND Role = 'Instructor'
                  AND IsActive = 1;";

            int affectedRows;
            using (SqlCommand deactivateInstructorCmd = new SqlCommand(deactivateInstructorSql, con, tx))
            {
                deactivateInstructorCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                affectedRows = deactivateInstructorCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                tx.Rollback();
                return false;
            }

            if (hasInstructorGroups)
            {
                const string deactivateInstructorGroupsSql = @"
                    UPDATE dbo.InstructorGroups
                    SET IsActive = 0
                    WHERE InstructorId = @InstructorId
                      AND IsActive = 1;";

                using SqlCommand deactivateInstructorGroupsCmd = new SqlCommand(deactivateInstructorGroupsSql, con, tx);
                deactivateInstructorGroupsCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                deactivateInstructorGroupsCmd.ExecuteNonQuery();
            }

            if (hasConversations)
            {
                const string deactivateConversationsSql = @"
                    UPDATE dbo.Conversations
                    SET IsActive = 0
                    WHERE InstructorId = @InstructorId
                      AND IsActive = 1;";

                using SqlCommand deactivateConversationsCmd = new SqlCommand(deactivateConversationsSql, con, tx);
                deactivateConversationsCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                deactivateConversationsCmd.ExecuteNonQuery();
            }

            if (hasPushTokens)
            {
                const string deactivateTokensSql = @"
                    UPDATE dbo.PushDeviceTokens
                    SET IsActive = 0
                    WHERE UserType = 'Instructor'
                      AND UserId = @InstructorId
                      AND IsActive = 1;";

                using SqlCommand deactivateTokensCmd = new SqlCommand(deactivateTokensSql, con, tx);
                deactivateTokensCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                deactivateTokensCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        public bool GetInstructorExists(int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Instructors
                WHERE Id = @InstructorId
                  AND Role = 'Instructor'
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            int count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        public bool GetChildAssignedToInstructor(int instructorId, int childId, int? groupId = null)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                INNER JOIN dbo.GroupChildren gc
                    ON gc.GroupId = ig.GroupId
                   AND gc.IsActive = 1
                INNER JOIN dbo.Children c
                    ON c.Id = gc.ChildId
                   AND c.IsActive = 1
                WHERE ig.InstructorId = @InstructorId
                  AND gc.ChildId = @ChildId
                  AND ig.IsActive = 1
                  AND (@GroupId IS NULL OR ig.GroupId = @GroupId);";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId.HasValue ? groupId.Value : DBNull.Value;

            int count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        public List<InstructorGroupRecord> GetGroupsForInstructor(int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    g.Id,
                    g.Name,
                    g.Description,
                    COUNT(DISTINCT c.Id) AS ActiveChildrenCount
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                LEFT JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                LEFT JOIN dbo.Children c
                    ON c.Id = gc.ChildId
                   AND c.IsActive = 1
                WHERE ig.InstructorId = @InstructorId
                  AND ig.IsActive = 1
                GROUP BY g.Id, g.Name, g.Description
                ORDER BY g.Name, g.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            List<InstructorGroupRecord> groups = new List<InstructorGroupRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                groups.Add(new InstructorGroupRecord
                {
                    GroupId = reader.GetInt32(0),
                    Name = GetNullableString(reader, 1),
                    Description = GetNullableString(reader, 2),
                    ActiveChildrenCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                });
            }

            return groups;
        }

        public InstructorGroupRecord? GetInstructorGroupById(int instructorId, int groupId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    g.Id,
                    g.Name,
                    g.Description,
                    COUNT(DISTINCT c.Id) AS ActiveChildrenCount
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                LEFT JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                LEFT JOIN dbo.Children c
                    ON c.Id = gc.ChildId
                   AND c.IsActive = 1
                WHERE ig.InstructorId = @InstructorId
                  AND ig.GroupId = @GroupId
                  AND ig.IsActive = 1
                GROUP BY g.Id, g.Name, g.Description;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new InstructorGroupRecord
            {
                GroupId = reader.GetInt32(0),
                Name = GetNullableString(reader, 1),
                Description = GetNullableString(reader, 2),
                ActiveChildrenCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
            };
        }

        public int? GetAssignedInstructorIdForGroup(int groupId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT TOP 1 ig.InstructorId
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                WHERE ig.GroupId = @GroupId
                  AND ig.IsActive = 1
                ORDER BY ig.InstructorId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            object? scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(scalar);
        }

        public List<InstructorChildRecord> GetChildrenForInstructorGroup(int instructorId, int groupId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.ParentId,
                    p.Email,
                    p.FirstName,
                    p.LastName
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                INNER JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                INNER JOIN dbo.Children c
                    ON c.Id = gc.ChildId
                   AND c.IsActive = 1
                LEFT JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                WHERE ig.InstructorId = @InstructorId
                  AND ig.GroupId = @GroupId
                  AND ig.IsActive = 1
                ORDER BY c.FirstName, c.LastName, c.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            List<InstructorChildRecord> children = new List<InstructorChildRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 1);
                string lastName = GetNullableString(reader, 2);
                string parentFirstName = GetNullableString(reader, 5);
                string parentLastName = GetNullableString(reader, 6);

                children.Add(new InstructorChildRecord
                {
                    ChildId = reader.GetInt32(0),
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    ParentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    ParentEmail = GetNullableString(reader, 4),
                    ParentFullName = $"{parentFirstName} {parentLastName}".Trim()
                });
            }

            return children;
        }

        public List<InstructorGroupWithChildrenRecord> GetChildrenByGroupsForInstructor(int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string groupsSql = @"
                SELECT DISTINCT
                    g.Id,
                    g.Name,
                    g.Description
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                WHERE ig.InstructorId = @InstructorId
                  AND ig.IsActive = 1
                ORDER BY g.Name, g.Id;";

            List<InstructorGroupWithChildrenRecord> groups = new List<InstructorGroupWithChildrenRecord>();
            Dictionary<int, InstructorGroupWithChildrenRecord> groupsById = new Dictionary<int, InstructorGroupWithChildrenRecord>();

            using (SqlCommand groupsCmd = new SqlCommand(groupsSql, con))
            {
                groupsCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                using SqlDataReader reader = groupsCmd.ExecuteReader();
                while (reader.Read())
                {
                    InstructorGroupWithChildrenRecord group = new InstructorGroupWithChildrenRecord
                    {
                        GroupId = reader.GetInt32(0),
                        GroupName = GetNullableString(reader, 1),
                        GroupDescription = GetNullableString(reader, 2),
                        Children = new List<InstructorChildRecord>()
                    };

                    groups.Add(group);
                    groupsById[group.GroupId] = group;
                }
            }

            if (groups.Count == 0)
            {
                return groups;
            }

            const string childrenSql = @"
                SELECT
                    g.Id,
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.ParentId,
                    p.Email,
                    p.FirstName,
                    p.LastName
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                   AND i.Role = 'Instructor'
                INNER JOIN dbo.Groups g
                    ON g.Id = ig.GroupId
                   AND g.IsActive = 1
                INNER JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                INNER JOIN dbo.Children c
                    ON c.Id = gc.ChildId
                   AND c.IsActive = 1
                LEFT JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                WHERE ig.InstructorId = @InstructorId
                  AND ig.IsActive = 1
                ORDER BY g.Name, g.Id, c.FirstName, c.LastName, c.Id;";

            using (SqlCommand childrenCmd = new SqlCommand(childrenSql, con))
            {
                childrenCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                using SqlDataReader reader = childrenCmd.ExecuteReader();
                while (reader.Read())
                {
                    int groupId = reader.GetInt32(0);
                    if (!groupsById.TryGetValue(groupId, out InstructorGroupWithChildrenRecord? group))
                    {
                        continue;
                    }

                    string firstName = GetNullableString(reader, 2);
                    string lastName = GetNullableString(reader, 3);
                    string parentFirstName = GetNullableString(reader, 6);
                    string parentLastName = GetNullableString(reader, 7);

                    group.Children.Add(new InstructorChildRecord
                    {
                        ChildId = reader.GetInt32(1),
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = $"{firstName} {lastName}".Trim(),
                        ParentId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        ParentEmail = GetNullableString(reader, 5),
                        ParentFullName = $"{parentFirstName} {parentLastName}".Trim()
                    });
                }
            }

            return groups;
        }

        public ChildReportRecord PostCreateChildReport(int childId, int instructorId, int? groupId, DateTime reportDate, string notesJson)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                INSERT INTO dbo.ReportChildren (ChildId, InstructorId, GroupId, ReportDate, Notes)
                OUTPUT
                    INSERTED.ReportId,
                    INSERTED.ChildId,
                    INSERTED.InstructorId,
                    INSERTED.GroupId,
                    INSERTED.ReportDate,
                    INSERTED.Notes
                VALUES (@ChildId, @InstructorId, @GroupId, @ReportDate, @Notes);";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId.HasValue ? groupId.Value : DBNull.Value;
            cmd.Parameters.Add("@ReportDate", SqlDbType.Date).Value = reportDate.Date;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(notesJson)
                ? "{}"
                : notesJson;

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Failed to create child report record.");
            }

            return new ChildReportRecord
            {
                ReportId = reader.GetInt32(0),
                ChildId = reader.GetInt32(1),
                InstructorId = reader.GetInt32(2),
                GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ReportDate = reader.GetDateTime(4),
                Notes = GetNullableString(reader, 5)
            };
        }

        public ChildReportRecord? GetChildReportByDateForInstructor(int childId, int instructorId, DateTime reportDate)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT TOP (1)
                    ReportId,
                    ChildId,
                    InstructorId,
                    GroupId,
                    ReportDate,
                    Notes
                FROM dbo.ReportChildren
                WHERE ChildId = @ChildId
                  AND InstructorId = @InstructorId
                  AND ReportDate = @ReportDate
                ORDER BY ReportId DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@ReportDate", SqlDbType.Date).Value = reportDate.Date;

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new ChildReportRecord
            {
                ReportId = reader.GetInt32(0),
                ChildId = reader.GetInt32(1),
                InstructorId = reader.GetInt32(2),
                GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ReportDate = reader.GetDateTime(4),
                Notes = GetNullableString(reader, 5)
            };
        }

        public ChildReportRecord UpdateChildReportForInstructor(
            int reportId,
            int childId,
            int instructorId,
            int? groupId,
            DateTime reportDate,
            string notesJson)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                UPDATE dbo.ReportChildren
                   SET GroupId = @GroupId,
                       ReportDate = @ReportDate,
                       Notes = @Notes
                OUTPUT
                    INSERTED.ReportId,
                    INSERTED.ChildId,
                    INSERTED.InstructorId,
                    INSERTED.GroupId,
                    INSERTED.ReportDate,
                    INSERTED.Notes
                 WHERE ReportId = @ReportId
                   AND ChildId = @ChildId
                   AND InstructorId = @InstructorId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ReportId", reportId);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId.HasValue ? groupId.Value : DBNull.Value;
            cmd.Parameters.Add("@ReportDate", SqlDbType.Date).Value = reportDate.Date;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(notesJson)
                ? "{}"
                : notesJson;

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Failed to update child report record.");
            }

            return new ChildReportRecord
            {
                ReportId = reader.GetInt32(0),
                ChildId = reader.GetInt32(1),
                InstructorId = reader.GetInt32(2),
                GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ReportDate = reader.GetDateTime(4),
                Notes = GetNullableString(reader, 5)
            };
        }

        public List<ChildReportRecord> GetChildReportsForMetrics(int childId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    ReportId,
                    ChildId,
                    InstructorId,
                    GroupId,
                    ReportDate,
                    Notes
                FROM dbo.ReportChildren
                WHERE ChildId = @ChildId
                ORDER BY ReportDate ASC, ReportId ASC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ChildId", childId);

            List<ChildReportRecord> reports = new List<ChildReportRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                reports.Add(new ChildReportRecord
                {
                    ReportId = reader.GetInt32(0),
                    ChildId = reader.GetInt32(1),
                    InstructorId = reader.GetInt32(2),
                    GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    ReportDate = reader.GetDateTime(4),
                    Notes = GetNullableString(reader, 5)
                });
            }

            return reports;
        }

        public List<ChildReportRecord> GetChildReportsForChildren(IEnumerable<int> childIds)
        {
            List<int> normalizedChildIds = (childIds ?? Enumerable.Empty<int>())
                .Where((childId) => childId > 0)
                .Distinct()
                .ToList();

            if (normalizedChildIds.Count == 0)
            {
                return new List<ChildReportRecord>();
            }

            using SqlConnection con = OpenConnection();

            StringBuilder sqlBuilder = new StringBuilder(@"
                SELECT
                    ReportId,
                    ChildId,
                    InstructorId,
                    GroupId,
                    ReportDate,
                    Notes
                FROM dbo.ReportChildren
                WHERE ChildId IN (");

            for (int index = 0; index < normalizedChildIds.Count; index++)
            {
                if (index > 0)
                {
                    sqlBuilder.Append(", ");
                }

                sqlBuilder.Append($"@ChildId{index}");
            }

            sqlBuilder.Append(@")
                ORDER BY ChildId ASC, ReportDate ASC, ReportId ASC;");

            using SqlCommand cmd = new SqlCommand(sqlBuilder.ToString(), con);
            for (int index = 0; index < normalizedChildIds.Count; index++)
            {
                cmd.Parameters.Add($"@ChildId{index}", SqlDbType.Int).Value = normalizedChildIds[index];
            }

            List<ChildReportRecord> reports = new List<ChildReportRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                reports.Add(new ChildReportRecord
                {
                    ReportId = reader.GetInt32(0),
                    ChildId = reader.GetInt32(1),
                    InstructorId = reader.GetInt32(2),
                    GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    ReportDate = reader.GetDateTime(4),
                    Notes = GetNullableString(reader, 5)
                });
            }

            return reports;
        }

        public List<ChildRecord> GetChildren(bool includeInactive = false)
        {
            using SqlConnection con = OpenConnection();
            EnsureChildrenDescriptionColumnExists(con);

            const string sql = @"
                SELECT
                    c.Id,
                    c.ParentId,
                    c.FirstName,
                    c.LastName,
                    c.BirthDate,
                    c.IsActive,
                    c.CreatedAt,
                    p.Email AS ParentEmail,
                    p.FirstName AS ParentFirstName,
                    p.LastName AS ParentLastName,
                    c.[Description] AS ChildDescription
                FROM dbo.Children c
                LEFT JOIN dbo.Parents p ON p.Id = c.ParentId
                WHERE (@IncludeInactive = 1 OR c.IsActive = 1)
                ORDER BY c.Id DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive ? 1 : 0);

            List<ChildRecord> children = new List<ChildRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                children.Add(MapChild(reader));
            }

            return children;
        }

        public ChildRecord? GetChildById(int id)
        {
            using SqlConnection con = OpenConnection();
            EnsureChildrenDescriptionColumnExists(con);

            const string sql = @"
                SELECT
                    c.Id,
                    c.ParentId,
                    c.FirstName,
                    c.LastName,
                    c.BirthDate,
                    c.IsActive,
                    c.CreatedAt,
                    p.Email AS ParentEmail,
                    p.FirstName AS ParentFirstName,
                    p.LastName AS ParentLastName,
                    c.[Description] AS ChildDescription
                FROM dbo.Children c
                LEFT JOIN dbo.Parents p ON p.Id = c.ParentId
                WHERE c.Id = @Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", id);

            using SqlDataReader reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapChild(reader);
            }

            return null;
        }

        public bool GetParentExists(int parentId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Parents
                WHERE Id = @ParentId AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);

            int count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        public List<ParentEmailOption> GetActiveParentEmailOptions()
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    Id,
                    Email
                FROM dbo.Parents
                WHERE IsActive = 1
                ORDER BY Email;";

            using SqlCommand cmd = new SqlCommand(sql, con);

            List<ParentEmailOption> parents = new List<ParentEmailOption>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                parents.Add(new ParentEmailOption
                {
                    Id = reader.GetInt32(0),
                    Email = GetNullableString(reader, 1)
                });
            }

            return parents;
        }

        public List<GroupChildOptionRecord> GetChildrenForGroupManagement(bool includeInactive = false)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    p.Email,
                    p.FirstName,
                    p.LastName,
                    activeGroup.GroupId,
                    activeGroup.GroupName
                FROM dbo.Children c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                OUTER APPLY
                (
                    SELECT TOP 1
                        gc.GroupId,
                        g.Name AS GroupName
                    FROM dbo.GroupChildren gc
                    INNER JOIN dbo.Groups g ON g.Id = gc.GroupId
                    WHERE gc.ChildId = c.Id
                      AND gc.IsActive = 1
                    ORDER BY gc.Id DESC
                ) activeGroup
                WHERE (@IncludeInactive = 1 OR c.IsActive = 1)
                ORDER BY c.FirstName, c.LastName, c.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive ? 1 : 0);

            List<GroupChildOptionRecord> children = new List<GroupChildOptionRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 1);
                string lastName = GetNullableString(reader, 2);
                string parentFirstName = GetNullableString(reader, 4);
                string parentLastName = GetNullableString(reader, 5);

                children.Add(new GroupChildOptionRecord
                {
                    ChildId = reader.GetInt32(0),
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    ParentEmail = GetNullableString(reader, 3),
                    ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                    ActiveGroupId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    ActiveGroupName = GetNullableString(reader, 7)
                });
            }

            return children;
        }

        public List<GroupInstructorOptionRecord> GetInstructorsForGroupManagement()
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    Id,
                    FirstName,
                    LastName,
                    Email
                FROM dbo.Instructors
                WHERE IsActive = 1
                  AND Role = 'Instructor'
                ORDER BY FirstName, LastName, Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);

            List<GroupInstructorOptionRecord> instructors = new List<GroupInstructorOptionRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 1);
                string lastName = GetNullableString(reader, 2);

                instructors.Add(new GroupInstructorOptionRecord
                {
                    InstructorId = reader.GetInt32(0),
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    Email = GetNullableString(reader, 3)
                });
            }

            return instructors;
        }

        public List<GroupSummaryRecord> GetGroupsForManagement(bool includeInactive = false)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsActive,
                    ig.InstructorId,
                    i.FirstName,
                    i.LastName,
                    i.Email,
                    COUNT(DISTINCT gc.ChildId) AS ActiveChildrenCount
                FROM dbo.Groups g
                LEFT JOIN dbo.InstructorGroups ig
                    ON ig.GroupId = g.Id
                   AND ig.IsActive = 1
                LEFT JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                LEFT JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                WHERE (@IncludeInactive = 1 OR g.IsActive = 1)
                GROUP BY
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsActive,
                    ig.InstructorId,
                    i.FirstName,
                    i.LastName,
                    i.Email
                ORDER BY g.Name, g.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive ? 1 : 0);

            List<GroupSummaryRecord> groups = new List<GroupSummaryRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 5);
                string lastName = GetNullableString(reader, 6);

                groups.Add(new GroupSummaryRecord
                {
                    GroupId = reader.GetInt32(0),
                    Name = GetNullableString(reader, 1),
                    Description = GetNullableString(reader, 2),
                    IsActive = reader.GetBoolean(3),
                    InstructorId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    InstructorFullName = $"{firstName} {lastName}".Trim(),
                    InstructorEmail = GetNullableString(reader, 7),
                    ActiveChildrenCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                });
            }

            return groups;
        }

        public GroupSummaryRecord PostCreateGroupWithInstructor(string groupName, string? description, int instructorId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            string normalizedGroupName = groupName.Trim();
            object normalizedDescription = string.IsNullOrWhiteSpace(description)
                ? DBNull.Value
                : description.Trim();

            EnsureInstructorCanTeach(con, tx, instructorId);

            const string existingGroupSql = @"
                SELECT TOP 1 Id, IsActive
                FROM dbo.Groups
                WHERE Name = @Name;";

            int existingGroupId = 0;
            bool existingGroupIsActive = false;
            using (SqlCommand existingGroupCmd = new SqlCommand(existingGroupSql, con, tx))
            {
                existingGroupCmd.Parameters.AddWithValue("@Name", normalizedGroupName);
                using SqlDataReader existingReader = existingGroupCmd.ExecuteReader();
                if (existingReader.Read())
                {
                    existingGroupId = existingReader.GetInt32(0);
                    existingGroupIsActive = existingReader.GetBoolean(1);
                }
            }

            if (existingGroupId > 0)
            {
                if (existingGroupIsActive)
                {
                    throw new InvalidOperationException("A group with this name already exists.");
                }

                const string reactivateGroupSql = @"
                    UPDATE dbo.Groups
                    SET
                        Description = @Description,
                        IsActive = 1
                    WHERE Id = @GroupId
                      AND IsActive = 0;";

                int affectedRows;
                using (SqlCommand reactivateGroupCmd = new SqlCommand(reactivateGroupSql, con, tx))
                {
                    reactivateGroupCmd.Parameters.AddWithValue("@GroupId", existingGroupId);
                    reactivateGroupCmd.Parameters.Add("@Description", SqlDbType.NVarChar).Value = normalizedDescription;
                    affectedRows = reactivateGroupCmd.ExecuteNonQuery();
                }

                if (affectedRows == 0)
                {
                    throw new InvalidOperationException("Group exists but could not be reactivated.");
                }

                UpsertActiveInstructorGroup(con, tx, existingGroupId, instructorId);
                tx.Commit();

                GroupSummaryRecord? reactivatedGroup = GetGroupByIdForManagement(existingGroupId);
                if (reactivatedGroup == null)
                {
                    throw new InvalidOperationException("Group was reactivated but could not be loaded.");
                }

                return reactivatedGroup;
            }

            const string insertGroupSql = @"
                INSERT INTO dbo.Groups (Name, Description, IsActive, CreatedAt)
                VALUES (@Name, @Description, 1, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            int groupId;
            using (SqlCommand insertGroupCmd = new SqlCommand(insertGroupSql, con, tx))
            {
                insertGroupCmd.Parameters.AddWithValue("@Name", normalizedGroupName);
                insertGroupCmd.Parameters.Add("@Description", SqlDbType.NVarChar).Value = normalizedDescription;

                try
                {
                    groupId = Convert.ToInt32(insertGroupCmd.ExecuteScalar());
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    throw new InvalidOperationException("A group with this name already exists.");
                }
            }

            UpsertActiveInstructorGroup(con, tx, groupId, instructorId);

            tx.Commit();

            GroupSummaryRecord? createdGroup = GetGroupByIdForManagement(groupId);
            if (createdGroup == null)
            {
                throw new InvalidOperationException("Group was created but could not be loaded.");
            }

            return createdGroup;
        }

        public GroupSummaryRecord PutSetGroupInstructor(int groupId, int instructorId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            if (!GetGroupExistsAndActive(con, tx, groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            EnsureInstructorCanTeach(con, tx, instructorId);
            UpsertActiveInstructorGroup(con, tx, groupId, instructorId);

            tx.Commit();

            GroupSummaryRecord? updatedGroup = GetGroupByIdForManagement(groupId);
            if (updatedGroup == null)
            {
                throw new InvalidOperationException("Group was updated but could not be loaded.");
            }

            return updatedGroup;
        }

        public GroupSummaryRecord PutUpdateGroupForManagement(int groupId, string groupName, string? description, int instructorId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            if (!GetGroupExistsAndActive(con, tx, groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            EnsureInstructorCanTeach(con, tx, instructorId);

            const string updateGroupSql = @"
                UPDATE dbo.Groups
                SET
                    Name = @Name,
                    Description = @Description
                WHERE Id = @GroupId
                  AND IsActive = 1;";

            int affectedRows;
            using (SqlCommand updateGroupCmd = new SqlCommand(updateGroupSql, con, tx))
            {
                updateGroupCmd.Parameters.AddWithValue("@GroupId", groupId);
                updateGroupCmd.Parameters.AddWithValue("@Name", groupName.Trim());
                updateGroupCmd.Parameters.Add("@Description", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(description)
                    ? DBNull.Value
                    : description.Trim();
                affectedRows = updateGroupCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("Group was not found or is inactive.");
            }

            UpsertActiveInstructorGroup(con, tx, groupId, instructorId);

            tx.Commit();

            GroupSummaryRecord? updatedGroup = GetGroupByIdForManagement(groupId);
            if (updatedGroup == null)
            {
                throw new InvalidOperationException("Group was updated but could not be loaded.");
            }

            return updatedGroup;
        }

        public bool DeleteDeactivateGroupForManagement(int groupId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            const string deactivateGroupSql = @"
                UPDATE dbo.Groups
                SET IsActive = 0
                WHERE Id = @GroupId
                  AND IsActive = 1;";

            int affectedRows;
            using (SqlCommand deactivateGroupCmd = new SqlCommand(deactivateGroupSql, con, tx))
            {
                deactivateGroupCmd.Parameters.AddWithValue("@GroupId", groupId);
                affectedRows = deactivateGroupCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                tx.Rollback();
                return false;
            }

            const string deactivateInstructorGroupsSql = @"
                UPDATE dbo.InstructorGroups
                SET IsActive = 0
                WHERE GroupId = @GroupId
                  AND IsActive = 1;";

            using (SqlCommand deactivateInstructorGroupsCmd = new SqlCommand(deactivateInstructorGroupsSql, con, tx))
            {
                deactivateInstructorGroupsCmd.Parameters.AddWithValue("@GroupId", groupId);
                deactivateInstructorGroupsCmd.ExecuteNonQuery();
            }

            const string deactivateGroupChildrenSql = @"
                UPDATE dbo.GroupChildren
                SET IsActive = 0
                WHERE GroupId = @GroupId
                  AND IsActive = 1;";

            using (SqlCommand deactivateGroupChildrenCmd = new SqlCommand(deactivateGroupChildrenSql, con, tx))
            {
                deactivateGroupChildrenCmd.Parameters.AddWithValue("@GroupId", groupId);
                deactivateGroupChildrenCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        public GroupChildOptionRecord PutAssignChildToGroup(int childId, int groupId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            if (!GetGroupExistsAndActive(con, tx, groupId))
            {
                throw new InvalidOperationException("Group does not exist or is inactive.");
            }

            if (!GetChildExistsAndActive(con, tx, childId))
            {
                throw new InvalidOperationException("Child does not exist or is inactive.");
            }

            EnsureGroupHasSingleActiveInstructor(con, tx, groupId);

            const string deactivateOtherGroupsSql = @"
                UPDATE dbo.GroupChildren
                SET IsActive = 0
                WHERE ChildId = @ChildId
                  AND GroupId <> @GroupId
                  AND IsActive = 1;";

            using (SqlCommand deactivateOtherCmd = new SqlCommand(deactivateOtherGroupsSql, con, tx))
            {
                deactivateOtherCmd.Parameters.AddWithValue("@ChildId", childId);
                deactivateOtherCmd.Parameters.AddWithValue("@GroupId", groupId);
                deactivateOtherCmd.ExecuteNonQuery();
            }

            const string activateExistingSql = @"
                UPDATE dbo.GroupChildren
                SET IsActive = 1
                WHERE ChildId = @ChildId
                  AND GroupId = @GroupId;";

            int affectedRows;
            using (SqlCommand activateExistingCmd = new SqlCommand(activateExistingSql, con, tx))
            {
                activateExistingCmd.Parameters.AddWithValue("@ChildId", childId);
                activateExistingCmd.Parameters.AddWithValue("@GroupId", groupId);
                affectedRows = activateExistingCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                const string insertSql = @"
                    INSERT INTO dbo.GroupChildren (GroupId, ChildId, IsActive, CreatedAt)
                    VALUES (@GroupId, @ChildId, 1, SYSUTCDATETIME());";

                using SqlCommand insertCmd = new SqlCommand(insertSql, con, tx);
                insertCmd.Parameters.AddWithValue("@GroupId", groupId);
                insertCmd.Parameters.AddWithValue("@ChildId", childId);
                insertCmd.ExecuteNonQuery();
            }

            tx.Commit();

            GroupChildOptionRecord? assignedChild = GetGroupChildOptionByChildId(childId);
            if (assignedChild == null)
            {
                throw new InvalidOperationException("Child assignment was saved but could not be loaded.");
            }

            return assignedChild;
        }

        public GroupChildOptionRecord PutUnassignChildFromGroup(int childId)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            if (!GetChildExistsAndActive(con, tx, childId))
            {
                throw new InvalidOperationException("Child does not exist or is inactive.");
            }

            const string deactivateGroupsSql = @"
                UPDATE dbo.GroupChildren
                SET IsActive = 0
                WHERE ChildId = @ChildId
                  AND IsActive = 1;";

            using (SqlCommand deactivateCmd = new SqlCommand(deactivateGroupsSql, con, tx))
            {
                deactivateCmd.Parameters.AddWithValue("@ChildId", childId);
                deactivateCmd.ExecuteNonQuery();
            }

            tx.Commit();

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

            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsActive,
                    ig.InstructorId,
                    i.FirstName,
                    i.LastName,
                    i.Email,
                    COUNT(DISTINCT gc.ChildId) AS ActiveChildrenCount
                FROM dbo.Groups g
                LEFT JOIN dbo.InstructorGroups ig
                    ON ig.GroupId = g.Id
                   AND ig.IsActive = 1
                LEFT JOIN dbo.Instructors i
                    ON i.Id = ig.InstructorId
                   AND i.IsActive = 1
                LEFT JOIN dbo.GroupChildren gc
                    ON gc.GroupId = g.Id
                   AND gc.IsActive = 1
                WHERE g.Id = @GroupId
                GROUP BY
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsActive,
                    ig.InstructorId,
                    i.FirstName,
                    i.LastName,
                    i.Email;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            string firstName = GetNullableString(reader, 5);
            string lastName = GetNullableString(reader, 6);

            return new GroupSummaryRecord
            {
                GroupId = reader.GetInt32(0),
                Name = GetNullableString(reader, 1),
                Description = GetNullableString(reader, 2),
                IsActive = reader.GetBoolean(3),
                InstructorId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                InstructorFullName = $"{firstName} {lastName}".Trim(),
                InstructorEmail = GetNullableString(reader, 7),
                ActiveChildrenCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
            };
        }

        public GroupChildOptionRecord? GetGroupChildOptionByChildId(int childId)
        {
            if (childId <= 0)
            {
                return null;
            }

            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    p.Email,
                    p.FirstName,
                    p.LastName,
                    activeGroup.GroupId,
                    activeGroup.GroupName
                FROM dbo.Children c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                OUTER APPLY
                (
                    SELECT TOP 1
                        gc.GroupId,
                        g.Name AS GroupName
                    FROM dbo.GroupChildren gc
                    INNER JOIN dbo.Groups g ON g.Id = gc.GroupId
                    WHERE gc.ChildId = c.Id
                      AND gc.IsActive = 1
                    ORDER BY gc.Id DESC
                ) activeGroup
                WHERE c.Id = @ChildId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ChildId", childId);

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            string firstName = GetNullableString(reader, 1);
            string lastName = GetNullableString(reader, 2);
            string parentFirstName = GetNullableString(reader, 4);
            string parentLastName = GetNullableString(reader, 5);

            return new GroupChildOptionRecord
            {
                ChildId = reader.GetInt32(0),
                FirstName = firstName,
                LastName = lastName,
                FullName = $"{firstName} {lastName}".Trim(),
                ParentEmail = GetNullableString(reader, 3),
                ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                ActiveGroupId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ActiveGroupName = GetNullableString(reader, 7)
            };
        }

        public ManagerCenterSettingsRecord GetManagerCenterSettings()
        {
            using SqlConnection con = OpenConnection();
            EnsureCenterSettingsTableExists(con);

            const string selectSql = @"
                SELECT TOP 1
                    Id,
                    CenterName,
                    CenterAddress,
                    SendAutoReports,
                    ReceiveAlerts,
                    ShowKidsAdvanced,
                    UpdatedAt
                FROM dbo.CenterSettings
                ORDER BY Id;";

            using SqlCommand selectCmd = new SqlCommand(selectSql, con);
            using (SqlDataReader reader = selectCmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new ManagerCenterSettingsRecord
                    {
                        CenterName = GetNullableString(reader, 1),
                        CenterAddress = GetNullableString(reader, 2),
                        SendAutoReports = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        ReceiveAlerts = !reader.IsDBNull(4) && reader.GetBoolean(4),
                        ShowKidsAdvanced = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        UpdatedAtUtc = reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6)
                    };
                }
            }

            DateTime updatedAtUtc = DateTime.UtcNow;

            const string insertSql = @"
                INSERT INTO dbo.CenterSettings
                    (Id, CenterName, CenterAddress, SendAutoReports, ReceiveAlerts, ShowKidsAdvanced, UpdatedAt)
                VALUES
                    (1, N'', N'', 1, 1, 0, @UpdatedAt);";

            using (SqlCommand insertCmd = new SqlCommand(insertSql, con))
            {
                insertCmd.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = updatedAtUtc;
                insertCmd.ExecuteNonQuery();
            }

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
            using SqlConnection con = OpenConnection();
            EnsureCenterSettingsTableExists(con);

            string normalizedCenterName = (centerName ?? string.Empty).Trim();
            string normalizedCenterAddress = (centerAddress ?? string.Empty).Trim();
            DateTime updatedAtUtc = DateTime.UtcNow;

            const string updateSql = @"
                UPDATE dbo.CenterSettings
                SET
                    CenterName = @CenterName,
                    CenterAddress = @CenterAddress,
                    SendAutoReports = @SendAutoReports,
                    ReceiveAlerts = @ReceiveAlerts,
                    ShowKidsAdvanced = @ShowKidsAdvanced,
                    UpdatedAt = @UpdatedAt
                WHERE Id = 1;";

            int affectedRows;
            using (SqlCommand updateCmd = new SqlCommand(updateSql, con))
            {
                updateCmd.Parameters.AddWithValue("@CenterName", normalizedCenterName);
                updateCmd.Parameters.AddWithValue("@CenterAddress", normalizedCenterAddress);
                updateCmd.Parameters.AddWithValue("@SendAutoReports", sendAutoReports);
                updateCmd.Parameters.AddWithValue("@ReceiveAlerts", receiveAlerts);
                updateCmd.Parameters.AddWithValue("@ShowKidsAdvanced", showKidsAdvanced);
                updateCmd.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = updatedAtUtc;
                affectedRows = updateCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                const string insertSql = @"
                    INSERT INTO dbo.CenterSettings
                        (Id, CenterName, CenterAddress, SendAutoReports, ReceiveAlerts, ShowKidsAdvanced, UpdatedAt)
                    VALUES
                        (1, @CenterName, @CenterAddress, @SendAutoReports, @ReceiveAlerts, @ShowKidsAdvanced, @UpdatedAt);";

                using SqlCommand insertCmd = new SqlCommand(insertSql, con);
                insertCmd.Parameters.AddWithValue("@CenterName", normalizedCenterName);
                insertCmd.Parameters.AddWithValue("@CenterAddress", normalizedCenterAddress);
                insertCmd.Parameters.AddWithValue("@SendAutoReports", sendAutoReports);
                insertCmd.Parameters.AddWithValue("@ReceiveAlerts", receiveAlerts);
                insertCmd.Parameters.AddWithValue("@ShowKidsAdvanced", showKidsAdvanced);
                insertCmd.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = updatedAtUtc;
                insertCmd.ExecuteNonQuery();
            }

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

            using SqlConnection con = OpenConnection();

            bool hasGroupTrainingSessions = GetGroupTrainingSessionsTableExists(con);
            string sql = hasGroupTrainingSessions
                ? @"
                    SELECT
                        ts.SessionId,
                        CAST('Private' AS NVARCHAR(20)) AS LessonType,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status,
                        c.Id AS ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        ts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        CAST(NULL AS INT) AS GroupId,
                        CAST(NULL AS NVARCHAR(200)) AS GroupName
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                    INNER JOIN dbo.Instructors i
                        ON i.Id = ts.InstructorId
                    WHERE ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate

                    UNION ALL

                    SELECT
                        gts.SessionId,
                        CAST('Group' AS NVARCHAR(20)) AS LessonType,
                        gts.MeetingDate,
                        gts.StartTime,
                        gts.EndTime,
                        gts.Status,
                        CAST(NULL AS INT) AS ChildId,
                        CAST(NULL AS NVARCHAR(200)) AS ChildFullName,
                        gts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        g.Id AS GroupId,
                        g.Name AS GroupName
                    FROM dbo.GroupTrainingSessions gts
                    INNER JOIN dbo.Groups g
                        ON g.Id = gts.GroupId
                    INNER JOIN dbo.Instructors i
                        ON i.Id = gts.InstructorId
                    WHERE gts.MeetingDate >= @StartDate
                      AND gts.MeetingDate <= @EndDate
                    ORDER BY MeetingDate DESC, StartTime DESC, SessionId DESC;"
                : @"
                    SELECT
                        ts.SessionId,
                        CAST('Private' AS NVARCHAR(20)) AS LessonType,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status,
                        c.Id AS ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        ts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        CAST(NULL AS INT) AS GroupId,
                        CAST(NULL AS NVARCHAR(200)) AS GroupName
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                    INNER JOIN dbo.Instructors i
                        ON i.Id = ts.InstructorId
                    WHERE ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate
                    ORDER BY ts.MeetingDate DESC, ts.StartTime DESC, ts.SessionId DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = normalizedStartDate;
            cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = normalizedEndDate;

            List<ManagerAttendanceReportRowRecord> rows = new List<ManagerAttendanceReportRowRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string status = GetNullableString(reader, 5);

                rows.Add(new ManagerAttendanceReportRowRecord
                {
                    SessionId = reader.GetInt32(0),
                    LessonType = GetNullableString(reader, 1),
                    MeetingDate = reader.GetDateTime(2),
                    StartTime = reader.GetTimeSpan(3),
                    EndTime = reader.GetTimeSpan(4),
                    Status = status,
                    AttendanceCategory = MapAttendanceCategory(status),
                    ChildId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    ChildFullName = GetNullableString(reader, 7),
                    InstructorId = reader.GetInt32(8),
                    InstructorFullName = GetNullableString(reader, 9),
                    GroupId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    GroupName = GetNullableString(reader, 11)
                });
            }

            return rows;
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

            using SqlConnection con = OpenConnection();

            int ExecuteCount(string sql, bool includeDateRange = false)
            {
                using SqlCommand cmd = new SqlCommand(sql, con);
                if (includeDateRange)
                {
                    cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = normalizedStartDate;
                    cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = normalizedEndDate;
                }

                return Convert.ToInt32(cmd.ExecuteScalar());
            }

            int activeGroupsCount = ExecuteCount("SELECT COUNT(1) FROM dbo.Groups WHERE IsActive = 1;");
            int activeInstructorsCount = ExecuteCount("SELECT COUNT(1) FROM dbo.Instructors WHERE IsActive = 1 AND Role = 'Instructor';");
            int activeChildrenCount = ExecuteCount("SELECT COUNT(1) FROM dbo.Children WHERE IsActive = 1;");
            int reportsCount = ExecuteCount(@"
                SELECT COUNT(1)
                FROM dbo.ReportChildren
                WHERE ReportDate >= @StartDate
                  AND ReportDate <= @EndDate;", true);

            int childrenWithReportsCount = ExecuteCount(@"
                SELECT COUNT(DISTINCT ChildId)
                FROM dbo.ReportChildren
                WHERE ReportDate >= @StartDate
                  AND ReportDate <= @EndDate;", true);

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
                    using SqlConnection con = OpenConnection();

                    const string activeChildrenSql = @"
                        SELECT COUNT(1)
                        FROM dbo.Children
                        WHERE IsActive = 1;";

                    const string childrenWithReportsSql = @"
                        SELECT COUNT(DISTINCT ChildId)
                        FROM dbo.ReportChildren
                        WHERE ReportDate >= @StartDate
                          AND ReportDate <= @EndDate;";

                    int activeChildrenCount;
                    int childrenWithReportsCount;

                    using (SqlCommand activeChildrenCmd = new SqlCommand(activeChildrenSql, con))
                    {
                        activeChildrenCount = Convert.ToInt32(activeChildrenCmd.ExecuteScalar());
                    }

                    using (SqlCommand childrenWithReportsCmd = new SqlCommand(childrenWithReportsSql, con))
                    {
                        childrenWithReportsCmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = normalizedStartDate;
                        childrenWithReportsCmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = normalizedEndDate;
                        childrenWithReportsCount = Convert.ToInt32(childrenWithReportsCmd.ExecuteScalar());
                    }

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
                    using SqlConnection con = OpenConnection();

                    const string activeInstructorsSql = @"
                        SELECT COUNT(1)
                        FROM dbo.Instructors
                        WHERE IsActive = 1
                          AND Role = 'Instructor';";

                    const string instructorsWithReportsSql = @"
                        SELECT COUNT(DISTINCT InstructorId)
                        FROM dbo.ReportChildren
                        WHERE ReportDate >= @StartDate
                          AND ReportDate <= @EndDate;";

                    int activeInstructorsCount;
                    int instructorsWithReportsCount;

                    using (SqlCommand activeInstructorsCmd = new SqlCommand(activeInstructorsSql, con))
                    {
                        activeInstructorsCount = Convert.ToInt32(activeInstructorsCmd.ExecuteScalar());
                    }

                    using (SqlCommand instructorsWithReportsCmd = new SqlCommand(instructorsWithReportsSql, con))
                    {
                        instructorsWithReportsCmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = normalizedStartDate;
                        instructorsWithReportsCmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = normalizedEndDate;
                        instructorsWithReportsCount = Convert.ToInt32(instructorsWithReportsCmd.ExecuteScalar());
                    }

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
                    using SqlConnection con = OpenConnection();

                    const string activeGroupsSql = @"
                        SELECT COUNT(1)
                        FROM dbo.Groups
                        WHERE IsActive = 1;";

                    const string groupsWithInstructorSql = @"
                        SELECT COUNT(DISTINCT g.Id)
                        FROM dbo.Groups g
                        INNER JOIN dbo.InstructorGroups ig
                            ON ig.GroupId = g.Id
                           AND ig.IsActive = 1
                        WHERE g.IsActive = 1;";

                    const string groupsWithChildrenSql = @"
                        SELECT COUNT(DISTINCT g.Id)
                        FROM dbo.Groups g
                        INNER JOIN dbo.GroupChildren gc
                            ON gc.GroupId = g.Id
                           AND gc.IsActive = 1
                        WHERE g.IsActive = 1;";

                    int activeGroupsCount;
                    int groupsWithInstructorCount;
                    int groupsWithChildrenCount;

                    using (SqlCommand activeGroupsCmd = new SqlCommand(activeGroupsSql, con))
                    {
                        activeGroupsCount = Convert.ToInt32(activeGroupsCmd.ExecuteScalar());
                    }

                    using (SqlCommand groupsWithInstructorCmd = new SqlCommand(groupsWithInstructorSql, con))
                    {
                        groupsWithInstructorCount = Convert.ToInt32(groupsWithInstructorCmd.ExecuteScalar());
                    }

                    using (SqlCommand groupsWithChildrenCmd = new SqlCommand(groupsWithChildrenSql, con))
                    {
                        groupsWithChildrenCount = Convert.ToInt32(groupsWithChildrenCmd.ExecuteScalar());
                    }

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

        public int PostCreateChild(int parentId, string firstName, string lastName, DateTime? birthDate, string? childDescription)
        {
            using SqlConnection con = OpenConnection();
            EnsureChildrenDescriptionColumnExists(con);

            using SqlTransaction tx = con.BeginTransaction();

            string normalizedFirstName = firstName.Trim();
            string normalizedLastName = lastName.Trim();
            object normalizedBirthDate = birthDate.HasValue
                ? birthDate.Value.Date
                : DBNull.Value;
            object normalizedDescription = string.IsNullOrWhiteSpace(childDescription)
                ? DBNull.Value
                : childDescription.Trim();

            const string existingChildSql = @"
                SELECT TOP 1 Id, IsActive
                FROM dbo.Children
                WHERE ParentId = @ParentId
                  AND LOWER(LTRIM(RTRIM(FirstName))) = LOWER(@FirstName)
                  AND LOWER(LTRIM(RTRIM(LastName))) = LOWER(@LastName)
                ORDER BY IsActive DESC, Id DESC;";

            int existingChildId = 0;
            bool existingChildIsActive = false;

            using (SqlCommand existingChildCmd = new SqlCommand(existingChildSql, con, tx))
            {
                existingChildCmd.Parameters.AddWithValue("@ParentId", parentId);
                existingChildCmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                existingChildCmd.Parameters.AddWithValue("@LastName", normalizedLastName);

                using SqlDataReader reader = existingChildCmd.ExecuteReader();
                if (reader.Read())
                {
                    existingChildId = reader.GetInt32(0);
                    existingChildIsActive = reader.GetBoolean(1);
                }
            }

            if (existingChildId > 0)
            {
                if (existingChildIsActive)
                {
                    throw new InvalidOperationException("Child already exists for this parent.");
                }

                const string reactivateChildSql = @"
                    UPDATE dbo.Children
                    SET
                        FirstName = @FirstName,
                        LastName = @LastName,
                        BirthDate = @BirthDate,
                        [Description] = @ChildDescription,
                        IsActive = 1
                    WHERE Id = @ChildId
                      AND IsActive = 0;";

                int affectedRows;
                using (SqlCommand reactivateChildCmd = new SqlCommand(reactivateChildSql, con, tx))
                {
                    reactivateChildCmd.Parameters.AddWithValue("@ChildId", existingChildId);
                    reactivateChildCmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
                    reactivateChildCmd.Parameters.AddWithValue("@LastName", normalizedLastName);
                    reactivateChildCmd.Parameters.Add("@BirthDate", SqlDbType.Date).Value = normalizedBirthDate;
                    reactivateChildCmd.Parameters.Add("@ChildDescription", SqlDbType.NVarChar, -1).Value = normalizedDescription;
                    affectedRows = reactivateChildCmd.ExecuteNonQuery();
                }

                if (affectedRows == 0)
                {
                    throw new InvalidOperationException("Child exists but could not be reactivated.");
                }

                tx.Commit();
                return existingChildId;
            }

            const string sql = @"
                INSERT INTO dbo.Children (ParentId, FirstName, LastName, BirthDate, [Description], IsActive, CreatedAt)
                VALUES (@ParentId, @FirstName, @LastName, @BirthDate, @ChildDescription, 1, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@FirstName", normalizedFirstName);
            cmd.Parameters.AddWithValue("@LastName", normalizedLastName);
            cmd.Parameters.Add("@BirthDate", SqlDbType.Date).Value = normalizedBirthDate;
            cmd.Parameters.Add("@ChildDescription", SqlDbType.NVarChar, -1).Value = normalizedDescription;

            int newChildId = Convert.ToInt32(cmd.ExecuteScalar());
            tx.Commit();
            return newChildId;
        }

        public bool PutUpdateChild(int id, int parentId, string firstName, string lastName, DateTime? birthDate, string? childDescription, bool isActive)
        {
            using SqlConnection con = OpenConnection();
            EnsureChildrenDescriptionColumnExists(con);

            const string sql = @"
                UPDATE dbo.Children
                SET
                    ParentId = @ParentId,
                    FirstName = @FirstName,
                    LastName = @LastName,
                    BirthDate = @BirthDate,
                    [Description] = @ChildDescription,
                    IsActive = @IsActive
                WHERE Id = @Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", lastName);
            cmd.Parameters.Add("@BirthDate", SqlDbType.Date).Value = birthDate.HasValue
                ? birthDate.Value.Date
                : DBNull.Value;
            cmd.Parameters.Add("@ChildDescription", SqlDbType.NVarChar, -1).Value = string.IsNullOrWhiteSpace(childDescription)
                ? DBNull.Value
                : childDescription.Trim();
            cmd.Parameters.AddWithValue("@IsActive", isActive);

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteDeactivateChild(int id)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            bool hasConversations = GetUserTableExists(con, tx, "Conversations");
            bool hasGroupChildren = GetUserTableExists(con, tx, "GroupChildren");

            const string deactivateChildSql = @"
                UPDATE dbo.Children
                SET IsActive = 0
                WHERE Id = @ChildId
                  AND IsActive = 1;";

            int affectedRows;
            using (SqlCommand deactivateChildCmd = new SqlCommand(deactivateChildSql, con, tx))
            {
                deactivateChildCmd.Parameters.AddWithValue("@ChildId", id);
                affectedRows = deactivateChildCmd.ExecuteNonQuery();
            }

            if (affectedRows == 0)
            {
                tx.Rollback();
                return false;
            }

            if (hasGroupChildren)
            {
                const string deactivateGroupChildrenSql = @"
                    UPDATE dbo.GroupChildren
                    SET IsActive = 0
                    WHERE ChildId = @ChildId
                      AND IsActive = 1;";

                using SqlCommand deactivateGroupChildrenCmd = new SqlCommand(deactivateGroupChildrenSql, con, tx);
                deactivateGroupChildrenCmd.Parameters.AddWithValue("@ChildId", id);
                deactivateGroupChildrenCmd.ExecuteNonQuery();
            }

            if (hasConversations)
            {
                const string deactivateConversationsSql = @"
                    UPDATE dbo.Conversations
                    SET IsActive = 0
                    WHERE ChildId = @ChildId
                      AND IsActive = 1;";

                using SqlCommand deactivateConversationsCmd = new SqlCommand(deactivateConversationsSql, con, tx);
                deactivateConversationsCmd.Parameters.AddWithValue("@ChildId", id);
                deactivateConversationsCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        public List<ChatChildRecord> GetParentChildrenForChat(int parentId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    COUNT(DISTINCT v.InstructorId) AS InstructorsCount
                FROM dbo.Children c
                LEFT JOIN dbo.vw_ParentChildInstructors v
                    ON v.ParentId = c.ParentId
                   AND v.ChildId = c.Id
                WHERE c.ParentId = @ParentId
                  AND c.IsActive = 1
                GROUP BY c.Id, c.FirstName, c.LastName
                ORDER BY c.FirstName, c.LastName, c.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);

            List<ChatChildRecord> children = new List<ChatChildRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 1);
                string lastName = GetNullableString(reader, 2);

                children.Add(new ChatChildRecord
                {
                    Id = reader.GetInt32(0),
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    InstructorsCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                });
            }

            return children;
        }

        public bool GetChildBelongsToParent(int parentId, int childId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Children
                WHERE Id = @ChildId
                  AND ParentId = @ParentId
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@ParentId", parentId);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public List<ChatInstructorRecord> GetInstructorsForParentChild(int parentId, int childId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT DISTINCT
                    i.Id,
                    i.FirstName,
                    i.LastName,
                    i.Email
                FROM dbo.vw_ParentChildInstructors v
                INNER JOIN dbo.Instructors i ON i.Id = v.InstructorId
                WHERE v.ParentId = @ParentId
                  AND v.ChildId = @ChildId
                  AND i.IsActive = 1
                ORDER BY i.FirstName, i.LastName, i.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@ChildId", childId);

            List<ChatInstructorRecord> instructors = new List<ChatInstructorRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string firstName = GetNullableString(reader, 1);
                string lastName = GetNullableString(reader, 2);

                instructors.Add(new ChatInstructorRecord
                {
                    Id = reader.GetInt32(0),
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    Email = GetNullableString(reader, 3)
                });
            }

            return instructors;
        }

        public bool GetInstructorAssignedToParentChild(int parentId, int childId, int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.vw_ParentChildInstructors
                WHERE ParentId = @ParentId
                  AND ChildId = @ChildId
                  AND InstructorId = @InstructorId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public bool GetIsTrainingSessionTimeOccupied(int instructorId, int childId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime)
        {
            using SqlConnection con = OpenConnection();

                        bool hasGroupTrainingSessions = GetGroupTrainingSessionsTableExists(con);

                        string sql = hasGroupTrainingSessions
                                ? @"
                                        SELECT COUNT(1)
                                        FROM
                                        (
                                                SELECT 1
                                                FROM dbo.TrainingSessions ts
                                                WHERE ts.MeetingDate = @MeetingDate
                                                    AND (ts.InstructorId = @InstructorId OR ts.ChildId = @ChildId)
                                                    AND ts.Status <> 'Cancelled'
                                                    AND ts.StartTime < @EndTime
                                                    AND ts.EndTime > @StartTime

                                                UNION ALL

                                                SELECT 1
                                                FROM dbo.GroupTrainingSessions gts
                                                LEFT JOIN dbo.GroupChildren gc
                                                        ON gc.GroupId = gts.GroupId
                                                     AND gc.IsActive = 1
                                                WHERE gts.MeetingDate = @MeetingDate
                                                    AND (gts.InstructorId = @InstructorId OR gc.ChildId = @ChildId)
                                                    AND gts.Status <> 'Cancelled'
                                                    AND gts.StartTime < @EndTime
                                                    AND gts.EndTime > @StartTime
                                        ) AS conflicts;"
                                : @"
                                        SELECT COUNT(1)
                                        FROM dbo.TrainingSessions
                                        WHERE MeetingDate = @MeetingDate
                                            AND (InstructorId = @InstructorId OR ChildId = @ChildId)
                                            AND Status <> 'Cancelled'
                                            AND StartTime < @EndTime
                                            AND EndTime > @StartTime;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@MeetingDate", SqlDbType.Date).Value = meetingDate.Date;
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = startTime;
            cmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = endTime;

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public TrainingSessionRecord PostCreateTrainingSession(
            int parentId,
            int childId,
            int instructorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string notes)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                INSERT INTO dbo.TrainingSessions
                    (ParentId, ChildId, InstructorId, MeetingDate, StartTime, EndTime, Notes, Status, CreatedAt)
                OUTPUT
                    INSERTED.SessionId,
                    INSERTED.ParentId,
                    INSERTED.ChildId,
                    INSERTED.InstructorId,
                    INSERTED.MeetingDate,
                    INSERTED.StartTime,
                    INSERTED.EndTime,
                    INSERTED.Notes,
                    INSERTED.Status,
                    INSERTED.CreatedAt
                VALUES
                    (@ParentId, @ChildId, @InstructorId, @MeetingDate, @StartTime, @EndTime, @Notes, 'Scheduled', SYSUTCDATETIME());";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.AddWithValue("@ChildId", childId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@MeetingDate", SqlDbType.Date).Value = meetingDate.Date;
            cmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = startTime;
            cmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = endTime;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(notes)
                ? string.Empty
                : notes.Trim();

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Training session could not be created.");
            }

            return new TrainingSessionRecord
            {
                SessionId = reader.GetInt32(0),
                ParentId = reader.GetInt32(1),
                ChildId = reader.GetInt32(2),
                InstructorId = reader.GetInt32(3),
                MeetingDate = reader.GetDateTime(4),
                StartTime = reader.GetTimeSpan(5),
                EndTime = reader.GetTimeSpan(6),
                Notes = GetNullableString(reader, 7),
                Status = GetNullableString(reader, 8),
                CreatedAt = reader.GetDateTime(9)
            };
        }

        public bool PostCancelTrainingSessionForInstructor(int instructorId, int sessionId, string cancelReason)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            EnsureLessonSchedulingTablesExists(con, tx);

            const string sessionSql = @"
                SELECT
                    ts.SessionId,
                    ts.ParentId,
                    ts.ChildId,
                    ts.MeetingDate,
                    ts.StartTime,
                    ts.EndTime,
                    ts.Status,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName
                FROM dbo.TrainingSessions ts
                INNER JOIN dbo.Children c
                    ON c.Id = ts.ChildId
                LEFT JOIN dbo.Parents p
                    ON p.Id = ts.ParentId
                WHERE ts.SessionId = @SessionId
                  AND ts.InstructorId = @InstructorId;";

            int parentId;
            int childId;
            DateTime meetingDate;
            TimeSpan startTime;
            TimeSpan endTime;
            string status;
            string childFullName;

            using (SqlCommand sessionCmd = new SqlCommand(sessionSql, con, tx))
            {
                sessionCmd.Parameters.AddWithValue("@SessionId", sessionId);
                sessionCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                using SqlDataReader sessionReader = sessionCmd.ExecuteReader();
                if (!sessionReader.Read())
                {
                    return false;
                }

                parentId = sessionReader.IsDBNull(1) ? 0 : sessionReader.GetInt32(1);
                childId = sessionReader.GetInt32(2);
                meetingDate = sessionReader.GetDateTime(3);
                startTime = sessionReader.GetTimeSpan(4);
                endTime = sessionReader.GetTimeSpan(5);
                status = GetNullableString(sessionReader, 6);
                childFullName = GetNullableString(sessionReader, 7);
            }

            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                tx.Commit();
                return true;
            }

            DateTime sessionStart = meetingDate.Date.Add(startTime);
            if (sessionStart <= DateTime.Now)
            {
                throw new InvalidOperationException("לא ניתן לבטל אימון שכבר התחיל או הסתיים.");
            }

            const string updateSql = @"
                UPDATE dbo.TrainingSessions
                SET Status = 'Cancelled'
                WHERE SessionId = @SessionId
                  AND InstructorId = @InstructorId
                  AND Status <> 'Cancelled';";

            using (SqlCommand updateCmd = new SqlCommand(updateSql, con, tx))
            {
                updateCmd.Parameters.AddWithValue("@SessionId", sessionId);
                updateCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                updateCmd.ExecuteNonQuery();
            }

            if (parentId > 0)
            {
                string normalizedChildName = string.IsNullOrWhiteSpace(childFullName)
                    ? $"ילד #{childId}"
                    : childFullName.Trim();

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
                    startTime = startTime.ToString(@"hh\\:mm", CultureInfo.InvariantCulture),
                    endTime = endTime.ToString(@"hh\\:mm", CultureInfo.InvariantCulture),
                    cancelledBy = "Instructor"
                });

                InsertLessonNotification(
                    con,
                    tx,
                    "Parent",
                    parentId,
                    "TrainingSessionCancelled",
                    notificationTitle,
                    notificationBody,
                    null,
                    null,
                    payloadJson);
            }

            tx.Commit();
            return true;
        }

        public List<InstructorTrainingSessionViewRecord> GetInstructorTrainingSessions(
            int instructorId,
            DateTime startDate,
            DateTime endDate)
        {
            using SqlConnection con = OpenConnection();

            bool hasLessonInvitations = GetUserTableExists(con, null, "LessonInvitations")
                && GetUserTableExists(con, null, "LessonInvitationRecipients");

            string sql = hasLessonInvitations
                ? @"
                    SELECT
                        ts.SessionId,
                        ts.InstructorId,
                        ts.ParentId,
                        ts.ChildId,
                        c.FirstName,
                        c.LastName,
                        ISNULL(p.FirstName, '') AS ParentFirstName,
                        ISNULL(p.LastName, '') AS ParentLastName,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                    LEFT JOIN dbo.Parents p
                        ON p.Id = ts.ParentId
                    WHERE ts.InstructorId = @InstructorId
                      AND ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate
                      AND ts.Status <> 'Cancelled'

                    UNION ALL

                    SELECT
                        -li.InvitationId AS SessionId,
                        li.InstructorId,
                        lir.ParentId,
                        lir.ChildId,
                        c.FirstName,
                        c.LastName,
                        ISNULL(p.FirstName, '') AS ParentFirstName,
                        ISNULL(p.LastName, '') AS ParentLastName,
                        li.MeetingDate,
                        li.StartTime,
                        li.EndTime,
                        li.Status
                    FROM dbo.LessonInvitationRecipients lir
                    INNER JOIN dbo.LessonInvitations li
                        ON li.InvitationId = lir.InvitationId
                    INNER JOIN dbo.Children c
                        ON c.Id = lir.ChildId
                    LEFT JOIN dbo.Parents p
                        ON p.Id = lir.ParentId
                    WHERE li.InstructorId = @InstructorId
                      AND li.LessonType = 'Private'
                      AND lir.IsActive = 1
                      AND lir.ResponseStatus = 'Approved'
                      AND li.MeetingDate >= @StartDate
                      AND li.MeetingDate <= @EndDate
                      AND li.Status <> 'Cancelled'
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.TrainingSessions ts
                          WHERE ts.InstructorId = li.InstructorId
                            AND ts.ChildId = lir.ChildId
                            AND ts.MeetingDate = li.MeetingDate
                            AND ts.StartTime = li.StartTime
                            AND ts.EndTime = li.EndTime
                            AND ts.Status <> 'Cancelled'
                      )

                    ORDER BY MeetingDate, StartTime, EndTime, SessionId;"
                : @"
                    SELECT
                        ts.SessionId,
                        ts.InstructorId,
                        ts.ParentId,
                        ts.ChildId,
                        c.FirstName,
                        c.LastName,
                        ISNULL(p.FirstName, '') AS ParentFirstName,
                        ISNULL(p.LastName, '') AS ParentLastName,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                    LEFT JOIN dbo.Parents p
                        ON p.Id = ts.ParentId
                    WHERE ts.InstructorId = @InstructorId
                      AND ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate
                      AND ts.Status <> 'Cancelled'
                    ORDER BY ts.MeetingDate, ts.StartTime, ts.EndTime, ts.SessionId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.Date;
            cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.Date;

            List<InstructorTrainingSessionViewRecord> sessions = new List<InstructorTrainingSessionViewRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string childFirstName = GetNullableString(reader, 4);
                string childLastName = GetNullableString(reader, 5);
                string parentFirstName = GetNullableString(reader, 6);
                string parentLastName = GetNullableString(reader, 7);

                sessions.Add(new InstructorTrainingSessionViewRecord
                {
                    SessionId = reader.GetInt32(0),
                    InstructorId = reader.GetInt32(1),
                    ParentId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    ChildId = reader.GetInt32(3),
                    ChildFullName = $"{childFirstName} {childLastName}".Trim(),
                    ParentFullName = $"{parentFirstName} {parentLastName}".Trim(),
                    MeetingDate = reader.GetDateTime(8),
                    StartTime = reader.GetTimeSpan(9),
                    EndTime = reader.GetTimeSpan(10),
                    Status = GetNullableString(reader, 11)
                });
            }

            return sessions;
        }

        public bool GetIsGroupTrainingSessionTimeOccupied(int instructorId, int groupId, DateTime meetingDate, TimeSpan startTime, TimeSpan endTime)
        {
            using SqlConnection con = OpenConnection();

            if (!GetGroupTrainingSessionsTableExists(con))
            {
                return false;
            }

            const string sql = @"
                SELECT COUNT(1)
                FROM
                (
                    SELECT 1 AS ConflictFlag
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.GroupChildren gc
                        ON gc.ChildId = ts.ChildId
                       AND gc.IsActive = 1
                    WHERE ts.MeetingDate = @MeetingDate
                      AND ts.Status <> 'Cancelled'
                      AND ts.StartTime < @EndTime
                      AND ts.EndTime > @StartTime
                      AND (ts.InstructorId = @InstructorId OR gc.GroupId = @GroupId)

                    UNION ALL

                    SELECT 1 AS ConflictFlag
                    FROM dbo.GroupTrainingSessions gts
                    WHERE gts.MeetingDate = @MeetingDate
                      AND gts.Status <> 'Cancelled'
                      AND gts.StartTime < @EndTime
                      AND gts.EndTime > @StartTime
                      AND (gts.InstructorId = @InstructorId OR gts.GroupId = @GroupId)
                ) AS conflicts;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@MeetingDate", SqlDbType.Date).Value = meetingDate.Date;
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = startTime;
            cmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = endTime;

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public GroupTrainingSessionRecord PostCreateGroupTrainingSession(
            int groupId,
            int instructorId,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string notes)
        {
            using SqlConnection con = OpenConnection();

            if (!GetGroupTrainingSessionsTableExists(con))
            {
                throw new InvalidOperationException("Group training sessions table does not exist. Run SQL migration 003 first.");
            }

            const string sql = @"
                INSERT INTO dbo.GroupTrainingSessions
                    (GroupId, InstructorId, MeetingDate, StartTime, EndTime, Notes, Status, CreatedAt)
                OUTPUT
                    INSERTED.SessionId,
                    INSERTED.GroupId,
                    INSERTED.InstructorId,
                    INSERTED.MeetingDate,
                    INSERTED.StartTime,
                    INSERTED.EndTime,
                    INSERTED.Notes,
                    INSERTED.Status,
                    INSERTED.CreatedAt
                VALUES
                    (@GroupId, @InstructorId, @MeetingDate, @StartTime, @EndTime, @Notes, 'Scheduled', SYSUTCDATETIME());";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@MeetingDate", SqlDbType.Date).Value = meetingDate.Date;
            cmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = startTime;
            cmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = endTime;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(notes)
                ? string.Empty
                : notes.Trim();

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Group training session could not be created.");
            }

            return new GroupTrainingSessionRecord
            {
                SessionId = reader.GetInt32(0),
                GroupId = reader.GetInt32(1),
                InstructorId = reader.GetInt32(2),
                MeetingDate = reader.GetDateTime(3),
                StartTime = reader.GetTimeSpan(4),
                EndTime = reader.GetTimeSpan(5),
                Notes = GetNullableString(reader, 6),
                Status = GetNullableString(reader, 7),
                CreatedAt = reader.GetDateTime(8)
            };
        }

        public List<InstructorGroupTrainingSessionViewRecord> GetInstructorGroupTrainingSessions(
            int instructorId,
            DateTime startDate,
            DateTime endDate)
        {
            using SqlConnection con = OpenConnection();

            if (!GetGroupTrainingSessionsTableExists(con))
            {
                return new List<InstructorGroupTrainingSessionViewRecord>();
            }

            const string sql = @"
                SELECT
                    gts.SessionId,
                    gts.GroupId,
                    g.Name,
                    gts.InstructorId,
                    gts.MeetingDate,
                    gts.StartTime,
                    gts.EndTime,
                    gts.Notes,
                    gts.Status,
                    COUNT(DISTINCT gc.ChildId) AS ActiveChildrenCount
                FROM dbo.GroupTrainingSessions gts
                INNER JOIN dbo.Groups g
                    ON g.Id = gts.GroupId
                   AND g.IsActive = 1
                LEFT JOIN dbo.GroupChildren gc
                    ON gc.GroupId = gts.GroupId
                   AND gc.IsActive = 1
                WHERE gts.InstructorId = @InstructorId
                  AND gts.MeetingDate >= @StartDate
                  AND gts.MeetingDate <= @EndDate
                  AND gts.Status <> 'Cancelled'
                GROUP BY
                    gts.SessionId,
                    gts.GroupId,
                    g.Name,
                    gts.InstructorId,
                    gts.MeetingDate,
                    gts.StartTime,
                    gts.EndTime,
                    gts.Notes,
                    gts.Status
                ORDER BY gts.MeetingDate, gts.StartTime, gts.EndTime, gts.SessionId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);
            cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.Date;
            cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.Date;

            List<InstructorGroupTrainingSessionViewRecord> sessions = new List<InstructorGroupTrainingSessionViewRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new InstructorGroupTrainingSessionViewRecord
                {
                    SessionId = reader.GetInt32(0),
                    GroupId = reader.GetInt32(1),
                    GroupName = GetNullableString(reader, 2),
                    InstructorId = reader.GetInt32(3),
                    MeetingDate = reader.GetDateTime(4),
                    StartTime = reader.GetTimeSpan(5),
                    EndTime = reader.GetTimeSpan(6),
                    Notes = GetNullableString(reader, 7),
                    Status = GetNullableString(reader, 8),
                    ActiveChildrenCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9)
                });
            }

            return sessions;
        }

        public List<ParentScheduledLessonRecord> GetParentScheduledLessons(int parentId, DateTime startDate, DateTime endDate)
        {
            using SqlConnection con = OpenConnection();

            bool hasGroupTrainingSessions = GetGroupTrainingSessionsTableExists(con);
            bool hasLessonInvitations = GetUserTableExists(con, null, "LessonInvitations")
                && GetUserTableExists(con, null, "LessonInvitationRecipients");

            string sql = hasGroupTrainingSessions
                ? @"
                    SELECT
                        ts.SessionId,
                        CAST('Private' AS NVARCHAR(20)) AS LessonType,
                        ts.ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        ts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        CAST(NULL AS INT) AS GroupId,
                        CAST(NULL AS NVARCHAR(200)) AS GroupName,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status,
                        ts.Notes
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                       AND c.ParentId = @ParentId
                       AND c.IsActive = 1
                    INNER JOIN dbo.Instructors i
                        ON i.Id = ts.InstructorId
                    WHERE ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate
                      AND ts.Status <> 'Cancelled'

                    UNION ALL

                    SELECT
                        gts.SessionId,
                        CAST('Group' AS NVARCHAR(20)) AS LessonType,
                        c.Id AS ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        gts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        g.Id AS GroupId,
                        g.Name AS GroupName,
                        gts.MeetingDate,
                        gts.StartTime,
                        gts.EndTime,
                        gts.Status,
                        gts.Notes
                    FROM dbo.GroupTrainingSessions gts
                    INNER JOIN dbo.Groups g
                        ON g.Id = gts.GroupId
                       AND g.IsActive = 1
                    INNER JOIN dbo.GroupChildren gc
                        ON gc.GroupId = g.Id
                       AND gc.IsActive = 1
                    INNER JOIN dbo.Children c
                        ON c.Id = gc.ChildId
                       AND c.ParentId = @ParentId
                       AND c.IsActive = 1
                    INNER JOIN dbo.Instructors i
                        ON i.Id = gts.InstructorId
                    WHERE gts.MeetingDate >= @StartDate
                      AND gts.MeetingDate <= @EndDate
                      AND gts.Status <> 'Cancelled'

                    ORDER BY MeetingDate, StartTime, EndTime, ChildFullName, LessonType;"
                : @"
                    SELECT
                        ts.SessionId,
                        CAST('Private' AS NVARCHAR(20)) AS LessonType,
                        ts.ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        ts.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        CAST(NULL AS INT) AS GroupId,
                        CAST(NULL AS NVARCHAR(200)) AS GroupName,
                        ts.MeetingDate,
                        ts.StartTime,
                        ts.EndTime,
                        ts.Status,
                        ts.Notes
                    FROM dbo.TrainingSessions ts
                    INNER JOIN dbo.Children c
                        ON c.Id = ts.ChildId
                       AND c.ParentId = @ParentId
                       AND c.IsActive = 1
                    INNER JOIN dbo.Instructors i
                        ON i.Id = ts.InstructorId
                    WHERE ts.MeetingDate >= @StartDate
                      AND ts.MeetingDate <= @EndDate
                      AND ts.Status <> 'Cancelled'
                    ORDER BY MeetingDate, StartTime, EndTime, ChildFullName, LessonType;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);
            cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.Date;
            cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.Date;

            List<ParentScheduledLessonRecord> lessons = new List<ParentScheduledLessonRecord>();
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    lessons.Add(new ParentScheduledLessonRecord
                    {
                        SessionId = reader.GetInt32(0),
                        LessonType = GetNullableString(reader, 1),
                        ChildId = reader.GetInt32(2),
                        ChildFullName = GetNullableString(reader, 3),
                        InstructorId = reader.GetInt32(4),
                        InstructorFullName = GetNullableString(reader, 5),
                        GroupId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        GroupName = GetNullableString(reader, 7),
                        MeetingDate = reader.GetDateTime(8),
                        StartTime = reader.GetTimeSpan(9),
                        EndTime = reader.GetTimeSpan(10),
                        Status = GetNullableString(reader, 11),
                        Notes = GetNullableString(reader, 12)
                    });
                }
            }

            if (hasLessonInvitations)
            {
                const string invitationsSql = @"
                    SELECT
                        li.InvitationId,
                        li.LessonType,
                        lir.ChildId,
                        LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))) AS ChildFullName,
                        li.InstructorId,
                        LTRIM(RTRIM(CONCAT(i.FirstName, ' ', i.LastName))) AS InstructorFullName,
                        li.GroupId,
                        ISNULL(g.Name, '') AS GroupName,
                        li.MeetingDate,
                        li.StartTime,
                        li.EndTime,
                        li.Status,
                        ISNULL(li.GeneralNote, '') AS GeneralNote
                    FROM dbo.LessonInvitationRecipients lir
                    INNER JOIN dbo.LessonInvitations li
                        ON li.InvitationId = lir.InvitationId
                    INNER JOIN dbo.Children c
                        ON c.Id = lir.ChildId
                    INNER JOIN dbo.Instructors i
                        ON i.Id = li.InstructorId
                    LEFT JOIN dbo.Groups g
                        ON g.Id = li.GroupId
                    WHERE lir.ParentId = @ParentId
                      AND lir.IsActive = 1
                      AND lir.ResponseStatus = 'Approved'
                      AND li.MeetingDate >= @StartDate
                      AND li.MeetingDate <= @EndDate
                      AND li.Status <> 'Cancelled'
                    ORDER BY li.MeetingDate, li.StartTime, li.EndTime, ChildFullName, li.LessonType;";

                using SqlCommand invitationsCmd = new SqlCommand(invitationsSql, con);
                invitationsCmd.Parameters.AddWithValue("@ParentId", parentId);
                invitationsCmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.Date;
                invitationsCmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.Date;

                using SqlDataReader invitationReader = invitationsCmd.ExecuteReader();
                while (invitationReader.Read())
                {
                    lessons.Add(new ParentScheduledLessonRecord
                    {
                        // Use negative id to avoid key collisions with TrainingSessions rows.
                        SessionId = -invitationReader.GetInt32(0),
                        LessonType = GetNullableString(invitationReader, 1),
                        ChildId = invitationReader.GetInt32(2),
                        ChildFullName = GetNullableString(invitationReader, 3),
                        InstructorId = invitationReader.GetInt32(4),
                        InstructorFullName = GetNullableString(invitationReader, 5),
                        GroupId = invitationReader.IsDBNull(6) ? null : invitationReader.GetInt32(6),
                        GroupName = GetNullableString(invitationReader, 7),
                        MeetingDate = invitationReader.GetDateTime(8),
                        StartTime = invitationReader.GetTimeSpan(9),
                        EndTime = invitationReader.GetTimeSpan(10),
                        Status = GetNullableString(invitationReader, 11),
                        Notes = GetNullableString(invitationReader, 12)
                    });
                }
            }

            return lessons;
        }

        public ConversationRecord PostGetOrCreateConversation(int parentId, int childId, int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string validateParentChildSql = @"
                SELECT COUNT(1)
                FROM dbo.Children
                WHERE Id = @ChildId
                  AND ParentId = @ParentId
                  AND IsActive = 1;";

            using (SqlCommand validateParentChildCmd = new SqlCommand(validateParentChildSql, con))
            {
                validateParentChildCmd.Parameters.AddWithValue("@ChildId", childId);
                validateParentChildCmd.Parameters.AddWithValue("@ParentId", parentId);

                int isValidParentChild = Convert.ToInt32(validateParentChildCmd.ExecuteScalar());
                if (isValidParentChild == 0)
                {
                    throw new InvalidOperationException("Selected child does not belong to this parent.");
                }
            }

            const string validateAssignmentSql = @"
                SELECT COUNT(1)
                FROM dbo.vw_ParentChildInstructors
                WHERE ParentId = @ParentId
                  AND ChildId = @ChildId
                  AND InstructorId = @InstructorId;";

            using (SqlCommand validateAssignmentCmd = new SqlCommand(validateAssignmentSql, con))
            {
                validateAssignmentCmd.Parameters.AddWithValue("@ParentId", parentId);
                validateAssignmentCmd.Parameters.AddWithValue("@ChildId", childId);
                validateAssignmentCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                int isAssigned = Convert.ToInt32(validateAssignmentCmd.ExecuteScalar());
                if (isAssigned == 0)
                {
                    throw new InvalidOperationException("Selected instructor is not assigned to this child.");
                }
            }

            const string existingSql = @"
                SELECT TOP 1
                    c.Id,
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    c.IsActive,
                    c.CreatedAt,
                    c.LastMessageAt,
                    p.FirstName,
                    p.LastName,
                    ch.FirstName,
                    ch.LastName,
                    i.FirstName,
                    i.LastName
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch ON ch.Id = c.ChildId
                INNER JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE c.ParentId = @ParentId
                  AND c.ChildId = @ChildId
                  AND c.InstructorId = @InstructorId
                ORDER BY c.Id DESC;";

            using (SqlCommand existingCmd = new SqlCommand(existingSql, con))
            {
                existingCmd.Parameters.AddWithValue("@ParentId", parentId);
                existingCmd.Parameters.AddWithValue("@ChildId", childId);
                existingCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                using SqlDataReader existingReader = existingCmd.ExecuteReader();
                if (existingReader.Read())
                {
                    return MapConversation(existingReader);
                }
            }

            const string insertSql = @"
                INSERT INTO dbo.Conversations (ParentId, ChildId, InstructorId, IsActive, CreatedAt)
                VALUES (@ParentId, @ChildId, @InstructorId, 1, SYSUTCDATETIME());

                SELECT
                    c.Id,
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    c.IsActive,
                    c.CreatedAt,
                    c.LastMessageAt,
                    p.FirstName,
                    p.LastName,
                    ch.FirstName,
                    ch.LastName,
                    i.FirstName,
                    i.LastName
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch ON ch.Id = c.ChildId
                INNER JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE c.Id = CAST(SCOPE_IDENTITY() AS int);";

            using SqlCommand insertCmd = new SqlCommand(insertSql, con);
            insertCmd.Parameters.AddWithValue("@ParentId", parentId);
            insertCmd.Parameters.AddWithValue("@ChildId", childId);
            insertCmd.Parameters.AddWithValue("@InstructorId", instructorId);

            using SqlDataReader insertReader = insertCmd.ExecuteReader();
            if (!insertReader.Read())
            {
                throw new InvalidOperationException("Conversation could not be created.");
            }

            return MapConversation(insertReader);
        }

        public ConversationRecord? GetConversationById(int conversationId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    c.IsActive,
                    c.CreatedAt,
                    c.LastMessageAt,
                    p.FirstName,
                    p.LastName,
                    ch.FirstName,
                    ch.LastName,
                    i.FirstName,
                    i.LastName
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch ON ch.Id = c.ChildId
                INNER JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE c.Id = @ConversationId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ConversationId", conversationId);

            using SqlDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return MapConversation(reader);
        }

        public List<ChatMessageRecord> GetConversationMessages(int conversationId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    m.Id,
                    m.ConversationId,
                    m.SenderType,
                    m.MessageText,
                    m.SentAt,
                    CASE
                        WHEN m.SenderType = 'Parent' THEN LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, ''))))
                        WHEN m.SenderType = 'Instructor' THEN LTRIM(RTRIM(CONCAT(ISNULL(i.FirstName, ''), ' ', ISNULL(i.LastName, ''))))
                        ELSE m.SenderType
                    END AS SenderName
                FROM dbo.Messages m
                INNER JOIN dbo.Conversations c ON c.Id = m.ConversationId
                LEFT JOIN dbo.Parents p ON p.Id = c.ParentId
                LEFT JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE m.ConversationId = @ConversationId
                ORDER BY m.SentAt, m.Id;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ConversationId", conversationId);

            List<ChatMessageRecord> messages = new List<ChatMessageRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                messages.Add(MapChatMessage(reader));
            }

            return messages;
        }

        public ChatMessageRecord PostCreateConversationMessage(int conversationId, string senderType, int senderId, string messageText)
        {
            using SqlConnection con = OpenConnection();

            const string conversationSql = @"
                SELECT
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    p.FirstName,
                    p.LastName,
                    i.FirstName,
                    i.LastName
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                INNER JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE c.Id = @ConversationId
                  AND c.IsActive = 1;";

            int parentId;
            int instructorId;
            string parentFullName;
            string instructorFullName;

            using (SqlCommand conversationCmd = new SqlCommand(conversationSql, con))
            {
                conversationCmd.Parameters.AddWithValue("@ConversationId", conversationId);

                using SqlDataReader conversationReader = conversationCmd.ExecuteReader();
                if (!conversationReader.Read())
                {
                    throw new InvalidOperationException("Conversation was not found.");
                }

                parentId = conversationReader.GetInt32(0);
                instructorId = conversationReader.GetInt32(2);

                string parentFirstName = GetNullableString(conversationReader, 3);
                string parentLastName = GetNullableString(conversationReader, 4);
                string instructorFirstName = GetNullableString(conversationReader, 5);
                string instructorLastName = GetNullableString(conversationReader, 6);

                parentFullName = $"{parentFirstName} {parentLastName}".Trim();
                instructorFullName = $"{instructorFirstName} {instructorLastName}".Trim();
            }

            string normalizedSenderType = NormalizeSenderType(senderType);

            if (normalizedSenderType == "Parent" && senderId != parentId)
            {
                throw new UnauthorizedAccessException("Sender id does not match the conversation parent.");
            }

            if (normalizedSenderType == "Instructor" && senderId != instructorId)
            {
                throw new UnauthorizedAccessException("Sender id does not match the conversation instructor.");
            }

            const string insertSql = @"
                INSERT INTO dbo.Messages (ParentId, InstructorId, SenderType, MessageText, SentAt, ConversationId)
                OUTPUT INSERTED.Id, INSERTED.ConversationId, INSERTED.SenderType, INSERTED.MessageText, INSERTED.SentAt
                VALUES (@ParentId, @InstructorId, @SenderType, @MessageText, SYSUTCDATETIME(), @ConversationId);";

            ChatMessageRecord createdMessage;

            using (SqlCommand insertCmd = new SqlCommand(insertSql, con))
            {
                insertCmd.Parameters.AddWithValue("@ParentId", parentId);
                insertCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                insertCmd.Parameters.AddWithValue("@SenderType", normalizedSenderType);
                insertCmd.Parameters.AddWithValue("@MessageText", messageText.Trim());
                insertCmd.Parameters.AddWithValue("@ConversationId", conversationId);

                using SqlDataReader insertReader = insertCmd.ExecuteReader();
                if (!insertReader.Read())
                {
                    throw new InvalidOperationException("Message could not be created.");
                }

                createdMessage = MapChatMessage(insertReader);
            }

            const string updateConversationSql = @"
                UPDATE dbo.Conversations
                SET LastMessageAt = SYSUTCDATETIME()
                WHERE Id = @ConversationId;";

            using (SqlCommand updateCmd = new SqlCommand(updateConversationSql, con))
            {
                updateCmd.Parameters.AddWithValue("@ConversationId", conversationId);
                updateCmd.ExecuteNonQuery();
            }

            createdMessage.SenderName = normalizedSenderType == "Parent"
                ? parentFullName
                : instructorFullName;

            return createdMessage;
        }

        public void UpsertPushDeviceToken(string userType, int userId, string pushToken, string platform, string? deviceId)
        {
            using SqlConnection con = OpenConnection();
            EnsurePushDeviceTokensTableExists(con);

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

            const string validateUserSql = @"
                SELECT CASE
                    WHEN @UserType = 'Parent' AND EXISTS (
                        SELECT 1
                        FROM dbo.Parents p
                        WHERE p.Id = @UserId
                          AND p.IsActive = 1
                    ) THEN 1
                    WHEN @UserType = 'Instructor' AND EXISTS (
                        SELECT 1
                        FROM dbo.Instructors i
                        WHERE i.Id = @UserId
                          AND i.IsActive = 1
                    ) THEN 1
                    ELSE 0
                END;";

            using (SqlCommand validateUserCmd = new SqlCommand(validateUserSql, con))
            {
                validateUserCmd.Parameters.AddWithValue("@UserType", normalizedUserType);
                validateUserCmd.Parameters.AddWithValue("@UserId", userId);

                if (Convert.ToInt32(validateUserCmd.ExecuteScalar()) == 0)
                {
                    throw new InvalidOperationException("User was not found or is inactive.");
                }
            }

            const string sql = @"
                UPDATE dbo.PushDeviceTokens
                SET
                    IsActive = 0,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ExpoPushToken = @PushToken
                  AND (UserType <> @UserType OR UserId <> @UserId);

                UPDATE dbo.PushDeviceTokens
                SET
                    IsActive = 1,
                    Platform = @Platform,
                    DeviceId = @DeviceId,
                    LastRegisteredAt = SYSUTCDATETIME(),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ExpoPushToken = @PushToken
                  AND UserType = @UserType
                  AND UserId = @UserId;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.PushDeviceTokens
                        (UserType, UserId, ExpoPushToken, Platform, DeviceId, IsActive, CreatedAt, UpdatedAt, LastRegisteredAt)
                    VALUES
                        (@UserType, @UserId, @PushToken, @Platform, @DeviceId, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
                END;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@PushToken", normalizedPushToken);
            cmd.Parameters.AddWithValue("@Platform", normalizedPlatform);
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(normalizedDeviceId)
                ? DBNull.Value
                : normalizedDeviceId;
            cmd.ExecuteNonQuery();
        }

        public void DeactivatePushDeviceToken(string userType, int userId, string pushToken)
        {
            using SqlConnection con = OpenConnection();
            EnsurePushDeviceTokensTableExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);
            string normalizedPushToken = string.IsNullOrWhiteSpace(pushToken) ? string.Empty : pushToken.Trim();

            if (userId <= 0 || string.IsNullOrWhiteSpace(normalizedPushToken))
            {
                return; // Nothing to deactivate
            }

            const string sql = @"
                UPDATE dbo.PushDeviceTokens
                SET
                    IsActive = 0,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ExpoPushToken = @PushToken
                  AND UserType = @UserType
                  AND UserId = @UserId;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@PushToken", normalizedPushToken);
            cmd.ExecuteNonQuery();
        }

        public List<string> GetConversationRecipientPushTokens(int conversationId, string senderType, int senderId)
        {
            using SqlConnection con = OpenConnection();
            EnsurePushDeviceTokensTableExists(con);

            string normalizedSenderType = NormalizeSenderType(senderType);
            if (senderId <= 0)
            {
                return new List<string>();
            }

            const string conversationSql = @"
                SELECT
                    c.ParentId,
                    c.InstructorId
                FROM dbo.Conversations c
                WHERE c.Id = @ConversationId
                  AND c.IsActive = 1;";

            int parentId;
            int instructorId;

            using (SqlCommand conversationCmd = new SqlCommand(conversationSql, con))
            {
                conversationCmd.Parameters.AddWithValue("@ConversationId", conversationId);

                using SqlDataReader reader = conversationCmd.ExecuteReader();
                if (!reader.Read())
                {
                    return new List<string>();
                }

                parentId = reader.GetInt32(0);
                instructorId = reader.GetInt32(1);
            }

            string recipientType;
            int recipientId;

            if (normalizedSenderType == "Parent")
            {
                if (senderId != parentId)
                {
                    return new List<string>();
                }

                recipientType = "Instructor";
                recipientId = instructorId;
            }
            else
            {
                if (senderId != instructorId)
                {
                    return new List<string>();
                }

                recipientType = "Parent";
                recipientId = parentId;
            }

            const string tokensSql = @"
                SELECT t.ExpoPushToken
                FROM dbo.PushDeviceTokens t
                WHERE t.UserType = @UserType
                  AND t.UserId = @UserId
                  AND t.IsActive = 1
                ORDER BY t.UpdatedAt DESC, t.Id DESC;";

            List<string> tokens = new List<string>();
            HashSet<string> uniqueTokens = new HashSet<string>(StringComparer.Ordinal);

            using SqlCommand tokensCmd = new SqlCommand(tokensSql, con);
            tokensCmd.Parameters.AddWithValue("@UserType", recipientType);
            tokensCmd.Parameters.AddWithValue("@UserId", recipientId);

            using SqlDataReader tokensReader = tokensCmd.ExecuteReader();
            while (tokensReader.Read())
            {
                string token = GetNullableString(tokensReader, 0).Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (uniqueTokens.Add(token))
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        public List<string> GetActivePushTokensForUser(string userType, int userId)
        {
            using SqlConnection con = OpenConnection();
            EnsurePushDeviceTokensTableExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);
            if (userId <= 0)
            {
                return new List<string>();
            }

            const string tokensSql = @"
                SELECT t.ExpoPushToken
                FROM dbo.PushDeviceTokens t
                WHERE t.UserType = @UserType
                  AND t.UserId = @UserId
                  AND t.IsActive = 1
                ORDER BY t.UpdatedAt DESC, t.Id DESC;";

            List<string> tokens = new List<string>();
            HashSet<string> uniqueTokens = new HashSet<string>(StringComparer.Ordinal);

            using SqlCommand tokensCmd = new SqlCommand(tokensSql, con);
            tokensCmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            tokensCmd.Parameters.AddWithValue("@UserId", userId);

            using SqlDataReader tokensReader = tokensCmd.ExecuteReader();
            while (tokensReader.Read())
            {
                string token = GetNullableString(tokensReader, 0).Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (uniqueTokens.Add(token))
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        public List<ConversationInboxRecord> GetInstructorConversationInbox(int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(ch.FirstName, ''), ' ', ISNULL(ch.LastName, '')))) AS ChildFullName,
                    c.LastMessageAt,
                    ISNULL(lastMessage.MessageText, '') AS LastMessageText,
                    ISNULL(lastMessage.SenderType, '') AS LastMessageSenderType
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch
                    ON ch.Id = c.ChildId
                OUTER APPLY
                (
                    SELECT TOP 1
                        m.MessageText,
                        m.SenderType,
                        m.SentAt
                    FROM dbo.Messages m
                    WHERE m.ConversationId = c.Id
                    ORDER BY m.SentAt DESC, m.Id DESC
                ) lastMessage
                WHERE c.InstructorId = @InstructorId
                  AND c.IsActive = 1
                  AND c.LastMessageAt IS NOT NULL
                ORDER BY c.LastMessageAt DESC, c.Id DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            List<ConversationInboxRecord> inboxItems = new List<ConversationInboxRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                inboxItems.Add(new ConversationInboxRecord
                {
                    ConversationId = reader.GetInt32(0),
                    ParentId = reader.GetInt32(1),
                    ChildId = reader.GetInt32(2),
                    InstructorId = reader.GetInt32(3),
                    ParentFullName = GetNullableString(reader, 4),
                    ChildFullName = GetNullableString(reader, 5),
                    LastMessageAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    LastMessageText = GetNullableString(reader, 7),
                    LastMessageSenderType = GetNullableString(reader, 8)
                });
            }

            return inboxItems;
        }

        public List<ConversationInboxRecord> GetParentConversationInbox(int parentId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    c.Id,
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(ch.FirstName, ''), ' ', ISNULL(ch.LastName, '')))) AS ChildFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(i.FirstName, ''), ' ', ISNULL(i.LastName, '')))) AS InstructorFullName,
                    c.LastMessageAt,
                    ISNULL(lastMessage.MessageText, '') AS LastMessageText,
                    ISNULL(lastMessage.SenderType, '') AS LastMessageSenderType
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch
                    ON ch.Id = c.ChildId
                INNER JOIN dbo.Instructors i
                    ON i.Id = c.InstructorId
                OUTER APPLY
                (
                    SELECT TOP 1
                        m.MessageText,
                        m.SenderType,
                        m.SentAt
                    FROM dbo.Messages m
                    WHERE m.ConversationId = c.Id
                    ORDER BY m.SentAt DESC, m.Id DESC
                ) lastMessage
                WHERE c.ParentId = @ParentId
                  AND c.IsActive = 1
                  AND c.LastMessageAt IS NOT NULL
                ORDER BY c.LastMessageAt DESC, c.Id DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ParentId", parentId);

            List<ConversationInboxRecord> inboxItems = new List<ConversationInboxRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                inboxItems.Add(new ConversationInboxRecord
                {
                    ConversationId = reader.GetInt32(0),
                    ParentId = reader.GetInt32(1),
                    ChildId = reader.GetInt32(2),
                    InstructorId = reader.GetInt32(3),
                    ParentFullName = GetNullableString(reader, 4),
                    ChildFullName = GetNullableString(reader, 5),
                    InstructorFullName = GetNullableString(reader, 6),
                    LastMessageAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    LastMessageText = GetNullableString(reader, 8),
                    LastMessageSenderType = GetNullableString(reader, 9)
                });
            }

            return inboxItems;
        }

        public void PostCreateChatMessageNotification(
            int conversationId,
            string senderType,
            int senderId,
            int messageId,
            string messageText)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

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

            const string conversationSql = @"
                SELECT
                    c.ParentId,
                    c.ChildId,
                    c.InstructorId,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(ch.FirstName, ''), ' ', ISNULL(ch.LastName, '')))) AS ChildFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(i.FirstName, ''), ' ', ISNULL(i.LastName, '')))) AS InstructorFullName
                FROM dbo.Conversations c
                INNER JOIN dbo.Parents p ON p.Id = c.ParentId
                INNER JOIN dbo.Children ch ON ch.Id = c.ChildId
                INNER JOIN dbo.Instructors i ON i.Id = c.InstructorId
                WHERE c.Id = @ConversationId
                  AND c.IsActive = 1;";

            int parentId;
            int childId;
            int instructorId;
            string parentFullName;
            string childFullName;
            string instructorFullName;

            using (SqlCommand conversationCmd = new SqlCommand(conversationSql, con))
            {
                conversationCmd.Parameters.AddWithValue("@ConversationId", conversationId);
                using SqlDataReader reader = conversationCmd.ExecuteReader();
                if (!reader.Read())
                {
                    return;
                }

                parentId = reader.GetInt32(0);
                childId = reader.GetInt32(1);
                instructorId = reader.GetInt32(2);
                parentFullName = GetNullableString(reader, 3);
                childFullName = GetNullableString(reader, 4);
                instructorFullName = GetNullableString(reader, 5);
            }

            string recipientUserType;
            int recipientUserId;
            string senderDisplayName;

            if (string.Equals(normalizedSenderType, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                if (senderId != parentId)
                {
                    return;
                }

                recipientUserType = "Instructor";
                recipientUserId = instructorId;
                senderDisplayName = string.IsNullOrWhiteSpace(parentFullName) ? "הורה" : parentFullName.Trim();
            }
            else
            {
                if (senderId != instructorId)
                {
                    return;
                }

                recipientUserType = "Parent";
                recipientUserId = parentId;
                senderDisplayName = string.IsNullOrWhiteSpace(instructorFullName) ? "מדריך" : instructorFullName.Trim();
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
                parentId,
                childId,
                childName = childFullName,
                instructorId,
                instructorName = instructorFullName,
                senderType = normalizedSenderType,
                senderId
            });

            const string insertSql = @"
                INSERT INTO dbo.LessonNotifications
                    (UserType, UserId, NotificationType, Title, Body, RelatedInvitationId, RelatedRecipientId, IsRead, CreatedAt, ReadAt, PayloadJson)
                VALUES
                    (@UserType, @UserId, 'ChatMessage', @Title, @Body, NULL, NULL, 0, SYSUTCDATETIME(), NULL, @PayloadJson);";

            using SqlCommand insertCmd = new SqlCommand(insertSql, con);
            insertCmd.Parameters.AddWithValue("@UserType", NormalizeNotificationUserType(recipientUserType));
            insertCmd.Parameters.AddWithValue("@UserId", recipientUserId);
            insertCmd.Parameters.AddWithValue("@Title", title);
            insertCmd.Parameters.AddWithValue("@Body", body);
            insertCmd.Parameters.AddWithValue("@PayloadJson", payloadJson);
            insertCmd.ExecuteNonQuery();
        }

        public List<LessonInvitationRecipientOptionRecord> GetInstructorInvitationRecipientOptions(int instructorId)
        {
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT DISTINCT
                    p.Id AS ParentId,
                    c.Id AS ChildId,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName,
                    ISNULL(p.Email, '') AS ParentEmail,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName
                FROM dbo.vw_ParentChildInstructors v
                INNER JOIN dbo.Parents p
                    ON p.Id = v.ParentId
                   AND p.IsActive = 1
                INNER JOIN dbo.Children c
                    ON c.Id = v.ChildId
                   AND c.IsActive = 1
                WHERE v.InstructorId = @InstructorId
                ORDER BY ParentFullName, ChildFullName;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            List<LessonInvitationRecipientOptionRecord> options = new List<LessonInvitationRecipientOptionRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                options.Add(new LessonInvitationRecipientOptionRecord
                {
                    ParentId = reader.GetInt32(0),
                    ChildId = reader.GetInt32(1),
                    ParentFullName = GetNullableString(reader, 2),
                    ParentEmail = GetNullableString(reader, 3),
                    ChildFullName = GetNullableString(reader, 4)
                });
            }

            return options;
        }

        public LessonInvitationRecord PostCreateLessonInvitation(
            int instructorId,
            string lessonType,
            DateTime meetingDate,
            TimeSpan startTime,
            TimeSpan endTime,
            int? groupId,
            string generalNote,
            int capacity,
            int minRegistrations,
            List<LessonInvitationRecipientSelectionRecord> recipients)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            EnsureLessonSchedulingTablesExists(con, tx);

            string normalizedLessonType = NormalizeLessonType(lessonType);
            TimeSpan duration = endTime - startTime;
            if (duration.TotalMinutes < 45 || duration.TotalMinutes > 90)
            {
                throw new InvalidOperationException("Lesson duration must be about one hour (45-90 minutes).");
            }

            int normalizedCapacity = Math.Clamp(capacity <= 0 ? 20 : capacity, 1, 20);
            int normalizedMinRegistrations = normalizedLessonType == "Private"
                ? 1
                : Math.Clamp(minRegistrations <= 0 ? 5 : minRegistrations, 5, normalizedCapacity);

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

            List<(int ParentId, int ChildId, string ParentFullName, string ChildFullName)> recipientDetails = new List<(int, int, string, string)>();

            const string recipientValidationSql = @"
                SELECT TOP 1
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName
                FROM dbo.Children c
                INNER JOIN dbo.Parents p
                    ON p.Id = c.ParentId
                   AND p.IsActive = 1
                INNER JOIN dbo.vw_ParentChildInstructors v
                    ON v.ParentId = p.Id
                   AND v.ChildId = c.Id
                   AND v.InstructorId = @InstructorId
                WHERE c.Id = @ChildId
                  AND p.Id = @ParentId
                  AND c.IsActive = 1;";

            foreach (LessonInvitationRecipientSelectionRecord recipient in normalizedRecipients)
            {
                using SqlCommand validateRecipientCmd = new SqlCommand(recipientValidationSql, con, tx);
                validateRecipientCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                validateRecipientCmd.Parameters.AddWithValue("@ChildId", recipient.ChildId);
                validateRecipientCmd.Parameters.AddWithValue("@ParentId", recipient.ParentId);

                using SqlDataReader recipientReader = validateRecipientCmd.ExecuteReader();
                if (!recipientReader.Read())
                {
                    throw new InvalidOperationException("One or more selected parent-child pairs are not assigned to the instructor.");
                }

                recipientDetails.Add((
                    recipient.ParentId,
                    recipient.ChildId,
                    GetNullableString(recipientReader, 0),
                    GetNullableString(recipientReader, 1)));
            }

            int invitationId;
            string invitationStatus;
            DateTime createdAtUtc;

            const string insertInvitationSql = @"
                INSERT INTO dbo.LessonInvitations
                    (InstructorId, LessonType, GroupId, MeetingDate, StartTime, EndTime, Capacity, MinRegistrations, GeneralNote, Status, CreatedAt, UpdatedAt)
                OUTPUT
                    INSERTED.InvitationId,
                    INSERTED.Status,
                    INSERTED.CreatedAt
                VALUES
                    (@InstructorId, @LessonType, @GroupId, @MeetingDate, @StartTime, @EndTime, @Capacity, @MinRegistrations, @GeneralNote, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME());";

            using (SqlCommand insertInvitationCmd = new SqlCommand(insertInvitationSql, con, tx))
            {
                insertInvitationCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                insertInvitationCmd.Parameters.AddWithValue("@LessonType", normalizedLessonType);
                insertInvitationCmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId.HasValue && groupId.Value > 0
                    ? groupId.Value
                    : DBNull.Value;
                insertInvitationCmd.Parameters.Add("@MeetingDate", SqlDbType.Date).Value = meetingDate.Date;
                insertInvitationCmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = startTime;
                insertInvitationCmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = endTime;
                insertInvitationCmd.Parameters.AddWithValue("@Capacity", normalizedCapacity);
                insertInvitationCmd.Parameters.AddWithValue("@MinRegistrations", normalizedMinRegistrations);
                insertInvitationCmd.Parameters.AddWithValue("@GeneralNote", normalizedNote);

                using SqlDataReader invitationReader = insertInvitationCmd.ExecuteReader();
                if (!invitationReader.Read())
                {
                    throw new InvalidOperationException("Lesson invitation could not be created.");
                }

                invitationId = invitationReader.GetInt32(0);
                invitationStatus = GetNullableString(invitationReader, 1);
                createdAtUtc = invitationReader.GetDateTime(2);
            }

            const string insertRecipientSql = @"
                INSERT INTO dbo.LessonInvitationRecipients
                    (InvitationId, ParentId, ChildId, ResponseStatus, RespondedAt, IsActive)
                OUTPUT INSERTED.RecipientId
                VALUES
                    (@InvitationId, @ParentId, @ChildId, 'Pending', NULL, 1);";

            foreach ((int ParentId, int ChildId, string ParentFullName, string ChildFullName) detail in recipientDetails)
            {
                int recipientId;
                using (SqlCommand insertRecipientCmd = new SqlCommand(insertRecipientSql, con, tx))
                {
                    insertRecipientCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                    insertRecipientCmd.Parameters.AddWithValue("@ParentId", detail.ParentId);
                    insertRecipientCmd.Parameters.AddWithValue("@ChildId", detail.ChildId);

                    recipientId = Convert.ToInt32(insertRecipientCmd.ExecuteScalar());
                }

                string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(detail.ChildFullName)
                    ? "הילד/ה"
                    : detail.ChildFullName.Trim();

                string title = normalizedLessonType == "Group"
                    ? "הזמנה לשיעור קבוצתי"
                    : "הזמנה לשיעור פרטי";

                string body = normalizedLessonType == "Group"
                    ? $"נפתחה עבורך הזמנה לשיעור קבוצתי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}. אנא אשרו או דחו את ההשתתפות דרך האפליקציה."
                    : $"נפתחה עבורך הזמנה לשיעור פרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}. אנא אשרו או דחו את ההזמנה דרך האפליקציה.";

                if (!string.IsNullOrWhiteSpace(normalizedNote))
                {
                    body = body + " הערת מדריך/ה: " + normalizedNote;
                }

                InsertLessonNotification(
                    con,
                    tx,
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
                con,
                tx,
                "Instructor",
                instructorId,
                "LessonCreated",
                "הזמנות לשיעור נשלחו בהצלחה",
                $"נשלחו {recipientDetails.Count} הזמנות לשיעור {(normalizedLessonType == "Group" ? "קבוצתי" : "פרטי")} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {startTime:hh\\:mm}-{endTime:hh\\:mm}.",
                invitationId,
                null,
                string.Empty);

            tx.Commit();

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
                Status = invitationStatus,
                RecipientsCount = recipientDetails.Count,
                ApprovedCount = 0,
                RejectedCount = 0,
                PendingCount = recipientDetails.Count,
                CreatedAtUtc = createdAtUtc
            };
        }

        public List<LessonInvitationRecord> GetInstructorLessonInvitations(int instructorId)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            const string sql = @"
                SELECT
                    li.InvitationId,
                    li.InstructorId,
                    li.LessonType,
                    li.GroupId,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    li.Capacity,
                    li.MinRegistrations,
                    ISNULL(li.GeneralNote, '') AS GeneralNote,
                    li.Status,
                    COUNT(CASE WHEN lir.IsActive = 1 THEN 1 END) AS RecipientsCount,
                    COUNT(CASE WHEN lir.IsActive = 1 AND lir.ResponseStatus = 'Approved' THEN 1 END) AS ApprovedCount,
                    COUNT(CASE WHEN lir.IsActive = 1 AND lir.ResponseStatus = 'Rejected' THEN 1 END) AS RejectedCount,
                    COUNT(CASE WHEN lir.IsActive = 1 AND lir.ResponseStatus = 'Pending' THEN 1 END) AS PendingCount,
                    li.CreatedAt
                FROM dbo.LessonInvitations li
                LEFT JOIN dbo.LessonInvitationRecipients lir
                    ON lir.InvitationId = li.InvitationId
                WHERE li.InstructorId = @InstructorId
                GROUP BY
                    li.InvitationId,
                    li.InstructorId,
                    li.LessonType,
                    li.GroupId,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    li.Capacity,
                    li.MinRegistrations,
                    li.GeneralNote,
                    li.Status,
                    li.CreatedAt
                ORDER BY li.MeetingDate DESC, li.StartTime DESC, li.InvitationId DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            List<LessonInvitationRecord> invitations = new List<LessonInvitationRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                invitations.Add(new LessonInvitationRecord
                {
                    InvitationId = reader.GetInt32(0),
                    InstructorId = reader.GetInt32(1),
                    LessonType = GetNullableString(reader, 2),
                    GroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    MeetingDate = reader.GetDateTime(4),
                    StartTime = reader.GetTimeSpan(5),
                    EndTime = reader.GetTimeSpan(6),
                    Capacity = reader.GetInt32(7),
                    MinRegistrations = reader.GetInt32(8),
                    GeneralNote = GetNullableString(reader, 9),
                    Status = GetNullableString(reader, 10),
                    RecipientsCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    ApprovedCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    RejectedCount = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                    PendingCount = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                    CreatedAtUtc = reader.GetDateTime(15)
                });
            }

            return invitations;
        }

        public bool PostCancelLessonInvitation(int instructorId, int invitationId, string cancelReason)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            EnsureLessonSchedulingTablesExists(con, tx);

            const string invitationSql = @"
                SELECT
                    li.LessonType,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    li.Status
                FROM dbo.LessonInvitations li
                WHERE li.InvitationId = @InvitationId
                  AND li.InstructorId = @InstructorId;";

            string lessonType = string.Empty;
            DateTime meetingDate = DateTime.MinValue;
            TimeSpan startTime = TimeSpan.Zero;
            TimeSpan endTime = TimeSpan.Zero;
            string status = string.Empty;

            using (SqlCommand invitationCmd = new SqlCommand(invitationSql, con, tx))
            {
                invitationCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                invitationCmd.Parameters.AddWithValue("@InstructorId", instructorId);

                using SqlDataReader invitationReader = invitationCmd.ExecuteReader();
                if (!invitationReader.Read())
                {
                    return false;
                }

                lessonType = GetNullableString(invitationReader, 0);
                meetingDate = invitationReader.GetDateTime(1);
                startTime = invitationReader.GetTimeSpan(2);
                endTime = invitationReader.GetTimeSpan(3);
                status = GetNullableString(invitationReader, 4);
            }

            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            const string updateSql = @"
                UPDATE dbo.LessonInvitations
                SET Status = 'Cancelled',
                    UpdatedAt = SYSUTCDATETIME()
                WHERE InvitationId = @InvitationId
                  AND InstructorId = @InstructorId;";

            using (SqlCommand updateCmd = new SqlCommand(updateSql, con, tx))
            {
                updateCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                updateCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                updateCmd.ExecuteNonQuery();
            }

            const string recipientsSql = @"
                SELECT DISTINCT
                    lir.ParentId,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName,
                    lir.RecipientId
                FROM dbo.LessonInvitationRecipients lir
                INNER JOIN dbo.Children c
                    ON c.Id = lir.ChildId
                WHERE lir.InvitationId = @InvitationId
                  AND lir.IsActive = 1;";

            List<(int ParentId, string ChildFullName, int RecipientId)> recipientsToNotify =
                new List<(int ParentId, string ChildFullName, int RecipientId)>();

            using (SqlCommand recipientsCmd = new SqlCommand(recipientsSql, con, tx))
            {
                recipientsCmd.Parameters.AddWithValue("@InvitationId", invitationId);

                using SqlDataReader recipientsReader = recipientsCmd.ExecuteReader();
                while (recipientsReader.Read())
                {
                    recipientsToNotify.Add((
                        recipientsReader.GetInt32(0),
                        GetNullableString(recipientsReader, 1),
                        recipientsReader.GetInt32(2)));
                }
            }

            string reasonText = string.IsNullOrWhiteSpace(cancelReason)
                ? ""
                : $" סיבה: {cancelReason.Trim()}";

            string lessonTypeLabel = string.Equals(lessonType, "Group", StringComparison.OrdinalIgnoreCase)
                ? "שיעור קבוצתי"
                : "שיעור פרטי";

            foreach ((int parentId, string childFullName, int recipientId) in recipientsToNotify)
            {
                string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(childFullName)
                    ? "הילד/ה"
                    : childFullName.Trim();

                InsertLessonNotification(
                    con,
                    tx,
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
                con,
                tx,
                "Instructor",
                instructorId,
                "LessonCancelled",
                "ביטול שיעור הושלם",
                $"ההזמנה מספר {invitationId} בוטלה ונשלחו עדכונים לכל ההורים.",
                invitationId,
                null,
                string.Empty);

            tx.Commit();
            return true;
        }

        public List<LessonNotificationRecord> GetUserLessonNotifications(string userType, int userId)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);

            const string sql = @"
                SELECT
                    n.NotificationId,
                    n.UserType,
                    n.UserId,
                    n.NotificationType,
                    n.Title,
                    n.Body,
                    n.RelatedInvitationId,
                    n.RelatedRecipientId,
                    n.IsRead,
                    n.CreatedAt,
                    n.ReadAt,
                    ISNULL(n.PayloadJson, '') AS PayloadJson,
                    ISNULL(li.LessonType, '') AS LessonType,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    ISNULL(li.Status, '') AS InvitationStatus,
                    ISNULL(lir.ResponseStatus, '') AS RecipientResponseStatus
                FROM dbo.LessonNotifications n
                LEFT JOIN dbo.LessonInvitationRecipients lir
                    ON lir.RecipientId = n.RelatedRecipientId
                LEFT JOIN dbo.LessonInvitations li
                    ON li.InvitationId = ISNULL(n.RelatedInvitationId, lir.InvitationId)
                WHERE n.UserType = @UserType
                  AND n.UserId = @UserId
                ORDER BY n.CreatedAt DESC, n.NotificationId DESC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);

            List<LessonNotificationRecord> notifications = new List<LessonNotificationRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notifications.Add(new LessonNotificationRecord
                {
                    NotificationId = reader.GetInt32(0),
                    UserType = GetNullableString(reader, 1),
                    UserId = reader.GetInt32(2),
                    NotificationType = GetNullableString(reader, 3),
                    Title = GetNullableString(reader, 4),
                    Body = GetNullableString(reader, 5),
                    RelatedInvitationId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    RelatedRecipientId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    IsRead = !reader.IsDBNull(8) && reader.GetBoolean(8),
                    CreatedAtUtc = reader.GetDateTime(9),
                    ReadAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    PayloadJson = GetNullableString(reader, 11),
                    LessonType = GetNullableString(reader, 12),
                    MeetingDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                    StartTime = reader.IsDBNull(14) ? null : reader.GetTimeSpan(14),
                    EndTime = reader.IsDBNull(15) ? null : reader.GetTimeSpan(15),
                    InvitationStatus = GetNullableString(reader, 16),
                    RecipientResponseStatus = GetNullableString(reader, 17)
                });
            }

            return notifications;
        }

        public List<LessonNotificationRecord> GetLessonNotificationsCreatedSince(DateTime sinceUtc, int? relatedInvitationId = null)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            const string sql = @"
                SELECT
                    n.NotificationId,
                    n.UserType,
                    n.UserId,
                    n.NotificationType,
                    n.Title,
                    n.Body,
                    n.RelatedInvitationId,
                    n.RelatedRecipientId,
                    n.IsRead,
                    n.CreatedAt,
                    n.ReadAt,
                    ISNULL(n.PayloadJson, '') AS PayloadJson,
                    ISNULL(li.LessonType, '') AS LessonType,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    ISNULL(li.Status, '') AS InvitationStatus,
                    ISNULL(lir.ResponseStatus, '') AS RecipientResponseStatus
                FROM dbo.LessonNotifications n
                LEFT JOIN dbo.LessonInvitationRecipients lir
                    ON lir.RecipientId = n.RelatedRecipientId
                LEFT JOIN dbo.LessonInvitations li
                    ON li.InvitationId = ISNULL(n.RelatedInvitationId, lir.InvitationId)
                WHERE n.CreatedAt >= @SinceUtc
                  AND (@RelatedInvitationId IS NULL OR n.RelatedInvitationId = @RelatedInvitationId)
                ORDER BY n.NotificationId ASC;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@SinceUtc", SqlDbType.DateTime2).Value = sinceUtc;
            cmd.Parameters.Add("@RelatedInvitationId", SqlDbType.Int).Value = relatedInvitationId.HasValue
                ? relatedInvitationId.Value
                : DBNull.Value;

            List<LessonNotificationRecord> notifications = new List<LessonNotificationRecord>();
            using SqlDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notifications.Add(new LessonNotificationRecord
                {
                    NotificationId = reader.GetInt32(0),
                    UserType = GetNullableString(reader, 1),
                    UserId = reader.GetInt32(2),
                    NotificationType = GetNullableString(reader, 3),
                    Title = GetNullableString(reader, 4),
                    Body = GetNullableString(reader, 5),
                    RelatedInvitationId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    RelatedRecipientId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    IsRead = !reader.IsDBNull(8) && reader.GetBoolean(8),
                    CreatedAtUtc = reader.GetDateTime(9),
                    ReadAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    PayloadJson = GetNullableString(reader, 11),
                    LessonType = GetNullableString(reader, 12),
                    MeetingDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                    StartTime = reader.IsDBNull(14) ? null : reader.GetTimeSpan(14),
                    EndTime = reader.IsDBNull(15) ? null : reader.GetTimeSpan(15),
                    InvitationStatus = GetNullableString(reader, 16),
                    RecipientResponseStatus = GetNullableString(reader, 17)
                });
            }

            return notifications;
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
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);

            const string sql = @"
                INSERT INTO dbo.LessonNotifications
                    (UserType, UserId, NotificationType, Title, Body, RelatedInvitationId, RelatedRecipientId, IsRead, CreatedAt, ReadAt, PayloadJson)
                VALUES
                    (@UserType, @UserId, @NotificationType, @Title, @Body, @RelatedInvitationId, @RelatedRecipientId, 0, SYSUTCDATETIME(), NULL, @PayloadJson);";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@NotificationType", string.IsNullOrWhiteSpace(notificationType) ? "General" : notificationType.Trim());
            cmd.Parameters.AddWithValue("@Title", string.IsNullOrWhiteSpace(title) ? "עדכון מערכת" : title.Trim());
            cmd.Parameters.AddWithValue("@Body", string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim());
            cmd.Parameters.Add("@RelatedInvitationId", SqlDbType.Int).Value = relatedInvitationId.HasValue ? relatedInvitationId.Value : DBNull.Value;
            cmd.Parameters.Add("@RelatedRecipientId", SqlDbType.Int).Value = relatedRecipientId.HasValue ? relatedRecipientId.Value : DBNull.Value;
            cmd.Parameters.AddWithValue("@PayloadJson", string.IsNullOrWhiteSpace(payloadJson) ? string.Empty : payloadJson.Trim());
            cmd.ExecuteNonQuery();
        }

        public int PostMarkUserLessonNotificationsAsRead(string userType, int userId)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);

            const string sql = @"
                UPDATE dbo.LessonNotifications
                SET
                    IsRead = 1,
                    ReadAt = SYSUTCDATETIME()
                WHERE UserType = @UserType
                  AND UserId = @UserId
                  AND IsRead = 0;";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);
            return cmd.ExecuteNonQuery();
        }

        public int PostMarkUserLessonNotificationsAsReadByIds(string userType, int userId, IEnumerable<int> notificationIds)
        {
            using SqlConnection con = OpenConnection();
            EnsureLessonSchedulingTablesExists(con);

            string normalizedUserType = NormalizeNotificationUserType(userType);
            List<int> normalizedIds = (notificationIds ?? Enumerable.Empty<int>())
                .Where((id) => id > 0)
                .Distinct()
                .ToList();

            if (normalizedIds.Count == 0)
            {
                return 0;
            }

            List<string> idParameterNames = new List<string>(normalizedIds.Count);

            using SqlCommand cmd = new SqlCommand();
            cmd.Connection = con;
            cmd.Parameters.AddWithValue("@UserType", normalizedUserType);
            cmd.Parameters.AddWithValue("@UserId", userId);

            for (int index = 0; index < normalizedIds.Count; index++)
            {
                string parameterName = $"@NotificationId{index}";
                idParameterNames.Add(parameterName);
                cmd.Parameters.AddWithValue(parameterName, normalizedIds[index]);
            }

            cmd.CommandText = $@"
                UPDATE dbo.LessonNotifications
                SET
                    IsRead = 1,
                    ReadAt = SYSUTCDATETIME()
                WHERE UserType = @UserType
                  AND UserId = @UserId
                  AND IsRead = 0
                  AND NotificationId IN ({string.Join(", ", idParameterNames)});";

            return cmd.ExecuteNonQuery();
        }

        public LessonInvitationActionResult PostRespondToLessonInvitation(int parentId, int recipientId, string responseAction)
        {
            using SqlConnection con = OpenConnection();
            using SqlTransaction tx = con.BeginTransaction();

            EnsureLessonSchedulingTablesExists(con, tx);

            string normalizedResponse = string.Equals((responseAction ?? string.Empty).Trim(), "approve", StringComparison.OrdinalIgnoreCase)
                || string.Equals((responseAction ?? string.Empty).Trim(), "approved", StringComparison.OrdinalIgnoreCase)
                ? "Approved"
                : "Rejected";

            const string recipientSql = @"
                SELECT
                    lir.RecipientId,
                    lir.InvitationId,
                    lir.ParentId,
                    lir.ChildId,
                    lir.ResponseStatus,
                    li.InstructorId,
                    li.LessonType,
                    li.Status,
                    li.MinRegistrations,
                    li.MeetingDate,
                    li.StartTime,
                    li.EndTime,
                    ISNULL(li.GeneralNote, '') AS GeneralNote,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName,
                    LTRIM(RTRIM(CONCAT(ISNULL(p.FirstName, ''), ' ', ISNULL(p.LastName, '')))) AS ParentFullName
                FROM dbo.LessonInvitationRecipients lir
                INNER JOIN dbo.LessonInvitations li
                    ON li.InvitationId = lir.InvitationId
                INNER JOIN dbo.Children c
                    ON c.Id = lir.ChildId
                LEFT JOIN dbo.Parents p
                    ON p.Id = lir.ParentId
                WHERE lir.RecipientId = @RecipientId
                  AND lir.ParentId = @ParentId
                  AND lir.IsActive = 1;";

            int invitationId;
            int instructorId;
            string lessonType;
            string invitationStatus;
            string currentResponseStatus;
            string childFullName;
            string parentFullName;
            DateTime meetingDate;
            TimeSpan meetingStartTime;
            TimeSpan meetingEndTime;

            using (SqlCommand recipientCmd = new SqlCommand(recipientSql, con, tx))
            {
                recipientCmd.Parameters.AddWithValue("@RecipientId", recipientId);
                recipientCmd.Parameters.AddWithValue("@ParentId", parentId);

                using SqlDataReader recipientReader = recipientCmd.ExecuteReader();
                if (!recipientReader.Read())
                {
                    throw new InvalidOperationException("Invitation recipient was not found for this parent.");
                }

                invitationId = recipientReader.GetInt32(1);
                instructorId = recipientReader.GetInt32(5);
                lessonType = GetNullableString(recipientReader, 6);
                invitationStatus = GetNullableString(recipientReader, 7);
                currentResponseStatus = GetNullableString(recipientReader, 4);
                meetingDate = recipientReader.GetDateTime(9);
                meetingStartTime = recipientReader.GetTimeSpan(10);
                meetingEndTime = recipientReader.GetTimeSpan(11);
                childFullName = GetNullableString(recipientReader, 13);
                parentFullName = GetNullableString(recipientReader, 14);
            }

            if (string.Equals(invitationStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This invitation was already cancelled.");
            }

            if (!string.Equals(currentResponseStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This invitation was already answered.");
            }

            if (string.Equals(normalizedResponse, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                DateTime meetingStartDateTime = meetingDate.Date.Add(meetingStartTime);
                DateTime latestApprovalDateTime = meetingStartDateTime.AddHours(-1);
                if (DateTime.Now > latestApprovalDateTime)
                {
                    throw new InvalidOperationException("לא ניתן לאשר את ההזמנה פחות משעה לפני תחילת השיעור.");
                }
            }

            const string updateRecipientSql = @"
                UPDATE dbo.LessonInvitationRecipients
                SET ResponseStatus = @ResponseStatus,
                    RespondedAt = SYSUTCDATETIME()
                WHERE RecipientId = @RecipientId
                  AND ParentId = @ParentId;";

            using (SqlCommand updateRecipientCmd = new SqlCommand(updateRecipientSql, con, tx))
            {
                updateRecipientCmd.Parameters.AddWithValue("@ResponseStatus", normalizedResponse);
                updateRecipientCmd.Parameters.AddWithValue("@RecipientId", recipientId);
                updateRecipientCmd.Parameters.AddWithValue("@ParentId", parentId);
                updateRecipientCmd.ExecuteNonQuery();
            }

            string responseLabel = string.Equals(normalizedResponse, "Approved", StringComparison.OrdinalIgnoreCase)
                ? "אישור"
                : "דחייה";

            string normalizedChildNameForNotification = string.IsNullOrWhiteSpace(childFullName)
                ? "הילד/ה"
                : childFullName.Trim();

            string normalizedParentNameForNotification = string.IsNullOrWhiteSpace(parentFullName)
                ? "ההורה"
                : parentFullName.Trim();

            InsertLessonNotification(
                con,
                tx,
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

                const string updateInvitationStatusSql = @"
                    UPDATE dbo.LessonInvitations
                    SET Status = @Status,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE InvitationId = @InvitationId;";

                using SqlCommand updateInvitationCmd = new SqlCommand(updateInvitationStatusSql, con, tx);
                updateInvitationCmd.Parameters.AddWithValue("@Status", finalizedStatus);
                updateInvitationCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                updateInvitationCmd.ExecuteNonQuery();

                string parentTitle = string.Equals(finalizedStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? "התגובה להזמנה נקלטה: שיעור פרטי אושר"
                    : "התגובה להזמנה נקלטה: שיעור פרטי בוטל";

                string parentBody = string.Equals(finalizedStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    ? $"הזמנת השיעור הפרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {meetingStartTime:hh\\:mm}-{meetingEndTime:hh\\:mm} אושרה, והמדריך עודכן בהתאם."
                    : $"הזמנת השיעור הפרטי עבור {normalizedChildNameForNotification} בתאריך {meetingDate:yyyy-MM-dd} בין השעות {meetingStartTime:hh\\:mm}-{meetingEndTime:hh\\:mm} נדחתה, והמדריך עודכן בהתאם.";

                InsertLessonNotification(
                    con,
                    tx,
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
                finalizedStatus = EvaluateAndFinalizeGroupInvitation(con, tx, invitationId, instructorId);
            }

            tx.Commit();

            return new LessonInvitationActionResult
            {
                InvitationId = invitationId,
                RecipientId = recipientId,
                ResponseStatus = normalizedResponse,
                InvitationStatus = finalizedStatus
            };
        }

        private static string EvaluateAndFinalizeGroupInvitation(SqlConnection con, SqlTransaction tx, int invitationId, int instructorId)
        {
            const string statusSql = @"
                SELECT
                    li.Status,
                    li.MinRegistrations
                FROM dbo.LessonInvitations li
                WHERE li.InvitationId = @InvitationId;";

            string currentStatus = string.Empty;
            int minRegistrations = 5;

            using (SqlCommand statusCmd = new SqlCommand(statusSql, con, tx))
            {
                statusCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                using SqlDataReader statusReader = statusCmd.ExecuteReader();
                if (!statusReader.Read())
                {
                    throw new InvalidOperationException("Invitation was not found.");
                }

                currentStatus = GetNullableString(statusReader, 0);
                minRegistrations = statusReader.IsDBNull(1) ? 5 : statusReader.GetInt32(1);
            }

            if (!string.Equals(currentStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                return currentStatus;
            }

            const string countsSql = @"
                SELECT
                    COUNT(CASE WHEN IsActive = 1 THEN 1 END) AS TotalCount,
                    COUNT(CASE WHEN IsActive = 1 AND ResponseStatus = 'Approved' THEN 1 END) AS ApprovedCount,
                    COUNT(CASE WHEN IsActive = 1 AND ResponseStatus = 'Rejected' THEN 1 END) AS RejectedCount,
                    COUNT(CASE WHEN IsActive = 1 AND ResponseStatus = 'Pending' THEN 1 END) AS PendingCount
                FROM dbo.LessonInvitationRecipients
                WHERE InvitationId = @InvitationId;";

            int approvedCount = 0;
            int pendingCount = 0;

            using (SqlCommand countsCmd = new SqlCommand(countsSql, con, tx))
            {
                countsCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                using SqlDataReader countsReader = countsCmd.ExecuteReader();
                if (countsReader.Read())
                {
                    approvedCount = countsReader.IsDBNull(1) ? 0 : countsReader.GetInt32(1);
                    pendingCount = countsReader.IsDBNull(3) ? 0 : countsReader.GetInt32(3);
                }
            }

            if (pendingCount > 0)
            {
                return "Pending";
            }

            string nextStatus = approvedCount >= minRegistrations ? "Confirmed" : "Cancelled";

            const string updateStatusSql = @"
                UPDATE dbo.LessonInvitations
                SET Status = @Status,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE InvitationId = @InvitationId;";

            using (SqlCommand updateStatusCmd = new SqlCommand(updateStatusSql, con, tx))
            {
                updateStatusCmd.Parameters.AddWithValue("@Status", nextStatus);
                updateStatusCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                updateStatusCmd.ExecuteNonQuery();
            }

            const string recipientsSql = @"
                SELECT
                    lir.ParentId,
                    lir.RecipientId,
                    lir.ResponseStatus,
                    LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS ChildFullName
                FROM dbo.LessonInvitationRecipients lir
                INNER JOIN dbo.Children c
                    ON c.Id = lir.ChildId
                WHERE lir.InvitationId = @InvitationId
                  AND lir.IsActive = 1;";

            List<(int ParentId, int RecipientId, string ChildFullName)> recipientsToNotify =
                new List<(int ParentId, int RecipientId, string ChildFullName)>();

            using (SqlCommand recipientsCmd = new SqlCommand(recipientsSql, con, tx))
            {
                recipientsCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                using SqlDataReader recipientsReader = recipientsCmd.ExecuteReader();
                while (recipientsReader.Read())
                {
                    recipientsToNotify.Add((
                        recipientsReader.GetInt32(0),
                        recipientsReader.GetInt32(1),
                        GetNullableString(recipientsReader, 3)));
                }
            }

            foreach ((int parentId, int recipientId, string childFullName) in recipientsToNotify)
            {
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
                    con,
                    tx,
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
                con,
                tx,
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

        private static void InsertLessonNotification(
            SqlConnection con,
            SqlTransaction tx,
            string userType,
            int userId,
            string notificationType,
            string title,
            string body,
            int? relatedInvitationId,
            int? relatedRecipientId,
            string payloadJson)
        {
            const string sql = @"
                INSERT INTO dbo.LessonNotifications
                    (UserType, UserId, NotificationType, Title, Body, RelatedInvitationId, RelatedRecipientId, IsRead, CreatedAt, ReadAt, PayloadJson)
                VALUES
                    (@UserType, @UserId, @NotificationType, @Title, @Body, @RelatedInvitationId, @RelatedRecipientId, 0, SYSUTCDATETIME(), NULL, @PayloadJson);";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@UserType", NormalizeNotificationUserType(userType));
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@NotificationType", string.IsNullOrWhiteSpace(notificationType) ? "General" : notificationType.Trim());
            cmd.Parameters.AddWithValue("@Title", string.IsNullOrWhiteSpace(title) ? "עדכון מערכת" : title.Trim());
            cmd.Parameters.AddWithValue("@Body", string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim());
            cmd.Parameters.Add("@RelatedInvitationId", SqlDbType.Int).Value = relatedInvitationId.HasValue ? relatedInvitationId.Value : DBNull.Value;
            cmd.Parameters.Add("@RelatedRecipientId", SqlDbType.Int).Value = relatedRecipientId.HasValue ? relatedRecipientId.Value : DBNull.Value;
            cmd.Parameters.AddWithValue("@PayloadJson", string.IsNullOrWhiteSpace(payloadJson) ? string.Empty : payloadJson.Trim());
            cmd.ExecuteNonQuery();
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
                ChildDescription = reader.IsDBNull(10) ? string.Empty : reader.GetString(10)
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
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LessonInvitations_CreatedAt DEFAULT (SYSUTCDATETIME()),
                        UpdatedAt DATETIME2 NULL,
                        CONSTRAINT FK_LessonInvitations_Instructor FOREIGN KEY (InstructorId) REFERENCES dbo.Instructors(Id)
                    );

                    CREATE INDEX IX_LessonInvitations_Instructor_MeetingDate
                        ON dbo.LessonInvitations (InstructorId, MeetingDate DESC, StartTime DESC);
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

        private static bool GetGroupExistsAndActive(SqlConnection con, SqlTransaction tx, int groupId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Groups
                WHERE Id = @GroupId
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static bool GetChildExistsAndActive(SqlConnection con, SqlTransaction tx, int childId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Children
                WHERE Id = @ChildId
                  AND IsActive = 1;";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@ChildId", childId);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static void EnsureInstructorCanTeach(SqlConnection con, SqlTransaction tx, int instructorId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Instructors
                WHERE Id = @InstructorId
                  AND IsActive = 1
                  AND Role = 'Instructor';";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@InstructorId", instructorId);

            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
            {
                throw new InvalidOperationException("Instructor does not exist, is inactive, or is not in Instructor role.");
            }
        }

        private static void EnsureGroupHasSingleActiveInstructor(SqlConnection con, SqlTransaction tx, int groupId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.InstructorGroups ig
                INNER JOIN dbo.Instructors i ON i.Id = ig.InstructorId
                WHERE ig.GroupId = @GroupId
                  AND ig.IsActive = 1
                  AND i.IsActive = 1
                  AND i.Role = 'Instructor';";

            using SqlCommand cmd = new SqlCommand(sql, con, tx);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            int count = Convert.ToInt32(cmd.ExecuteScalar());
            if (count != 1)
            {
                throw new InvalidOperationException("Group must have exactly one active instructor before assigning children.");
            }
        }

        private static void UpsertActiveInstructorGroup(SqlConnection con, SqlTransaction tx, int groupId, int instructorId)
        {
            const string deactivateOthersSql = @"
                UPDATE dbo.InstructorGroups
                SET IsActive = 0
                WHERE GroupId = @GroupId
                  AND InstructorId <> @InstructorId
                  AND IsActive = 1;";

            using (SqlCommand deactivateOthersCmd = new SqlCommand(deactivateOthersSql, con, tx))
            {
                deactivateOthersCmd.Parameters.AddWithValue("@GroupId", groupId);
                deactivateOthersCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                deactivateOthersCmd.ExecuteNonQuery();
            }

            const string activateExistingSql = @"
                UPDATE dbo.InstructorGroups
                SET IsActive = 1
                WHERE GroupId = @GroupId
                  AND InstructorId = @InstructorId;";

            int affectedRows;
            using (SqlCommand activateExistingCmd = new SqlCommand(activateExistingSql, con, tx))
            {
                activateExistingCmd.Parameters.AddWithValue("@GroupId", groupId);
                activateExistingCmd.Parameters.AddWithValue("@InstructorId", instructorId);
                affectedRows = activateExistingCmd.ExecuteNonQuery();
            }

            if (affectedRows > 0)
            {
                return;
            }

            const string insertSql = @"
                INSERT INTO dbo.InstructorGroups (InstructorId, GroupId, IsActive, CreatedAt)
                VALUES (@InstructorId, @GroupId, 1, SYSUTCDATETIME());";

            using SqlCommand insertCmd = new SqlCommand(insertSql, con, tx);
            insertCmd.Parameters.AddWithValue("@InstructorId", instructorId);
            insertCmd.Parameters.AddWithValue("@GroupId", groupId);
            insertCmd.ExecuteNonQuery();
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
            using SqlConnection con = OpenConnection();

            const string sql = @"
                SELECT
                    g.Id AS GroupId,
                    g.Name AS GroupName
                FROM dbo.Groups g
                WHERE g.IsActive = 1
                GROUP BY g.Id, g.Name;

                SELECT
                    r.GroupId,
                    r.Notes
                FROM dbo.ReportChildren r
                WHERE r.GroupId IS NOT NULL 
                  AND r.ReportDate >= @Start AND r.ReportDate <= @End
                  AND ISJSON(r.Notes) = 1;
            ";

            using SqlCommand cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@Start", SqlDbType.Date).Value = startDate.Date;
            cmd.Parameters.Add("@End", SqlDbType.Date).Value = endDate.Date;

            using SqlDataReader reader = cmd.ExecuteReader();
            
            Dictionary<int, GroupAiStatisticRecord> dict = new Dictionary<int, GroupAiStatisticRecord>();

            while (reader.Read())
            {
                int groupId = reader.GetInt32(0);
                var stat = new GroupAiStatisticRecord
                {
                    GroupId = groupId,
                    GroupName = GetNullableString(reader, 1),
                    MedianMetrics = new Dictionary<string, double>()
                };
                dict[groupId] = stat;
                result.Add(stat);
            }

            if (reader.NextResult())
            {
                Dictionary<int, Dictionary<string, List<double>>> rawMetrics = new Dictionary<int, Dictionary<string, List<double>>>();

                while (reader.Read())
                {
                    int groupId = reader.GetInt32(0);
                    string notesJson = GetNullableString(reader, 1);

                    if (string.IsNullOrWhiteSpace(notesJson)) continue;

                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(notesJson);
                        if (doc.RootElement.TryGetProperty("metrics", out System.Text.Json.JsonElement metricsEl))
                        {
                            if (!rawMetrics.ContainsKey(groupId))
                                rawMetrics[groupId] = new Dictionary<string, List<double>>();

                            if (metricsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var metricObj in metricsEl.EnumerateArray())
                                {
                                    if (metricObj.TryGetProperty("label", out var labelEl) && metricObj.TryGetProperty("value", out var valueEl))
                                    {
                                        string label = NormalizeAiMetricLabel(labelEl.GetString() ?? string.Empty);
                                        if (!string.IsNullOrWhiteSpace(label) && valueEl.TryGetDouble(out double val))
                                        {
                                            if (!rawMetrics[groupId].ContainsKey(label))
                                                rawMetrics[groupId][label] = new List<double>();
                                            rawMetrics[groupId][label].Add(val);
                                        }
                                    }
                                }
                            }
                            else if (metricsEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                foreach (var metric in metricsEl.EnumerateObject())
                                {
                                    string normalizedLabel = NormalizeAiMetricLabel(metric.Name);
                                    if (string.IsNullOrWhiteSpace(normalizedLabel))
                                    {
                                        continue;
                                    }

                                    if (metric.Value.ValueKind == System.Text.Json.JsonValueKind.Number && metric.Value.TryGetDouble(out double val))
                                    {
                                        if (!rawMetrics[groupId].ContainsKey(normalizedLabel))
                                            rawMetrics[groupId][normalizedLabel] = new List<double>();
                                        rawMetrics[groupId][normalizedLabel].Add(val);
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
            }

            return result;
        }
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
        public string Notes { get; set; } = string.Empty;
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

