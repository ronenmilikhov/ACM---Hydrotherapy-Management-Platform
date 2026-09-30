using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.Model;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DbMigrationController : ControllerBase
    {
        private readonly DBServices _db;

        public DbMigrationController(DBServices db)
        {
            _db = db;
        }

        [HttpPost("run")]
        public async System.Threading.Tasks.Task<IActionResult> RunMigration()
        {
            string result = await _db.MigrateSqlToDynamoDbAsync();
            return Ok(new { log = result });
        }

        [HttpPost("reset-and-seed-demo")]
        public async System.Threading.Tasks.Task<IActionResult> ResetAndSeedDemo()
        {
            var log = new List<string>();

            try
            {
                // ═══════════════════════════════════════════════════════════
                // PHASE 0: Gather all entity data we need
                // ═══════════════════════════════════════════════════════════
                var children = _db.GetChildren(false) ?? new List<ChildRecord>();
                var instructors = _db.GetInstructorsForGroupManagement() ?? new List<GroupInstructorOptionRecord>();

                // Find Christian (instructorId)
                var christian = instructors.FirstOrDefault(i => i.Email.Trim().ToLower() == "christian@gmail.com");
                int christianId = christian?.InstructorId ?? 2;
                log.Add($"Christian instructorId: {christianId}");

                // Find Shlomi (parent of login screen shortcut button)
                var parentsScan = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "Parents" }).GetAwaiter().GetResult();
                var shlomiParent = parentsScan.Items.FirstOrDefault(p => p.ContainsKey("Email") && p["Email"].S.Trim().ToLower() == "shlomi@gmail.com");
                int shlomiParentId = 3;
                if (shlomiParent != null && shlomiParent.ContainsKey("Id") && int.TryParse(shlomiParent["Id"].N, out int spid))
                {
                    shlomiParentId = spid;
                }
                log.Add($"Shlomi parentId: {shlomiParentId}");

                // ═══════════════════════════════════════════════════════════
                // PHASE 1: FULL CLEANUP — delete ALL old seeded data
                // ═══════════════════════════════════════════════════════════

                // 1a. Clean ReportChildren
                log.Add("Cleaning ReportChildren table...");
                var scanReports = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "ReportChildren" }).GetAwaiter().GetResult();
                foreach (var item in scanReports.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "ReportId", item["ReportId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "ReportChildren", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanReports.Items.Count} reports.");

                // 1b. Clean ALL TrainingSessions
                var scanSessions = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "TrainingSessions" }).GetAwaiter().GetResult();
                foreach (var item in scanSessions.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "SessionId", item["SessionId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "TrainingSessions", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanSessions.Items.Count} training sessions.");

                // 1c. Clean ALL GroupTrainingSessions
                var scanGroupSessions = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupTrainingSessions" }).GetAwaiter().GetResult();
                foreach (var item in scanGroupSessions.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "SessionId", item["SessionId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "GroupTrainingSessions", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanGroupSessions.Items.Count} group training sessions.");

                // 1d. Clean ALL LessonInvitations
                var scanInvitations = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitations" }).GetAwaiter().GetResult();
                foreach (var item in scanInvitations.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "InvitationId", item["InvitationId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "LessonInvitations", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanInvitations.Items.Count} lesson invitations.");

                // 1e. Clean ALL LessonInvitationRecipients
                var scanRecipients = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
                foreach (var item in scanRecipients.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "RecipientId", item["RecipientId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "LessonInvitationRecipients", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanRecipients.Items.Count} lesson invitation recipients.");

                // 1f. Clean ALL LessonNotifications
                var scanNotifs = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonNotifications" }).GetAwaiter().GetResult();
                foreach (var item in scanNotifs.Items)
                {
                    var key = new Dictionary<string, AttributeValue> { { "NotificationId", item["NotificationId"] } };
                    _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "LessonNotifications", Key = key }).GetAwaiter().GetResult();
                }
                log.Add($"Deleted {scanNotifs.Items.Count} lesson notifications.");

                // 1g. Clean up trial groups (GroupId > 3)
                log.Add("Cleaning trial groups from Groups table...");
                var scanGroups = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "Groups" }).GetAwaiter().GetResult();
                int deletedGroupsCount = 0;
                foreach (var item in scanGroups.Items)
                {
                    if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int gid) && gid > 3)
                    {
                        var key = new Dictionary<string, AttributeValue> { { "Id", item["Id"] } };
                        _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "Groups", Key = key }).GetAwaiter().GetResult();
                        deletedGroupsCount++;
                    }
                }
                log.Add($"Deleted {deletedGroupsCount} trial groups from Groups table.");

                // 1h. Clean up instructor group assignments for trial groups
                log.Add("Cleaning trial group mappings from InstructorGroups table...");
                var scanIg = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "InstructorGroups" }).GetAwaiter().GetResult();
                int deletedIgCount = 0;
                foreach (var item in scanIg.Items)
                {
                    if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) && gid > 3)
                    {
                        var key = new Dictionary<string, AttributeValue> { { "Id", item["Id"] } };
                        _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "InstructorGroups", Key = key }).GetAwaiter().GetResult();
                        deletedIgCount++;
                    }
                }
                log.Add($"Deleted {deletedIgCount} mappings from InstructorGroups table.");

                // 1i. Clean up group children assignments for trial groups
                log.Add("Cleaning trial group children mappings from GroupChildren table...");
                var scanGc = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
                int deletedGcCount = 0;
                foreach (var item in scanGc.Items)
                {
                    if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) && gid > 3)
                    {
                        var key = new Dictionary<string, AttributeValue>
                        {
                            { "GroupId", item["GroupId"] },
                            { "ChildId", item["ChildId"] }
                        };
                        _db.DynamoDbClient.DeleteItemAsync(new DeleteItemRequest { TableName = "GroupChildren", Key = key }).GetAwaiter().GetResult();
                        deletedGcCount++;
                    }
                }
                log.Add($"Deleted {deletedGcCount} mappings from GroupChildren table.");

                // ═══════════════════════════════════════════════════════════
                // PHASE 2: Ensure correct group mappings
                // ═══════════════════════════════════════════════════════════

                // 2a. Ensure Shlomi has at least one child
                var defaultChild = children.FirstOrDefault() ?? new ChildRecord { Id = 2, ParentId = shlomiParentId, FirstName = "נועם", LastName = "כהן" };
                var shlomiChildren = children.Where(c => c.ParentId == shlomiParentId).ToList();
                if (shlomiChildren.Count == 0)
                {
                    log.Add($"Updating child {defaultChild.Id} to be parented by Shlomi (ParentId={shlomiParentId})...");
                    var keyChild = new Dictionary<string, AttributeValue> { { "Id", new AttributeValue { N = defaultChild.Id.ToString() } } };
                    var updateChild = new Dictionary<string, AttributeValueUpdate>
                    {
                        { "ParentId", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { N = shlomiParentId.ToString() } } }
                    };
                    _db.DynamoDbClient.UpdateItemAsync("Children", keyChild, updateChild).GetAwaiter().GetResult();
                    defaultChild.ParentId = shlomiParentId;
                    shlomiChildren.Add(defaultChild);
                }

                // 2b. Ensure Christian is assigned to Group 2 in InstructorGroups
                var igScan = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "InstructorGroups" }).GetAwaiter().GetResult();
                bool christianHasGroup2 = igScan.Items.Any(item =>
                    item.ContainsKey("InstructorId") && int.TryParse(item["InstructorId"].N, out int iid) && iid == christianId &&
                    item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) && gid == 2 &&
                    item.ContainsKey("IsActive") && item["IsActive"].BOOL == true
                );

                if (!christianHasGroup2)
                {
                    int maxIgId = 0;
                    foreach (var item in igScan.Items)
                    {
                        if (item.ContainsKey("Id") && int.TryParse(item["Id"].N, out int idVal) && idVal > maxIgId)
                            maxIgId = idVal;
                    }
                    var newIgItem = new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = new AttributeValue { N = (maxIgId + 1).ToString() },
                        ["InstructorId"] = new AttributeValue { N = christianId.ToString() },
                        ["GroupId"] = new AttributeValue { N = "2" },
                        ["IsActive"] = new AttributeValue { BOOL = true },
                        ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    };
                    _db.DynamoDbClient.PutItemAsync("InstructorGroups", newIgItem).GetAwaiter().GetResult();
                    log.Add("Added Christian → Group 2 mapping in InstructorGroups.");
                }

                // 2c. Ensure all children are actively mapped to Group 2 in GroupChildren
                var gcScanAll = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
                var childGroupMap = new Dictionary<int, int>();
                foreach (var item in gcScanAll.Items)
                {
                    if (item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cid) &&
                        item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) &&
                        item.ContainsKey("IsActive") && item["IsActive"].BOOL == true)
                    {
                        childGroupMap[cid] = gid;
                    }
                }

                foreach (var child in children)
                {
                    if (!childGroupMap.ContainsKey(child.Id) || childGroupMap[child.Id] != 2)
                    {
                        bool existsInactive = gcScanAll.Items.Any(item =>
                            item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cid) && cid == child.Id &&
                            item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) && gid == 2
                        );

                        if (!existsInactive)
                        {
                            var newGcItem = new Dictionary<string, AttributeValue>
                            {
                                ["GroupId"] = new AttributeValue { N = "2" },
                                ["ChildId"] = new AttributeValue { N = child.Id.ToString() },
                                ["IsActive"] = new AttributeValue { BOOL = true },
                                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                            };
                            _db.DynamoDbClient.PutItemAsync("GroupChildren", newGcItem).GetAwaiter().GetResult();
                        }
                        else
                        {
                            // Activate existing mapping
                            var gcItem = gcScanAll.Items.First(item =>
                                item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cid) && cid == child.Id &&
                                item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gid) && gid == 2
                            );
                            // Find the primary key — GroupChildren uses composite key (GroupId, ChildId) or just one field
                            var gcKey = new Dictionary<string, AttributeValue>();
                            if (gcItem.ContainsKey("GroupId")) gcKey["GroupId"] = gcItem["GroupId"];
                            if (gcItem.ContainsKey("ChildId")) gcKey["ChildId"] = gcItem["ChildId"];
                            var gcUpdate = new Dictionary<string, AttributeValueUpdate>
                            {
                                { "IsActive", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { BOOL = true } } }
                            };
                            _db.DynamoDbClient.UpdateItemAsync("GroupChildren", gcKey, gcUpdate).GetAwaiter().GetResult();
                        }
                    }
                }
                log.Add("Ensured all children are mapped to Group 2.");

                // ═══════════════════════════════════════════════════════════
                // PHASE 3: Create child reports with CORRECT ExerciseCatalog keys
                // ═══════════════════════════════════════════════════════════
                log.Add("Inserting child reports with correct catalog keys...");
                int reportCount = 0;

                // These keys MUST match the ExerciseCatalog in InstructorController.cs
                var exercises = new[]
                {
                    (Key: "front_float",      Title: "ציפה על הבטן",   Comment1: "הפגין שליטה מעולה בציפת בטן ללא עזרה.",                           Comment2: "תרגלנו ציפת בטן, שמר על יציבות מרשימה במים."),
                    (Key: "back_float",       Title: "ציפה על הגב",    Comment1: "הראה ביטחון רב בציפת גב ורגיעה טובה במים.",                       Comment2: "מציג שיפור יפה מאוד בציפת הגב עם תמיכה קלה."),
                    (Key: "front_kicks",      Title: "בעיטות בטן",     Comment1: "בעיטות רגליים חזקות ויציבות עם גלשן.",                            Comment2: "תרגל בעיטות בטן לאורך כל המסלול בהתמדה."),
                    (Key: "back_kicks",       Title: "בעיטות גב",      Comment1: "עבודת רגליים יפה בשחיית גב, שמר על קצב קבוע.",                   Comment2: "ביצע בעיטות גב בצורה רציפה עם סנפירים."),
                    (Key: "arrow_jump",       Title: "קפיצה חץ",       Comment1: "ביצע קפיצת חץ מעולה ועבר מיידית לציפה.",                         Comment2: "קפץ בביטחון רב למים העמוקים בצורת חץ."),
                    (Key: "deep_jump",        Title: "קפיצה עמוק",     Comment1: "הפגין אומץ רב בקפיצה למים עמוקים ללא היסוס.",                    Comment2: "קפץ למים עמוקים ושיתף פעולה יפה מאוד עם המאמן."),
                    (Key: "hoop_pass",        Title: "חישוק",          Comment1: "עבר דרך החישוק במים בצורה חלקה ועם נשימה נכונה.",                 Comment2: "תרגיל מעבר בחישוק תת-ימי בוצע בהצלחה יתרה."),
                    (Key: "water_confidence", Title: "ביטחון במים",     Comment1: "מראה ביטחון מוחלט במים עמוקים ותרגול נשימות רציף.",               Comment2: "התקדמות מדהימה בהסתגלות למים והכנסת ראש.")
                };

                var rand = new Random();

                foreach (var child in children)
                {
                    int childId = child.Id;
                    int groupId = 2; // All demo children belong to Group 2
                    int instructorId = christianId;

                    // Choose 2 distinct exercises randomly for each child
                    var chosenExs = exercises.OrderBy(x => rand.Next()).Take(2).ToList();

                    // Report 1 — 6 days ago
                    var metrics1 = new[]
                    {
                        new { label = "שליטה בנשימות (הכנסת ראש למים)", value = rand.Next(3, 6) },
                        new { label = "יציבה וציפה", value = rand.Next(3, 6) },
                        new { label = "הסתגלות וביטחון במים", value = rand.Next(3, 6) },
                        new { label = "תנועתיות וקואורדינציה", value = rand.Next(3, 6) },
                        new { label = "תקשורת במים (ושיתוף פעולה)", value = rand.Next(3, 6) },
                        new { label = "התמדה ומאמץ", value = rand.Next(3, 6) }
                    };
                    string metricsJson1 = System.Text.Json.JsonSerializer.Serialize(metrics1);
                    _db.PostCreateChildReport(childId, instructorId, groupId,
                        DateTime.Today.AddDays(-6), chosenExs[0].Key, chosenExs[0].Title,
                        true, chosenExs[0].Comment1, metricsJson1);

                    // Past Training Session 1 — 6 days ago
                    var pastSession1 = _db.PostCreateTrainingSession(
                        child.ParentId, childId, instructorId,
                        DateTime.Today.AddDays(-6), new TimeSpan(16, 0, 0), new TimeSpan(16, 45, 0),
                        chosenExs[0].Comment1, chosenExs[0].Title);
                    _db.DynamoDbClient.UpdateItemAsync("TrainingSessions",
                        new Dictionary<string, AttributeValue> { { "SessionId", new AttributeValue { N = pastSession1.SessionId.ToString() } } },
                        new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                    ).GetAwaiter().GetResult();

                    // Report 2 — 2 days ago
                    var metrics2 = new[]
                    {
                        new { label = "שליטה בנשימות (הכנסת ראש למים)", value = rand.Next(3, 6) },
                        new { label = "יציבה וציפה", value = rand.Next(3, 6) },
                        new { label = "הסתגלות וביטחון במים", value = rand.Next(3, 6) },
                        new { label = "תנועתיות וקואורדינציה", value = rand.Next(3, 6) },
                        new { label = "תקשורת במים (ושיתוף פעולה)", value = rand.Next(3, 6) },
                        new { label = "התמדה ומאמץ", value = rand.Next(3, 6) }
                    };
                    string metricsJson2 = System.Text.Json.JsonSerializer.Serialize(metrics2);
                    _db.PostCreateChildReport(childId, instructorId, groupId,
                        DateTime.Today.AddDays(-2), chosenExs[1].Key, chosenExs[1].Title,
                        true, chosenExs[1].Comment2, metricsJson2);

                    // Past Training Session 2 — 2 days ago
                    var pastSession2 = _db.PostCreateTrainingSession(
                        child.ParentId, childId, instructorId,
                        DateTime.Today.AddDays(-2), new TimeSpan(17, 0, 0), new TimeSpan(17, 45, 0),
                        chosenExs[1].Comment2, chosenExs[1].Title);
                    _db.DynamoDbClient.UpdateItemAsync("TrainingSessions",
                        new Dictionary<string, AttributeValue> { { "SessionId", new AttributeValue { N = pastSession2.SessionId.ToString() } } },
                        new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                    ).GetAwaiter().GetResult();

                    reportCount += 2;
                }
                log.Add($"Created {reportCount} reports (2 per child, {children.Count} children).");

                // ═══════════════════════════════════════════════════════════
                // PHASE 4: Schedule training sessions
                // ═══════════════════════════════════════════════════════════

                DateTime upcomingIndividualDate = new DateTime(2026, 7, 17);
                DateTime groupDate = new DateTime(2026, 7, 18);
                TimeSpan groupStart = new TimeSpan(17, 0, 0);
                TimeSpan groupEnd = new TimeSpan(17, 45, 0);

                // 4a. Seed exactly 1 upcoming PRIVATE session (approved/confirmed)
                var privateRecipients = new List<LessonInvitationRecipientSelectionRecord>
                {
                    new LessonInvitationRecipientSelectionRecord { ParentId = shlomiParentId, ChildId = shlomiChildren[0].Id }
                };
                var privateInvitation = _db.PostCreateLessonInvitation(
                    christianId, "Private", upcomingIndividualDate, new TimeSpan(16, 0, 0), new TimeSpan(16, 45, 0),
                    null, "אימון אישי - קפיצה עמוק", "קפיצה עמוק",
                    1, 1, privateRecipients);

                // Approve private recipient
                var privateRecScan = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
                var privateRecItem = privateRecScan.Items.FirstOrDefault(item => 
                    item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int invId) && invId == privateInvitation.InvitationId);
                int privateRecipientId = 0;
                if (privateRecItem != null)
                {
                    privateRecipientId = int.Parse(privateRecItem["RecipientId"].N);
                    var keyRec = new Dictionary<string, AttributeValue> { { "RecipientId", privateRecItem["RecipientId"] } };
                    _db.DynamoDbClient.UpdateItemAsync("LessonInvitationRecipients", keyRec,
                        new Dictionary<string, AttributeValueUpdate>
                        {
                            { "ResponseStatus", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Approved" } } },
                            { "RespondedAt", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } } }
                        }
                    ).GetAwaiter().GetResult();
                }

                // Confirm private invitation
                _db.DynamoDbClient.UpdateItemAsync("LessonInvitations",
                    new Dictionary<string, AttributeValue> { { "InvitationId", new AttributeValue { N = privateInvitation.InvitationId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();

                // Create individual training session
                var individualSession = _db.PostCreateTrainingSession(
                    shlomiParentId, shlomiChildren[0].Id, christianId,
                    upcomingIndividualDate, new TimeSpan(16, 0, 0), new TimeSpan(16, 45, 0),
                    "אימון אישי - קפיצה עמוק", "קפיצה עמוק");
                _db.DynamoDbClient.UpdateItemAsync("TrainingSessions",
                    new Dictionary<string, AttributeValue> { { "SessionId", new AttributeValue { N = individualSession.SessionId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();
                log.Add($"Confirmed private session (Id={individualSession.SessionId}) on {upcomingIndividualDate:yyyy-MM-dd}.");

                // 4b. Seed exactly 1 upcoming GROUP session (approved/confirmed)
                var groupChildrenList = children.Take(5).ToList();
                var recipientsList = groupChildrenList.Select(c => new LessonInvitationRecipientSelectionRecord
                {
                    ParentId = c.ParentId,
                    ChildId = c.Id
                }).ToList();

                var groupInvitation = _db.PostCreateLessonInvitation(
                    christianId, "Group", groupDate, groupStart, groupEnd,
                    2, "שיעור שחייה קבוצתי - ציפה וקפיצות ראש", "ציפה על הבטן",
                    20, 5, recipientsList);

                // Approve all group recipients
                var freshRecipients = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
                int approvedGroupCount = 0;
                foreach (var item in freshRecipients.Items)
                {
                    if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int invId) && invId == groupInvitation.InvitationId)
                    {
                        var keyRec = new Dictionary<string, AttributeValue> { { "RecipientId", item["RecipientId"] } };
                        _db.DynamoDbClient.UpdateItemAsync("LessonInvitationRecipients", keyRec,
                            new Dictionary<string, AttributeValueUpdate>
                            {
                                { "ResponseStatus", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Approved" } } },
                                { "RespondedAt", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } } }
                            }
                        ).GetAwaiter().GetResult();

                        // Parent notification for group approval
                        if (item.ContainsKey("ParentId") && int.TryParse(item["ParentId"].N, out int pId))
                        {
                            string childFirstName = "הילד/ה";
                            if (item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cIdVal))
                            {
                                var matchChild = children.FirstOrDefault(c => c.Id == cIdVal);
                                if (matchChild != null) childFirstName = matchChild.FirstName;
                            }
                            _db.PostCreateUserLessonNotification(
                                "Parent", pId, "LessonApproved", "שיעור קבוצתי אושר",
                                $"אישרתם את השיעור הקבוצתי ב-{groupDate:dd/MM/yyyy} בשעה {groupStart:hh\\:mm} עבור {childFirstName}.",
                                groupInvitation.InvitationId, int.Parse(item["RecipientId"].N), "{}");
                        }
                        approvedGroupCount++;
                    }
                }

                // Confirm group invitation
                _db.DynamoDbClient.UpdateItemAsync("LessonInvitations",
                    new Dictionary<string, AttributeValue> { { "InvitationId", new AttributeValue { N = groupInvitation.InvitationId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();

                // Create Group Training Session record
                var groupSession = _db.PostCreateGroupTrainingSession(2, christianId, groupDate, groupStart, groupEnd,
                    "שיעור שחייה קבוצתי - ציפה וקפיצות ראש", "ציפה על הבטן");
                _db.DynamoDbClient.UpdateItemAsync("GroupTrainingSessions",
                    new Dictionary<string, AttributeValue> { { "SessionId", new AttributeValue { N = groupSession.SessionId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();
                log.Add($"Confirmed group session (Id={groupSession.SessionId}) on {groupDate:yyyy-MM-dd}.");

                // 4c. Seed a PAST group session (3 days ago) WITHOUT reports — for AI Group Reports testing
                DateTime pastGroupDate = DateTime.Today.AddDays(-3);
                TimeSpan pastGroupStart = new TimeSpan(10, 0, 0);
                TimeSpan pastGroupEnd = new TimeSpan(10, 45, 0);

                var pastGroupRecipients = groupChildrenList.Select(c => new LessonInvitationRecipientSelectionRecord
                {
                    ParentId = c.ParentId,
                    ChildId = c.Id
                }).ToList();

                var pastGroupInvitation = _db.PostCreateLessonInvitation(
                    christianId, "Group", pastGroupDate, pastGroupStart, pastGroupEnd,
                    2, "שיעור קבוצתי - בעיטות גב וציפה", "בעיטות גב",
                    20, 5, pastGroupRecipients);

                // Approve all past group recipients
                var pastFreshRec = _db.DynamoDbClient.ScanAsync(new ScanRequest { TableName = "LessonInvitationRecipients" }).GetAwaiter().GetResult();
                foreach (var item in pastFreshRec.Items)
                {
                    if (item.ContainsKey("InvitationId") && int.TryParse(item["InvitationId"].N, out int invId2) && invId2 == pastGroupInvitation.InvitationId)
                    {
                        var keyRec2 = new Dictionary<string, AttributeValue> { { "RecipientId", item["RecipientId"] } };
                        _db.DynamoDbClient.UpdateItemAsync("LessonInvitationRecipients", keyRec2,
                            new Dictionary<string, AttributeValueUpdate>
                            {
                                { "ResponseStatus", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Approved" } } },
                                { "RespondedAt", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = DateTime.UtcNow.ToString("o") } } }
                            }
                        ).GetAwaiter().GetResult();
                    }
                }

                // Confirm past group invitation
                _db.DynamoDbClient.UpdateItemAsync("LessonInvitations",
                    new Dictionary<string, AttributeValue> { { "InvitationId", new AttributeValue { N = pastGroupInvitation.InvitationId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();

                // Create Past Group Training Session record
                var pastGroupSession = _db.PostCreateGroupTrainingSession(2, christianId, pastGroupDate, pastGroupStart, pastGroupEnd,
                    "שיעור קבוצתי - בעיטות גב וציפה", "בעיטות גב");
                _db.DynamoDbClient.UpdateItemAsync("GroupTrainingSessions",
                    new Dictionary<string, AttributeValue> { { "SessionId", new AttributeValue { N = pastGroupSession.SessionId.ToString() } } },
                    new Dictionary<string, AttributeValueUpdate> { { "Status", new AttributeValueUpdate { Action = AttributeAction.PUT, Value = new AttributeValue { S = "Confirmed" } } } }
                ).GetAwaiter().GetResult();
                log.Add($"Seeded past group session (Id={pastGroupSession.SessionId}) on {pastGroupDate:yyyy-MM-dd} for AI Group Reports testing.");

                // ═══════════════════════════════════════════════════════════
                // PHASE 5: Seed matching clean notifications
                // ═══════════════════════════════════════════════════════════

                // Private lesson notifications
                _db.PostCreateUserLessonNotification(
                    "Parent", shlomiParentId, "LessonUpdate", "התגובה להזמנה נקלטה: שיעור פרטי אושר",
                    $"הזמנת השיעור הפרטי עבור נועם כהן בתאריך 17/07/2026 בין השעות 16:00-16:45 אושרה, והמדריך עודכן בהתאם.",
                    privateInvitation.InvitationId, privateRecipientId, "{}");

                _db.PostCreateUserLessonNotification(
                    "Instructor", christianId, "LessonResponse", "התקבלה תגובת הורה להזמנה",
                    $"התקבלה תגובת הורה (אישור) להזמנה עבור נועם כהן. ההורה: שלומי כהן. מועד השיעור: 2026-07-17 16:00-16:45.",
                    privateInvitation.InvitationId, privateRecipientId, "{}");

                // Group lesson notifications
                _db.PostCreateUserLessonNotification(
                    "Instructor", christianId, "LessonUpdate", "עדכון הזמנה קבוצתית",
                    $"הזמנה מספר {groupInvitation.InvitationId} אושרה עם 5 נרשמים.",
                    groupInvitation.InvitationId, null, "{}");

                // Combined summary notification for instructor
                _db.PostCreateUserLessonNotification(
                    "Instructor", christianId, "LessonScheduled", "נקבעו עבורך שיעורים חדשים",
                    $"נקבע עבורך אימון אישי ב-17/07/2026 ושיעור קבוצתי ב-18/07/2026.",
                    privateInvitation.InvitationId, null, "{}");

                log.Add("All custom notifications for both sessions created successfully.");

                return Ok(new { success = true, logs = log });
            }
            catch (Exception ex)
            {
                log.Add($"FATAL ERROR: {ex.Message}");
                return StatusCode(500, new { success = false, error = ex.ToString(), logs = log });
            }
        }
    }
}
