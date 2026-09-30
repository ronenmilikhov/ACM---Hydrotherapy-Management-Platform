using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
            "התמדה ומאמץ",
            "יוזמה",
            "קשב וריכוז",
            "תגובה להוראות",
            "עצמאות בתרגיל"
        };

        private static readonly string[] ExerciseDomainNames =
        {
            "motor",
            "communication",
            "emotional",
            "safety"
        };

        private static readonly List<ExerciseDefinitionRecord> ExerciseCatalog = new List<ExerciseDefinitionRecord>
        {
            new ExerciseDefinitionRecord
            {
                Key = "front_float",
                Title = "ציפה על הבטן",
                DefaultOrder = 1,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["motor"] = 0.55,
                    ["safety"] = 0.25,
                    ["emotional"] = 0.20
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "back_float",
                Title = "ציפה על הגב",
                DefaultOrder = 2,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["motor"] = 0.50,
                    ["safety"] = 0.20,
                    ["emotional"] = 0.30
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "front_kicks",
                Title = "בעיטות בטן",
                DefaultOrder = 3,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["motor"] = 0.65,
                    ["safety"] = 0.20,
                    ["emotional"] = 0.15
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "back_kicks",
                Title = "בעיטות גב",
                DefaultOrder = 4,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["motor"] = 0.65,
                    ["safety"] = 0.20,
                    ["emotional"] = 0.15
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "arrow_jump",
                Title = "קפיצה חץ",
                DefaultOrder = 5,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["motor"] = 0.35,
                    ["safety"] = 0.35,
                    ["emotional"] = 0.30
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "deep_jump",
                Title = "קפיצה עמוק",
                DefaultOrder = 6,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["safety"] = 0.40,
                    ["emotional"] = 0.40,
                    ["motor"] = 0.20
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "hoop_pass",
                Title = "חישוק",
                DefaultOrder = 7,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["communication"] = 0.40,
                    ["motor"] = 0.30,
                    ["emotional"] = 0.30
                }
            },
            new ExerciseDefinitionRecord
            {
                Key = "water_confidence",
                Title = "ביטחון במים",
                DefaultOrder = 8,
                DomainWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["emotional"] = 0.45,
                    ["safety"] = 0.35,
                    ["communication"] = 0.20
                }
            }
        };

        private static readonly Dictionary<string, string[]> DomainKeywordMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["motor"] = new[] { "motor", "מוטור", "קואורדינציה", "שיווי", "יציבה", "תנועה", "כוח" },
            ["communication"] = new[] { "communication", "תקשורת", "קשר עין", "שפה", "דיבור", "שיתוף" },
            ["emotional"] = new[] { "emotional", "רגשי", "ביטחון", "פחד", "חרדה", "ויסות", "חושי" },
            ["safety"] = new[] { "safety", "בטיחות", "נשימה", "ראש למים", "מים עמוקים", "זהירות" }
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
        private static readonly AmazonDynamoDBClient _dynamo = new AmazonDynamoDBClient(RegionEndpoint.EUNorth1);
        private const string SummaryJobsTable = "AISummaryJobs";

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

            DateTime endDate = startDate.AddDays(14);
            List<InstructorTrainingSessionViewRecord> sessions = _db.GetInstructorTrainingSessions(instructorId, startDate, endDate);
            List<InstructorGroupTrainingSessionViewRecord> groupSessions = _db.GetInstructorGroupTrainingSessions(instructorId, startDate, endDate);

            DateTime nowIsrael = DateTime.UtcNow.AddHours(3);
            TimeSpan timeOfDayIsrael = nowIsrael.TimeOfDay;
            DateTime dateIsrael = nowIsrael.Date;

            var combined = new List<object>();

            foreach (var session in sessions)
            {
                if (session.MeetingDate.Date < dateIsrael || (session.MeetingDate.Date == dateIsrael && session.EndTime < timeOfDayIsrael))
                {
                    continue;
                }

                combined.Add(new
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
                    status = session.Status,
                    targetMetric = session.TargetMetric
                });
            }

            foreach (var gSession in groupSessions)
            {
                if (gSession.MeetingDate.Date < dateIsrael || (gSession.MeetingDate.Date == dateIsrael && gSession.EndTime < timeOfDayIsrael))
                {
                    continue;
                }

                combined.Add(new
                {
                    sessionId = gSession.SessionId,
                    instructorId = gSession.InstructorId,
                    parentId = 0,
                    parentName = "",
                    childId = 0,
                    childName = gSession.GroupName,
                    meetingDate = gSession.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    weekday = FormatWeekdayName((int)gSession.MeetingDate.DayOfWeek),
                    weekdayOrder = (int)gSession.MeetingDate.DayOfWeek,
                    startTime = FormatTimeAsHourMinute(gSession.StartTime),
                    endTime = FormatTimeAsHourMinute(gSession.EndTime),
                    status = gSession.Status,
                    targetMetric = gSession.TargetMetric,
                    groupId = gSession.GroupId
                });
            }

            return Ok(combined);
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
            DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);

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

            if (!_db.GetChildHadLessonOnDate(instructorId, childId, reportDate, request.GroupId))
            {
                return BadRequest(new { message = "לא ניתן לדווח על ילד שלא היה לו שיעור בתאריך זה." });
            }

            bool isPresent = request.IsPresent ?? true;

            string exerciseKey = (request.ExerciseKey ?? string.Empty).Trim();
            string exerciseTitle = (request.ExerciseTitle ?? string.Empty).Trim();

            List<object> normalizedMetrics = isPresent
                ? (request.Metrics ?? new List<ChildReportMetricRequest>())
                    .Where((metric) => !string.IsNullOrWhiteSpace(metric.Label))
                    .Select((metric) => new
                    {
                        label = metric.Label.Trim(),
                        value = Math.Clamp(metric.Value, 0, 5),
                        isIncluded = metric.IsIncluded ?? true
                    })
                    .Cast<object>()
                    .ToList()
                : new List<object>();

            string normalizedComment = isPresent
                ? request.Comment?.Trim() ?? string.Empty
                : string.Empty;

            string metricsJson = JsonSerializer.Serialize(normalizedMetrics);

            // Check if a report already exists for the same date + exercise
            ChildReportRecord? existingReport = _db.GetChildReportByDateAndExercise(childId, instructorId, reportDate, exerciseKey);

            if (existingReport != null)
            {
                // If the client did NOT explicitly confirm the overwrite, warn them
                if (request.OverwriteExisting != true)
                {
                    return Conflict(new
                    {
                        message = "duplicate_report",
                        existingReportId = existingReport.ReportId,
                        exerciseKey = existingReport.ExerciseKey,
                        exerciseTitle = existingReport.ExerciseTitle,
                        reportDate = existingReport.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }

                // Client confirmed → update the existing row
                ChildReportRecord updatedReport = _db.UpdateChildReportForInstructor(
                    existingReport.ReportId,
                    childId,
                    instructorId,
                    request.GroupId,
                    reportDate,
                    exerciseKey,
                    exerciseTitle,
                    isPresent,
                    normalizedComment,
                    metricsJson);

                return Ok(MapReportToResponse(updatedReport));
            }

            // No duplicate → create a new row
            ChildReportRecord createdReport = _db.PostCreateChildReport(
                childId,
                instructorId,
                request.GroupId,
                reportDate,
                exerciseKey,
                exerciseTitle,
                isPresent,
                normalizedComment,
                metricsJson);

            return Created(string.Empty, MapReportToResponse(createdReport));
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
            return Ok(reports.Select(MapReportToResponse));
        }

        [HttpPost("{instructorId:int}/children/{childId:int}/exercise-plan/recommendation")]
        public IActionResult GetExercisePlanRecommendationForChild(
            int instructorId,
            int childId,
            [FromBody] ExercisePlanRecommendationRequest? request)
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

            ChildRecord? child = _db.GetChildById(childId);
            if (child == null)
            {
                return NotFound(new { message = "Child was not found." });
            }

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            List<MetricMedianRecord> medianMetrics = BuildMedianMetricsFromReports(reports);

            // Build exercise progress: prefer client-provided data, fall back to DB reports
            Dictionary<string, ExerciseProgressInput> progressByKey;
            List<ExerciseProgressInput> clientProgress = (request?.ExerciseProgress ?? new List<ExerciseProgressInput>())
                .Where((item) => !string.IsNullOrWhiteSpace(item.ExerciseKey))
                .ToList();

            if (clientProgress.Count > 0)
            {
                progressByKey = clientProgress
                    .GroupBy((item) => item.ExerciseKey.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select((group) => group
                        .OrderByDescending((item) => item.CompletedCount)
                        .ThenByDescending((item) => item.LastScore ?? 0)
                        .First())
                    .ToDictionary((item) => item.ExerciseKey.Trim(), (item) => item, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // Derive exercise progress from DB reports
                progressByKey = reports
                    .Where((r) => r.IsPresent && !string.IsNullOrWhiteSpace(r.ExerciseKey))
                    .GroupBy((r) => r.ExerciseKey.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        (group) => group.Key,
                        (group) =>
                        {
                            ChildReportRecord latestReport = group.OrderByDescending((r) => r.ReportDate).First();
                            double? lastScore = CalculateExerciseMetricsScore(
                                ParseMetricsJsonToInputList(latestReport.Metrics));
                            return new ExerciseProgressInput
                            {
                                ExerciseKey = group.Key,
                                CompletedCount = group.Count(),
                                LastScore = lastScore
                            };
                        },
                        StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, double> domainWeaknessScores = BuildDomainWeaknessScores(
                medianMetrics,
                child.ChildDescription,
                request?.CurrentDifficulties);

            List<ExerciseRecommendationItem> orderedRecommendations = ExerciseCatalog
                .Select((exercise) => BuildExerciseRecommendationItem(exercise, progressByKey, domainWeaknessScores))
                .OrderByDescending((item) => item.PriorityScore)
                .ThenBy((item) => item.CompletedCount)
                .ThenBy((item) => item.DefaultOrder)
                .ToList();

            if (request?.IncludeCompleted == false)
            {
                orderedRecommendations = orderedRecommendations
                    .Where((item) => item.CompletedCount <= 0)
                    .ToList();
            }

            ExerciseRecommendationItem? nextExercise = orderedRecommendations.FirstOrDefault();

            int completedExercises = ExerciseCatalog.Count((exercise) =>
                progressByKey.TryGetValue(exercise.Key, out ExerciseProgressInput? progress)
                && progress.CompletedCount > 0);

            double coveragePercent = ExerciseCatalog.Count == 0
                ? 0
                : Math.Round((double)completedExercises / ExerciseCatalog.Count * 100, 1, MidpointRounding.AwayFromZero);

            string childName = $"{child.FirstName} {child.LastName}".Trim();

            return Ok(new ExercisePlanRecommendationResponse
            {
                ChildId = childId,
                ChildName = childName,
                TotalExercises = ExerciseCatalog.Count,
                CompletedExercises = completedExercises,
                CoveragePercent = coveragePercent,
                ReportsCount = reports.Count,
                DomainWeakness = domainWeaknessScores,
                NextExercise = nextExercise,
                OrderedExercises = orderedRecommendations,
                GeneratedAtUtc = DateTime.UtcNow
            });
        }

        [HttpPost("{instructorId:int}/children/{childId:int}/reports/summary")]
        public async Task<IActionResult> CreateChildReportsSummaryForInstructor(int instructorId, int childId)
        {
            if (instructorId <= 0 || childId <= 0)
                return BadRequest(new { message = "Invalid instructor id or child id." });

            if (!_db.GetInstructorExists(instructorId))
                return NotFound(new { message = "Instructor was not found." });

            if (!_db.GetChildAssignedToInstructor(instructorId, childId))
                return NotFound(new { message = "Child is not assigned to this instructor." });

            List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(childId);
            ChildRecord? child = _db.GetChildById(childId);
            string childDisplayName = child == null
                ? $"ילד #{childId}"
                : $"{child.FirstName} {child.LastName}".Trim();
            List<MetricMedianRecord> medianMetrics = BuildMedianMetricsFromReports(reports);

            if (reports.Count == 0)
            {
                return Ok(new
                {
                    status = "done",
                    summary = "עדיין אין דיווחים עבור הילד, לכן לא ניתן לייצר סיכום התקדמות מבוסס נתונים.",
                    reportsCount = 0,
                    source = "fallback",
                    medianMetrics
                });
            }

            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "לא הוגדרו מפתחות API תקינים עבור שירות ה-AI." });

            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();

            string medianMetricsContext = BuildMedianMetricsContext(medianMetrics);
            string recentPresentCommentsContext = BuildLastPresentInstructorCommentsContext(reports, 5);
            double overallMetricAverage = CalculateOverallMetricAverage(medianMetrics);
            int totalReports = reports.Count;
            int presentCount = reports.Count(r => r.IsPresent);
            int absentCount = totalReports - presentCount;
            double attendanceRate = totalReports > 0 ? Math.Round((presentCount * 100.0) / totalReports, 0) : 0;
            string trendContext = BuildMetricTrendContext(reports);

            string systemPrompt = (_configuration["OpenAI:InstructorSystemPrompt"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(systemPrompt))
                systemPrompt = BuildInstructorProgressSummarySystemPrompt();

            string userPromptTemplate = (_configuration["OpenAI:InstructorUserPromptTemplate"] ?? string.Empty).Trim();
            string userPrompt = !string.IsNullOrWhiteSpace(userPromptTemplate)
                ? userPromptTemplate
                    .Replace("{childDisplayName}", childDisplayName)
                    .Replace("{overallMetricAverage}", overallMetricAverage.ToString("0.0", CultureInfo.InvariantCulture))
                    .Replace("{medianMetricsContext}", medianMetricsContext)
                    .Replace("{recentPresentCommentsContext}", recentPresentCommentsContext)
                : BuildInstructorProgressSummaryUserPrompt(
                    childDisplayName, medianMetricsContext, recentPresentCommentsContext,
                    overallMetricAverage, totalReports, presentCount, absentCount, attendanceRate, trendContext);

            object requestPayload = new
            {
                model,
                temperature = 0.4,
                max_tokens = 900,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };
            string requestJson = JsonSerializer.Serialize(requestPayload);
            string medianMetricsJson = JsonSerializer.Serialize(medianMetrics);

            string jobId = Guid.NewGuid().ToString("N");

            await _dynamo.PutItemAsync(SummaryJobsTable, new Dictionary<string, AttributeValue>
            {
                ["jobId"]  = new AttributeValue { S = jobId },
                ["status"] = new AttributeValue { S = "pending" },
                ["ttl"]    = new AttributeValue { N = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString() }
            });

            // Fire-and-forget: runs after response is returned, Lambda stays alive until done
            _ = Task.Run(async () =>
            {
                try
                {
                    HttpClient bgClient = _httpClientFactory.CreateClient();
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(55));
                    using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    req.Headers.Add("HTTP-Referer", "https://equal-aquatics.app");
                    req.Headers.Add("X-Title", "Equal Aquatics");

                    using HttpResponseMessage resp = await bgClient.SendAsync(req, cts.Token);
                    string respJson = await resp.Content.ReadAsStringAsync();

                    string summary = resp.IsSuccessStatusCode ? ExtractSummaryFromOpenAiResponse(respJson) : string.Empty;

                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        await _dynamo.PutItemAsync(SummaryJobsTable, new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]        = new AttributeValue { S = jobId },
                            ["status"]       = new AttributeValue { S = "done" },
                            ["summary"]      = new AttributeValue { S = summary },
                            ["source"]       = new AttributeValue { S = "ai" },
                            ["reportsCount"] = new AttributeValue { N = totalReports.ToString() },
                            ["medianMetrics"]= new AttributeValue { S = medianMetricsJson },
                            ["ttl"]          = new AttributeValue { N = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString() }
                        });
                    }
                    else
                    {
                        await _dynamo.PutItemAsync(SummaryJobsTable, new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]  = new AttributeValue { S = jobId },
                            ["status"] = new AttributeValue { S = "error" },
                            ["error"]  = new AttributeValue { S = "שירות ה-AI לא זמין כרגע, נסו שוב." },
                            ["ttl"]    = new AttributeValue { N = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString() }
                        });
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        await _dynamo.PutItemAsync(SummaryJobsTable, new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]  = new AttributeValue { S = jobId },
                            ["status"] = new AttributeValue { S = "error" },
                            ["error"]  = new AttributeValue { S = $"שגיאה: {ex.GetType().Name}" },
                            ["ttl"]    = new AttributeValue { N = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString() }
                        });
                    }
                    catch { }
                }
            });

            return Accepted(new { jobId, status = "pending" });
        }

        [HttpGet("{instructorId:int}/children/{childId:int}/reports/summary/{jobId}")]
        public async Task<IActionResult> GetSummaryJobStatus(int instructorId, int childId, string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return BadRequest(new { message = "Invalid jobId." });

            var result = await _dynamo.GetItemAsync(SummaryJobsTable, new Dictionary<string, AttributeValue>
            {
                ["jobId"] = new AttributeValue { S = jobId }
            });

            if (result.Item == null || result.Item.Count == 0)
                return NotFound(new { message = "Job not found." });

            string status = result.Item.TryGetValue("status", out var sv) ? sv.S : "pending";

            if (status == "done")
            {
                string summary = result.Item.TryGetValue("summary", out var sum) ? sum.S : string.Empty;
                string source = result.Item.TryGetValue("source", out var src) ? src.S : "ai";
                int reportsCount = result.Item.TryGetValue("reportsCount", out var rc) && int.TryParse(rc.N, out int rcv) ? rcv : 0;
                List<MetricMedianRecord>? medianMetrics = null;
                if (result.Item.TryGetValue("medianMetrics", out var mm))
                {
                    try { medianMetrics = JsonSerializer.Deserialize<List<MetricMedianRecord>>(mm.S); } catch { }
                }
                return Ok(new { status = "done", summary, source, reportsCount, medianMetrics });
            }

            if (status == "error")
            {
                string error = result.Item.TryGetValue("error", out var ev) ? ev.S : "שגיאה לא ידועה.";
                return Ok(new { status = "error", message = error });
            }

            return Ok(new { status = "pending" });
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

            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(500, new { message = "לא הוגדרו מפתחות API תקינים עבור שירות ה-AI." });
            }

            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();

            string commentSystemPrompt = (_configuration["OpenAI:CommentAchievementSystemPrompt"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(commentSystemPrompt))
            {
                commentSystemPrompt = BuildCommentAchievementSystemPrompt();
            }

            string commentUserPromptTemplate = (_configuration["OpenAI:CommentAchievementUserPromptTemplate"] ?? string.Empty).Trim();
            string commentUserPrompt;
            if (!string.IsNullOrWhiteSpace(commentUserPromptTemplate))
            {
                StringBuilder sb = new StringBuilder();
                for (int index = 0; index < comments.Count; index += 1)
                {
                    sb.AppendLine($"{index + 1}. {comments[index]}");
                }
                commentUserPrompt = commentUserPromptTemplate.Replace("{comments}", sb.ToString().Trim());
            }
            else
            {
                commentUserPrompt = BuildCommentAchievementUserPrompt(comments);
            }

            HttpClient client = _httpClientFactory.CreateClient();
            object requestPayload = new
            {
                model = model,
                temperature = 0.0,
                top_p = 0.1,
                max_tokens = 1250,
                messages = new object[]
                {
                    new { role = "system", content = commentSystemPrompt },
                    new { role = "user", content = commentUserPrompt }
                }
            };

            string requestJson = JsonSerializer.Serialize(requestPayload);
            (bool isSuccess, string modelResponse, string failureReason) =
                await TryCreateSummaryWithApiKeyAsync(client, apiUrl, apiKey, requestJson);

            if (!isSuccess)
            {
                return StatusCode(500, new { message = "שגיאה בניתוח הישגי התגובה: " + failureReason });
            }

            if (!TryExtractCommentAchievementDecision(modelResponse, out CommentAchievementDecision decision))
            {
                return StatusCode(500, new { message = "תגובת ה-AI אינה במבנה הנתונים הנדרש." });
            }

            int commentIndex = Math.Clamp(decision.CommentIndex, 0, comments.Count);
            bool hasAchievement = decision.IsAchievement || commentIndex > 0 || !string.IsNullOrWhiteSpace(decision.SelectedSentence);
            if (!hasAchievement)
            {
                return Ok(new
                {
                    hasAchievement = false,
                    commentIndex = 0,
                    selectedComment = string.Empty,
                    achievementSentence = string.Empty,
                    positiveWords = string.Empty,
                    source = "ai"
                });
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
                return StatusCode(500, new { message = "לא נמצא משפט הישג חיובי בתגובת ה-AI." });
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

        [HttpPost("{instructorId:int}/groups/{groupId:int}/ai-bulk-reports-draft")]
        public async Task<IActionResult> GenerateAIBulkReportsDraft(
            int instructorId,
            int groupId,
            [FromBody] BulkReportDraftRequest request)
        {
            if (instructorId <= 0 || groupId <= 0 || request == null)
            {
                return BadRequest(new { message = "Invalid parameters." });
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
            if (children.Count == 0)
            {
                return Ok(new List<ChildAIBulkReportResponse>());
            }

            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(500, new { message = "לא הוגדרו מפתחות API תקינים עבור שירות ה-AI." });
            }

            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();

            var allMetrics = new List<string>
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

            var targetMetrics = (request.SelectedMetrics != null && request.SelectedMetrics.Count > 0)
                ? request.SelectedMetrics.Where(m => allMetrics.Contains(m)).ToList()
                : allMetrics;

            string allChildrenNames = string.Join(", ", children.Select(c => $"{c.FirstName} {c.LastName}".Trim()));
            string groupSentimentContext = ExtractGroupLevelSentiment(request.GeneralComment ?? string.Empty);

            HttpClient client = _httpClientFactory.CreateClient();
            var drafts = new List<ChildAIBulkReportResponse>();
            var randGen = new Random();

            using var semaphore = new SemaphoreSlim(2, 2);

            var tasks = children.Select(async (child) =>
            {
                string childName = $"{child.FirstName} {child.LastName}".Trim();
                await semaphore.WaitAsync();
                try
                {
                    // Add a small random jitter (0 to 600ms) to avoid simultaneous requests triggering rate limits
                    int jitter = 0;
                    lock (randGen)
                    {
                        jitter = randGen.Next(0, 600);
                    }
                    await Task.Delay(jitter);

                List<ChildReportRecord> reports = _db.GetChildReportsForMetrics(child.ChildId);
                string recentCommentsContext = BuildLastPresentInstructorCommentsContext(reports, 5);

                List<MetricMedianRecord> childMedianMetrics = BuildMedianMetricsFromReports(reports);
                string childHistoricalScores = childMedianMetrics.Count > 0
                    ? string.Join("\n", childMedianMetrics.Select(m => $"- {m.Label}: {m.Value:0.#}/5"))
                    : "אין היסטוריה קודמת לילד זה.";

                var fullChild = _db.GetChildById(child.ChildId);
                string childDescription = fullChild?.ChildDescription ?? string.Empty;

                string mentionContext = ExtractChildMentionFromNotes(request.GeneralComment ?? string.Empty, child.FirstName);

                // Soft history only for the metrics the instructor selected for this report
                string selectedHistoricalScores = childMedianMetrics.Count > 0
                    ? string.Join("\n", childMedianMetrics
                        .Where(m => targetMetrics.Contains(m.Label))
                        .Select(m => $"- {m.Label}: {m.Value:0.#}/5"))
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(selectedHistoricalScores))
                    selectedHistoricalScores = "אין היסטוריה רלוונטית למדדים שנבחרו.";

                string selectedMetricsList = string.Join("\n", targetMetrics.Select((m, i) => $"{i + 1}. {m}"));

                string systemPrompt = @"אתה עוזר AI של מדריך שחייה/הידרותרפיה בבית הספר לשחייה ACM.
המשימה שלך: להבין טקסט חופשי של מדריך על שיעור, ולתרגם אותו לדיווח אישי מדויק עבור ילד אחד בלבד.

== מה אתה מקבל ==
1) הערות חופשיות של המדריך על השיעור — זהו המקור הקובע היחיד לקביעת הציונים.
2) ניתוח סנטימנט כללי של הקבוצה (ברירת מחדל לילדים שלא הוזכרו בשמם).
3) רשימת מדדים שהמדריך בחר מראש לדיווח הזה — רק אותם מדדים.
4) שם הילד הנוכחי + שמות ילדי הקבוצה (כדי להבין למי מתייחס הטקסט).
5) אופציונלי: היסטוריה/רקע כהקשר עדין בלבד (לא דורס את ההערות).

