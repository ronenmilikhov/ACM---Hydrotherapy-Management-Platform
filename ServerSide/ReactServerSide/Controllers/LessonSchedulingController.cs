using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ReactServerSide.Controllers
{
    [Route("api/lesson-scheduling")]
    [ApiController]
    public class LessonSchedulingController : ControllerBase
    {
        private readonly DBServices _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public LessonSchedulingController(DBServices db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpGet("temp-insert")]
        public IActionResult TempInsert()
        {
            _db.CreateTempCompletedLessonInvitation(out int instructorId, out int groupId, out string meetingDateStr, out string targetMetric);
            return Ok(new { message = "Successfully inserted temp completed lesson invitation!", instructorId, groupId, meetingDate = meetingDateStr, targetMetric });
        }

        [HttpGet("instructor/{instructorId:int}/invitations")]
        public IActionResult GetInstructorInvitations(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<LessonInvitationRecord> invitations = _db.GetInstructorLessonInvitations(instructorId);
            return Ok(invitations.Select(MapInvitation));
        }

        [HttpGet("instructor/{instructorId:int}/child/{childId:int}/lesson-dates")]
        public IActionResult GetChildLessonDates(int instructorId, int childId)
        {
            if (instructorId <= 0 || childId <= 0)
                return BadRequest(new { message = "Invalid instructor or child id." });

            if (!_db.GetInstructorExists(instructorId))
                return NotFound(new { message = "Instructor was not found." });

            string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var dateMap = new Dictionary<string, string>();

            // 1. Get scheduled Private lessons for this child
            try
            {
                List<DateTime> privateDates = _db.GetLessonDatesForChild(instructorId, childId);
                foreach (var d in privateDates)
                {
                    string dateStr = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    dateMap[dateStr] = "אישי";
                }
            }
            catch { }

            // 2. Get active group memberships for this child to find Group session dates
            try
            {
                var gcScan = _db.DynamoDbClient.ScanAsync(new Amazon.DynamoDBv2.Model.ScanRequest
                {
                    TableName = "GroupChildren",
                    FilterExpression = "ChildId = :cid AND IsActive = :active",
                    ExpressionAttributeValues = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
                    {
                        [":cid"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = childId.ToString() },
                        [":active"] = new Amazon.DynamoDBv2.Model.AttributeValue { BOOL = true }
                    }
                }).GetAwaiter().GetResult();

                var activeGroupIds = new List<int>();
                foreach (var item in gcScan.Items)
                {
                    if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId))
                    {
                        activeGroupIds.Add(gId);
                    }
                }

                if (activeGroupIds.Count > 0)
                {
                    var gtsScan = _db.DynamoDbClient.ScanAsync(new Amazon.DynamoDBv2.Model.ScanRequest
                    {
                        TableName = "GroupTrainingSessions",
                        FilterExpression = "MeetingDate <= :today AND Status <> :cancelled",
                        ExpressionAttributeValues = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
                        {
                            [":today"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = todayStr },
                            [":cancelled"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "Cancelled" }
                        }
                    }).GetAwaiter().GetResult();

                    foreach (var item in gtsScan.Items)
                    {
                        if (item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId) &&
                            activeGroupIds.Contains(gId) && item.ContainsKey("MeetingDate"))
                        {
                            string mDate = item["MeetingDate"].S;
                            dateMap[mDate] = "קבוצתי";
                        }
                    }
                }
            }
            catch { }

            // 3. Get dates from existing progress reports in ReportChildren
            try
            {
                var pastReports = _db.GetChildReportsForMetrics(childId);
                foreach (var rep in pastReports)
                {
                    string dateStr = rep.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string mType = (rep.GroupId.HasValue && rep.GroupId.Value > 0) ? "קבוצתי" : "אישי";
                    dateMap[dateStr] = mType;
                }
            }
            catch { }

            // 4. Sort and return objects
            var result = dateMap
                .Select(kv => new
                {
                    date = kv.Key,
                    meetingType = kv.Value
                })
                .OrderByDescending(x => x.date)
                .ToList();

            return Ok(result);
        }

        [HttpGet("instructor/{instructorId:int}/recipients")]
        public IActionResult GetInstructorRecipients(int instructorId, [FromQuery] string? groupIds)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<LessonInvitationRecipientOptionRecord> recipients = _db.GetInstructorInvitationRecipientOptions(instructorId);

            List<int> selectedGroupIds = new List<int>();
            if (!string.IsNullOrWhiteSpace(groupIds))
            {
                selectedGroupIds = groupIds.Split(',')
                    .Select(s => int.TryParse(s, out int id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();
            }

            if (selectedGroupIds.Count > 0)
            {
                var childIdsInGroups = new HashSet<int>();
                foreach (int gId in selectedGroupIds)
                {
                    var childIds = _db.GetActiveChildIdsInGroup(gId);
                    foreach (int cid in childIds)
                    {
                        childIdsInGroups.Add(cid);
                    }
                }
                recipients = recipients.Where(r => childIdsInGroups.Contains(r.ChildId)).ToList();
            }

            return Ok(recipients.Select(MapRecipientOption));
        }

        [HttpPost("instructor/{instructorId:int}/invitations")]
        public async Task<IActionResult> CreateInvitation(int instructorId, [FromBody] CreateLessonInvitationRequest request)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            if (!TryParseIsoDate(request.MeetingDate, out DateTime meetingDate))
            {
                return BadRequest(new { message = "MeetingDate must be in yyyy-MM-dd format." });
            }

            if (!TryParseHourMinute(request.StartTime, out TimeSpan startTime) ||
                !TryParseHourMinute(request.EndTime, out TimeSpan endTime))
            {
                return BadRequest(new { message = "StartTime and EndTime must be in HH:mm format." });
            }

            if (startTime >= endTime)
            {
                return BadRequest(new { message = "StartTime must be earlier than EndTime." });
            }

            if (string.Equals(request.LessonType, "Group", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Capacity < 2 || request.Capacity > 20)
                {
                    return BadRequest(new { message = "קיבולת בשיעור קבוצתי חייבת להיות בין 2 ל-20." });
                }
                if (request.MinRegistrations < 2 || request.MinRegistrations > 20)
                {
                    return BadRequest(new { message = "מינימום נרשמים בשיעור קבוצתי חייב להיות בין 2 ל-20." });
                }
                if (request.MinRegistrations > request.Capacity)
                {
                    return BadRequest(new { message = "מינימום נרשמים לא יכול להיות גדול מהקיבולת." });
                }
            }

            List<LessonInvitationRecipientSelectionRecord> recipients = (request.Recipients ?? new List<RecipientSelectionRequest>())
                .Where((item) => item.ParentId > 0 && item.ChildId > 0)
                .Select((item) => new LessonInvitationRecipientSelectionRecord
                {
                    ParentId = item.ParentId,
                    ChildId = item.ChildId
                })
                .ToList();

            try
            {
                DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);

                LessonInvitationRecord invitation = _db.PostCreateLessonInvitation(
                    instructorId,
                    request.LessonType,
                    meetingDate,
                    startTime,
                    endTime,
                    request.GroupId,
                    request.GeneralNote ?? string.Empty,
                    request.TargetMetric ?? string.Empty,
                    request.Capacity,
                    request.MinRegistrations,
                    recipients);

                await TrySendNewLessonNotificationsPushAsync(notificationsStartUtc, invitation.InvitationId);

                return Created(string.Empty, MapInvitation(invitation));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("instructor/{instructorId:int}/invitations/{invitationId:int}/cancel")]
        public async Task<IActionResult> CancelInvitation(int instructorId, int invitationId, [FromBody] CancelLessonInvitationRequest request)
        {
            if (instructorId <= 0 || invitationId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or invitation id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);

            bool canceled = _db.PostCancelLessonInvitation(instructorId, invitationId, request?.Reason ?? string.Empty);
            if (!canceled)
            {
                return NotFound(new { message = "Invitation was not found." });
            }

            await TrySendNewLessonNotificationsPushAsync(notificationsStartUtc, invitationId);

            return Ok(new { invitationId, status = "Cancelled" });
        }

        [HttpGet("instructor/{instructorId:int}/notifications")]
        public IActionResult GetInstructorNotifications(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<LessonNotificationRecord> notifications = _db.GetUserLessonNotifications("Instructor", instructorId);
            return Ok(notifications.Select(MapNotification));
        }

        [HttpPost("instructor/{instructorId:int}/notifications/mark-read")]
        public IActionResult MarkInstructorNotificationsAsRead(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            int markedCount = _db.PostMarkUserLessonNotificationsAsRead("Instructor", instructorId);
            return Ok(new { markedCount });
        }

        [HttpPost("instructor/{instructorId:int}/broadcast")]
        public async Task<IActionResult> BroadcastToInstructorParents(int instructorId, [FromBody] InstructorBroadcastRequest request)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            string message = (request.Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                return BadRequest(new { message = "Message is required." });
            }

            if (message.Length > 1200)
            {
                return BadRequest(new { message = "Message is too long (max 1200 characters)." });
            }

            List<int> parentIds;
            if (request.GroupIds != null && request.GroupIds.Count > 0)
            {
                var distinctParents = new List<int>();
                foreach (int gId in request.GroupIds)
                {
                    var parentsForGroup = _db.GetInstructorGroupRecipientParents(instructorId, gId);
                    distinctParents.AddRange(parentsForGroup);
                }
                parentIds = distinctParents.Distinct().ToList();
            }
            else
            {
                parentIds = _db.GetInstructorInvitationRecipientOptions(instructorId)
                    .Select((item) => item.ParentId)
                    .Where((parentId) => parentId > 0)
                    .Distinct()
                    .ToList();
            }

            if (parentIds.Count == 0)
            {
                return Ok(new
                {
                    sentCount = 0,
                    message = "No parent recipients were found for this instructor."
                });
            }

            string instructorName = string.IsNullOrWhiteSpace(request.InstructorName)
                ? "מדריך"
                : request.InstructorName.Trim();

            if (instructorName.Length > 120)
            {
                instructorName = instructorName[..120];
            }

            string payloadJson = JsonSerializer.Serialize(new
            {
                type = "instructor_broadcast",
                instructorId,
                instructorName,
                message,
                icon = "📣"
            });

            const string notificationType = "InstructorBroadcast";
            string notificationTitle = $"📣 הודעה חדשה מ-{instructorName}";

            DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);

            foreach (int parentId in parentIds)
            {
                _db.PostCreateUserLessonNotification(
                    "Parent",
                    parentId,
                    notificationType,
                    notificationTitle,
                    message,
                    relatedInvitationId: null,
                    relatedRecipientId: null,
                    payloadJson);
            }

            HashSet<int> recipientParentIds = parentIds.ToHashSet();
            List<LessonNotificationRecord> createdBroadcastNotifications = _db.GetLessonNotificationsCreatedSince(notificationsStartUtc)
                .Where((item) => string.Equals(item.UserType, "Parent", StringComparison.OrdinalIgnoreCase))
                .Where((item) => recipientParentIds.Contains(item.UserId))
                .Where((item) => string.Equals(item.NotificationType, notificationType, StringComparison.OrdinalIgnoreCase))
                .Where((item) => NotificationMatchesInstructorBroadcast(item.PayloadJson, instructorId))
                .ToList();

            foreach (LessonNotificationRecord notification in createdBroadcastNotifications)
            {
                await TrySendLessonNotificationPushAsync(notification);
            }

            return Ok(new
            {
                sentCount = parentIds.Count
            });
        }

        [HttpGet("parent/{parentId:int}/notifications")]
        public IActionResult GetParentNotifications(int parentId)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            List<LessonNotificationRecord> notifications = _db.GetUserLessonNotifications("Parent", parentId);
            return Ok(notifications.Select(MapNotification));
        }

        [HttpPost("parent/{parentId:int}/notifications/mark-read")]
        public IActionResult MarkParentNotificationsAsRead(int parentId)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            int markedCount = _db.PostMarkUserLessonNotificationsAsRead("Parent", parentId);
            return Ok(new { markedCount });
        }

        [HttpPost("parent/{parentId:int}/notifications/chat/mark-read")]
        public IActionResult MarkParentChatNotificationsAsRead(int parentId, [FromBody] MarkParentChatNotificationsAsReadRequest request)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (request == null || request.InstructorId <= 0)
            {
                return BadRequest(new { message = "InstructorId is required." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            List<LessonNotificationRecord> notifications = _db.GetUserLessonNotifications("Parent", parentId);
            List<int> notificationIdsToMark = notifications
                .Where((item) => !item.IsRead)
                .Where((item) => string.Equals(item.NotificationType, "ChatMessage", StringComparison.OrdinalIgnoreCase))
                .Where((item) => NotificationMatchesInstructorChat(item.PayloadJson, request.InstructorId))
                .Select((item) => item.NotificationId)
                .Distinct()
                .ToList();

            int markedCount = _db.PostMarkUserLessonNotificationsAsReadByIds("Parent", parentId, notificationIdsToMark);
            return Ok(new { markedCount });
        }

        [HttpPost("parent/{parentId:int}/recipients/{recipientId:int}/respond")]
        public async Task<IActionResult> RespondToInvitation(int parentId, int recipientId, [FromBody] RespondToInvitationRequest request)
        {
            if (parentId <= 0 || recipientId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id or recipient id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            string action = (request?.Action ?? string.Empty).Trim();
            if (!string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(action, "approved", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(action, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Action must be either approve or reject." });
            }

            try
            {
                DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);
                LessonInvitationActionResult result = _db.PostRespondToLessonInvitation(parentId, recipientId, action);

                await TrySendNewLessonNotificationsPushAsync(notificationsStartUtc, result.InvitationId);

                return Ok(new
                {
                    invitationId = result.InvitationId,
                    recipientId = result.RecipientId,
                    responseStatus = result.ResponseStatus,
                    invitationStatus = result.InvitationStatus
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task TrySendNewLessonNotificationsPushAsync(DateTime createdSinceUtc, int? relatedInvitationId = null)
        {
            try
            {
                List<LessonNotificationRecord> notifications = _db.GetLessonNotificationsCreatedSince(createdSinceUtc, relatedInvitationId);
                if (notifications.Count == 0)
                {
                    return;
                }

                foreach (LessonNotificationRecord notification in notifications)
                {
                    await TrySendLessonNotificationPushAsync(notification);
                }
            }
            catch
            {
                // Push failures should never block API operations.
            }
        }

        private async Task TrySendLessonNotificationPushAsync(LessonNotificationRecord notification)
        {
            string normalizedUserType = string.IsNullOrWhiteSpace(notification.UserType)
                ? string.Empty
                : notification.UserType.Trim();

            if (notification.UserId <= 0
                || (!string.Equals(normalizedUserType, "Parent", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedUserType, "Instructor", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            List<string> tokens = _db.GetActivePushTokensForUser(normalizedUserType, notification.UserId);
            if (tokens.Count == 0)
            {
                return;
            }

            string title = string.IsNullOrWhiteSpace(notification.Title)
                ? "עדכון חדש"
                : notification.Title.Trim();

            string body = string.IsNullOrWhiteSpace(notification.Body)
                ? "יש עדכון חדש במערכת."
                : notification.Body.Trim();

            if (body.Length > 220)
            {
                body = body[..217] + "...";
            }

            object[] payload = tokens.Select((token) => new
            {
                to = token,
                title,
                body,
                sound = "default",
                data = new
                {
                    type = "lesson_notification",
                    notificationType = notification.NotificationType,
                    notificationId = notification.NotificationId,
                    relatedInvitationId = notification.RelatedInvitationId,
                    relatedRecipientId = notification.RelatedRecipientId,
                    userType = normalizedUserType,
                    userId = notification.UserId
                }
            }).ToArray();

            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string expoAccessToken = (_configuration["Expo:AccessToken"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(expoAccessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expoAccessToken);
            }

            string json = JsonSerializer.Serialize(payload);
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync("https://exp.host/--/api/v2/push/send", content);

            _ = response.IsSuccessStatusCode;
        }

        private static object MapInvitation(LessonInvitationRecord invitation)
        {
            return new
            {
                invitationId = invitation.InvitationId,
                instructorId = invitation.InstructorId,
                lessonType = invitation.LessonType,
                groupId = invitation.GroupId,
                meetingDate = invitation.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                startTime = invitation.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                endTime = invitation.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                capacity = invitation.Capacity,
                minRegistrations = invitation.MinRegistrations,
                generalNote = invitation.GeneralNote,
                targetMetric = invitation.TargetMetric,
                status = invitation.Status,
                recipientsCount = invitation.RecipientsCount,
                approvedCount = invitation.ApprovedCount,
                rejectedCount = invitation.RejectedCount,
                pendingCount = invitation.PendingCount,
                createdAt = invitation.CreatedAtUtc,
                hasReport = invitation.HasReport
            };
        }

        private static object MapNotification(LessonNotificationRecord notification)
        {
            return new
            {
                notificationId = notification.NotificationId,
                userType = notification.UserType,
                userId = notification.UserId,
                notificationType = notification.NotificationType,
                title = notification.Title,
                body = notification.Body,
                relatedInvitationId = notification.RelatedInvitationId,
                relatedRecipientId = notification.RelatedRecipientId,
                isRead = notification.IsRead,
                createdAt = notification.CreatedAtUtc,
                readAt = notification.ReadAtUtc,
                payloadJson = notification.PayloadJson,
                lessonType = notification.LessonType,
                meetingDate = notification.MeetingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                startTime = notification.StartTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                endTime = notification.EndTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                invitationStatus = notification.InvitationStatus,
                recipientResponseStatus = notification.RecipientResponseStatus,
                targetMetric = notification.TargetMetric
            };
        }

        private static object MapRecipientOption(LessonInvitationRecipientOptionRecord recipient)
        {
            return new
            {
                parentId = recipient.ParentId,
                childId = recipient.ChildId,
                parentFullName = recipient.ParentFullName,
                parentEmail = recipient.ParentEmail,
                childFullName = recipient.ChildFullName
            };
        }

        private static bool TryParseIsoDate(string rawValue, out DateTime dateValue)
        {
            dateValue = DateTime.MinValue;
            string normalized = (rawValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return DateTime.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateValue);
        }

        private static bool TryParseHourMinute(string rawValue, out TimeSpan timeValue)
        {
            timeValue = TimeSpan.Zero;
            string normalized = (rawValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return TimeSpan.TryParseExact(normalized, @"hh\:mm", CultureInfo.InvariantCulture, out timeValue)
                || TimeSpan.TryParseExact(normalized, @"h\:mm", CultureInfo.InvariantCulture, out timeValue);
        }

        private static bool NotificationMatchesInstructorChat(string payloadJson, int instructorId)
        {
            if (instructorId <= 0 || string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                using JsonDocument jsonDocument = JsonDocument.Parse(payloadJson);
                JsonElement root = jsonDocument.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetJsonPropertyCaseInsensitive(root, "senderType", out JsonElement senderTypeElement))
                {
                    return false;
                }

                string senderType = senderTypeElement.ValueKind == JsonValueKind.String
                    ? (senderTypeElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;

                if (!string.Equals(senderType, "Instructor", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!TryGetJsonPropertyCaseInsensitive(root, "instructorId", out JsonElement instructorIdElement))
                {
                    return false;
                }

                if (!instructorIdElement.TryGetInt32(out int payloadInstructorId))
                {
                    return false;
                }

                return payloadInstructorId == instructorId;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool NotificationMatchesInstructorBroadcast(string payloadJson, int instructorId)
        {
            if (instructorId <= 0 || string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                using JsonDocument jsonDocument = JsonDocument.Parse(payloadJson);
                JsonElement root = jsonDocument.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetJsonPropertyCaseInsensitive(root, "type", out JsonElement typeElement))
                {
                    return false;
                }

                string payloadType = typeElement.ValueKind == JsonValueKind.String
                    ? (typeElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;

                if (!string.Equals(payloadType, "instructor_broadcast", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!TryGetJsonPropertyCaseInsensitive(root, "instructorId", out JsonElement instructorIdElement))
                {
                    return false;
                }

                if (!instructorIdElement.TryGetInt32(out int payloadInstructorId))
                {
                    return false;
                }

                return payloadInstructorId == instructorId;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetJsonPropertyCaseInsensitive(JsonElement source, string propertyName, out JsonElement value)
        {
            if (source.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in source.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        public class CreateLessonInvitationRequest
        {
            public string LessonType { get; set; } = string.Empty;
            public int? GroupId { get; set; }
            public string MeetingDate { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public int Capacity { get; set; } = 20;
            public int MinRegistrations { get; set; } = 2;
            public string? GeneralNote { get; set; }
            public string? TargetMetric { get; set; }
            public List<RecipientSelectionRequest> Recipients { get; set; } = new List<RecipientSelectionRequest>();
        }

        public class RecipientSelectionRequest
        {
            public int ParentId { get; set; }
            public int ChildId { get; set; }
        }

        public class RespondToInvitationRequest
        {
            public string Action { get; set; } = string.Empty;
        }

        public class CancelLessonInvitationRequest
        {
            public string Reason { get; set; } = string.Empty;
        }

        public class InstructorBroadcastRequest
        {
            public string Message { get; set; } = string.Empty;
            public string? InstructorName { get; set; }
            public List<int>? GroupIds { get; set; }
        }

        public class MarkParentChatNotificationsAsReadRequest
        {
            public int InstructorId { get; set; }
        }
    }
}
