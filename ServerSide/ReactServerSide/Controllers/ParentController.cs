using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParentController : ControllerBase
    {
        private static readonly string[] WeekdayNames = { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };
        private static readonly string[] PreferredMetricOrder =
        {
            "הסתגלות וביטחון במים",
            "שליטה בנשימות (הכנסת ראש למים)",
            "תנועתיות וקואורדינציה",
            "יציבה וציפה",
            "תקשורת במים (ושיתוף פעולה)",
            "התמדה ומאמץ",
            "יוזמה",
            "קשב וריכוז",
            "תגובה להוראות",
            "עצמאות בתרגיל"
        };

        private static readonly Dictionary<string, string> MetricAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        private readonly DBServices _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ParentController(DBServices db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpGet("list-active")]
        public IActionResult GetActiveParents()
        {
            List<ParentEmailOption> parents = _db.GetActiveParentEmailOptions();
            return Ok(parents);
        }

        [Authorize(Roles = "Manager")]
        [HttpGet("manager-list")]
        public IActionResult GetParentsForManager()
        {
            List<ManagerParentRecord> parents = _db.GetParentsForManager();
            return Ok(parents);
        }

        [Authorize(Roles = "Manager")]
        [HttpPut("manager-update/{id:int}")]
        public IActionResult UpdateParentForManager(int id, [FromBody] UpdateParentRequest request)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                return BadRequest(new { message = "First name and last name are required." });
            }

            bool updated = _db.PutUpdateParentNames(id, request.FirstName.Trim(), request.LastName.Trim());
            if (!updated)
            {
                return NotFound(new { message = "Parent was not found." });
            }

            List<ManagerParentRecord> parents = _db.GetParentsForManager();
            ManagerParentRecord? parent = parents.FirstOrDefault((item) => item.ParentId == id);
            if (parent == null)
            {
                return Ok(new { parentId = id, firstName = request.FirstName.Trim(), lastName = request.LastName.Trim() });
            }

            return Ok(parent);
        }

        [Authorize(Roles = "Manager")]
        [HttpDelete("manager-delete/{id:int}")]
        public IActionResult DeleteParentForManager(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            bool deleted = _db.DeleteDeactivateParentForManager(id);
            if (!deleted)
            {
                return NotFound(new { message = "Parent was not found." });
            }

            return NoContent();
        }

        [Authorize(Roles = "Manager")]
        [HttpPost("manager-create")]
        public IActionResult CreateParent([FromBody] CreateParentRequest request)
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
            string? phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

            try
            {
                int newId = _db.PostCreateParent(targetEmail, request.Password, firstName, lastName, phone);

                return Created(string.Empty, new
                {
                    id = newId,
                    email = targetEmail,
                    role = "Parent"
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpGet("{parentId:int}/children/{childId:int}/reports")]
        public IActionResult GetChildReportsForParent(int parentId, int childId)
        {
            if (parentId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id or child id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            if (!_db.GetChildBelongsToParent(parentId, childId))
            {
                return NotFound(new { message = "Child was not found for this parent." });
            }

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            return Ok(reports.Select(MapReportToResponse));
        }

        [HttpPost("{parentId:int}/children/{childId:int}/reports/summary")]
        public async Task<IActionResult> CreateChildReportsSummaryForParent(
            int parentId,
            int childId,
            [FromBody] CreateParentChildReportSummaryRequest? _request)
        {
            if (parentId <= 0 || childId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id or child id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            if (!_db.GetChildBelongsToParent(parentId, childId))
            {
                return NotFound(new { message = "Child was not found for this parent." });
            }

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            List<MetricMedianRecord> medianMetrics = BuildMedianMetricsFromReports(reports);

            ChildRecord? child = _db.GetChildById(childId);
            string childDisplayName = child == null
                ? $"ילד #{childId}"
                : $"{child.FirstName} {child.LastName}".Trim();

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

            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(500, new { message = "לא הוגדרו מפתחות API תקינים עבור שירות ה-AI." });
            }

            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();

            string medianMetricsContext = BuildMedianMetricsContext(medianMetrics);
            string recentPresentCommentsContext = BuildLastPresentInstructorCommentsContext(reports, 3);
            double overallMetricAverage = CalculateOverallMetricAverage(medianMetrics);

            string systemPrompt = (_configuration["OpenAI:ParentSystemPrompt"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                systemPrompt = BuildChildProgressSummarySystemPrompt();
            }

            string userPromptTemplate = (_configuration["OpenAI:ParentUserPromptTemplate"] ?? string.Empty).Trim();
            string userPrompt;
            if (!string.IsNullOrWhiteSpace(userPromptTemplate))
            {
                userPrompt = userPromptTemplate
                    .Replace("{childDisplayName}", childDisplayName)
                    .Replace("{overallMetricAverage}", overallMetricAverage.ToString("0.0", CultureInfo.InvariantCulture))
                    .Replace("{mediansContext}", medianMetricsContext)
                    .Replace("{recentPresentCommentsContext}", recentPresentCommentsContext);
            }
            else
            {
                userPrompt = BuildChildProgressSummaryUserPrompt(
                    childDisplayName,
                    childId,
                    medianMetricsContext,
                    recentPresentCommentsContext,
                    overallMetricAverage);
            }

            HttpClient client = _httpClientFactory.CreateClient();
            object requestPayload = new
            {
                model = model,
                temperature = 0.7,
                top_p = 0.9,
                max_tokens = 1250,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            string requestJson = JsonSerializer.Serialize(requestPayload);
            (bool isSuccess, string summary, string failureReason) =
                await TryCreateSummaryWithApiKeyAsync(client, apiUrl, apiKey, requestJson);

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

            return StatusCode(500, new
            {
                message = "שירות ה-AI לא זמין כרגע או נכשל: " + failureReason
            });
        }

        [HttpPost("{parentId:int}/training-sessions")]
        public async Task<IActionResult> CreateTrainingSessionForParent(int parentId, [FromBody] CreateTrainingSessionRequest request)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ChildId <= 0 || request.InstructorId <= 0)
            {
                return BadRequest(new { message = "ChildId and InstructorId are required." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            if (!_db.GetChildBelongsToParent(parentId, request.ChildId))
            {
                return NotFound(new { message = "Child was not found for this parent." });
            }

            if (!_db.GetInstructorAssignedToParentChild(parentId, request.ChildId, request.InstructorId))
            {
                return NotFound(new { message = "Instructor is not assigned to this child." });
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

            if (_db.GetIsTrainingSessionTimeOccupied(request.InstructorId, request.ChildId, meetingDate, startTime, endTime))
            {
                return Conflict(new { message = "Requested slot is no longer available." });
            }

            TrainingSessionRecord session = _db.PostCreateTrainingSession(
                parentId,
                request.ChildId,
                request.InstructorId,
                meetingDate,
                startTime,
                endTime,
                request.Notes ?? string.Empty,
                null);

            ChildRecord? childRecord = _db.GetChildById(request.ChildId);
            string childDisplayName = childRecord == null
                ? $"ילד #{request.ChildId}"
                : $"{childRecord.FirstName} {childRecord.LastName}".Trim();

            List<ChatInstructorRecord> instructors = _db.GetInstructorsForParentChild(parentId, request.ChildId);
            ChatInstructorRecord? matchedInstructor = instructors.FirstOrDefault((item) => item.Id == request.InstructorId);
            string instructorDisplayName = matchedInstructor == null
                ? $"מדריך #{request.InstructorId}"
                : string.IsNullOrWhiteSpace(matchedInstructor.FullName)
                    ? $"{matchedInstructor.FirstName} {matchedInstructor.LastName}".Trim()
                    : matchedInstructor.FullName.Trim();

            string parentNotificationTitle = "שיעור קרוב נקבע לילד";
            string parentNotificationBody =
                $"נקבע שיעור עבור {childDisplayName} עם {instructorDisplayName} בתאריך {meetingDate:yyyy-MM-dd} בשעה {startTime:hh\\:mm}.";

            string instructorNotificationTitle = "נקבע שיעור חדש לילד";
            string instructorNotificationBody =
                $"נקבע שיעור עבור {childDisplayName} בתאריך {meetingDate:yyyy-MM-dd} בשעה {startTime:hh\\:mm}.";

            string sessionPayloadJson = JsonSerializer.Serialize(new
            {
                type = "upcoming_lesson",
                sessionId = session.SessionId,
                parentId = session.ParentId,
                childId = session.ChildId,
                instructorId = session.InstructorId,
                childName = childDisplayName,
                instructorName = instructorDisplayName,
                meetingDate = meetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                startTime = startTime.ToString(@"hh\\:mm", CultureInfo.InvariantCulture),
                endTime = endTime.ToString(@"hh\\:mm", CultureInfo.InvariantCulture)
            });

            _db.PostCreateUserLessonNotification(
                "Parent",
                parentId,
                "UpcomingLesson",
                parentNotificationTitle,
                parentNotificationBody,
                null,
                null,
                sessionPayloadJson);

            _db.PostCreateUserLessonNotification(
                "Instructor",
                request.InstructorId,
                "UpcomingLesson",
                instructorNotificationTitle,
                instructorNotificationBody,
                null,
                null,
                sessionPayloadJson);

            await TrySendUserPushNotificationAsync(
                "Parent",
                parentId,
                parentNotificationTitle,
                parentNotificationBody,
                new
                {
                    type = "upcoming_lesson",
                    notificationType = "UpcomingLesson",
                    sessionId = session.SessionId,
                    childId = session.ChildId,
                    instructorId = session.InstructorId
                });

            await TrySendUserPushNotificationAsync(
                "Instructor",
                request.InstructorId,
                instructorNotificationTitle,
                instructorNotificationBody,
                new
                {
                    type = "upcoming_lesson",
                    notificationType = "UpcomingLesson",
                    sessionId = session.SessionId,
                    childId = session.ChildId,
                    parentId = session.ParentId
                });

            return Created(string.Empty, new
            {
                sessionId = session.SessionId,
                parentId = session.ParentId,
                childId = session.ChildId,
                instructorId = session.InstructorId,
                meetingDate = session.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                startTime = FormatTimeAsHourMinute(session.StartTime),
                endTime = FormatTimeAsHourMinute(session.EndTime),
                notes = session.Notes,
                status = session.Status,
                createdAt = session.CreatedAt
            });
        }

        private async Task TrySendUserPushNotificationAsync(string userType, int userId, string title, string body, object dataPayload)
        {
            try
            {
                if (userId <= 0)
                {
                    return;
                }

                List<string> pushTokens = _db.GetActivePushTokensForUser(userType, userId);
                if (pushTokens.Count == 0)
                {
                    return;
                }

                string normalizedTitle = string.IsNullOrWhiteSpace(title)
                    ? "עדכון חדש"
                    : title.Trim();

                string normalizedBody = string.IsNullOrWhiteSpace(body)
                    ? "יש עדכון חדש במערכת."
                    : body.Trim();

                if (normalizedBody.Length > 220)
                {
                    normalizedBody = normalizedBody[..217] + "...";
                }

                object[] payload = pushTokens.Select((token) => new
                {
                    to = token,
                    title = normalizedTitle,
                    body = normalizedBody,
                    sound = "default",
                    data = dataPayload
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
            catch
            {
                // Push delivery should never block session scheduling.
            }
        }

        [HttpGet("{parentId:int}/scheduled-lessons")]
        public IActionResult GetScheduledLessonsForParent(int parentId, [FromQuery] string? fromDate = null, [FromQuery] int days = 14)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            if (days < 1 || days > 60)
            {
                return BadRequest(new { message = "days must be between 1 and 60." });
            }

            DateTime startDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(fromDate) && !TryParseIsoDate(fromDate, out startDate))
            {
                return BadRequest(new { message = "fromDate must be in yyyy-MM-dd format." });
            }

            DateTime endDate = startDate.AddDays(days - 1);

            List<ParentScheduledLessonRecord> lessons = _db.GetParentScheduledLessons(parentId, startDate, endDate);
            return Ok(lessons.Select((lesson) => new
            {
                sessionId = lesson.SessionId,
                lessonType = lesson.LessonType,
                childId = lesson.ChildId,
                childName = lesson.ChildFullName,
                instructorId = lesson.InstructorId,
                instructorName = lesson.InstructorFullName,
                groupId = lesson.GroupId,
                groupName = lesson.GroupName,
                meetingDate = lesson.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekday = FormatWeekdayName((int)lesson.MeetingDate.DayOfWeek),
                weekdayOrder = (int)lesson.MeetingDate.DayOfWeek,
                startTime = FormatTimeAsHourMinute(lesson.StartTime),
                endTime = FormatTimeAsHourMinute(lesson.EndTime),
                status = lesson.Status,
                notes = lesson.Notes,
                targetMetric = lesson.TargetMetric
            }));
        }

        [HttpGet("{parentId:int}/lesson-history")]
        public IActionResult GetLessonHistoryForParent(int parentId, [FromQuery] string? fromDate = null, [FromQuery] string? toDate = null)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            DateTime endDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(toDate) && !TryParseIsoDate(toDate, out endDate))
            {
                return BadRequest(new { message = "toDate must be in yyyy-MM-dd format." });
            }

            DateTime startDate = endDate.AddDays(-179);
            if (!string.IsNullOrWhiteSpace(fromDate) && !TryParseIsoDate(fromDate, out startDate))
            {
                return BadRequest(new { message = "fromDate must be in yyyy-MM-dd format." });
            }

            DateTime today = DateTime.Today;
            if (endDate > today)
            {
                endDate = today;
            }

            if (startDate > endDate)
            {
                return BadRequest(new { message = "fromDate must be earlier than or equal to toDate." });
            }

            if ((endDate.Date - startDate.Date).TotalDays > 730)
            {
                return BadRequest(new { message = "Date range must be up to 730 days." });
            }

            List<ParentScheduledLessonRecord> lessons = _db
                .GetParentScheduledLessons(parentId, startDate, endDate)
                .Where((lesson) => lesson.MeetingDate.Date <= today)
                .OrderByDescending((lesson) => lesson.MeetingDate)
                .ThenByDescending((lesson) => lesson.StartTime)
                .ToList();

            return Ok(lessons.Select((lesson) => new
            {
                sessionId = lesson.SessionId,
                lessonType = lesson.LessonType,
                childId = lesson.ChildId,
                childName = lesson.ChildFullName,
                instructorId = lesson.InstructorId,
                instructorName = lesson.InstructorFullName,
                groupId = lesson.GroupId,
                groupName = lesson.GroupName,
                meetingDate = lesson.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekday = FormatWeekdayName((int)lesson.MeetingDate.DayOfWeek),
                weekdayOrder = (int)lesson.MeetingDate.DayOfWeek,
                startTime = FormatTimeAsHourMinute(lesson.StartTime),
                endTime = FormatTimeAsHourMinute(lesson.EndTime),
                status = lesson.Status,
                notes = lesson.Notes
            }));
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

        private static string FormatTimeAsHourMinute(TimeSpan timeValue)
        {
            return timeValue.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        private static string FormatWeekdayName(int weekday)
        {
            if (weekday < 0 || weekday >= WeekdayNames.Length)
            {
                return string.Empty;
            }

            return WeekdayNames[weekday];
        }

        private static string BuildChildProgressSummarySystemPrompt()
        {
            return "אתה מדריך שחייה רגיש, סבלני ומעצים שכותב משוב התקדמות קצר להורה. חשוב מאוד: אל תכתוב רשימה של עובדות יבשות או מספרים! כתוב טקסט זורם, חם, אישי ומעודד. גם כשהציונים נמוכים, התמקד תמיד בתהליך הלמידה ובמאמץ של הילד בצורה הכי חיובית שיש. אל תשתמש במילים כמו 'נמוך', 'לא קיים', או 'אפס'. חובה להחזיר בדיוק 4 שורות (רד שורה אחרי כל משפט), ללא מספור (1,2,3). כתוב בעברית בלבד ובטון של שיחה נעימה.";
        }

        private static string BuildChildProgressSummaryUserPrompt(
            string childDisplayName,
            int childId,
            string mediansContext,
            string recentPresentCommentsContext,
            double overallMetricAverage)
        {
            return $@"כתוב פסקה קצרה, אנושית, חמה ומעצימה עבור הילד/ה {childDisplayName}.
ממוצע ציונים כרגע: {overallMetricAverage.ToString("0.0", CultureInfo.InvariantCulture)} מתוך 5.
פירוט מדדים:
{mediansContext}
הערות אחרונות:
{recentPresentCommentsContext}

עליך לכתוב בדיוק 4 משפטים מלאים, וכל משפט חייב להיות בשורה נפרדת (ללא שום מספור בתחילת השורה):
שורה 1: משפט פתיחה חם ויצירתי שמברך את הילד על המסע שלו במים.
שורה 2: משפט שמוצא נקודת אור חיובית מההערות או מהמדדים ומחזק עליה (הימנע מציון מספרים יבשים).
שורה 3: משפט עדין שמספר על מיומנות שאנחנו בונים עכשיו יחד, בקצב שלו, באהבה ובהכלה.
שורה 4: טיפ מעשי, משעשע או קליל להורים להמשך חיזוק הביטחון בבית. שורה זו חייבת להתחיל במילים: 'להורה מומלץ'.";
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
                if (!report.IsPresent || string.IsNullOrWhiteSpace(report.Comment))
                {
                    continue;
                }

                comments.Add($"{report.ReportDate:yyyy-MM-dd}: {report.Comment}");
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

        private static string BuildReportsContextForPrompt(List<ChildReportRecord> reports)
        {
            StringBuilder promptBuilder = new StringBuilder();

            foreach (ChildReportRecord report in reports)
            {
                (List<string> metrics, string comment) = ExtractMetricsAndComment(report.Notes);

                promptBuilder.AppendLine($"דוח #{report.ReportId} | תאריך: {report.ReportDate:yyyy-MM-dd}");
                if (metrics.Count > 0)
                {
                    promptBuilder.AppendLine("מדדים: " + string.Join(", ", metrics));
                }
                else
                {
                    promptBuilder.AppendLine("מדדים: לא נמצאו מדדים מובנים");
                }

                if (!string.IsNullOrWhiteSpace(comment))
                {
                    promptBuilder.AppendLine("הערת מדריך: " + comment);
                }

                promptBuilder.AppendLine();
            }

            return promptBuilder.ToString().Trim();
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
                builder.AppendLine($"- {metric.Label}: {metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/5");
            }

            return builder.ToString().Trim();
        }

        private static double NormalizeProgressValue(double rawValue)
        {
            if (!double.IsFinite(rawValue))
            {
                return 0;
            }

            return Math.Round(Math.Clamp(rawValue, 0, 5), MidpointRounding.AwayFromZero);
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

        private static List<MetricMedianRecord> BuildMedianMetricsFromReports(List<ChildReportRecord> reports)
        {
            Dictionary<string, List<double>> metricValuesByLabel = PreferredMetricOrder
                .ToDictionary((label) => label, (_) => new List<double>(), StringComparer.OrdinalIgnoreCase);

            foreach (ChildReportRecord report in reports)
            {
                if (!report.IsPresent || string.IsNullOrWhiteSpace(report.Metrics))
                {
                    continue;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(report.Metrics);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    int index = 0;
                    foreach (JsonElement metric in doc.RootElement.EnumerateArray())
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

                        string normalizedLabel = NormalizeMetricLabel(rawLabel);
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
                    // Ignore malformed rows so one bad report does not break aggregation.
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

        private static List<MetricMedianRecord> NormalizeRequestedMetrics(List<MetricMedianInputRecord>? requestedMetrics)
        {
            if (requestedMetrics == null || requestedMetrics.Count == 0)
            {
                return new List<MetricMedianRecord>();
            }

            List<MetricMedianRecord> normalized = requestedMetrics
                .Where((metric) => !string.IsNullOrWhiteSpace(metric.Label) && double.IsFinite(metric.Value))
                .Select((metric) => new MetricMedianRecord
                {
                    Label = metric.Label.Trim(),
                    Value = Math.Round(Math.Clamp(metric.Value, 0, 5), 1)
                })
                .ToList();

            if (normalized.Count == 0)
            {
                return normalized;
            }

            List<MetricMedianRecord> ordered = new List<MetricMedianRecord>();
            foreach (string label in PreferredMetricOrder)
            {
                MetricMedianRecord? match = normalized.FirstOrDefault((metric) => string.Equals(metric.Label, label, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ordered.Add(match);
                }
            }

            foreach (MetricMedianRecord metric in normalized)
            {
                if (!ordered.Any((existing) => string.Equals(existing.Label, metric.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(metric);
                }
            }

            return ordered;
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

        private static object MapReportToResponse(ChildReportRecord report)
        {
            object parsedMetrics;
            try
            {
                parsedMetrics = JsonSerializer.Deserialize<object>(
                    string.IsNullOrWhiteSpace(report.Metrics) ? "[]" : report.Metrics) ?? new object[0];
            }
            catch
            {
                parsedMetrics = new object[0];
            }

            return new
            {
                reportId = report.ReportId,
                childId = report.ChildId,
                instructorId = report.InstructorId,
                groupId = report.GroupId,
                reportDate = report.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                exerciseKey = report.ExerciseKey,
                exerciseTitle = report.ExerciseTitle,
                isPresent = report.IsPresent,
                comment = report.Comment,
                metrics = parsedMetrics
            };
        }

        private static string NormalizeMetricLabel(string label)
        {
            string normalized = (label ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = Regex.Replace(normalized, @"\s+", " ");
            if (MetricAliasMap.TryGetValue(normalized, out string? aliasedLabel))
            {
                return aliasedLabel;
            }

            return normalized;
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

                if (string.IsNullOrWhiteSpace(comment) && root.TryGetProperty("generalNotes", out JsonElement generalNotesElement) && generalNotesElement.ValueKind == JsonValueKind.String)
                {
                    comment = (generalNotesElement.GetString() ?? string.Empty).Trim();
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
                JsonValueKind.Number when element.TryGetInt32(out int intValue) => intValue.ToString(CultureInfo.InvariantCulture),
                JsonValueKind.Number when element.TryGetDouble(out double doubleValue) => doubleValue.ToString("0.##", CultureInfo.InvariantCulture),
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

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
                        string partType = part.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
                            ? (typeElement.GetString() ?? string.Empty).Trim()
                            : string.Empty;

                        // Some providers include separate reasoning parts; ignore anything that is not final output text.
                        if (!string.IsNullOrWhiteSpace(partType)
                            && !string.Equals(partType, "output_text", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(partType, "text", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

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
                    return SanitizeGeneratedSummary(cleanedResult);
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string SanitizeGeneratedSummary(string rawSummary)
        {
            if (string.IsNullOrWhiteSpace(rawSummary))
            {
                return string.Empty;
            }

            rawSummary = rawSummary
                .Replace("הבוגרים יום", "הבתולת ים")
                .Replace("בוגרים יום", "בתולת ים")
                .Replace("הבוגריםיום", "הבתולת ים")
                .Replace("בוגריםיום", "בתולת ים");

            string cleaned = rawSummary
                .Replace("\r", "\n")
                .Trim();

            cleaned = Regex.Replace(cleaned, @"```[\s\S]*?```", " ", RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
            cleaned = Regex.Replace(cleaned, @"\n{2,}", "\n").Trim();

            string[] rawLines = cleaned.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> selectedSentences = new List<string>();

            foreach (string rawLine in rawLines)
            {
                string sentence = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                // Remove numbers if the AI accidentally ignored the prompt
                sentence = Regex.Replace(sentence, @"^\d+[\.\-\)\*]*\s*", string.Empty);

                int firstHebrewIndex = IndexOfHebrewCharacter(sentence);
                if (firstHebrewIndex < 0)
                {
                    continue;
                }

                if (firstHebrewIndex > 0)
                {
                    sentence = sentence.Substring(firstHebrewIndex);
                }

                sentence = sentence.Trim().TrimStart('"', '\'', '״', '“', '”', ':', '-', ' ').TrimEnd('"', '\'', '״', '“', '”');
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                if (LooksLikePromptLeak(sentence))
                {
                    continue;
                }

                if (!ContainsHebrewCharacters(sentence))
                {
                    continue;
                }

                if (ContainsLatinLetters(sentence))
                {
                    continue;
                }

                int hebrewChars = Regex.Matches(sentence, @"[\u0590-\u05FF]").Count;
                if (hebrewChars == 0)
                {
                    continue;
                }

                string normalizedSentence = Regex.Replace(sentence, @"\s+", " ").Trim();
                normalizedSentence = normalizedSentence.TrimStart('-', '*', '•', ' ');
                if (string.IsNullOrWhiteSpace(normalizedSentence))
                {
                    continue;
                }

                selectedSentences.Add(normalizedSentence);
                if (selectedSentences.Count >= 5)
                {
                    break;
                }
            }

            if (selectedSentences.Count < 4)
            {
                return string.Empty;
            }

            List<string> finalSentences = selectedSentences.Take(4).ToList();

            for (int index = 0; index < finalSentences.Count; index += 1)
            {
                string sentence = finalSentences[index];
                if (!Regex.IsMatch(sentence, @"[.!?]$"))
                {
                    finalSentences[index] = sentence + ".";
                }
            }

            if (finalSentences.Count >= 4)
            {
                finalSentences[3] = EnsureParentRecommendationSentence(finalSentences[3]);
            }

            return string.Join(Environment.NewLine, finalSentences).Trim();
        }

        private static string EnsureParentRecommendationSentence(string sentence)
        {
            string normalized = Regex.Replace((sentence ?? string.Empty).Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "להורה מומלץ לקבוע בבית תרגול קצר וקבוע, לחזק הצלחות קטנות ולשמור על אווירה מעודדת.";
            }

            bool hasParentKeyword = Regex.IsMatch(normalized, @"להורה|הורה|בבית");
            bool hasRecommendationKeyword = Regex.IsMatch(normalized, @"מומלץ|כדאי|רצוי|המלצה");

            if (!Regex.IsMatch(normalized, @"[.!?]$"))
            {
                normalized += ".";
            }

            if (hasParentKeyword && hasRecommendationKeyword)
            {
                return normalized;
            }

            return "להורה מומלץ לקבוע בבית תרגול קצר וקבוע, לחזק הצלחות קטנות ולשמור על אווירה מעודדת.";
        }

        private static bool LooksLikePromptLeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string normalized = text.Trim();
            string lowered = normalized.ToLowerInvariant();

            string[] markers =
            {
                "we need to produce",
                "let's craft",
                "lets craft",
                "single paragraph",
                "first sentence",
                "second sentence",
                "third sentence",
                "sentence 1",
                "sentence 2",
                "sentence 3",
                "skip second sentence",
                "all >6",
                "all > 6",
                "hebrew only",
                "instruction",
                "משימה:",
                "המשפט הראשון",
                "המשפט השני",
                "המשפט השלישי",
                "דוגמה לתשובה",
                "תבנית",
                "כתוב פסקת",
                "עברית בלבד",
                "אסור לכתוב רשימות",
                "ילד:",
                "מדדים:",
                "הערות אחרונות:"
            };

            return markers.Any((marker) => lowered.Contains(marker.ToLowerInvariant()));
        }

        private static bool ContainsHebrewCharacters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text, @"[\u0590-\u05FF]");
        }

        private static bool ContainsLatinLetters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text, @"[A-Za-z]");
        }

        private static int IndexOfHebrewCharacter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1;
            }

            Match match = Regex.Match(text, @"[\u0590-\u05FF]");
            return match.Success ? match.Index : -1;
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
            double overallAverage = CalculateOverallMetricAverage(effectiveMedianMetrics);

            MetricMedianRecord? strongest = effectiveMedianMetrics
                .OrderByDescending((metric) => metric.Value)
                .ThenBy((metric) => metric.Label)
                .FirstOrDefault();

            MetricMedianRecord? secondStrongest = effectiveMedianMetrics
                .OrderByDescending((metric) => metric.Value)
                .ThenBy((metric) => metric.Label)
                .Skip(1)
                .FirstOrDefault();

            MetricMedianRecord? weakest = effectiveMedianMetrics
                .Where((metric) => metric.Value <= 3)
                .OrderBy((metric) => metric.Value)
                .ThenBy((metric) => metric.Label)
                .FirstOrDefault();

            string latestComment = NormalizeInstructorCommentForSummary(instructorComments.FirstOrDefault());

            List<string> lines = new List<string>();

            if (hasMetricData)
            {
                lines.Add($"{childDisplayName} מציג התקדמות יציבה בשיעורי השחייה, עם ממוצע כולל של {overallAverage.ToString("0.0", CultureInfo.InvariantCulture)}/5.");
            }
            else
            {
                lines.Add($"כרגע יש מעט נתוני מדדים עבור {childDisplayName}, ולכן הסיכום מתבסס בעיקר על הדיווחים האחרונים.");
            }

            if (strongest != null && secondStrongest != null)
            {
                lines.Add($"החוזקות הבולטות כרגע הן {strongest.Label} ו-{secondStrongest.Label}.");
            }
            else if (strongest != null)
            {
                lines.Add($"החוזקה המרכזית כרגע היא {strongest.Label}.");
            }
            else
            {
                lines.Add("ככל שיצטברו עוד דיווחים, יהיה אפשר לדייק עוד יותר את תמונת ההתקדמות.");
            }

            if (weakest != null)
            {
                lines.Add($"כדאי להמשיך לחזק בהדרגה את {weakest.Label} באמצעות תרגול קצר ועקבי בשיעורים ובבית.");
            }
            else
            {
                lines.Add("כל המדדים מעל 3/5, ונראית יציבות טובה ורצף התקדמות חיובי לאורך התקופה האחרונה.");
            }

            if (!string.IsNullOrWhiteSpace(latestComment))
            {
                lines.Add($"להורה מומלץ להמשיך בבית את הדגשים מהמשוב האחרון של המדריך, ולשלב תרגול קצר ועקבי באווירה חיובית. {latestComment}");
            }
            else
            {
                lines.Add("להורה מומלץ לקבוע בבית תרגול קצר וקבוע, לחזק הצלחות קטנות ולשמור על רצף תרגול חיובי לאורך השבוע.");
            }

            List<string> normalizedLines = lines
                .Where((line) => !string.IsNullOrWhiteSpace(line))
                .Select((line) => Regex.Replace(line.Trim(), @"\s+", " "))
                .Take(4)
                .ToList();

            for (int index = 0; index < normalizedLines.Count; index += 1)
            {
                string line = normalizedLines[index];
                if (!Regex.IsMatch(line, @"[.!?]$"))
                {
                    normalizedLines[index] = line + ".";
                }
            }

            if (normalizedLines.Count >= 4)
            {
                normalizedLines[3] = EnsureParentRecommendationSentence(normalizedLines[3]);
            }

            return string.Join(Environment.NewLine, normalizedLines);
        }

        private static string NormalizeInstructorCommentForSummary(string? datedComment)
        {
            if (string.IsNullOrWhiteSpace(datedComment))
            {
                return string.Empty;
            }

            string cleaned = datedComment.Trim();
            cleaned = Regex.Replace(cleaned, @"^\d{4}-\d{2}-\d{2}:\s*", string.Empty);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (!ContainsHebrewCharacters(cleaned) || ContainsLatinLetters(cleaned) || LooksLikePromptLeak(cleaned))
            {
                return string.Empty;
            }

            if (!Regex.IsMatch(cleaned, @"[.!?]$"))
            {
                cleaned += ".";
            }

            return cleaned;
        }

        public class CreateParentRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string? Phone { get; set; }
        }

        public class UpdateParentRequest
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
        }

        public class CreateTrainingSessionRequest
        {
            public int ChildId { get; set; }
            public int InstructorId { get; set; }
            public string MeetingDate { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
        }

        public class CreateParentChildReportSummaryRequest
        {
            public string Prompt { get; set; } = string.Empty;
            public List<MetricMedianInputRecord> Metrics { get; set; } = new List<MetricMedianInputRecord>();
        }

        public class MetricMedianInputRecord
        {
            public string Label { get; set; } = string.Empty;
            public double Value { get; set; }
        }

        public class MetricMedianRecord
        {
            public string Label { get; set; } = string.Empty;
            public double Value { get; set; }
        }
    }
}