== מה אתה מחזיר ==
עבור הילד הנוכחי בלבד:
- comment: הערה קצרה להורים בעברית (2–4 משפטים, גוף שלישי, חיובית ומעודדת כשאפשר).
- metrics: ציון שלם 0–5 לכל מדד ברשימה שנבחרה — ורק לה.

== סולם ציונים 0–5: מיפוי מדויק של מילים לציונים ==
הבן את שפת המדריך באופן טבעי וכללי, והמר אותה לסולם הבא:

5 (מצוין / מעולה / הישג בולט):
  מילות מפתח: ""מעולה"", ""מצוין"", ""מדהים"", ""פנטסטי"", ""שליטה מלאה"", ""ביצע בצורה מושלמת"",
  ""הפגין רמה גבוהה מאוד"", ""הצטיין"", ""הישג יוצא דופן"", ""מרשים מאוד""

4 (טוב / מעל הממוצע):
  מילות מפתח: ""טוב"", ""יפה"", ""נהדר"", ""השתפר"", ""עבד יפה"", ""ביצע היטב"",
  ""שיתף פעולה"", ""הצליח"", ""התקדם"", ""עשה עבודה טובה"", ""הראה שיפור""

3 (בסדר / ממוצע / ניטרלי):
  מילות מפתח: ""בסדר"", ""ממוצע"", ""רגיל"", ""סביר"", ""לא בולט לטוב או לרע""
  גם: כשמדד לא הוזכר כלל ואין סנטימנט כללי ברור.

