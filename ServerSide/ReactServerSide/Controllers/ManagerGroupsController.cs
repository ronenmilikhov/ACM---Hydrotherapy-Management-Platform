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
    [Authorize(Roles = "Manager")]
    [Route("api/manager-groups")]
    [ApiController]
    public class ManagerGroupsController : ControllerBase
    {
        private const string AiEngineUnavailableMessage = "לא הצלחנו לקבל תשובה ממנוע ה-AI, תנסה שוב מאוחר יותר...";
        private const string AiStatisticsUnavailableMessage = "לא הצלחנו להפיק סטטיסטיקות באמצעות בינה מלאכותית כרגע. נסו שוב בעוד כמה דקות.";
        private readonly DBServices _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ManagerGroupsController(DBServices db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpGet("children")]
        public IActionResult GetChildrenForGroupManagement([FromQuery] bool includeInactive = false)
        {
            List<GroupChildOptionRecord> children = _db.GetChildrenForGroupManagement(includeInactive);
            return Ok(children);
        }

        [HttpGet("groups")]
        public IActionResult GetGroupsForManagement([FromQuery] bool includeInactive = false)
        {
            List<GroupSummaryRecord> groups = _db.GetGroupsForManagement(includeInactive);
            return Ok(groups);
        }

        [HttpGet("instructors")]
        public IActionResult GetInstructorsForManagement()
        {
            List<GroupInstructorOptionRecord> instructors = _db.GetInstructorsForGroupManagement();
            return Ok(instructors);
        }

        [HttpGet("center-settings")]
        public IActionResult GetCenterSettings()
        {
            ManagerCenterSettingsRecord settings = _db.GetManagerCenterSettings();
            return Ok(settings);
        }

        [HttpPut("center-settings")]
        public IActionResult UpdateCenterSettings([FromBody] UpdateCenterSettingsRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.CenterName))
            {
                return BadRequest(new { message = "CenterName is required." });
            }

            if (string.IsNullOrWhiteSpace(request.CenterAddress))
            {
                return BadRequest(new { message = "CenterAddress is required." });
            }

            ManagerCenterSettingsRecord updated = _db.PutManagerCenterSettings(
                request.CenterName,
                request.CenterAddress,
                request.SendAutoReports,
                request.ReceiveAlerts,
                request.ShowKidsAdvanced);

            return Ok(updated);
        }

        [HttpGet("system-reports/overview")]
        public IActionResult GetSystemReportsOverview([FromQuery] string? fromDate = null, [FromQuery] string? toDate = null)
        {
            if (!TryResolveDateRange(fromDate, toDate, out DateTime startDate, out DateTime endDate, out string errorMessage))
            {
                return BadRequest(new { message = errorMessage });
            }

            ManagerSystemReportsOverviewRecord overview = _db.GetManagerSystemReportsOverview(startDate, endDate);
            return Ok(overview);
        }

        [HttpGet("system-reports/group-stats")]
        public async Task<IActionResult> GetGroupStatisticsAi([FromQuery] string? fromDate = null, [FromQuery] string? toDate = null)
        {
            if (!TryResolveDateRange(fromDate, toDate, out DateTime startDate, out DateTime endDate, out string errorMessage))
            {
                return BadRequest(new { message = errorMessage });
            }

            try
            {
                var stats = _db.GetGroupAiStatistics(startDate, endDate);

                if (stats.Count == 0)
                {
                    return Ok(new { Groups = stats, AiSummary = string.Empty });
                }
                
                // Only iterate over the three configured OpenAI keys for this endpoint.
                string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return StatusCode(503, new { message = AiStatisticsUnavailableMessage });
                }

                string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
                string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();
                string lastFailure = string.Empty;

                StringBuilder groupsData = new StringBuilder();
                foreach (var s in stats)
                {
                    if (s.MedianMetrics == null || s.MedianMetrics.Count == 0)
                    {
                        s.AiNotes = "אין עדיין מספיק מדדים לקבוצה זו בטווח התאריכים שנבחר.";
                        continue;
                    }

                    groupsData.AppendLine("- שם קבוצה: " + s.GroupName);
                    groupsData.AppendLine("מדדים (חציון):");
                    foreach (var m in s.MedianMetrics)
                    {
                        groupsData.AppendLine("  * " + m.Key + ": " + m.Value);
                    }
                }

                if (groupsData.Length == 0)
                {
                    return Ok(new { Groups = stats, AiSummary = "אין עדיין נתוני מדדים של אף קבוצה בטווח התאריכים הנוכחי. יש להוסיף דוחות כדי לראות תובנות חכמות." });
                }

                string statsSystemPrompt = (_configuration["OpenAI:GroupStatsSystemPrompt"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(statsSystemPrompt))
                {
                    statsSystemPrompt = "החזר JSON בלבד במבנה: {\"generalSummary\": \"...\", \"groupNotes\": { \"שם קבוצה 1\": \"...\", \"שם קבוצה 2\": \"...\" }}. נתח את חציון המדדים לכל קבוצה. עבור כל קבוצה ב-groupNotes, תן פסקת 'הצלחות ונתונים לשיפור' ממוקדת. ה-generalSummary לסיכום כולל. השתמש בעברית. חובה להחזיר JSON טהור ללא עטיפות קוד כגון ```json או הערות נוספות.";
                }

                string statsUserPromptTemplate = (_configuration["OpenAI:GroupStatsUserPromptTemplate"] ?? string.Empty).Trim();
                string statsUserPrompt;
                if (!string.IsNullOrWhiteSpace(statsUserPromptTemplate))
                {
                    statsUserPrompt = statsUserPromptTemplate.Replace("{groupsData}", groupsData.ToString());
                }
                else
                {
                    statsUserPrompt = "הנה הנתונים: \n" + groupsData.ToString();
                }

                HttpClient client = _httpClientFactory.CreateClient();
                try
                {
                    string requestJson = JsonSerializer.Serialize(new
                    {
                        model = model,
                        temperature = 0.5,
                        messages = new object[]
                        {
                            new { role = "system", content = statsSystemPrompt },
                            new { role = "user", content = statsUserPrompt }
                        }
                    });

                    using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    using HttpResponseMessage response = await client.SendAsync(requestMessage);
                    string responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        string shortBody = responseJson.Length > 200 ? responseJson[..200] : responseJson;
                        lastFailure = $"model {model}: API returned status {(int)response.StatusCode}: {shortBody}";
                    }
                    else
                    {
                        string content = ExtractMessageContentFromOpenAiResponse(responseJson);

                        if (!TryParseAiGroupStatisticsResponse(content, out string generalSummary, out Dictionary<string, string> groupNotesByName))
                        {
                            lastFailure = $"model {model}: AI response could not be parsed into the required JSON structure.";
                        }
                        else
                        {
                            ApplyAiGroupNotes(stats, groupNotesByName);
                            ApplyFallbackGroupNotes(stats);

                            string resolvedGeneralSummary = string.IsNullOrWhiteSpace(generalSummary)
                                ? BuildFallbackGeneralSummary(stats)
                                : generalSummary;

                            return Ok(new { Groups = stats, AiSummary = resolvedGeneralSummary });
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastFailure = $"model {model}: Request failed while calling AI provider: {ex.Message}";
                }

                Console.WriteLine($"[AI GroupStats] API failed: {lastFailure}");
                return StatusCode(503, new { message = AiStatisticsUnavailableMessage + " פירוט: " + lastFailure });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI GroupStats] Exception in GetGroupStatisticsAi: {ex}");
                return StatusCode(500, new { message = "לא ניתן לטעון סטטיסטיקות כרגע." });
            }
        }

        [HttpPost("system-reports/generate")]
        public IActionResult GenerateSystemReport([FromBody] GenerateSystemReportRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.ReportType))
            {
                return BadRequest(new { message = "ReportType is required." });
            }

            if (!TryResolveDateRange(request.FromDate, request.ToDate, out DateTime startDate, out DateTime endDate, out string errorMessage))
            {
                return BadRequest(new { message = errorMessage });
            }

            try
            {
                ManagerGeneratedSystemReportRecord report = _db.GenerateManagerSystemReport(request.ReportType, startDate, endDate);
                return Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("attendance-report")]
        public IActionResult GetAttendanceReport(
            [FromQuery] string? fromDate = null,
            [FromQuery] string? toDate = null,
            [FromQuery] string? status = null,
            [FromQuery] string? search = null,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? sortDirection = "desc")
        {
            if (!TryResolveDateRange(fromDate, toDate, out DateTime startDate, out DateTime endDate, out string errorMessage))
            {
                return BadRequest(new { message = errorMessage });
            }

            List<ManagerAttendanceReportRowRecord> rows = _db.GetManagerAttendanceReportRows(startDate, endDate);

            string normalizedStatus = (status ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedStatus) && !string.Equals(normalizedStatus, "all", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows
                    .Where((row) =>
                        string.Equals(row.AttendanceCategory, normalizedStatus, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(row.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            string normalizedSearch = (search ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                rows = rows
                    .Where((row) =>
                        row.ChildFullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        || row.GroupName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        || row.InstructorFullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        || row.Status.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        || row.AttendanceCategory.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        || row.MeetingDate.ToString("yyyy-MM-dd").Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            bool isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
            string normalizedSortBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant();

            rows = normalizedSortBy switch
            {
                "status" => isDescending
                    ? rows.OrderByDescending((row) => row.AttendanceCategory).ThenByDescending((row) => row.MeetingDate).ThenByDescending((row) => row.StartTime).ToList()
                    : rows.OrderBy((row) => row.AttendanceCategory).ThenBy((row) => row.MeetingDate).ThenBy((row) => row.StartTime).ToList(),

                "participant" => isDescending
                    ? rows.OrderByDescending((row) => string.IsNullOrWhiteSpace(row.ChildFullName) ? row.GroupName : row.ChildFullName).ThenByDescending((row) => row.MeetingDate).ThenByDescending((row) => row.StartTime).ToList()
                    : rows.OrderBy((row) => string.IsNullOrWhiteSpace(row.ChildFullName) ? row.GroupName : row.ChildFullName).ThenBy((row) => row.MeetingDate).ThenBy((row) => row.StartTime).ToList(),

                "instructor" => isDescending
                    ? rows.OrderByDescending((row) => row.InstructorFullName).ThenByDescending((row) => row.MeetingDate).ThenByDescending((row) => row.StartTime).ToList()
                    : rows.OrderBy((row) => row.InstructorFullName).ThenBy((row) => row.MeetingDate).ThenBy((row) => row.StartTime).ToList(),

                _ => isDescending
                    ? rows.OrderByDescending((row) => row.MeetingDate).ThenByDescending((row) => row.StartTime).ThenByDescending((row) => row.SessionId).ToList()
                    : rows.OrderBy((row) => row.MeetingDate).ThenBy((row) => row.StartTime).ThenBy((row) => row.SessionId).ToList()
            };

            int presentCount = rows.Count((row) => row.AttendanceCategory == "Present");
            int absentCount = rows.Count((row) => row.AttendanceCategory == "Absent");
            int lateCount = rows.Count((row) => row.AttendanceCategory == "Late");
            int scheduledCount = rows.Count((row) => row.AttendanceCategory == "Scheduled");
            int cancelledCount = rows.Count((row) => row.AttendanceCategory == "Cancelled");

            return Ok(new
            {
                fromDate = startDate.ToString("yyyy-MM-dd"),
                toDate = endDate.ToString("yyyy-MM-dd"),
                totalRows = rows.Count,
                summary = new
                {
                    presentCount,
                    absentCount,
                    lateCount,
                    scheduledCount,
                    cancelledCount
                },
                rows = rows.Select((row) => new
                {
                    sessionId = row.SessionId,
                    lessonType = row.LessonType,
                    meetingDate = row.MeetingDate.ToString("yyyy-MM-dd"),
                    startTime = row.StartTime.ToString(@"hh\:mm"),
                    endTime = row.EndTime.ToString(@"hh\:mm"),
                    status = row.Status,
                    attendanceCategory = row.AttendanceCategory,
                    attendanceLabel = GetAttendanceLabel(row.AttendanceCategory),
                    childId = row.ChildId,
                    childFullName = row.ChildFullName,
                    instructorId = row.InstructorId,
                    instructorFullName = row.InstructorFullName,
                    groupId = row.GroupId,
                    groupName = row.GroupName,
                    participantName = string.IsNullOrWhiteSpace(row.ChildFullName) ? row.GroupName : row.ChildFullName
                }).ToList()
            });
        }

        [HttpGet("attendance-groups-summary")]
        public IActionResult GetAttendanceGroupsSummary([FromQuery] bool includeInactive = true)
        {
            try
            {
                List<GroupSummaryRecord> groups = _db.GetGroupsForManagement(includeInactive);
                List<GroupChildOptionRecord> allChildren = _db.GetChildrenForGroupManagement(includeInactive);

                List<GroupChildOptionRecord> childrenWithGroups = allChildren
                    .Where((child) => child.ActiveGroupId.HasValue && child.ActiveGroupId.Value > 0)
                    .ToList();

                List<int> childIds = childrenWithGroups
                    .Select((child) => child.ChildId)
                    .Where((childId) => childId > 0)
                    .Distinct()
                    .ToList();

                List<ChildReportRecord> reports = _db.GetChildReportsForChildren(childIds);

                Dictionary<int, List<GroupChildOptionRecord>> childrenByGroupId = childrenWithGroups
                    .GroupBy((child) => child.ActiveGroupId!.Value)
                    .ToDictionary(
                        (bucket) => bucket.Key,
                        (bucket) => bucket
                            .OrderBy((child) => child.FullName)
                            .ThenBy((child) => child.ChildId)
                            .ToList());

                Dictionary<int, List<ChildReportRecord>> reportsByChildId = reports
                    .GroupBy((report) => report.ChildId)
                    .ToDictionary((bucket) => bucket.Key, (bucket) => bucket.ToList());

                List<object> groupSummaries = new List<object>();

                foreach (GroupSummaryRecord group in groups.OrderBy((item) => item.Name).ThenBy((item) => item.GroupId))
                {
                    List<GroupChildOptionRecord> groupChildren = childrenByGroupId.TryGetValue(group.GroupId, out List<GroupChildOptionRecord>? mappedChildren)
                        ? mappedChildren
                        : new List<GroupChildOptionRecord>();

                    List<object> childSummaries = new List<object>();
                    int groupAttendedCount = 0;
                    int groupReportsCount = 0;

                    foreach (GroupChildOptionRecord child in groupChildren)
                    {
                        List<ChildReportRecord> childReports = reportsByChildId.TryGetValue(child.ChildId, out List<ChildReportRecord>? mappedReports)
                            ? mappedReports
                            : new List<ChildReportRecord>();

                        List<ChildReportRecord> groupReports = childReports
                            .Where((report) => report.GroupId.HasValue && report.GroupId.Value == group.GroupId)
                            .ToList();

                        int totalCount = groupReports.Count;
                        int attendedCount = 0;

                        foreach (ChildReportRecord report in groupReports)
                        {
                            if (ResolveReportPresenceFromNotes(report.Notes))
                            {
                                attendedCount++;
                            }
                        }

                        int missedCount = Math.Max(totalCount - attendedCount, 0);
                        int? attendancePercent = totalCount > 0
                            ? (int?)Math.Round((attendedCount * 100.0) / totalCount, MidpointRounding.AwayFromZero)
                            : null;

                        groupAttendedCount += attendedCount;
                        groupReportsCount += totalCount;

                        childSummaries.Add(new
                        {
                            childId = child.ChildId,
                            childFullName = child.FullName,
                            parentFullName = child.ParentFullName,
                            parentEmail = child.ParentEmail,
                            attendedCount,
                            totalCount,
                            missedCount,
                            attendancePercent
                        });
                    }

                    int? groupAverageAttendancePercent = groupReportsCount > 0
                        ? (int?)Math.Round((groupAttendedCount * 100.0) / groupReportsCount, MidpointRounding.AwayFromZero)
                        : null;

                    groupSummaries.Add(new
                    {
                        groupId = group.GroupId,
                        groupName = group.Name,
                        groupDescription = group.Description,
                        isActive = group.IsActive,
                        instructorFullName = group.InstructorFullName,
                        activeChildrenCount = group.ActiveChildrenCount,
                        totalAttendedCount = groupAttendedCount,
                        totalMeetingsCount = groupReportsCount,
                        averageAttendancePercent = groupAverageAttendancePercent,
                        children = childSummaries
                    });
                }

                return Ok(new
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    groups = groupSummaries
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching group attendance summary: " + ex.Message });
            }
        }

        [HttpPost("groups")]
        public IActionResult CreateGroup([FromBody] CreateGroupRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Group name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "Group description is required." });
            }

            if (request.InstructorId <= 0)
            {
                return BadRequest(new { message = "InstructorId is required." });
            }

            try
            {
                GroupSummaryRecord created = _db.PostCreateGroupWithInstructor(
                    request.Name.Trim(),
                    request.Description,
                    request.InstructorId);

                return Created(string.Empty, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("groups/{groupId:int}")]
        public IActionResult UpdateGroup(int groupId, [FromBody] UpdateGroupRequest request)
        {
            if (groupId <= 0)
            {
                return BadRequest(new { message = "GroupId is required." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Group name is required." });
            }

            if (request.InstructorId <= 0)
            {
                return BadRequest(new { message = "InstructorId is required." });
            }

            try
            {
                GroupSummaryRecord updated = _db.PutUpdateGroupForManagement(
                    groupId,
                    request.Name.Trim(),
                    request.Description,
                    request.InstructorId);

                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("groups/{groupId:int}")]
        public IActionResult DeleteGroup(int groupId)
        {
            if (groupId <= 0)
            {
                return BadRequest(new { message = "GroupId is required." });
            }

            try
            {
                bool deleted = _db.DeleteDeactivateGroupForManagement(groupId);
                if (!deleted)
                {
                    return NotFound(new { message = "Group was not found or already inactive." });
                }

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("groups/set-instructor")]
        public IActionResult SetGroupInstructor([FromBody] SetGroupInstructorRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.GroupId <= 0 || request.InstructorId <= 0)
            {
                return BadRequest(new { message = "GroupId and InstructorId are required." });
            }

            try
            {
                GroupSummaryRecord updated = _db.PutSetGroupInstructor(request.GroupId, request.InstructorId);
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("assign-child")]
        public IActionResult AssignChildToGroup([FromBody] AssignChildToGroupRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ChildId <= 0)
            {
                return BadRequest(new { message = "ChildId is required." });
            }

            try
            {
                GroupChildOptionRecord assigned = request.GroupId.HasValue && request.GroupId.Value > 0
                    ? _db.PutAssignChildToGroup(request.ChildId, request.GroupId.Value)
                    : _db.PutUnassignChildFromGroup(request.ChildId);

                return Ok(assigned);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("recommend-group")]
        public async Task<IActionResult> RecommendGroupForChild([FromBody] RecommendGroupRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ChildId <= 0)
            {
                return BadRequest(new { message = "ChildId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.ChildDescription))
            {
                return BadRequest(new { message = "ChildDescription is required." });
            }

            try
            {
                GroupRecommendationResultRecord recommendation = await RecommendGroupUsingAiAsync(
                    request.ChildId,
                    request.ChildDescription);

                return Ok(recommendation);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == AiEngineUnavailableMessage)
                {
                    return Ok(new
                    {
                        aiUnavailable = true,
                        message = AiEngineUnavailableMessage
                    });
                }

                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<GroupRecommendationResultRecord> RecommendGroupUsingAiAsync(int childId, string childDescription)
        {
            GroupChildOptionRecord? child = _db.GetGroupChildOptionByChildId(childId);
            if (child == null)
            {
                throw new InvalidOperationException("Child does not exist.");
            }

            List<GroupSummaryRecord> candidateGroups = _db.GetGroupsForManagement(false)
                .Where((group) => group.IsActive && group.InstructorId.HasValue)
                .ToList();

            if (candidateGroups.Count == 0)
            {
                throw new InvalidOperationException("לא נמצאו קבוצות פעילות עם מדריך משויך.");
            }

            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("[AI RecommendGroup] No API keys configured.");
                throw new InvalidOperationException(AiEngineUnavailableMessage);
            }

            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();
            Console.WriteLine($"[AI RecommendGroup] Configured model from settings: {model}");

            // Build the simple prompt: send all group descriptions + child description
            StringBuilder groupsList = new StringBuilder();
            foreach (GroupSummaryRecord group in candidateGroups)
            {
                string desc = string.IsNullOrWhiteSpace(group.Description) ? "ללא תיאור" : group.Description.Trim();
                groupsList.AppendLine($"- קבוצה מספר {group.GroupId}, שם: \"{group.Name}\", תיאור: \"{desc}\"");
            }

            string systemPrompt = (_configuration["OpenAI:GroupMatchSystemPrompt"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                systemPrompt = "אתה עוזר שמתאים ילדים לקבוצות לימוד. החזר JSON בלבד, ללא markdown, ללא קוד, ללא הסברים נוספים. הערך reasoning חייב להיות משפט אחד קצר בעברית.";
            }

            string userPromptTemplate = (_configuration["OpenAI:GroupMatchUserPromptTemplate"] ?? string.Empty).Trim();
            string userPrompt;
            if (!string.IsNullOrWhiteSpace(userPromptTemplate))
            {
                userPrompt = userPromptTemplate
                    .Replace("{childDescription}", childDescription.Trim())
                    .Replace("{groupsList}", groupsList.ToString());
            }
            else
            {
                userPrompt = $@"להלן תיאור של ילד:
""{childDescription.Trim()}""

להלן רשימת הקבוצות הקיימות:
{groupsList}
איזו קבוצה הכי מתאימה לילד הזה?
ענה בפורמט JSON בלבד, ללא שום טקסט נוסף, בדיוק כך:
{{""recommendedGroupId"": <מספר הקבוצה>, ""reasoning"": ""<משפט אחד קצר בעברית שמסביר למה הקבוצה הזו הכי מתאימה לילד>""}}
כלל מחייב: reasoning חייב להיות משפט אחד בלבד (בלי רשימות ובלי ירידות שורה).";
            }

            HashSet<int> validGroupIds = candidateGroups.Select((g) => g.GroupId).ToHashSet();
            HttpClient client = _httpClientFactory.CreateClient();
            string lastFailure = string.Empty;

            try
            {
                string requestJson = JsonSerializer.Serialize(new
                {
                    model = model,
                    temperature = 0.3,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    }
                });

                using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                requestMessage.Headers.TryAddWithoutValidation("HTTP-Referer", "https://localhost");
                requestMessage.Headers.TryAddWithoutValidation("X-Title", "ACM Manager Groups");

                using HttpResponseMessage response = await client.SendAsync(requestMessage);
                string responseJson = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[AI RecommendGroup] Model: {model}, Status: {(int)response.StatusCode}");
                Console.WriteLine($"[AI RecommendGroup] Response: {responseJson}");

                if (!response.IsSuccessStatusCode)
                {
                    string shortBody = responseJson.Length > 200 ? responseJson[..200] : responseJson;
                    lastFailure = $"model {model}: API returned status {(int)response.StatusCode}: {shortBody}";
                    Console.WriteLine($"[AI RecommendGroup] FAILED: {lastFailure}");
                }
                else
                {
                    // Extract the AI message content
                    string aiContent = ExtractMessageContentFromOpenAiResponse(responseJson);
                    Console.WriteLine($"[AI RecommendGroup] AI content: {aiContent}");

                    if (string.IsNullOrWhiteSpace(aiContent))
                    {
                        lastFailure = $"model {model}: AI returned empty content";
                    }
                    else
                    {
                        // Clean up: strip markdown code fences if present
                        string cleaned = aiContent.Trim();
                        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                            cleaned = cleaned.Substring(7).Trim();
                        else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                            cleaned = cleaned.Substring(3).Trim();
                        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
                            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();

                        // Find JSON object boundaries
                        int firstBrace = cleaned.IndexOf('{');
                        int lastBrace = cleaned.LastIndexOf('}');
                        if (firstBrace >= 0 && lastBrace > firstBrace)
                        {
                            cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
                        }

                        Console.WriteLine($"[AI RecommendGroup] Cleaned JSON: {cleaned}");

                        // Parse the JSON
                        int recommendedGroupId = 0;
                        string reasoning = string.Empty;

                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(cleaned);
                            JsonElement root = doc.RootElement;

                            // Try to get recommendedGroupId
                            if (TryGetJsonInt(root, out int parsedId, "recommendedGroupId", "groupId", "recommended_group_id", "group_id", "id"))
                            {
                                recommendedGroupId = parsedId;
                            }

                            // Try to get reasoning
                            if (TryGetJsonString(root, out string parsedReasoning, "reasoning", "reason", "explanation", "why", "description"))
                            {
                                reasoning = NormalizeSingleSentenceReasoning(parsedReasoning);
                            }
                        }
                        catch (Exception parseEx)
                        {
                            Console.WriteLine($"[AI RecommendGroup] JSON parse error: {parseEx.Message}");

                            // Regex fallback for group ID
                            Match idMatch = Regex.Match(cleaned, @"""?(?:recommendedGroupId|groupId|group_id|id)""?\s*[:=]\s*""?(\d+)""?", RegexOptions.IgnoreCase);
                            if (idMatch.Success) int.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out recommendedGroupId);

                            // Regex fallback for reasoning
                            Match reasonMatch = Regex.Match(cleaned, @"""?(?:reasoning|reason|explanation)""?\s*[:=]\s*""([^""]+)""", RegexOptions.IgnoreCase);
                            if (reasonMatch.Success) reasoning = NormalizeSingleSentenceReasoning(reasonMatch.Groups[1].Value);
                        }

                        Console.WriteLine($"[AI RecommendGroup] Parsed groupId={recommendedGroupId}, reasoning={reasoning}");

                        // Validate group ID
                        if (!validGroupIds.Contains(recommendedGroupId))
                        {
                            // Try to match by name from the reasoning text
                            foreach (GroupSummaryRecord g in candidateGroups)
                            {
                                if (!string.IsNullOrWhiteSpace(g.Name) && cleaned.Contains(g.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    recommendedGroupId = g.GroupId;
                                    Console.WriteLine($"[AI RecommendGroup] Resolved group by name match: {g.Name} -> {g.GroupId}");
                                    break;
                                }
                            }
                        }

                        if (!validGroupIds.Contains(recommendedGroupId))
                        {
                            lastFailure = $"model {model}: AI returned invalid group id: {recommendedGroupId}";
                            Console.WriteLine($"[AI RecommendGroup] FAILED: {lastFailure}");
                        }
                        else
                        {
                            GroupSummaryRecord recommendedGroup = candidateGroups.First((g) => g.GroupId == recommendedGroupId);

                            if (string.IsNullOrWhiteSpace(reasoning))
                            {
                                reasoning = $"הקבוצה \"{recommendedGroup.Name}\" מתאימה ביותר לילד לפי תיאור הצרכים והמאפיינים שהוזנו.";
                            }

                            return new GroupRecommendationResultRecord
                            {
                                ChildId = child.ChildId,
                                ChildFullName = child.FullName,
                                RecommendedGroupId = recommendedGroup.GroupId,
                                RecommendedGroupName = recommendedGroup.Name,
                                RecommendedInstructorFullName = recommendedGroup.InstructorFullName,
                                Score = 0,
                                UsedFallback = false,
                                Reasoning = reasoning,
                                SharedTerms = new List<string>(),
                                SharedConcepts = new List<string>()
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastFailure = $"model {model}: Request error: {ex.Message}";
                Console.WriteLine($"[AI RecommendGroup] Exception: {ex}");
            }

            Console.WriteLine($"[AI RecommendGroup] API failed. Last failure: {lastFailure}");
            throw new InvalidOperationException(AiEngineUnavailableMessage + " פירוט: " + lastFailure);
        }

        private static string NormalizeSingleSentenceReasoning(string rawReasoning)
        {
            string normalized = Regex.Replace(rawReasoning ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            int sentenceBreakIndex = normalized.IndexOfAny(new[] { '.', '!', '?', ';', '\n', '\r' });
            if (sentenceBreakIndex > 0)
            {
                normalized = normalized.Substring(0, sentenceBreakIndex).Trim();
            }

            normalized = normalized.Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return normalized.EndsWith('.') ? normalized : normalized + ".";
        }

        private static string ExtractMessageContentFromOpenAiResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseJson);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("choices", out JsonElement choices) ||
                    choices.ValueKind != JsonValueKind.Array ||
                    choices.GetArrayLength() == 0)
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

                string rawText = string.Empty;
                if (contentElement.ValueKind == JsonValueKind.String)
                {
                    rawText = (contentElement.GetString() ?? string.Empty).Trim();
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

                    rawText = contentBuilder.ToString().Trim();
                }

                return rawText
                    .Replace("הבוגרים יום", "הבתולת ים")
                    .Replace("בוגרים יום", "בתולת ים")
                    .Replace("הבוגריםיום", "הבתולת ים")
                    .Replace("בוגריםיום", "בתולת ים");
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static void ApplyFallbackGroupNotes(List<GroupAiStatisticRecord> stats)
        {
            foreach (GroupAiStatisticRecord stat in stats)
            {
                if (!string.IsNullOrWhiteSpace(stat.AiNotes))
                {
                    continue;
                }

                stat.AiNotes = BuildFallbackGroupNote(stat);
            }
        }

        private static void ApplyAiGroupNotes(List<GroupAiStatisticRecord> stats, IReadOnlyDictionary<string, string> notesByNormalizedGroupName)
        {
            foreach (GroupAiStatisticRecord stat in stats)
            {
                string normalizedGroupName = NormalizeGroupNameKey(stat.GroupName);
                if (string.IsNullOrWhiteSpace(normalizedGroupName))
                {
                    continue;
                }

                if (notesByNormalizedGroupName.TryGetValue(normalizedGroupName, out string? matchedNote)
                    && !string.IsNullOrWhiteSpace(matchedNote))
                {
                    stat.AiNotes = matchedNote;
                }
            }
        }

        private static bool TryParseAiGroupStatisticsResponse(
            string rawContent,
            out string generalSummary,
            out Dictionary<string, string> groupNotesByNormalizedName)
        {
            generalSummary = string.Empty;
            groupNotesByNormalizedName = new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                return false;
            }

            string content = rawContent.Trim();

            if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Substring(7).Trim();
            }
            else if (content.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Substring(3).Trim();
            }

            if (content.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Substring(0, content.Length - 3).Trim();
            }

            int firstBraceIndex = content.IndexOf('{');
            int lastBraceIndex = content.LastIndexOf('}');
            if (firstBraceIndex >= 0 && lastBraceIndex > firstBraceIndex)
            {
                content = content.Substring(firstBraceIndex, lastBraceIndex - firstBraceIndex + 1);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(content);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (TryGetJsonString(root, out string parsedGeneralSummary, "generalSummary", "summary", "aiSummary"))
                {
                    generalSummary = parsedGeneralSummary;
                }

                if (TryGetJsonProperty(root, out JsonElement groupNotesElement, "groupNotes", "groupsNotes", "group_notes")
                    && groupNotesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty noteProperty in groupNotesElement.EnumerateObject())
                    {
                        if (noteProperty.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string note = (noteProperty.Value.GetString() ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(note))
                        {
                            continue;
                        }

                        string normalizedKey = NormalizeGroupNameKey(noteProperty.Name);
                        if (string.IsNullOrWhiteSpace(normalizedKey))
                        {
                            continue;
                        }

                        groupNotesByNormalizedName[normalizedKey] = note;
                    }
                }

                return !string.IsNullOrWhiteSpace(generalSummary)
                    || groupNotesByNormalizedName.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyAiUnavailableGroupNotes(List<GroupAiStatisticRecord> stats, string unavailableMessage)
        {
            foreach (GroupAiStatisticRecord stat in stats)
            {
                if (string.IsNullOrWhiteSpace(stat.AiNotes))
                {
                    stat.AiNotes = unavailableMessage;
                }
            }
        }

        private static string NormalizeGroupNameKey(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            StringBuilder normalized = new StringBuilder(rawName.Length);
            foreach (char ch in rawName.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    normalized.Append(char.ToLowerInvariant(ch));
                }
            }

            return normalized.ToString();
        }

        private static string BuildFallbackGroupNote(GroupAiStatisticRecord stat)
        {
            if (stat.MedianMetrics == null || stat.MedianMetrics.Count == 0)
            {
                return "אין עדיין מספיק מדדים לקבוצה זו בטווח התאריכים שנבחר.";
            }

            string strongestMetric = string.Empty;
            double strongestValue = double.MinValue;
            string weakestMetric = string.Empty;
            double weakestValue = double.MaxValue;

            foreach (var metric in stat.MedianMetrics)
            {
                if (metric.Value > strongestValue)
                {
                    strongestValue = metric.Value;
                    strongestMetric = metric.Key;
                }

                if (metric.Value < weakestValue)
                {
                    weakestValue = metric.Value;
                    weakestMetric = metric.Key;
                }
            }

            if (string.IsNullOrWhiteSpace(strongestMetric) || string.IsNullOrWhiteSpace(weakestMetric))
            {
                return "נאספו מדדים חלקיים בלבד, מומלץ לעדכן עוד דיווחים לקבלת תובנות מדויקות.";
            }

            if (string.Equals(strongestMetric, weakestMetric, StringComparison.Ordinal))
            {
                return $"המדדים בקבוצה יציבים סביב {Math.Round(strongestValue, 2):0.##}/5. מומלץ לשמר את רמת הביצוע ולהמשיך בתרגול עקבי.";
            }

            return $"חוזקה מרכזית: {strongestMetric} ({Math.Round(strongestValue, 2):0.##}/5). נתון לשיפור: {weakestMetric} ({Math.Round(weakestValue, 2):0.##}/5). מומלץ לשמר את החוזקה ולבנות תרגול ממוקד לשיפור המדד החלש.";
        }

        private static string BuildFallbackGeneralSummary(List<GroupAiStatisticRecord> stats)
        {
            if (stats == null || stats.Count == 0)
            {
                return "לא נמצאו קבוצות פעילות להצגת סטטיסטיקות בטווח התאריכים שנבחר.";
            }

            List<GroupAiStatisticRecord> groupsWithMetrics = new List<GroupAiStatisticRecord>();
            foreach (GroupAiStatisticRecord stat in stats)
            {
                if (stat.MedianMetrics != null && stat.MedianMetrics.Count > 0)
                {
                    groupsWithMetrics.Add(stat);
                }
            }

            if (groupsWithMetrics.Count == 0)
            {
                return "לא נמצאו מספיק נתוני מדדים לחישוב סיכום חכם בטווח התאריכים שנבחר.";
            }

            GroupAiStatisticRecord? bestGroup = null;
            GroupAiStatisticRecord? weakestGroup = null;
            double bestAverage = double.MinValue;
            double weakestAverage = double.MaxValue;

            Dictionary<string, List<double>> metricBuckets = new Dictionary<string, List<double>>();

            foreach (GroupAiStatisticRecord stat in groupsWithMetrics)
            {
                double total = 0;
                int count = 0;

                foreach (var metric in stat.MedianMetrics)
                {
                    total += metric.Value;
                    count++;

                    if (!metricBuckets.TryGetValue(metric.Key, out List<double>? values))
                    {
                        values = new List<double>();
                        metricBuckets[metric.Key] = values;
                    }

                    values.Add(metric.Value);
                }

                if (count == 0)
                {
                    continue;
                }

                double average = total / count;
                if (average > bestAverage)
                {
                    bestAverage = average;
                    bestGroup = stat;
                }

                if (average < weakestAverage)
                {
                    weakestAverage = average;
                    weakestGroup = stat;
                }
            }

            string topMetric = string.Empty;
            double topMetricAverage = double.MinValue;
            string weakestMetric = string.Empty;
            double weakestMetricAverage = double.MaxValue;

            foreach (var bucket in metricBuckets)
            {
                if (bucket.Value.Count == 0)
                {
                    continue;
                }

                double sum = 0;
                foreach (double value in bucket.Value)
                {
                    sum += value;
                }

                double average = sum / bucket.Value.Count;
                if (average > topMetricAverage)
                {
                    topMetricAverage = average;
                    topMetric = bucket.Key;
                }

                if (average < weakestMetricAverage)
                {
                    weakestMetricAverage = average;
                    weakestMetric = bucket.Key;
                }
            }

            if (bestGroup == null || weakestGroup == null)
            {
                return "התקבלו נתוני מדדים חלקיים בלבד, מומלץ להזין עוד דוחות כדי לקבל תמונת מצב מערכתית מדויקת.";
            }

            if (groupsWithMetrics.Count == 1)
            {
                return $"קיימת כרגע קבוצה אחת עם נתונים ({bestGroup.GroupName}). ממוצע המדדים שלה הוא {Math.Round(bestAverage, 2):0.##}/5. מומלץ להמשיך להזין דוחות כדי לאפשר השוואה בין קבוצות.";
            }

            return $"בהשוואת חציון המדדים בין הקבוצות, הקבוצה המובילה היא {bestGroup.GroupName} עם ממוצע {Math.Round(bestAverage, 2):0.##}/5, בעוד {weakestGroup.GroupName} דורשת חיזוק עם ממוצע {Math.Round(weakestAverage, 2):0.##}/5. בכלל המתחם, המדד החזק הוא {topMetric} ({Math.Round(topMetricAverage, 2):0.##}/5) והמדד שדורש את מירב תשומת הלב הוא {weakestMetric} ({Math.Round(weakestMetricAverage, 2):0.##}/5).";
        }

        private static bool TryParseAiGroupRecommendation(string content, out AiGroupRecommendationPayload recommendation)
        {
            recommendation = new AiGroupRecommendationPayload();

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            string trimmed = content.Trim();
            int firstBraceIndex = trimmed.IndexOf('{');
            int lastBraceIndex = trimmed.LastIndexOf('}');

            if (firstBraceIndex >= 0 && lastBraceIndex > firstBraceIndex)
            {
                trimmed = trimmed.Substring(firstBraceIndex, lastBraceIndex - firstBraceIndex + 1);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(trimmed);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (TryGetJsonInt(root, out int recommendedGroupId, "recommendedGroupId", "groupId", "recommended_group_id"))
                {
                    recommendation.RecommendedGroupId = recommendedGroupId;
                }

                if (TryGetJsonString(root, out string recommendedGroupName, "recommendedGroupName", "groupName", "recommended_group_name"))
                {
                    recommendation.RecommendedGroupName = recommendedGroupName;
                }

                if (recommendation.RecommendedGroupId <= 0 && string.IsNullOrWhiteSpace(recommendation.RecommendedGroupName))
                {
                    return false;
                }

                if (TryGetJsonString(root, out string reasoning, "reasoning", "reason", "explanation", "why"))
                {
                    recommendation.Reasoning = reasoning;
                }

                if (TryGetJsonDouble(root, out double score, "score", "confidence", "matchScore"))
                {
                    recommendation.Score = score;
                }

                recommendation.SharedTerms = TryGetJsonStringList(root, "sharedTerms", "shared_terms", "keywords");
                recommendation.SharedConcepts = TryGetJsonStringList(root, "sharedConcepts", "shared_concepts", "concepts");

                return true;
            }
            catch
            {
                int recommendedGroupId = 0;
                string recommendedGroupName = string.Empty;
                string reasoning = string.Empty;
                double score = 0;

                Match idMatch = Regex.Match(
                    trimmed,
                    "(?:recommendedGroupId|groupId|recommended_group_id)\\s*[:=]\\s*\"?(\\d+)\"?",
                    RegexOptions.IgnoreCase);
                if (idMatch.Success)
                {
                    int.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out recommendedGroupId);
                }

                Match nameMatch = Regex.Match(
                    trimmed,
                    "(?:recommendedGroupName|groupName|recommended_group_name)\\s*[:=]\\s*\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    recommendedGroupName = nameMatch.Groups[1].Value.Trim();
                }

                Match reasoningMatch = Regex.Match(
                    trimmed,
                    "(?:reasoning|reason|explanation|why)\\s*[:=]\\s*\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (reasoningMatch.Success)
                {
                    reasoning = reasoningMatch.Groups[1].Value.Trim();
                }

                Match scoreMatch = Regex.Match(
                    trimmed,
                    "(?:score|confidence|matchScore)\\s*[:=]\\s*\"?(-?\\d+(?:\\.\\d+)?)\"?",
                    RegexOptions.IgnoreCase);
                if (scoreMatch.Success)
                {
                    double.TryParse(scoreMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out score);
                }

                if (recommendedGroupId <= 0 && string.IsNullOrWhiteSpace(recommendedGroupName))
                {
                    return false;
                }

                recommendation.RecommendedGroupId = recommendedGroupId;
                recommendation.RecommendedGroupName = recommendedGroupName;
                recommendation.Reasoning = reasoning;
                recommendation.Score = score;
                recommendation.SharedTerms = new List<string>();
                recommendation.SharedConcepts = new List<string>();
                return true;
            }
        }

        private static bool TryGetJsonInt(JsonElement source, out int value, params string[] candidateKeys)
        {
            value = 0;

            if (!TryGetJsonProperty(source, out JsonElement propertyValue, candidateKeys))
            {
                return false;
            }

            if (propertyValue.ValueKind == JsonValueKind.Number)
            {
                return propertyValue.TryGetInt32(out value);
            }

            if (propertyValue.ValueKind == JsonValueKind.String)
            {
                return int.TryParse(propertyValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            return false;
        }

        private static bool TryGetJsonDouble(JsonElement source, out double value, params string[] candidateKeys)
        {
            value = 0;

            if (!TryGetJsonProperty(source, out JsonElement propertyValue, candidateKeys))
            {
                return false;
            }

            if (propertyValue.ValueKind == JsonValueKind.Number)
            {
                return propertyValue.TryGetDouble(out value);
            }

            if (propertyValue.ValueKind == JsonValueKind.String)
            {
                return double.TryParse(propertyValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || double.TryParse(propertyValue.GetString(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            }

            return false;
        }

        private static bool TryGetJsonString(JsonElement source, out string value, params string[] candidateKeys)
        {
            value = string.Empty;

            if (!TryGetJsonProperty(source, out JsonElement propertyValue, candidateKeys))
            {
                return false;
            }

            if (propertyValue.ValueKind == JsonValueKind.String)
            {
                value = (propertyValue.GetString() ?? string.Empty).Trim();
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static List<string> TryGetJsonStringList(JsonElement source, params string[] candidateKeys)
        {
            List<string> values = new List<string>();

            if (!TryGetJsonProperty(source, out JsonElement propertyValue, candidateKeys))
            {
                return values;
            }

            if (propertyValue.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (JsonElement item in propertyValue.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string text = (item.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text);
                    }
                }
            }

            return values;
        }

        private static bool TryGetJsonProperty(JsonElement source, out JsonElement propertyValue, params string[] candidateKeys)
        {
            propertyValue = default;

            if (source.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            HashSet<string> normalizedCandidateKeys = candidateKeys
                .Where((key) => !string.IsNullOrWhiteSpace(key))
                .Select(NormalizeJsonKey)
                .ToHashSet(StringComparer.Ordinal);

            if (normalizedCandidateKeys.Count == 0)
            {
                return false;
            }

            foreach (JsonProperty property in source.EnumerateObject())
            {
                if (normalizedCandidateKeys.Contains(NormalizeJsonKey(property.Name)))
                {
                    propertyValue = property.Value;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeJsonKey(string key)
        {
            string raw = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            StringBuilder normalized = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    normalized.Append(char.ToLowerInvariant(ch));
                }
            }

            return normalized.ToString();
        }

        private static bool TryResolveDateRange(
            string? fromDate,
            string? toDate,
            out DateTime startDate,
            out DateTime endDate,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            DateTime todayUtc = DateTime.UtcNow.Date;
            startDate = todayUtc.AddDays(-30);
            endDate = todayUtc;

            if (!string.IsNullOrWhiteSpace(fromDate))
            {
                if (!TryParseDateValue(fromDate, out startDate))
                {
                    errorMessage = "Invalid fromDate format. Use yyyy-MM-dd.";
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(toDate))
            {
                if (!TryParseDateValue(toDate, out endDate))
                {
                    errorMessage = "Invalid toDate format. Use yyyy-MM-dd.";
                    return false;
                }
            }

            startDate = startDate.Date;
            endDate = endDate.Date;

            if (startDate > endDate)
            {
                errorMessage = "fromDate must be earlier than or equal to toDate.";
                return false;
            }

            return true;
        }

        private static bool TryParseDateValue(string rawDate, out DateTime parsedDate)
        {
            string normalized = (rawDate ?? string.Empty).Trim();

            return DateTime.TryParseExact(
                       normalized,
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal,
                       out parsedDate)
                || DateTime.TryParse(
                       normalized,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal,
                       out parsedDate);
        }

        private static bool ResolveReportPresenceFromNotes(string rawNotes)
        {
            if (string.IsNullOrWhiteSpace(rawNotes))
            {
                return true;
            }

            try
            {
                using JsonDocument notesDocument = JsonDocument.Parse(rawNotes);
                JsonElement root = notesDocument.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return true;
                }

                if (!TryGetJsonProperty(root, out JsonElement isPresentElement, "isPresent", "present", "attendance"))
                {
                    return true;
                }

                return ReadBooleanJsonValue(isPresentElement, fallbackValue: true);
            }
            catch
            {
                return true;
            }
        }

        private static bool ReadBooleanJsonValue(JsonElement valueElement, bool fallbackValue)
        {
            switch (valueElement.ValueKind)
            {
                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Number:
                    if (valueElement.TryGetInt32(out int intValue))
                    {
                        return intValue > 0;
                    }

                    if (valueElement.TryGetDouble(out double doubleValue))
                    {
                        return doubleValue > 0;
                    }

                    return fallbackValue;

                case JsonValueKind.String:
                    return TryParseBooleanText(valueElement.GetString(), fallbackValue);

                default:
                    return fallbackValue;
            }
        }

        private static bool TryParseBooleanText(string? rawValue, bool fallbackValue)
        {
            string normalized = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallbackValue;
            }

            if (normalized is "true" or "1" or "yes" or "y" or "present" or "נוכח" or "כן")
            {
                return true;
            }

            if (normalized is "false" or "0" or "no" or "n" or "absent" or "לא נוכח" or "לא")
            {
                return false;
            }

            return fallbackValue;
        }

        private static string GetAttendanceLabel(string category)
        {
            return category switch
            {
                "Present" => "נוכח",
                "Absent" => "חיסור",
                "Late" => "איחור",
                "Cancelled" => "בוטל",
                _ => "מתוזמן"
            };
        }

        private sealed class AiGroupRecommendationPayload
        {
            public int RecommendedGroupId { get; set; }
            public string RecommendedGroupName { get; set; } = string.Empty;
            public double Score { get; set; }
            public string Reasoning { get; set; } = string.Empty;
            public List<string> SharedTerms { get; set; } = new List<string>();
            public List<string> SharedConcepts { get; set; } = new List<string>();
        }

        public class CreateGroupRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public int InstructorId { get; set; }
        }

        public class UpdateGroupRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public int InstructorId { get; set; }
        }

        public class SetGroupInstructorRequest
        {
            public int GroupId { get; set; }
            public int InstructorId { get; set; }
        }

        public class AssignChildToGroupRequest
        {
            public int? GroupId { get; set; }
            public int ChildId { get; set; }
        }

        public class UpdateCenterSettingsRequest
        {
            public string CenterName { get; set; } = string.Empty;
            public string CenterAddress { get; set; } = string.Empty;
            public bool SendAutoReports { get; set; }
            public bool ReceiveAlerts { get; set; }
            public bool ShowKidsAdvanced { get; set; }
        }

        public class GenerateSystemReportRequest
        {
            public string ReportType { get; set; } = string.Empty;
            public string? FromDate { get; set; }
            public string? ToDate { get; set; }
        }

        public class RecommendGroupRequest
        {
            public int ChildId { get; set; }
            public string ChildDescription { get; set; } = string.Empty;
        }
    }
}
