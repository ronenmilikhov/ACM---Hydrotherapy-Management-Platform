using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ReactServerSide.DAL;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InstructorController : ControllerBase
    {
        private static readonly string[] WeekdayNames = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };
        private static readonly string[] PreferredMetricOrder =
        {
            "הסתגלות וביטחון במים",
            "שליטה בנשימות (הכנסת ראש למים)",
            "תנועתיות וקואורדינציה",
            "יציבה וציפה",
            "תקשורת במים (ושיתוף פעולה)",
            "התמדה ומאמץ"
        };

        private static readonly string[] PositiveAchievementMarkers =
        {
            "מעולה",
            "מצוין",
            "נהדר",
            "כל הכבוד",
            "התקדמות",
            "שיפור",
            "השתפר",
            "משתפר",
            "מצליח",
            "הצלחה",
            "התמדה",
            "excellent",
            "great",
            "well done",
            "good progress",
            "improved"
        };

        private static readonly string[] NegativeAchievementMarkers =
        {
            "צריך שיפור",
            "דורש שיפור",
            "טעון שיפור",
            "מתקשה",
            "קושי",
            "חלש",
            "נמוך",
            "לא מצליח",
            "לא טוב",
            "בעיית",
            "needs improvement",
            "poor",
            "struggle",
            "bad"
        };

        private readonly DBServices _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public InstructorController(DBServices db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpGet("{instructorId:int}/groups")]
        public IActionResult GetGroupsForInstructor(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<InstructorGroupRecord> groups = _db.GetGroupsForInstructor(instructorId);
            return Ok(groups);
        }

        [HttpGet("{instructorId:int}/groups/{groupId:int}/children")]
        public IActionResult GetGroupChildrenForInstructor(int instructorId, int groupId)
        {
            if (instructorId <= 0 || groupId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or group id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<InstructorGroupRecord> groups = _db.GetGroupsForInstructor(instructorId);
            InstructorGroupRecord? group = groups.FirstOrDefault((item) => item.GroupId == groupId);
            if (group == null)
            {
                return NotFound(new { message = "Group is not assigned to this instructor." });
            }

            List<InstructorChildRecord> children = _db.GetChildrenForInstructorGroup(instructorId, groupId);
            return Ok(new InstructorGroupWithChildrenRecord
            {
                GroupId = group.GroupId,
                GroupName = group.Name,
                GroupDescription = group.Description,
                Children = children
            });
        }

        [HttpGet("{instructorId:int}/children-by-groups")]
        public IActionResult GetChildrenByGroupsForInstructor(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<InstructorGroupWithChildrenRecord> groups = _db.GetChildrenByGroupsForInstructor(instructorId);
            return Ok(groups);
        }

        [HttpGet("{instructorId:int}/training-sessions")]
        public IActionResult GetInstructorTrainingSessions(int instructorId, [FromQuery] string? fromDate = null)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            DateTime startDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(fromDate) && !TryParseIsoDate(fromDate, out startDate))
            {
                return BadRequest(new { message = "fromDate must be in yyyy-MM-dd format." });
            }

            DateTime endDate = startDate.AddDays(6);
            List<InstructorTrainingSessionViewRecord> sessions = _db.GetInstructorTrainingSessions(instructorId, startDate, endDate);

            return Ok(sessions.Select((session) => new
            {
                sessionId = session.SessionId,
                instructorId = session.InstructorId,
                parentId = session.ParentId,
                parentName = session.ParentFullName,
                childId = session.ChildId,
                childName = session.ChildFullName,
                meetingDate = session.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekday = FormatWeekdayName((int)session.MeetingDate.DayOfWeek),
                weekdayOrder = (int)session.MeetingDate.DayOfWeek,
                startTime = FormatTimeAsHourMinute(session.StartTime),
                endTime = FormatTimeAsHourMinute(session.EndTime),
                status = session.Status
            }));
        }

        [HttpPost("{instructorId:int}/training-sessions/{sessionId:int}/cancel")]
        public async Task<IActionResult> CancelInstructorTrainingSession(
            int instructorId,
            int sessionId,
            [FromBody] CancelTrainingSessionRequest? request)
        {
            if (instructorId <= 0 || sessionId == 0)
            {
                return BadRequest(new { message = "Invalid instructor id or session id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            string cancelReason = request?.Reason ?? string.Empty;
            DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-2);

            try
            {
                if (sessionId < 0)
                {
                    int invitationId = Math.Abs(sessionId);
                    bool invitationCanceled = _db.PostCancelLessonInvitation(instructorId, invitationId, cancelReason);
                    if (!invitationCanceled)
                    {
                        return NotFound(new { message = "Training session was not found." });
                    }

                    await TrySendNewLessonNotificationsPushAsync(notificationsStartUtc, invitationId);

                    return Ok(new { sessionId, status = "Cancelled" });
                }

                bool canceled = _db.PostCancelTrainingSessionForInstructor(instructorId, sessionId, cancelReason);
                if (!canceled)
                {
                    return NotFound(new { message = "Training session was not found." });
                }

                await TrySendNewLessonNotificationsPushAsync(
                    notificationsStartUtc,
                    relatedInvitationId: null,
                    notificationTypeFilter: "TrainingSessionCancelled");

                return Ok(new { sessionId, status = "Cancelled" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{instructorId:int}/children/{childId:int}/reports")]
        public IActionResult CreateChildReportForInstructor(int instructorId, int childId, [FromBody] CreateChildReportRequest request)
        {
            if (instructorId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or child id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            if (request.GroupId.HasValue && request.GroupId.Value <= 0)
            {
                return BadRequest(new { message = "Invalid group id." });
            }

            if (!_db.GetChildAssignedToInstructor(instructorId, childId, request.GroupId))
            {
                return NotFound(new { message = "Child is not assigned to this instructor." });
            }

            DateTime reportDate = (request.ReportDate ?? DateTime.UtcNow).Date;
            bool isPresent = request.IsPresent ?? true;

            List<object> normalizedMetrics = isPresent
                ? (request.Metrics ?? new List<ChildReportMetricRequest>())
                    .Where((metric) => !string.IsNullOrWhiteSpace(metric.Label))
                    .Select((metric) => new
                    {
                        label = metric.Label.Trim(),
                        value = Math.Clamp(metric.Value, 0, 10)
                    })
                    .Cast<object>()
                    .ToList()
                : new List<object>();

            string normalizedComment = isPresent
                ? request.Comment?.Trim() ?? string.Empty
                : string.Empty;

            string notesJson = JsonSerializer.Serialize(new
            {
                isPresent,
                metrics = normalizedMetrics,
                comment = normalizedComment
            });

            bool overwriteExisting = request.OverwriteExisting ?? false;
            ChildReportRecord? existingReport = _db.GetChildReportByDateForInstructor(childId, instructorId, reportDate);
            if (existingReport != null)
            {
                if (!overwriteExisting)
                {
                    return Conflict(new
                    {
                        code = "report_exists_for_date",
                        message = "כבר קיים דיווח לילד בתאריך הזה.",
                        existingReportId = existingReport.ReportId,
                        reportDate = reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }

                ChildReportRecord updatedReport = _db.UpdateChildReportForInstructor(
                    existingReport.ReportId,
                    childId,
                    instructorId,
                    request.GroupId,
                    reportDate,
                    notesJson);

                return Ok(updatedReport);
            }

            ChildReportRecord createdReport = _db.PostCreateChildReport(
                childId,
                instructorId,
                request.GroupId,
                reportDate,
                notesJson);

            return Created(string.Empty, createdReport);
        }

        [HttpGet("{instructorId:int}/children/{childId:int}/reports")]
        public IActionResult GetChildReportsForInstructor(int instructorId, int childId)
        {
            if (instructorId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or child id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            if (!_db.GetChildAssignedToInstructor(instructorId, childId))
            {
                return NotFound(new { message = "Child is not assigned to this instructor." });
            }

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            return Ok(reports);
        }

        [HttpPost("{instructorId:int}/children/{childId:int}/reports/summary")]
        public async Task<IActionResult> CreateChildReportsSummaryForInstructor(int instructorId, int childId)
        {
            if (instructorId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or child id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            if (!_db.GetChildAssignedToInstructor(instructorId, childId))
            {
                return NotFound(new { message = "Child is not assigned to this instructor." });
            }

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            ChildRecord? child = _db.GetChildById(childId);
            string childDisplayName = child == null
                ? $"ילד #{childId}"
                : $"{child.FirstName} {child.LastName}".Trim();
            List<MetricMedianRecord> medianMetrics = BuildMedianMetricsFromReports(reports);
            string medianMetricsContext = BuildMedianMetricsContext(medianMetrics);

            if (reports.Count == 0)
            {
                return Ok(new
                {
                    summary = "עדיין אין דיווחים עבור הילד, לכן לא ניתן לייצר סיכום התקדמות מבוסס נתונים.",
                    reportsCount = 0,
                    source = "fallback",
                    medianMetrics
                });
            }

            string primaryApiKey = (_configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty).Trim();
            string secondaryApiKey = (_configuration["OpenAI:ApiKeySecondary"] ?? _configuration["OpenAI:SecondaryApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_SECONDARY") ?? string.Empty).Trim();
            string tertiaryApiKey = (_configuration["OpenAI:ApiKeyTertiary"]
                ?? _configuration["OpenAI:ApiKeyThird"]
                ?? _configuration["OpenAI:ThirdApiKey"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_TERTIARY")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_THIRD")
                ?? string.Empty).Trim();

            List<string> apiKeys = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryApiKey))
            {
                apiKeys.Add(primaryApiKey);
            }

            if (!string.IsNullOrWhiteSpace(secondaryApiKey) &&
                !string.Equals(primaryApiKey, secondaryApiKey, StringComparison.Ordinal))
            {
                apiKeys.Add(secondaryApiKey);
            }

            if (!string.IsNullOrWhiteSpace(tertiaryApiKey)
                && !string.Equals(primaryApiKey, tertiaryApiKey, StringComparison.Ordinal)
                && !string.Equals(secondaryApiKey, tertiaryApiKey, StringComparison.Ordinal))
            {
                apiKeys.Add(tertiaryApiKey);
            }

            if (apiKeys.Count == 0)
            {
                return Ok(new
                {
                    summary = BuildFallbackSummaryFromReports(childDisplayName, reports, medianMetrics),
                    reportsCount = reports.Count,
                    source = "fallback",
                    warning = BuildMissingAiSummaryWarning(),
                    medianMetrics
                });
            }

            string apiUrl = (_configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (_configuration["OpenAI:Model"] ?? "gpt-4.1-mini").Trim();

            string recentPresentCommentsContext = BuildLastPresentInstructorCommentsContext(reports, 3);
            double overallMetricAverage = CalculateOverallMetricAverage(medianMetrics);
            string systemPrompt = BuildChildProgressSummarySystemPrompt();
            string userPrompt = BuildChildProgressSummaryUserPrompt(
                childDisplayName,
                childId,
                medianMetricsContext,
                recentPresentCommentsContext,
                overallMetricAverage);

            object requestPayload = new
            {
                model,
                temperature = 0.1,
                top_p = 0.2,
                max_tokens = 250,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            HttpClient client = _httpClientFactory.CreateClient();
            string requestJson = JsonSerializer.Serialize(requestPayload);
            List<string> failedAttempts = new List<string>();

            for (int index = 0; index < apiKeys.Count; index += 1)
            {
                string currentApiKey = apiKeys[index];
                (bool isSuccess, string summary, string failureReason) =
                    await TryCreateSummaryWithApiKeyAsync(client, apiUrl, currentApiKey, requestJson);

                if (isSuccess)
                {
                    return Ok(new
                    {
                        summary,
                        reportsCount = reports.Count,
                        source = "ai",
                        medianMetrics
                    });
                }

                failedAttempts.Add($"key #{index + 1}: {failureReason}");
            }

            return Ok(new
            {
                summary = BuildFallbackSummaryFromReports(childDisplayName, reports, medianMetrics),
                reportsCount = reports.Count,
                source = "fallback",
                warning = BuildAiSummaryFailureWarning(failedAttempts),
                details = failedAttempts,
                medianMetrics
            });
        }

        [HttpPost("{instructorId:int}/children/{childId:int}/reports/comment-achievement")]
        public async Task<IActionResult> AnalyzeCommentAchievementForInstructor(
            int instructorId,
            int childId,
            [FromBody] AnalyzeCommentsForAchievementRequest? request)
        {
            if (instructorId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id or child id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            if (!_db.GetChildAssignedToInstructor(instructorId, childId))
            {
                return NotFound(new { message = "Child is not assigned to this instructor." });
            }

            List<string> comments = (request?.Comments ?? new List<CommentForAchievementRequest>())
                .Select((item) => item?.Text?.Trim() ?? string.Empty)
                .Where((text) => !string.IsNullOrWhiteSpace(text))
                .Take(3)
                .ToList();

            if (comments.Count == 0)
            {
                return Ok(new
                {
                    hasAchievement = false,
                    commentIndex = 0,
                    selectedComment = string.Empty,
                    positiveWords = string.Empty,
                    source = "fallback",
                    reason = "no_comments"
                });
            }

            string primaryApiKey = (_configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty).Trim();
            string secondaryApiKey = (_configuration["OpenAI:ApiKeySecondary"] ?? _configuration["OpenAI:SecondaryApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_SECONDARY") ?? string.Empty).Trim();
            string tertiaryApiKey = (_configuration["OpenAI:ApiKeyTertiary"]
                ?? _configuration["OpenAI:ApiKeyThird"]
                ?? _configuration["OpenAI:ThirdApiKey"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_TERTIARY")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY_THIRD")
                ?? string.Empty).Trim();

            List<string> apiKeys = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryApiKey))
            {
                apiKeys.Add(primaryApiKey);
            }

            if (!string.IsNullOrWhiteSpace(secondaryApiKey) &&
                !string.Equals(primaryApiKey, secondaryApiKey, StringComparison.Ordinal))
            {
                apiKeys.Add(secondaryApiKey);
            }

            if (!string.IsNullOrWhiteSpace(tertiaryApiKey)
                && !string.Equals(primaryApiKey, tertiaryApiKey, StringComparison.Ordinal)
                && !string.Equals(secondaryApiKey, tertiaryApiKey, StringComparison.Ordinal))
            {
                apiKeys.Add(tertiaryApiKey);
            }

            if (apiKeys.Count == 0)
            {
                if (TryBuildCommentAchievementFallback(comments, out int fallbackIndex, out string fallbackComment, out string fallbackSentence, out string fallbackWords))
                {
                    return Ok(new
                    {
                        hasAchievement = true,
                        commentIndex = fallbackIndex,
                        selectedComment = fallbackComment,
                        achievementSentence = fallbackSentence,
                        positiveWords = fallbackWords,
                        source = "fallback",
                        reason = "missing_api_key"
                    });
                }

                return Ok(new
                {
                    hasAchievement = false,
                    commentIndex = 0,
                    selectedComment = string.Empty,
                    positiveWords = string.Empty,
                    source = "fallback",
                    reason = "missing_api_key"
                });
            }

            string apiUrl = (_configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (_configuration["OpenAI:Model"] ?? "gpt-4.1-mini").Trim();

            object requestPayload = new
            {
                model,
                temperature = 0.0,
                top_p = 0.1,
                max_tokens = 150,
                messages = new object[]
                {
                    new { role = "system", content = BuildCommentAchievementSystemPrompt() },
                    new { role = "user", content = BuildCommentAchievementUserPrompt(comments) }
                }
            };

            HttpClient client = _httpClientFactory.CreateClient();
            string requestJson = JsonSerializer.Serialize(requestPayload);
            List<string> failedAttempts = new List<string>();

            for (int index = 0; index < apiKeys.Count; index += 1)
            {
                string currentApiKey = apiKeys[index];
                (bool isSuccess, string modelResponse, string failureReason) =
                    await TryCreateSummaryWithApiKeyAsync(client, apiUrl, currentApiKey, requestJson);

                if (!isSuccess)
                {
                    failedAttempts.Add($"key #{index + 1}: {failureReason}");
                    continue;
                }

                if (!TryExtractCommentAchievementDecision(modelResponse, out CommentAchievementDecision decision))
                {
                    failedAttempts.Add($"key #{index + 1}: invalid response format");
                    continue;
                }

                int commentIndex = Math.Clamp(decision.CommentIndex, 0, comments.Count);
                bool hasAchievement = decision.IsAchievement || commentIndex > 0 || !string.IsNullOrWhiteSpace(decision.SelectedSentence);
                if (!hasAchievement)
                {
                    failedAttempts.Add($"key #{index + 1}: no achievement found");
                    continue;
                }

                if (commentIndex <= 0)
                {
                    commentIndex = 1;
                }

                string selectedComment = comments[commentIndex - 1];
                string rawAchievementSentence = string.IsNullOrWhiteSpace(decision.SelectedSentence)
                    ? selectedComment
                    : decision.SelectedSentence.Trim();

                string achievementSentence = ExtractPositiveAchievementFragment(rawAchievementSentence);
                if (string.IsNullOrWhiteSpace(achievementSentence))
                {
                    achievementSentence = ExtractPositiveAchievementFragment(selectedComment);
                }

                if (string.IsNullOrWhiteSpace(achievementSentence))
                {
                    failedAttempts.Add($"key #{index + 1}: no positive fragment");
                    continue;
                }

                return Ok(new
                {
                    hasAchievement = true,
                    commentIndex,
                    selectedComment,
                    achievementSentence,
                    positiveWords = decision.PositiveWords,
                    source = "ai"
                });
            }

            string warningMessage = apiKeys.Count > 1
                ? $"AI comment achievement analysis failed for all {apiKeys.Count} API keys."
                : "AI comment achievement analysis failed.";

            if (failedAttempts.Count > 0)
            {
                warningMessage = warningMessage + " Details: " + string.Join("; ", failedAttempts);
            }

            if (TryBuildCommentAchievementFallback(comments, out int fallbackCommentIndex, out string fallbackSelectedComment, out string fallbackAchievementSentence, out string fallbackPositiveWords))
            {
                return Ok(new
                {
                    hasAchievement = true,
                    commentIndex = fallbackCommentIndex,
                    selectedComment = fallbackSelectedComment,
                    achievementSentence = fallbackAchievementSentence,
                    positiveWords = fallbackPositiveWords,
                    source = "fallback",
                    warning = warningMessage,
                    reason = "heuristic_comment_fallback"
                });
            }

            return Ok(new
            {
                hasAchievement = false,
                commentIndex = 0,
                selectedComment = string.Empty,
                achievementSentence = string.Empty,
                positiveWords = string.Empty,
                source = "fallback",
                warning = warningMessage
            });
        }

        [Authorize(Roles = "Manager")]
        [HttpGet("manager-list")]
        public IActionResult GetInstructorsForManager()
        {
            List<ManagerInstructorRecord> instructors = _db.GetInstructorsForManager();
            return Ok(instructors);
        }

        [Authorize(Roles = "Manager")]
        [HttpPost("manager-create")]
        public IActionResult CreateInstructor([FromBody] CreateInstructorRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName))
            {
                return BadRequest(new { message = "Email, password, first name and last name are required." });
            }

            string targetEmail = request.Email.Trim().ToLowerInvariant();
            string firstName = request.FirstName.Trim();
            string lastName = request.LastName.Trim();

            try
            {
                int newId = _db.PostCreateInstructor(targetEmail, request.Password, firstName, lastName);

                return Created(string.Empty, new
                {
                    id = newId,
                    email = targetEmail,
                    role = "Instructor"
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [Authorize(Roles = "Manager")]
        [HttpPut("manager-update/{id:int}")]
        public IActionResult UpdateInstructorForManager(int id, [FromBody] UpdateInstructorRequest request)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                return BadRequest(new { message = "First name and last name are required." });
            }

            bool updated = _db.PutUpdateInstructorNames(id, request.FirstName.Trim(), request.LastName.Trim());
            if (!updated)
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<ManagerInstructorRecord> instructors = _db.GetInstructorsForManager();
            ManagerInstructorRecord? instructor = instructors.FirstOrDefault((item) => item.InstructorId == id);
            if (instructor == null)
            {
                return Ok(new { instructorId = id, firstName = request.FirstName.Trim(), lastName = request.LastName.Trim() });
            }

            return Ok(instructor);
        }

        [Authorize(Roles = "Manager")]
        [HttpDelete("manager-delete/{id:int}")]
        public IActionResult DeleteInstructorForManager(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            try
            {
                bool deleted = _db.DeleteDeactivateInstructorForManager(id);
                if (!deleted)
                {
                    return NotFound(new { message = "Instructor was not found." });
                }

                return NoContent();
            }
            catch (SqlException ex)
            {
                return BadRequest(new
                {
                    message = "Cannot delete instructor because related records still exist.",
                    details = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Unexpected error while deleting instructor." });
            }
        }

        private async Task TrySendNewLessonNotificationsPushAsync(
            DateTime createdSinceUtc,
            int? relatedInvitationId = null,
            string? notificationTypeFilter = null)
        {
            try
            {
                IEnumerable<LessonNotificationRecord> notifications = _db.GetLessonNotificationsCreatedSince(createdSinceUtc, relatedInvitationId);

                if (!string.IsNullOrWhiteSpace(notificationTypeFilter))
                {
                    notifications = notifications.Where((item) =>
                        string.Equals(item.NotificationType, notificationTypeFilter, StringComparison.OrdinalIgnoreCase));
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

        private static string BuildChildProgressSummarySystemPrompt()
        {
            return @"
אתה מדריך שחייה מקצועי ואמפתי שכותב משוב להורים של ילדים עם קשיים.
כתוב בעברית בלבד, בשפה מכבדת, תומכת ולא שיפוטית.
העברית חייבת להיות תקנית ומדויקת ככל האפשר: דקדוק נכון, תחביר ברור, פיסוק תקין וכתיב מלא תקני.
אין לתת אבחנה רפואית, אין להשתמש בשפה פוגעת, ואין לקבוע קביעות שאינן נתמכות בנתונים.
המטרה: לסכם להורה בצורה ברורה מה הילד עושה טוב, מה צריך לחזק, ומה כדאי לתרגל בהמשך.
".Trim();
        }

        private static string BuildInstructorProgressSummarySystemPrompt()
        {
            return @"
אתה מדריך שחייה שכותב סיכום לעצמו על תלמיד.
כתוב בעברית קצרה וממוקדת. הייה תמציתי.
DO NOT WRITE ANY INNER THOUGHTS OR REASONING. OUTPUT *ONLY* THE FINAL HEBREW TEXT.
".Trim();
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

        private static string FormatWeekdayName(int weekday)
        {
            if (weekday < 0 || weekday >= WeekdayNames.Length)
            {
                return string.Empty;
            }

            return WeekdayNames[weekday];
        }

        private static string FormatTimeAsHourMinute(TimeSpan timeValue)
        {
            return timeValue.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        private static string BuildChildProgressSummaryUserPrompt(
            string childDisplayName,
            int childId,
            string medianMetricsContext,
            string recentPresentCommentsContext,
            double overallMetricAverage)
        {
            return $@"
ילד: {childDisplayName} (מזהה {childId})

הנתונים הזמינים היחידים לניתוח:
1) חציון מדדים רשמי (0-10):
{medianMetricsContext}

2) ממוצע כללי מחושב מהמדדים: {overallMetricAverage.ToString("0.0", CultureInfo.InvariantCulture)}/10

3) שלוש הערות המדריך האחרונות ממפגשים שבהם IsPresent=true בלבד:
{recentPresentCommentsContext}

משימה:
1) התחל בדיוק בסעיף כותרת: ""ממוצע ציונים במדדים"".
2) מתחת לכותרת זו כתוב תחילה את הממוצע הכללי, ואז רשימת ציונים לפי המדדים שסופקו.
3) כתוב סעיף: ""מה הילד עושה טוב"" עם 2-4 נקודות קצרות.
4) כתוב סעיף: ""מה הילד צריך לשפר"" עם מדדים בלבד שציונם קטן או שווה ל-6 (<=6), ובשפה עדינה.
5) כתוב בסוף סעיף קצר: ""המלצה להורה"" עם 1-2 פעולות פרקטיות.

כללים מחייבים:
- להסתמך רק על הנתונים שסופקו כאן.
- לא להמציא מספרים חדשים.
- טון אמפתי, מחזק ומכבד כלפי הילד וההורה.
- בסעיף ""מה הילד צריך לשפר"" אסור לכלול מדד שציונו גבוה מ-6.
- אם אין מדדים בציון <=6, כתוב שאין כרגע מדדים שדורשים חיזוק ממוקד.
".Trim();
        }

        private static string BuildCommentAchievementSystemPrompt()
        {
            return @"
You are a strict JSON-only classifier. You receive instructor comments about a child's progress.
Your task: decide if any comment contains a clear positive achievement.

Reply with ONLY a raw JSON object (no markdown, no explanation, no code fences):
{""ok"":1,""i"":N,""s"":""..."",""w"":""...""}

Fields:
- ok: 1 if any comment is clearly positive/praising/encouraging. 0 if none are positive.
- i: the 1-based index of the best positive comment (1, 2, or 3). 0 if ok=0.
- s: ONLY the positive fragment from that comment. If the comment mixes positive and negative parts, keep only the positive part and remove the negative text.
- w: up to 4 key positive words from that sentence, comma separated. Empty string if ok=0.

Rules:
- If a comment is mostly criticism/needs-improvement, do NOT mark it as positive.
- If there is no clear positive fragment, return ok=0.
- Never return a full negative or mixed sentence in s.

If no comment is positive, return: {""ok"":0,""i"":0,""s"":"""",""w"":""""}
If a comment says something like 'great progress' or 'did well', that IS positive (ok=1).
".Trim();
        }

        private static string ExtractPositiveAchievementFragment(string rawComment)
        {
            string normalizedComment = Regex.Replace(rawComment ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(normalizedComment))
            {
                return string.Empty;
            }

            List<string> segments = SplitCommentIntoCandidateSegments(normalizedComment);
            if (segments.Count == 0)
            {
                segments.Add(normalizedComment);
            }

            string bestSegment = segments
                .Select((segment) => new
                {
                    Segment = segment,
                    PositiveScore = CountMarkerMatches(segment, PositiveAchievementMarkers),
                    NegativeScore = CountMarkerMatches(segment, NegativeAchievementMarkers)
                })
                .Where((item) => item.PositiveScore > 0 && item.PositiveScore > item.NegativeScore)
                .OrderByDescending((item) => item.PositiveScore - item.NegativeScore)
                .ThenBy((item) => item.Segment.Length)
                .Select((item) => item.Segment)
                .FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(bestSegment))
            {
                return bestSegment;
            }

            if (ContainsAnyMarker(normalizedComment, PositiveAchievementMarkers) &&
                !ContainsAnyMarker(normalizedComment, NegativeAchievementMarkers))
            {
                return normalizedComment;
            }

            return string.Empty;
        }

        private static List<string> SplitCommentIntoCandidateSegments(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return new List<string>();
            }

            string[] splitParts = Regex.Split(
                comment,
                @"[\.!?;\n\r]+|(?:\s+אבל\s+)|(?:\s+אך\s+)|(?:\s+עם\s+זאת\s+)|(?:\s+יחד\s+עם\s+זאת\s+)|(?:\s+however\s+)|(?:\s+but\s+)|(?:\s+though\s+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return splitParts
                .Select((part) => Regex.Replace(part ?? string.Empty, @"\s+", " ").Trim())
                .Where((part) => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static int CountMarkerMatches(string text, string[] markers)
        {
            if (string.IsNullOrWhiteSpace(text) || markers.Length == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (string marker in markers)
            {
                if (!string.IsNullOrWhiteSpace(marker) &&
                    text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count += 1;
                }
            }

            return count;
        }

        private static bool ContainsAnyMarker(string text, string[] markers)
        {
            return CountMarkerMatches(text, markers) > 0;
        }

        private static string BuildCommentAchievementUserPrompt(List<string> comments)
        {
            StringBuilder promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Comments:");

            for (int index = 0; index < comments.Count; index += 1)
            {
                promptBuilder.AppendLine($"{index + 1}. {comments[index]}");
            }

            return promptBuilder.ToString().Trim();
        }

        private static bool TryBuildCommentAchievementFallback(
            List<string> comments,
            out int commentIndex,
            out string selectedComment,
            out string achievementSentence,
            out string positiveWords)
        {
            commentIndex = 0;
            selectedComment = string.Empty;
            achievementSentence = string.Empty;
            positiveWords = string.Empty;

            if (comments == null || comments.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < comments.Count; index += 1)
            {
                string comment = (comments[index] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(comment))
                {
                    continue;
                }

                string positiveFragment = ExtractPositiveAchievementFragment(comment);
                if (string.IsNullOrWhiteSpace(positiveFragment))
                {
                    continue;
                }

                commentIndex = index + 1;
                selectedComment = comment;
                achievementSentence = positiveFragment;
                positiveWords = BuildPositiveWordsFromSentence(positiveFragment);
                return true;
            }

            string firstComment = (comments[0] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(firstComment))
            {
                return false;
            }

            commentIndex = 1;
            selectedComment = firstComment;
            achievementSentence = firstComment;
            positiveWords = BuildPositiveWordsFromSentence(firstComment);
            return true;
        }

        private static string BuildPositiveWordsFromSentence(string sentence)
        {
            List<string> words = Regex.Matches(sentence ?? string.Empty, @"[\p{L}\p{N}']+")
                .Select((match) => (match.Value ?? string.Empty).Trim())
                .Where((word) => word.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            return string.Join(", ", words);
        }

        private static string BuildLastPresentInstructorCommentsContext(List<ChildReportRecord> reports, int maxComments)
        {
            List<string> comments = GetLastPresentInstructorComments(reports, maxComments);
            if (comments.Count == 0)
            {
                return "אין הערות מדריך זמינות ממפגשים שבהם הילד נכח.";
            }

            return string.Join(Environment.NewLine, comments.Select((comment) => $"- {comment}"));
        }

        private static List<string> GetLastPresentInstructorComments(List<ChildReportRecord> reports, int maxComments)
        {
            int safeMaxComments = Math.Max(1, maxComments);
            List<string> comments = new List<string>();

            foreach (ChildReportRecord report in reports
                .OrderByDescending((item) => item.ReportDate)
                .ThenByDescending((item) => item.ReportId))
            {
                if (!TryExtractIsPresentAndComment(report.Notes, out bool isPresent, out string comment))
                {
                    continue;
                }

                if (!isPresent || string.IsNullOrWhiteSpace(comment))
                {
                    continue;
                }

                comments.Add($"{report.ReportDate:yyyy-MM-dd}: {comment}");
                if (comments.Count >= safeMaxComments)
                {
                    break;
                }
            }

            return comments;
        }

        private static bool TryExtractIsPresentAndComment(string rawNotes, out bool isPresent, out string comment)
        {
            isPresent = false;
            comment = string.Empty;

            if (string.IsNullOrWhiteSpace(rawNotes))
            {
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(rawNotes);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("isPresent", out JsonElement isPresentElement))
                {
                    return false;
                }

                isPresent = ReadBooleanValue(isPresentElement);

                if (root.TryGetProperty("comment", out JsonElement commentElement) && commentElement.ValueKind == JsonValueKind.String)
                {
                    comment = (commentElement.GetString() ?? string.Empty).Trim();
                }

                if (string.IsNullOrWhiteSpace(comment) &&
                    root.TryGetProperty("generalNotes", out JsonElement generalNotesElement) &&
                    generalNotesElement.ValueKind == JsonValueKind.String)
                {
                    comment = (generalNotesElement.GetString() ?? string.Empty).Trim();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBooleanValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetInt32(out int numberValue) => numberValue != 0,
                JsonValueKind.String when bool.TryParse((element.GetString() ?? string.Empty).Trim(), out bool parsedBool) => parsedBool,
                JsonValueKind.String when int.TryParse((element.GetString() ?? string.Empty).Trim(), out int parsedInt) => parsedInt != 0,
                _ => false
            };
        }

        private static string BuildMedianMetricsContext(List<MetricMedianRecord> metrics)
        {
            if (metrics.Count == 0)
            {
                return "אין נתוני מדדים זמינים.";
            }

            StringBuilder builder = new StringBuilder();
            foreach (MetricMedianRecord metric in metrics)
            {
                builder.AppendLine($"- {metric.Label}: {metric.Value.ToString("0.#", CultureInfo.InvariantCulture)}");
            }

            return builder.ToString().Trim();
        }

        private static List<MetricMedianRecord> BuildMedianMetricsFromReports(List<ChildReportRecord> reports)
        {
            Dictionary<string, List<double>> metricValuesByLabel = PreferredMetricOrder
                .ToDictionary((label) => label, (_) => new List<double>(), StringComparer.OrdinalIgnoreCase);

            foreach (ChildReportRecord report in reports)
            {
                if (string.IsNullOrWhiteSpace(report.Notes))
                {
                    continue;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(report.Notes);
                    if (!doc.RootElement.TryGetProperty("metrics", out JsonElement metricsElement) || metricsElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    int index = 0;
                    foreach (JsonElement metric in metricsElement.EnumerateArray())
                    {
                        double? parsedValue = TryGetNumericMetricValue(metric);
                        if (!parsedValue.HasValue)
                        {
                            index += 1;
                            continue;
                        }

                        double normalizedValue = NormalizeProgressValue(parsedValue.Value);

                        string rawLabel = metric.TryGetProperty("label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
                            ? (labelElement.GetString() ?? string.Empty).Trim()
                            : string.Empty;

                        string normalizedLabel = rawLabel;
                        if (string.IsNullOrWhiteSpace(normalizedLabel) || !metricValuesByLabel.ContainsKey(normalizedLabel))
                        {
                            if (index >= 0 && index < PreferredMetricOrder.Length)
                            {
                                normalizedLabel = PreferredMetricOrder[index];
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(normalizedLabel) && metricValuesByLabel.ContainsKey(normalizedLabel))
                        {
                            metricValuesByLabel[normalizedLabel].Add(normalizedValue);
                        }

                        index += 1;
                    }
                }
                catch
                {
                    // Ignore malformed notes rows so one bad report does not break aggregation.
                }
            }

            List<MetricMedianRecord> medianMetrics = new List<MetricMedianRecord>();
            foreach (string metricLabel in PreferredMetricOrder)
            {
                List<double> values = metricValuesByLabel[metricLabel];
                double median = values.Count == 0 ? 0 : CalculateMedian(values);
                medianMetrics.Add(new MetricMedianRecord
                {
                    Label = metricLabel,
                    Value = NormalizeProgressValue(median)
                });
            }

            return medianMetrics;
        }

        private static double? TryGetNumericMetricValue(JsonElement metricElement)
        {
            if (!metricElement.TryGetProperty("value", out JsonElement valueElement))
            {
                return null;
            }

            if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDouble(out double numberValue))
            {
                return numberValue;
            }

            if (valueElement.ValueKind == JsonValueKind.String)
            {
                string valueText = (valueElement.GetString() ?? string.Empty).Trim();
                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
                {
                    return parsedValue;
                }

                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedValue))
                {
                    return parsedValue;
                }
            }

            return null;
        }

        private static double CalculateMedian(List<double> values)
        {
            List<double> sortedValues = values.OrderBy((value) => value).ToList();
            int middleIndex = sortedValues.Count / 2;

            if (sortedValues.Count % 2 == 1)
            {
                return sortedValues[middleIndex];
            }

            return (sortedValues[middleIndex - 1] + sortedValues[middleIndex]) / 2.0;
        }

        private static double NormalizeProgressValue(double rawValue)
        {
            if (!double.IsFinite(rawValue))
            {
                return 0;
            }

            return Math.Round(Math.Clamp(rawValue, 0, 10), MidpointRounding.AwayFromZero);
        }

        private static double CalculateOverallMetricAverage(List<MetricMedianRecord> metrics)
        {
            if (metrics.Count == 0)
            {
                return 0;
            }

            double average = metrics.Average((metric) => metric.Value);
            return Math.Round(average, 1, MidpointRounding.AwayFromZero);
        }

        private static (List<string> metrics, string comment) ExtractMetricsAndComment(string rawNotes)
        {
            List<string> metrics = new List<string>();
            string comment = string.Empty;

            if (string.IsNullOrWhiteSpace(rawNotes))
            {
                return (metrics, comment);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(rawNotes);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("metrics", out JsonElement metricsElement) && metricsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement metric in metricsElement.EnumerateArray())
                    {
                        string label = metric.TryGetProperty("label", out JsonElement labelElement)
                            ? (labelElement.GetString() ?? string.Empty).Trim()
                            : string.Empty;

                        string valueText = metric.TryGetProperty("value", out JsonElement valueElement)
                            ? GetJsonElementAsText(valueElement)
                            : string.Empty;

                        if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(valueText))
                        {
                            metrics.Add($"{label}: {valueText}");
                        }
                    }
                }

                if (root.TryGetProperty("comment", out JsonElement commentElement) && commentElement.ValueKind == JsonValueKind.String)
                {
                    comment = (commentElement.GetString() ?? string.Empty).Trim();
                }
            }
            catch
            {
                comment = rawNotes.Length > 240
                    ? rawNotes.Substring(0, 240) + "..."
                    : rawNotes;
            }

            return (metrics, comment);
        }

        private static string GetJsonElementAsText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt32(out int intValue) => intValue.ToString(),
                JsonValueKind.Number when element.TryGetDouble(out double doubleValue) => doubleValue.ToString("0.##"),
                JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        private static async Task<(bool IsSuccess, string Summary, string FailureReason)> TryCreateSummaryWithApiKeyAsync(
            HttpClient client,
            string apiUrl,
            string apiKey,
            string requestJson)
        {
            try
            {
                using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };

                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using HttpResponseMessage response = await client.SendAsync(requestMessage, cts.Token);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (false, string.Empty, $"provider returned {(int)response.StatusCode}");
                }

                string summary = ExtractSummaryFromOpenAiResponse(responseJson);
                if (string.IsNullOrWhiteSpace(summary))
                {
                    return (false, string.Empty, "provider returned empty summary");
                }

                return (true, summary, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"request error ({ex.GetType().Name})");
            }
        }

        private static string ExtractSummaryFromOpenAiResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseJson);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    return string.Empty;
                }

                JsonElement firstChoice = choices[0];
                if (!firstChoice.TryGetProperty("message", out JsonElement messageElement))
                {
                    return string.Empty;
                }

                if (!messageElement.TryGetProperty("content", out JsonElement contentElement))
                {
                    return string.Empty;
                }

                string rawResult = string.Empty;
                if (contentElement.ValueKind == JsonValueKind.String)
                {
                    rawResult = (contentElement.GetString() ?? string.Empty).Trim();
                }
                else if (contentElement.ValueKind == JsonValueKind.Array)
                {
                    StringBuilder contentBuilder = new StringBuilder();
                    foreach (JsonElement part in contentElement.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                        {
                            contentBuilder.AppendLine(textElement.GetString());
                        }
                    }

                    rawResult = contentBuilder.ToString().Trim();
                }

                if (!string.IsNullOrWhiteSpace(rawResult))
                {
                    string cleanedResult = Regex.Replace(rawResult, @"<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    return cleanedResult.Trim();
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static bool TryExtractCommentAchievementDecision(string rawResponse, out CommentAchievementDecision decision)
        {
            decision = new CommentAchievementDecision
            {
                IsAchievement = false,
                CommentIndex = 0,
                SelectedSentence = string.Empty,
                PositiveWords = string.Empty
            };

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return false;
            }

            string normalized = rawResponse.Trim();

            if (normalized.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLineBreak = normalized.IndexOf('\n');
                if (firstLineBreak >= 0)
                {
                    normalized = normalized.Substring(firstLineBreak + 1);
                }

                int closingFenceIndex = normalized.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFenceIndex >= 0)
                {
                    normalized = normalized.Substring(0, closingFenceIndex);
                }

                normalized = normalized.Trim();
            }

            int objectStart = normalized.IndexOf('{');
            int objectEnd = normalized.LastIndexOf('}');
            if (objectStart >= 0 && objectEnd > objectStart)
            {
                normalized = normalized.Substring(objectStart, objectEnd - objectStart + 1);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(normalized);
                JsonElement root = doc.RootElement;

                bool isAchievement = false;
                if (root.TryGetProperty("isAchievement", out JsonElement isAchievementElement))
                {
                    isAchievement = ReadBooleanValue(isAchievementElement);
                }
                else if (root.TryGetProperty("hasAchievement", out JsonElement hasAchievementElement))
                {
                    isAchievement = ReadBooleanValue(hasAchievementElement);
                }
                else if (root.TryGetProperty("ok", out JsonElement okElement))
                {
                    isAchievement = ReadBooleanValue(okElement);
                }

                int commentIndex = 0;
                if (root.TryGetProperty("commentIndex", out JsonElement commentIndexElement))
                {
                    commentIndex = ReadIntegerValue(commentIndexElement);
                }
                else if (root.TryGetProperty("index", out JsonElement indexElement))
                {
                    commentIndex = ReadIntegerValue(indexElement);
                }
                else if (root.TryGetProperty("i", out JsonElement iElement))
                {
                    commentIndex = ReadIntegerValue(iElement);
                }

                string selectedSentence = string.Empty;
                if (root.TryGetProperty("selectedSentence", out JsonElement selectedSentenceElement) && selectedSentenceElement.ValueKind == JsonValueKind.String)
                {
                    selectedSentence = (selectedSentenceElement.GetString() ?? string.Empty).Trim();
                }
                else if (root.TryGetProperty("sentence", out JsonElement sentenceElement) && sentenceElement.ValueKind == JsonValueKind.String)
                {
                    selectedSentence = (sentenceElement.GetString() ?? string.Empty).Trim();
                }
                else if (root.TryGetProperty("s", out JsonElement sElement) && sElement.ValueKind == JsonValueKind.String)
                {
                    selectedSentence = (sElement.GetString() ?? string.Empty).Trim();
                }

                string positiveWords = string.Empty;
                if (root.TryGetProperty("positiveWords", out JsonElement positiveWordsElement) && positiveWordsElement.ValueKind == JsonValueKind.String)
                {
                    positiveWords = (positiveWordsElement.GetString() ?? string.Empty).Trim();
                }
                else if (root.TryGetProperty("words", out JsonElement wordsElement) && wordsElement.ValueKind == JsonValueKind.String)
                {
                    positiveWords = (wordsElement.GetString() ?? string.Empty).Trim();
                }
                else if (root.TryGetProperty("w", out JsonElement wElement) && wElement.ValueKind == JsonValueKind.String)
                {
                    positiveWords = (wElement.GetString() ?? string.Empty).Trim();
                }

                if (!isAchievement && (commentIndex > 0 || !string.IsNullOrWhiteSpace(selectedSentence) || !string.IsNullOrWhiteSpace(positiveWords)))
                {
                    isAchievement = true;
                }

                decision = new CommentAchievementDecision
                {
                    IsAchievement = isAchievement,
                    CommentIndex = commentIndex,
                    SelectedSentence = selectedSentence,
                    PositiveWords = positiveWords
                };

                return true;
            }
            catch
            {
                return false;
            }

            static bool ReadBooleanValue(JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when element.TryGetInt32(out int numberValue) => numberValue != 0,
                    JsonValueKind.String when bool.TryParse((element.GetString() ?? string.Empty).Trim(), out bool parsedBool) => parsedBool,
                    JsonValueKind.String when int.TryParse((element.GetString() ?? string.Empty).Trim(), out int parsedInt) => parsedInt != 0,
                    _ => false
                };
            }

            static int ReadIntegerValue(JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.Number when element.TryGetInt32(out int intValue) => intValue,
                    JsonValueKind.String when int.TryParse((element.GetString() ?? string.Empty).Trim(), out int parsedInt) => parsedInt,
                    _ => 0
                };
            }
        }

        private static string BuildMissingAiSummaryWarning()
        {
            return "לא הוגדר אף מפתח AI בשרת, לכן הוצג סיכום גיבוי מבוסס נתונים.";
        }

        private static string BuildAiSummaryFailureWarning(List<string> failedAttempts)
        {
            if (failedAttempts == null || failedAttempts.Count == 0)
            {
                return "שירות ה-AI לא החזיר תשובה תקינה, לכן הוצג סיכום גיבוי מבוסס נתונים.";
            }

            bool hasRateLimit = failedAttempts.Any((item) =>
                item.Contains("429", StringComparison.OrdinalIgnoreCase)
                || item.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

            bool hasTimeout = failedAttempts.Any((item) =>
                item.Contains("timeout", StringComparison.OrdinalIgnoreCase));

            bool hasEmptySummary = failedAttempts.Any((item) =>
                item.Contains("empty summary", StringComparison.OrdinalIgnoreCase));

            List<string> reasons = new List<string>();
            if (hasRateLimit)
            {
                reasons.Add("זוהתה מגבלת שימוש זמנית בשירות ה-AI");
            }

            if (hasTimeout)
            {
                reasons.Add("התקבלה חריגת זמן מהספק");
            }

            if (hasEmptySummary)
            {
                reasons.Add("הספק החזיר תשובה ריקה");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("כל המפתחות שהוגדרו נכשלו");
            }

            return "הופק סיכום גיבוי מבוסס נתונים כי " + string.Join(", ", reasons) + ".";
        }

        private static string BuildFallbackSummaryFromReports(
            string childDisplayName,
            List<ChildReportRecord> reports,
            List<MetricMedianRecord> medianMetrics)
        {
            List<ChildReportRecord> orderedReports = reports
                .OrderBy((report) => report.ReportDate)
                .ThenBy((report) => report.ReportId)
                .ToList();

            List<MetricMedianRecord> effectiveMedianMetrics = (medianMetrics ?? new List<MetricMedianRecord>())
                .Where((metric) => !string.IsNullOrWhiteSpace(metric.Label))
                .Select((metric) => new MetricMedianRecord
                {
                    Label = metric.Label.Trim(),
                    Value = NormalizeProgressValue(metric.Value)
                })
                .ToList();

            if (effectiveMedianMetrics.Count == 0)
            {
                effectiveMedianMetrics = PreferredMetricOrder
                    .Select((label) => new MetricMedianRecord
                    {
                        Label = label,
                        Value = 0
                    })
                    .ToList();
            }

            bool hasMetricData = orderedReports.Any((report) => ExtractMetricsAndComment(report.Notes).metrics.Count > 0);
            List<string> instructorComments = GetLastPresentInstructorComments(orderedReports, 3);
            string reportsCountPhrase = orderedReports.Count == 1
                ? "דיווח אחד"
                : $"{orderedReports.Count} דיווחים";

            StringBuilder summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine("משוב אמפתי להורים");
            summaryBuilder.AppendLine($"הסיכום הופק אוטומטית על בסיס {reportsCountPhrase} עבור {childDisplayName}.");

            if (hasMetricData)
            {
                List<MetricMedianRecord> strongest = effectiveMedianMetrics
                    .OrderByDescending((metric) => metric.Value)
                    .ToList();

                List<MetricMedianRecord> weakest = effectiveMedianMetrics
                    .OrderBy((metric) => metric.Value)
                    .ToList();

                List<MetricMedianRecord> needsImprovement = weakest
                    .Where((metric) => metric.Value <= 6)
                    .ToList();

                double overallAverage = CalculateOverallMetricAverage(effectiveMedianMetrics);
                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("ממוצע ציונים במדדים:");
                summaryBuilder.AppendLine($"ממוצע כללי: {overallAverage.ToString("0.0", CultureInfo.InvariantCulture)}/10");

                foreach (MetricMedianRecord metric in effectiveMedianMetrics)
                {
                    summaryBuilder.AppendLine($"- {metric.Label}: {metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/10");
                }

                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("מה הילד עושה טוב:");
                foreach (MetricMedianRecord metric in strongest.Take(2))
                {
                    summaryBuilder.AppendLine($"- {metric.Label} ({metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/10)");
                }

                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("מה הילד צריך לשפר:");
                if (needsImprovement.Count == 0)
                {
                    summaryBuilder.AppendLine("- אין כרגע מדדים שדורשים חיזוק ממוקד (כל המדדים מעל 6).");
                }
                else
                {
                    foreach (MetricMedianRecord metric in needsImprovement)
                    {
                        summaryBuilder.AppendLine($"- {metric.Label} ({metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/10)");
                    }
                }
            }
            else
            {
                summaryBuilder.AppendLine("לא נמצאו מדדים מספריים מובנים בדוחות, לכן ההמלצות נשענות על הערות המדריך בלבד.");
            }

            if (instructorComments.Count > 0)
            {
                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("הערות מדריך אחרונות (רק מפגשים שבהם הילד נכח):");
                foreach (string comment in instructorComments)
                {
                    summaryBuilder.AppendLine($"- {comment}");
                }
            }

            summaryBuilder.AppendLine();
            summaryBuilder.AppendLine("המלצה להורה:");
            summaryBuilder.AppendLine("- להתמקד בשתי מיומנויות לחיזוק, לתרגל בקצב רגוע, ולחזק כל הצלחה קטנה באופן עקבי.");

            return summaryBuilder.ToString().Trim();
        }

        public class CreateInstructorRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
        }

        public class UpdateInstructorRequest
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
        }

        public class CreateChildReportRequest
        {
            public int? GroupId { get; set; }
            public DateTime? ReportDate { get; set; }
            public bool? IsPresent { get; set; }
            public bool? OverwriteExisting { get; set; }
            public string Comment { get; set; } = string.Empty;
            public List<ChildReportMetricRequest> Metrics { get; set; } = new List<ChildReportMetricRequest>();
        }

        public class CancelTrainingSessionRequest
        {
            public string Reason { get; set; } = string.Empty;
        }

        public class AnalyzeCommentsForAchievementRequest
        {
            public List<CommentForAchievementRequest> Comments { get; set; } = new List<CommentForAchievementRequest>();
        }

        public class CommentForAchievementRequest
        {
            public string Text { get; set; } = string.Empty;
        }

        public class ChildReportMetricRequest
        {
            public string Label { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        public class MetricMedianRecord
        {
            public string Label { get; set; } = string.Empty;
            public double Value { get; set; }
        }

        private sealed class CommentAchievementDecision
        {
            public bool IsAchievement { get; set; }
            public int CommentIndex { get; set; }
            public string SelectedSentence { get; set; } = string.Empty;
            public string PositiveWords { get; set; } = string.Empty;
        }
    }
}