2 (צריך שיפור / קשיים ניכרים):
  מילות מפתח: ""צריך שיפור"", ""דורש שיפור"", ""טעון שיפור"", ""חלש"", ""מתקשה"",
  ""לא קל לו"", ""התקשה"", ""עדיין לא שולט"", ""צריך לעבוד על"", ""לא מספיק טוב""

1 (חלש מאוד / קשיים חמורים):
  מילות מפתח: ""חלש מאוד"", ""התקשה מאוד"", ""כמעט לא הצליח"", ""קשיים חמורים"",
  ""נדרשת עזרה רבה"", ""רחוק מהרמה הנדרשת""

0 (לא ביצע / סירוב / פחד מוחלט):
  מילות מפתח: ""סירב"", ""פחד חמור"", ""לא ביצע כלל"", ""לא רצה"", ""בכה ולא השתתף"",
  ""סירב להיכנס למים"", ""כישלון מלא""

== טבלת מילים נרדפות — מיפוי ביטויים למדדים ==
כשהמדריך כותב ביטוי שמתייחס לתחום מסוים, שייך אותו למדד המתאים מהרשימה שנבחרה:

""ביטחון""/""פחד""/""חשש""/""חרדה""/""הסתגלות""/""נכנס למים""/""לא פחד"" → הסתגלות וביטחון במים
""נשימות""/""הכניס ראש""/""ראש למים""/""נשף בועות""/""הכנסת פנים"" → שליטה בנשימות (הכנסת ראש למים)
""תנועה""/""קואורדינציה""/""ידיים ורגליים""/""תנועות גוף""/""מוטוריקה"" → תנועתיות וקואורדינציה
""ציפה""/""צף""/""יציבות""/""שמר על יציבה""/""גוף ישר"" → יציבה וציפה
""שיתוף פעולה""/""חברתיות""/""עבד עם חברים""/""תקשורת""/""קשר""/""עזר לאחרים"" → תקשורת במים (ושיתוף פעולה)
""התמדה""/""מאמץ""/""ניסה שוב""/""לא ויתר""/""המשיך לנסות""/""השקיע"" → התמדה ומאמץ
""יוזמה""/""יזם""/""ביקש לנסות""/""הציע""/""רצה עוד"" → יוזמה
""קשב""/""ריכוז""/""הקשיב""/""מרוכז""/""ממוקד""/""שם לב"" → קשב וריכוז
""הוראות""/""הקשיב להוראות""/""ציית""/""עשה מה שביקשתי""/""הגיב להנחיות""/""הוראה"" → תגובה להוראות
""עצמאי""/""בעצמו""/""לבד""/""ללא עזרה""/""עצמאות""/""בלי תמיכה"" → עצמאות בתרגיל

== כללי שיוך לילדים (קריטי!) ==

1. הילד מוזכר בשמו:
   - הציונים וההערה חייבים לשקף בדיוק את מה שנאמר עליו.
   - אם נאמר עליו משהו חיובי במדד מסוים — תן 4 או 5 לפי עוצמת החיוב.
   - אם נאמר עליו משהו שלילי במדד מסוים — תן 1 או 2 לפי עוצמת השלילה.
   - מדדים שלא הוזכרו עבורו ספציפית: השתמש בסנטימנט הכללי של הקבוצה.

2. הילד לא מוזכר בשמו:
   - השתמש בסנטימנט הכללי של הקבוצה כבסיס לכל המדדים.
   - אם הקבוצה תוארה כ""טובים"" → 4 לכל המדדים.
   - אם הקבוצה תוארה כ""מצוינים/מעולים"" → 5 לכל המדדים.
   - אם הקבוצה תוארה כ""בסדר"" → 3 לכל המדדים.
   - אם הקבוצה תוארה כ""צריכים שיפור"" → 2 לכל המדדים.
   - אם אין תיאור כללי → ברירת מחדל 3.

3. ""כולם""/""כל הילדים""/""הקבוצה כולה""/""שאר הילדים""/""שאר הקבוצה"":
   - החל על כל ילד שלא הוזכר עם תיאור סותר/שונה בשמו.
   - אם נאמר ""כולם טובים בכל המדדים"" — תן 4 לכולם בכל המדדים.
   - אם נאמר ""כולם מצוינים"" — תן 5.
   - אם נאמר ""שאר הקבוצה בסדר"" — תן 3.

4. הבחנה בין מדדים (חשוב!):
   - אל תעתיק אוטומטית ציון זהה לכל המדדים, אלא אם הטקסט באמת אומר כך.
   - אם הטקסט אומר ""טוב בקשב אבל צריך שיפור בעצמאות"" → קשב=4, עצמאות=2.
   - אם הטקסט אומר ""טוב בכל המדדים"" → 4 לכולם (זה מותר כי הטקסט אומר כך).
   - מדדים שלא הוזכרו ספציפית: ציון לפי הסנטימנט הכללי (של הילד או הקבוצה).

5. עקביות מוחלטת:
   - ההערה (comment) והציונים (metrics) חייבים לספר בדיוק את אותו סיפור.
   - אם ההערה אומרת ""הצטיין בקשב"" → ציון קשב חייב להיות 5.
   - אם ההערה אומרת ""צריך שיפור בעצמאות"" → ציון עצמאות חייב להיות 1 או 2.

== מגבלות ==
- אסור להוסיף מדדים שלא ברשימה שנבחרה.
- אסור לשנות את תוויות המדדים — העתק אותן בדיוק כפי שניתנו.
- אסור להמציא עובדות שלא משתמעות מהערות המדריך.
- אסור markdown. JSON בלבד, בלי טקסט מסביב, בלי הסברים.";

                string metricsJsonTemplate = string.Join(",\n    ",
                    targetMetrics.Select(m => $"{{ \"label\": \"{m}\", \"value\": 0 }}"));

                string userPrompt = $@"=== פרטי השיעור ===
תאריך: {request.ReportDate}
תרגיל מרכזי (הקשר בלבד): {request.ExerciseTitle} ({request.ExerciseKey})
ילדי הקבוצה: {allChildrenNames}

=== הערות המדריך — הטקסט המלא (המקור הקובע היחיד לציונים) ===
{request.GeneralComment}

=== ניתוח סנטימנט כללי של הקבוצה (ברירת מחדל לילדים שלא הוזכרו) ===
{groupSentimentContext}

=== הילד הנוכחי שעבורו תיצור דיווח ===
שם: {childName}
אזכור בהערות: {mentionContext}

=== המדדים הנבחרים (חובה לתת ציון 0–5 לכל אחד, ורק להם) ===
{selectedMetricsList}

=== הקשר היסטורי עדין (רקע בלבד — לא דורס את ההערות של היום) ===
היסטוריית מדדים נבחרים (ממוצעים מדיווחים קודמים):
{selectedHistoricalScores}

הערות מדריך קודמות על {childName}:
{recentCommentsContext}

רקע הילד:
{(string.IsNullOrWhiteSpace(childDescription) ? "אין." : childDescription)}

=== משימה — בצע בדיוק ===
קרא את הערות המדריך. קבע עבור {childName} בלבד:
1) comment: הערה להורים בעברית (2–4 משפטים, גוף שלישי) שמשקפת בדיוק את הביצוע שתואר.
2) metrics: ציון שלם 0–5 לכל מדד מהרשימה שנבחרה.

כללי ציון:
- מדד שהוזכר עם תיאור חיובי (""מעולה""/""מצוין"") → 5
- מדד שהוזכר עם תיאור טוב (""טוב""/""יפה""/""נהדר"") → 4
- מדד שלא הוזכר ספציפית → השתמש בסנטימנט הכללי (ראה למעלה)
- מדד שהוזכר עם ""צריך שיפור"" → 2
- מדד שהוזכר עם ""חלש מאוד"" → 1
- מדד שהוזכר עם ""סירב""/""לא ביצע"" → 0

החזר JSON בלבד (בלי הסברים, בלי markdown):
{{
  ""comment"": ""..."",
  ""metrics"": [
    {metricsJsonTemplate}
  ]
}}
מלא value מדויק (0–5) לכל מדד. אל תוסיף מדדים שלא ברשימה.";

                object requestPayload = new
                {
                    model = model,
                    temperature = 0.15,
                    max_tokens = 1000,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    }
                };

                string requestJson = JsonSerializer.Serialize(requestPayload);
                string draftComment = string.Empty;
                List<ChildReportMetricResponse> draftMetrics = new List<ChildReportMetricResponse>();

                    (bool isSuccess, string modelResponse, string failureReason) =
                        await TryCreateSummaryWithApiKeyAsync(client, apiUrl, apiKey, requestJson);

                    if (!isSuccess || string.IsNullOrWhiteSpace(modelResponse))
                    {
                        throw new InvalidOperationException($"השירות נכשל לאחר מספר ניסיונות. שגיאה: {failureReason}");
                    }

                    string cleanJson = modelResponse.Trim();
                    if (cleanJson.StartsWith("```", StringComparison.Ordinal))
                    {
                        int firstLineBreak = cleanJson.IndexOf('\n');
                        if (firstLineBreak >= 0) cleanJson = cleanJson.Substring(firstLineBreak + 1);
                        int closingFenceIndex = cleanJson.LastIndexOf("```", StringComparison.Ordinal);
                        if (closingFenceIndex >= 0) cleanJson = cleanJson.Substring(0, closingFenceIndex);
                        cleanJson = cleanJson.Trim();
                    }
                    int objectStart = cleanJson.IndexOf('{');
                    int objectEnd = cleanJson.LastIndexOf('}');
                    if (objectStart >= 0 && objectEnd > objectStart)
                    {
                        cleanJson = cleanJson.Substring(objectStart, objectEnd - objectStart + 1);
                    }

                    cleanJson = RepairTruncatedJson(cleanJson);

                    using JsonDocument doc = JsonDocument.Parse(cleanJson);
                    JsonElement root = doc.RootElement;
                    string? comment = root.GetProperty("comment").GetString();
                    if (string.IsNullOrWhiteSpace(comment))
                    {
                        throw new InvalidOperationException("תגובת ה-AI אינה כוללת הערה תקינה.");
                    }
                    draftComment = comment;

                    // Keep only instructor-selected metrics; drop any extras the model may invent
                    var scoresByLabel = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (root.TryGetProperty("metrics", out JsonElement metricsElement) && metricsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement m in metricsElement.EnumerateArray())
                        {
                            string label = m.GetProperty("label").GetString() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(label) || !targetMetrics.Contains(label))
                                continue;

                            int val = m.GetProperty("value").GetInt32();
                            scoresByLabel[label] = Math.Clamp(val, 0, 5);
                        }
                    }

                    if (scoresByLabel.Count == 0)
                    {
                        throw new InvalidOperationException("תגובת ה-AI אינה כוללת מדדים תקינים.");
                    }

                    // Ensure every selected metric appears exactly once (missing → neutral 3)
                    draftMetrics = targetMetrics
                        .Select(label => new ChildReportMetricResponse
                        {
                            Label = label,
                            Value = scoresByLabel.TryGetValue(label, out int score) ? score : 3
                        })
                        .ToList();
                    lock (drafts)
                    {
                        drafts.Add(new ChildAIBulkReportResponse
                        {
                            ChildId = child.ChildId,
                            ChildName = childName,
                            IsPresent = true,
                            Comment = draftComment,
                            Metrics = draftMetrics
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogAiError("GenerateAIBulkReportsDraft failed", $"Error: {ex.Message} (Child: {childName})", ex.StackTrace ?? string.Empty);
                    throw new InvalidOperationException($"נכשלה יצירת הדיווח עבור {childName}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            return Ok(drafts);
        }

        [HttpPost("{instructorId:int}/reports-bulk")]
        public async Task<IActionResult> CreateBulkReportsForInstructor(int instructorId, [FromBody] BulkReportSaveRequest request)
        {
            if (instructorId <= 0 || request == null)
            {
                return BadRequest(new { message = "Invalid parameters." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            DateTime reportDate = DateTime.TryParse(request.ReportDate, out DateTime parsedDate)
                ? parsedDate.Date
                : DateTime.UtcNow.Date;

            int savedCount = 0;

            foreach (var draft in request.Drafts)
            {
                int childId = draft.ChildId;
                if (!_db.GetChildAssignedToInstructor(instructorId, childId, request.GroupId))
                {
                    continue; 
                }

                bool isPresent = draft.IsPresent;
                string metricsJson = string.Empty;
                string comment = string.Empty;

                if (isPresent)
                {
                    var normalizedMetrics = draft.Metrics
                        .Where((metric) => !string.IsNullOrWhiteSpace(metric.Label))
                        .Select((metric) => new
                        {
                            label = metric.Label.Trim(),
                            value = Math.Clamp(metric.Value, 0, 5),
                            isIncluded = metric.IsIncluded ?? true
                        })
                        .ToList();

                    metricsJson = JsonSerializer.Serialize(normalizedMetrics);
                    comment = draft.Comment.Trim();
                }

                ChildReportRecord? existingReport = _db.GetChildReportByDateAndExercise(childId, instructorId, reportDate, request.ExerciseKey);

                if (existingReport != null)
                {
                    _db.UpdateChildReportForInstructor(
                        existingReport.ReportId,
                        childId,
                        instructorId,
                        request.GroupId,
                        reportDate,
                        request.ExerciseKey,
                        request.ExerciseTitle,
                        isPresent,
                        comment,
                        metricsJson);
                }
                else
                {
                    _db.PostCreateChildReport(
                        childId,
                        instructorId,
                        request.GroupId,
                        reportDate,
                        request.ExerciseKey,
                        request.ExerciseTitle,
                        isPresent,
                        comment,
                        metricsJson);
                }

                savedCount++;

                var childObj = _db.GetChildById(childId);
                if (childObj != null)
                {
                    string notificationTitle = "דיווח התקדמות חדש";
                    string notificationBody = $"התקבל דיווח התקדמות חדש עבור {childObj.FirstName} בשיעור {request.ExerciseTitle}.";
                    _db.PostCreateUserLessonNotification(
                        "Parent",
                        childObj.ParentId,
                        "ChildReportCreated",
                        notificationTitle,
                        notificationBody,
                        relatedInvitationId: null,
                        relatedRecipientId: null,
                        payloadJson: JsonSerializer.Serialize(new { childId = childId, exerciseKey = request.ExerciseKey }));
                }
            }

            DateTime notificationsStartUtc = DateTime.UtcNow.AddSeconds(-30);
            await TrySendNewLessonNotificationsPushAsync(
                notificationsStartUtc,
                relatedInvitationId: null,
                notificationTypeFilter: "ChildReportCreated");

            return Ok(new { savedCount });
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

        private static string BuildInstructorProgressSummarySystemPrompt()
        {
            return @"אתה עוזר מקצועי של מדריך שחייה והידרותרפיה בבית הספר לשחייה ACM.
תפקידך: לייצר סיכום התקדמות מפורט, מדויק ומעצב עבור ילד ספציפי, המבוסס אך ורק על הנתונים שסופקו.

חוקים מחייבים:
1. כתוב תמיד בעברית תקנית, רהוטה ומקצועית.
2. אל תמציא מידע שאינו קיים בנתונים.
3. אל תשתמש ב-markdown (כוכביות להדגשה, #, ``` וכו').
4. עצב את הפלט בדיוק לפי המבנה המבוקש עם אימוג'ים כותרות.
5. הסקות שלך חייבות להיות מבוססות על הציונים, מגמות וההערות שסופקו בלבד.";
        }

        private static string BuildInstructorProgressSummaryUserPrompt(
            string childDisplayName,
            string medianMetricsContext,
            string recentPresentCommentsContext,
            double overallMetricAverage,
            int totalReports,
            int presentCount,
            int absentCount,
            double attendanceRate,
            string trendContext)
        {
            return $@"הפק סיכום התקדמות מקיף עבור התלמיד: {childDisplayName}

📊 נתוני נוכחות:
- סה""כ שיעורים: {totalReports}
- נכח: {presentCount} | נעדר: {absentCount}
- אחוז נוכחות: {attendanceRate}%

📈 ממוצע כללי: {overallMetricAverage.ToString("0.0", CultureInfo.InvariantCulture)}/5

🎯 ציוני מדדים (חציון מכל הדיווחים):
{medianMetricsContext}

📉 מגמת התקדמות:
{trendContext}

💬 הערות מדריך אחרונות (שיעורים שבהם הילד נכח):
{recentPresentCommentsContext}

כתוב את הסיכום בעברית בדיוק במבנה הבא (אל תשנה את הכותרות):

✅ חוזקות:
(2-3 משפטים המתארים את נקודות החוזק של הילד לפי הנתונים)

⚠️ תחומים לשיפור:
(2-3 משפטים המתארים את האתגרים הספציפיים לפי הציונים הנמוכים)

🎯 המלצות למדריך:
(2-3 המלצות פעולה קונקרטיות לשיעורים הבאים)

🏠 המלצות להורה:
(1-2 משפטים עם פעולות תמיכה שההורה יכול לעשות בבית)";
        }

        private static string ExtractChildMentionFromNotes(string generalComment, string firstName)
        {
            if (string.IsNullOrWhiteSpace(generalComment) || string.IsNullOrWhiteSpace(firstName))
                return "לא מוזכר/ת בשמו/ה בהערות — יש להחיל את הסנטימנט הכללי של הקבוצה.";

            string normalized = generalComment;
            // Split on sentence boundaries (.!?) and newlines, but preserve commas within sentences
            string[] sentences = Regex.Split(normalized, @"(?<=[.!?])\s+|\n")
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            var relevantSentences = sentences
                .Where(s => s.IndexOf(firstName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (relevantSentences.Count == 0)
                return "לא מוזכר/ת בשמו/ה בהערות — יש להחיל את הסנטימנט הכללי של הקבוצה.";

            // Detect sentiment for this child's mentions
            string combinedMentions = string.Join(" ", relevantSentences);
            string sentimentTag = DetectSentimentTag(combinedMentions);

            return $"כן — מוזכר/ת בהערות ({sentimentTag}). הקטעים הרלוונטיים: {string.Join(" | ", relevantSentences)}";
        }

        private static string DetectSentimentTag(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "ניטרלי";
            string lower = text;

            string[] strongPositive = { "מעולה", "מצוין", "מדהים", "פנטסטי", "הצטיין", "מושלם", "מרשים מאוד" };
            string[] positive = { "טוב", "יפה", "נהדר", "השתפר", "הצליח", "שיתף פעולה", "התקדם", "עבד יפה" };
            string[] negative = { "צריך שיפור", "דורש שיפור", "טעון שיפור", "חלש", "מתקשה", "התקשה", "לא קל", "עדיין לא" };
            string[] strongNegative = { "חלש מאוד", "סירב", "פחד", "לא ביצע", "לא רצה", "בכה", "כישלון" };

            bool hasStrongPos = strongPositive.Any(w => lower.Contains(w));
            bool hasPos = positive.Any(w => lower.Contains(w));
            bool hasNeg = negative.Any(w => lower.Contains(w));
            bool hasStrongNeg = strongNegative.Any(w => lower.Contains(w));

            if (hasStrongPos && hasNeg) return "מעורב — חיובי מאוד בחלק, צריך שיפור בחלק";
            if (hasPos && hasStrongNeg) return "מעורב — חיובי בחלק, חלש מאוד בחלק";
            if (hasPos && hasNeg) return "מעורב — חיובי בחלק, צריך שיפור בחלק";
            if (hasStrongPos) return "חיובי מאוד (5)";
            if (hasPos) return "חיובי (4)";
            if (hasStrongNeg) return "שלילי מאוד (0-1)";
            if (hasNeg) return "צריך שיפור (2)";
            return "ניטרלי (3)";
        }

        private static string ExtractGroupLevelSentiment(string generalComment)
        {
            if (string.IsNullOrWhiteSpace(generalComment))
                return "אין הערות כלליות — ברירת מחדל: ציון 3 (ניטרלי) לכל מדד.";

            string text = generalComment;
            var results = new List<string>();

            // Check for group-wide statements
            string[] groupIndicators = { "כולם", "כל הילדים", "הקבוצה", "שאר הילדים", "שאר הקבוצה", "כל הקבוצה", "כולם חוץ" };
            string[] allMetricsIndicators = { "בכל המדדים", "בהכל", "בכל דבר", "בכל התחומים" };

            // Detect group-level positive
            string[] strongPositiveWords = { "מעולים", "מצוינים", "מדהימים", "מושלמים" };
            string[] positiveWords = { "טובים", "יפים", "נהדרים", "עבדו יפה", "עבדו טוב", "שיתפו פעולה", "הצליחו" };
            string[] neutralWords = { "בסדר", "ממוצעים", "רגילים", "סבירים" };
            string[] negativeWords = { "צריכים שיפור", "התקשו", "חלשים", "מתקשים", "לא טובים" };
            string[] strongNegativeWords = { "חלשים מאוד", "נכשלו", "סירבו", "פחדו" };

            bool hasGroupIndicator = groupIndicators.Any(g => text.Contains(g));
            bool hasAllMetrics = allMetricsIndicators.Any(m => text.Contains(m));

            if (hasGroupIndicator)
            {
                if (strongPositiveWords.Any(w => text.Contains(w)))
                {
                    results.Add(hasAllMetrics
                        ? "הקבוצה תוארה כמצוינת/מעולה בכל המדדים → ברירת מחדל: ציון 5 לכל מדד."
                        : "הקבוצה תוארה כמצוינת/מעולה → ברירת מחדל: ציון 5 למדדים שלא צוינו ספציפית.");
                }
                else if (positiveWords.Any(w => text.Contains(w)))
                {
                    results.Add(hasAllMetrics
                        ? "הקבוצה תוארה כטובה בכל המדדים → ברירת מחדל: ציון 4 לכל מדד."
                        : "הקבוצה תוארה כטובה → ברירת מחדל: ציון 4 למדדים שלא צוינו ספציפית.");
                }
                else if (neutralWords.Any(w => text.Contains(w)))
                {
                    results.Add("הקבוצה תוארה כבסדר/ממוצעת → ברירת מחדל: ציון 3 לכל מדד.");
                }
                else if (negativeWords.Any(w => text.Contains(w)))
                {
                    results.Add("הקבוצה תוארה כצריכה שיפור → ברירת מחדל: ציון 2 לכל מדד.");
                }
                else if (strongNegativeWords.Any(w => text.Contains(w)))
                {
                    results.Add("הקבוצה תוארה כחלשה מאוד → ברירת מחדל: ציון 1 לכל מדד.");
                }
            }

            if (results.Count == 0)
            {
                // Try to detect overall tone even without explicit group indicators
                string sentimentTag = DetectSentimentTag(text);
                results.Add($"סנטימנט כללי מזוהה: {sentimentTag}. אם ילד לא מוזכר בשמו — השתמש בסנטימנט הזה כברירת מחדל.");
            }

            return string.Join("\n", results);
        }

        private static string BuildMetricTrendContext(List<ChildReportRecord> reports)
        {
            var presentReports = reports
                .Where(r => r.IsPresent && !string.IsNullOrWhiteSpace(r.Metrics))
                .OrderBy(r => r.ReportDate)
                .ToList();

            if (presentReports.Count < 4)
            {
                return "אין מספיק נתונים לחישוב מגמה (נדרשים לפחות 4 שיעורים עם נוכחות).";
            }

            int halfCount = presentReports.Count / 2;
            var olderReports = presentReports.Take(halfCount).ToList();
            var newerReports = presentReports.Skip(halfCount).ToList();

            double OldAvg(List<ChildReportRecord> reps)
            {
                var scores = new List<double>();
                foreach (var r in reps)
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(r.Metrics!);
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                                scores.Add(v.GetDouble());
                        }
                    }
                    catch { }
                }
                return scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0;
            }

            double oldAvg = OldAvg(olderReports);
            double newAvg = OldAvg(newerReports);
            double delta = newAvg - oldAvg;

            string trend = delta > 0.5 ? $"📈 שיפור מובהק (+{delta:0.1} נקודות)" :
                           delta < -0.5 ? $"📉 ירידה ({delta:0.1} נקודות)" :
                           "➡️ יציב (שינוי קטן מ-0.5 נקודות)";

            return $"{trend} | ממוצע תקופה ראשונה: {oldAvg:0.1} | ממוצע תקופה אחרונה: {newAvg:0.1}";
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

        private static string BuildCommentAchievementSystemPrompt()
        {
            return @"JSON ONLY. Find positive achievement.
Reply: {""ok"":1/0,""i"":index(1-3),""s"":""positive fragment"",""w"":""<=4 positive words""}
0 if mostly negative. No markdown/text.".Trim();
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

        private static ExerciseRecommendationItem BuildExerciseRecommendationItem(
            ExerciseDefinitionRecord exercise,
            Dictionary<string, ExerciseProgressInput> progressByKey,
            Dictionary<string, double> domainWeaknessScores)
        {
            progressByKey.TryGetValue(exercise.Key, out ExerciseProgressInput? progress);

            int completedCount = Math.Max(0, progress?.CompletedCount ?? 0);
            double? explicitLastScore = NormalizeOptionalToTenScale(progress?.LastScore);
            double? metricsLastScore = CalculateExerciseMetricsScore(progress?.Metrics);
            double? effectiveLastScore = explicitLastScore ?? metricsLastScore;

            double weaknessFit = CalculateDomainFitScore(exercise.DomainWeights, domainWeaknessScores);
            double unfinishedBoost = completedCount == 0 ? 4.0 : 0;
            double strugglingBoost = effectiveLastScore.HasValue
                ? Math.Max(0, (7.0 - effectiveLastScore.Value) * 0.55)
                : 0;
            double sequenceBias = (ExerciseCatalog.Count - exercise.DefaultOrder) * 0.08;
            double repetitionPenalty = Math.Min(6, completedCount) * 0.35;

            double priorityScore = Math.Round(
                weaknessFit + unfinishedBoost + strugglingBoost + sequenceBias - repetitionPenalty,
                2,
                MidpointRounding.AwayFromZero);

            string topDomain = exercise.DomainWeights
                .OrderByDescending((entry) => entry.Value)
                .Select((entry) => entry.Key)
                .FirstOrDefault() ?? "motor";

            string reason = completedCount == 0
                ? $"תרגיל שטרם בוצע; חיזוק ממוקד בתחום {TranslateDomain(topDomain)}."
                : $"תרגיל לחיזוק תחום {TranslateDomain(topDomain)} עם קצב אישי לפי ביצועים קודמים.";

            return new ExerciseRecommendationItem
            {
                ExerciseKey = exercise.Key,
                Title = exercise.Title,
                DefaultOrder = exercise.DefaultOrder,
                CompletedCount = completedCount,
                LastScore = effectiveLastScore,
                PriorityScore = priorityScore,
                MainDomain = topDomain,
                Reason = reason
            };
        }

        private static Dictionary<string, double> BuildDomainWeaknessScores(
            List<MetricMedianRecord> medianMetrics,
            string? childDescription,
            List<string>? currentDifficulties)
        {
            double adaptation = GetMetricValue(medianMetrics, "הסתגלות וביטחון במים");
            double breathing = GetMetricValue(medianMetrics, "שליטה בנשימות (הכנסת ראש למים)");
            double motor = GetMetricValue(medianMetrics, "תנועתיות וקואורדינציה");
            double posture = GetMetricValue(medianMetrics, "יציבה וציפה");
            double communication = GetMetricValue(medianMetrics, "תקשורת במים (ושיתוף פעולה)");
            double persistence = GetMetricValue(medianMetrics, "התמדה ומאמץ");

            Dictionary<string, double> weaknesses = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["motor"] = Math.Clamp(10 - ((motor + posture) / 2.0), 0, 10),
                ["communication"] = Math.Clamp(10 - communication, 0, 10),
                ["emotional"] = Math.Clamp(10 - ((adaptation + persistence) / 2.0), 0, 10),
                ["safety"] = Math.Clamp(10 - ((breathing + adaptation) / 2.0), 0, 10)
            };

            string profileText = (childDescription ?? string.Empty).Trim();
            foreach (string difficulty in currentDifficulties ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(difficulty))
                {
                    continue;
                }

                profileText += " " + difficulty;
            }

            string loweredProfile = profileText.ToLowerInvariant();
            foreach (string domain in ExerciseDomainNames)
            {
                if (!DomainKeywordMap.TryGetValue(domain, out string[]? keywords))
                {
                    continue;
                }

                int matches = keywords.Count((keyword) => loweredProfile.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal));
                if (matches > 0)
                {
                    double boost = Math.Min(3.0, matches * 1.2);
                    weaknesses[domain] = Math.Min(10, weaknesses[domain] + boost);
                }
            }

            return weaknesses;
        }

        private static double CalculateDomainFitScore(
            Dictionary<string, double> domainWeights,
            Dictionary<string, double> domainWeaknessScores)
        {
            if (domainWeights.Count == 0)
            {
                return 0;
            }

            double weightedSum = 0;
            double weightsTotal = 0;

            foreach ((string domain, double weight) in domainWeights)
            {
                if (weight <= 0)
                {
                    continue;
                }

                double weakness = domainWeaknessScores.TryGetValue(domain, out double score)
                    ? score
                    : 0;

                weightedSum += weakness * weight;
                weightsTotal += weight;
            }

            if (weightsTotal <= 0)
            {
                return 0;
            }

            return Math.Round(weightedSum / weightsTotal, 2, MidpointRounding.AwayFromZero);
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

        private static double GetMetricValue(List<MetricMedianRecord> metrics, string label)
        {
            MetricMedianRecord? metric = metrics.FirstOrDefault((item) => string.Equals(item.Label, label, StringComparison.OrdinalIgnoreCase));
            return metric?.Value ?? 0;
        }

        private static List<ExerciseProgressMetricInput> ParseMetricsJsonToInputList(string? metricsJson)
        {
            if (string.IsNullOrWhiteSpace(metricsJson))
            {
                return new List<ExerciseProgressMetricInput>();
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(metricsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new List<ExerciseProgressMetricInput>();
                }

                List<ExerciseProgressMetricInput> result = new List<ExerciseProgressMetricInput>();
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    string label = element.TryGetProperty("label", out JsonElement labelEl)
                        ? labelEl.GetString() ?? string.Empty
                        : string.Empty;

                    double? numericValue = element.TryGetProperty("value", out JsonElement valueEl)
                        && valueEl.TryGetDouble(out double v)
                        ? v
                        : null;

                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        result.Add(new ExerciseProgressMetricInput
                        {
                            Label = label.Trim(),
                            NumericValue = numericValue,
                            TextValue = numericValue?.ToString() ?? string.Empty,
                        });
                    }
                }

                return result;
            }
            catch
            {
                return new List<ExerciseProgressMetricInput>();
            }
        }

        private static double? CalculateExerciseMetricsScore(List<ExerciseProgressMetricInput>? metrics)
        {
            if (metrics == null || metrics.Count == 0)
            {
                return null;
            }

            double weightedSum = 0;
            double weightsTotal = 0;

            foreach (ExerciseProgressMetricInput metric in metrics)
            {
                double? metricScore = NormalizeMetricInputToTenScale(metric);
                if (!metricScore.HasValue)
                {
                    continue;
                }

                double weight = metric.Weight.HasValue && metric.Weight.Value > 0
                    ? metric.Weight.Value
                    : 1;

                weightedSum += metricScore.Value * weight;
                weightsTotal += weight;
            }

            if (weightsTotal <= 0)
            {
                return null;
            }

            return Math.Round(weightedSum / weightsTotal, 1, MidpointRounding.AwayFromZero);
        }

        private static double? NormalizeMetricInputToTenScale(ExerciseProgressMetricInput metric)
        {
            if (metric.NumericValue.HasValue)
            {
                return NormalizeOptionalToTenScale(metric.NumericValue);
            }

            string raw = (metric.TextValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedInvariant))
            {
                return NormalizeOptionalToTenScale(parsedInvariant);
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsedCurrent))
            {
                return NormalizeOptionalToTenScale(parsedCurrent);
            }

            string lowered = raw.ToLowerInvariant();
            if (lowered == "+") return 10;
            if (lowered == "0") return 5;
            if (lowered == "-") return 0;
            if (lowered is "עצמאי" or "עצמאי למחצה") return 10;
            if (lowered is "חלקי" or "חלקית") return 6;
            if (lowered is "מלא" or "מלאה") return 2;

            return null;
        }

        private static double? NormalizeOptionalToTenScale(double? rawScore)
        {
            if (!rawScore.HasValue || !double.IsFinite(rawScore.Value))
            {
                return null;
            }

            double score = rawScore.Value;
            if (score >= 0 && score <= 5)
            {
                score *= 2;
            }

            return Math.Round(Math.Clamp(score, 0, 10), 1, MidpointRounding.AwayFromZero);
        }

        private static string TranslateDomain(string domain)
        {
            return domain.ToLowerInvariant() switch
            {
                "motor" => "מוטוריקה",
                "communication" => "תקשורת",
                "emotional" => "ויסות/ביטחון רגשי",
                "safety" => "בטיחות במים",
                _ => "מיומנויות בסיס"
            };
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
                        if (metric.TryGetProperty("isIncluded", out JsonElement isIncElement) && isIncElement.ValueKind == JsonValueKind.False)
                        {
                            index += 1;
                            continue;
                        }

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
                    // Ignore malformed metrics rows so one bad report does not break aggregation.
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
            int maxAttempts = 6;
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };

                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    requestMessage.Headers.Add("HTTP-Referer", "https://equal-aquatics.app");
                    requestMessage.Headers.Add("X-Title", "Equal Aquatics");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    using HttpResponseMessage response = await client.SendAsync(requestMessage, cts.Token);
                    string responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        string shortBody = responseJson.Length > 200 ? responseJson[..200] : responseJson;
                        lastError = $"provider returned {(int)response.StatusCode}: {shortBody}";
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(attempt * attempt * 400);
                        }
                        continue;
                    }

                    string summary = ExtractSummaryFromOpenAiResponse(responseJson);
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        lastError = "provider returned empty summary";
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(attempt * attempt * 400);
                        }
                        continue;
                    }

                    return (true, summary, string.Empty);
                }
                catch (Exception ex)
                {
                    lastError = $"request error ({ex.GetType().Name}): {ex.Message}";
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(attempt * attempt * 400);
                    }
                }
            }

            return (false, string.Empty, lastError);
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
                    return cleanedResult.Trim()
                        .Replace("הבוגרים יום", "הבתולת ים")
                        .Replace("בוגרים יום", "בתולת ים")
                        .Replace("הבוגריםיום", "הבתולת ים")
                        .Replace("בוגריםיום", "בתולת ים");
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
                    .Where((metric) => metric.Value <= 3)
                    .ToList();

                double overallAverage = CalculateOverallMetricAverage(effectiveMedianMetrics);
                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("ממוצע ציונים במדדים:");
                summaryBuilder.AppendLine($"ממוצע כללי: {overallAverage.ToString("0.0", CultureInfo.InvariantCulture)}/5");

                foreach (MetricMedianRecord metric in effectiveMedianMetrics)
                {
                    summaryBuilder.AppendLine($"- {metric.Label}: {metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/5");
                }

                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("מה הילד עושה טוב:");
                foreach (MetricMedianRecord metric in strongest.Take(2))
                {
                    summaryBuilder.AppendLine($"- {metric.Label} ({metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/5)");
                }

                summaryBuilder.AppendLine();
                summaryBuilder.AppendLine("מה הילד צריך לשפר:");
                if (needsImprovement.Count == 0)
                {
                    summaryBuilder.AppendLine("- אין כרגע מדדים שדורשים חיזוק ממוקד (כל המדדים מעל 3).");
                }
                else
                {
                    foreach (MetricMedianRecord metric in needsImprovement)
                    {
                        summaryBuilder.AppendLine($"- {metric.Label} ({metric.Value.ToString("0.0", CultureInfo.InvariantCulture)}/5)");
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
            summaryBuilder.AppendLine("המלצה למדריך/להורה:");
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
            public string ExerciseKey { get; set; } = string.Empty;
            public string ExerciseTitle { get; set; } = string.Empty;
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
            public bool? IsIncluded { get; set; }
        }

        public class MetricMedianRecord
        {
            public string Label { get; set; } = string.Empty;
            public double Value { get; set; }
        }

        public class ExercisePlanRecommendationRequest
        {
            public List<string> CurrentDifficulties { get; set; } = new List<string>();
            public List<ExerciseProgressInput> ExerciseProgress { get; set; } = new List<ExerciseProgressInput>();
            public bool IncludeCompleted { get; set; } = true;
        }

        public class ExerciseProgressInput
        {
            public string ExerciseKey { get; set; } = string.Empty;
            public int CompletedCount { get; set; }
            public double? LastScore { get; set; }
            public List<ExerciseProgressMetricInput> Metrics { get; set; } = new List<ExerciseProgressMetricInput>();
        }

        public class ExerciseProgressMetricInput
        {
            public string Label { get; set; } = string.Empty;
            public string TextValue { get; set; } = string.Empty;
            public double? NumericValue { get; set; }
            public double? Weight { get; set; }
        }

        public class ExercisePlanRecommendationResponse
        {
            public int ChildId { get; set; }
            public string ChildName { get; set; } = string.Empty;
            public int TotalExercises { get; set; }
            public int CompletedExercises { get; set; }
            public double CoveragePercent { get; set; }
            public int ReportsCount { get; set; }
            public DateTime GeneratedAtUtc { get; set; }
            public Dictionary<string, double> DomainWeakness { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public ExerciseRecommendationItem? NextExercise { get; set; }
            public List<ExerciseRecommendationItem> OrderedExercises { get; set; } = new List<ExerciseRecommendationItem>();
        }

        public class ExerciseRecommendationItem
        {
            public string ExerciseKey { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public int DefaultOrder { get; set; }
            public int CompletedCount { get; set; }
            public double? LastScore { get; set; }
            public double PriorityScore { get; set; }
            public string MainDomain { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }

        private sealed class ExerciseDefinitionRecord
        {
            public string Key { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public int DefaultOrder { get; set; }
            public Dictionary<string, double> DomainWeights { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CommentAchievementDecision
        {
            public bool IsAchievement { get; set; }
            public int CommentIndex { get; set; }
            public string SelectedSentence { get; set; } = string.Empty;
            public string PositiveWords { get; set; } = string.Empty;
        }

        public class BulkReportDraftRequest
        {
            public string GeneralComment { get; set; } = string.Empty;
            public string ExerciseKey { get; set; } = string.Empty;
            public string ExerciseTitle { get; set; } = string.Empty;
            public string ReportDate { get; set; } = string.Empty;
            public List<string>? SelectedMetrics { get; set; }
        }

        public class ChildAIBulkReportResponse
        {
            public int ChildId { get; set; }
            public string ChildName { get; set; } = string.Empty;
            public bool IsPresent { get; set; } = true;
            public string Comment { get; set; } = string.Empty;
            public List<ChildReportMetricResponse> Metrics { get; set; } = new();
        }

        public class ChildReportMetricResponse
        {
            public string Label { get; set; } = string.Empty;
            public int Value { get; set; }
            public bool? IsIncluded { get; set; }
        }

        public class BulkReportSaveRequest
        {
            public int GroupId { get; set; }
            public string ExerciseKey { get; set; } = string.Empty;
            public string ExerciseTitle { get; set; } = string.Empty;
            public string ReportDate { get; set; } = string.Empty;
            public List<ChildReportDraftItem> Drafts { get; set; } = new();
        }

        public class ChildReportDraftItem
        {
            public int ChildId { get; set; }
            public bool IsPresent { get; set; }
            public string Comment { get; set; } = string.Empty;
            public List<ChildReportMetricResponse> Metrics { get; set; } = new();
        }

        private static string RepairTruncatedJson(string json)
        {
            json = json.Trim();
            if (string.IsNullOrWhiteSpace(json)) return json;

            if (json.EndsWith("}", StringComparison.Ordinal) || json.EndsWith("}", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var tempDoc = JsonDocument.Parse(json);
                    return json;
                }
                catch
                {
                    // Fallback to slicing if parse fails
                }
            }

            int lastClosedCurly = json.LastIndexOf('}');
            if (lastClosedCurly > 0)
            {
                string sliced = json.Substring(0, lastClosedCurly + 1);
                int openCurlies = 0;
                int openBrackets = 0;
                for (int i = 0; i < sliced.Length; i++)
                {
                    if (sliced[i] == '{') openCurlies++;
                    else if (sliced[i] == '}') openCurlies--;
                    else if (sliced[i] == '[') openBrackets++;
                    else if (sliced[i] == ']') openBrackets--;
                }

                StringBuilder sb = new StringBuilder(sliced);
                if (openBrackets > 0)
                {
                    sb.Append("\n  ]");
                }
                if (openCurlies > 0)
                {
                    sb.Append("\n}");
                }
                return sb.ToString();
            }

            return json;
        }

        private static void LogAiError(string endpoint, string message, string details)
        {
            try
            {
                string logLine = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Endpoint: {endpoint} | Message: {message} | Details: {details}{Environment.NewLine}";
                System.IO.File.AppendAllText("ai_error_log.txt", logLine);
            }
            catch
            {
                // ignore
            }
        }
    }
}
