using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;
using System.Text.Json;

namespace ReactServerSide.Controllers
{
    [Authorize(Roles = "Manager")]
    [Route("api/[controller]")]
    [ApiController]
    public class ChildrenController : ControllerBase
    {
        private readonly DBServices _db;

        private static readonly Dictionary<string, string> SkillAreaLabels = new()
        {
            { "water_confidence", "הסתגלות וביטחון במים" },
            { "breathing", "שליטה בנשימות" },
            { "coordination", "תנועתיות וקואורדינציה" },
            { "floating", "יציבה וציפה" },
            { "communication", "תקשורת במים ושיתוף פעולה" },
            { "persistence", "התמדה ומאמץ" },
            { "initiative", "יוזמה" },
            { "focus", "קשב וריכוז" },
            { "instructions", "תגובה להוראות" },
            { "independence", "עצמאות בתרגיל" },
        };

        private static readonly Dictionary<string, string> WeaknessGoals = new()
        {
            { "water_confidence", "לפתח ביטחון בסיסי במים דרך תרגילים הדרגתיים ומשחקי מים" },
            { "breathing", "לשפר שליטה בנשימות ולהתרגל להכניס ראש למים בצורה מבוקרת" },
            { "coordination", "לחזק תנועתיות וקואורדינציה דרך תרגילי תנועה מגוונים" },
            { "floating", "לשפר יציבות וציפה דרך תרגול מודרג עם תמיכה הולכת ופוחתת" },
            { "communication", "לפתח תקשורת ושיתוף פעולה במים דרך עבודה בזוגות" },
            { "persistence", "לחזק התמדה ומאמץ דרך משימות קצרות עם תגמול מיידי" },
            { "initiative", "לעודד יוזמה עצמית דרך בחירת תרגילים ומשימות אישיות" },
            { "focus", "לשפר קשב וריכוז דרך הנחיות ברורות ומשימות ממוקדות" },
            { "instructions", "לחזק תגובה להוראות דרך תרגול שלבי עם חיזוקים חיוביים" },
            { "independence", "לפתח עצמאות בביצוע תרגילים עם הפחתה הדרגתית של סיוע" },
        };

        private static readonly Dictionary<string, string> StrengthGoals = new()
        {
            { "water_confidence", "להרחיב ביטחון במים לתרגילים מתקדמים ועומקים שונים" },
            { "breathing", "לשכלל טכניקת נשימה לשחייה רציפה ולמרחקים ארוכים" },
            { "coordination", "להעמיק קואורדינציה דרך תרגילים מורכבים ושילובי סגנונות" },
            { "floating", "להתקדם לציפה עצמאית ומעבר לתנוחות ציפה מתקדמות" },
            { "communication", "להוביל פעילויות קבוצתיות ולסייע לחברים בקבוצה" },
            { "persistence", "לקחת אתגרים מתקדמים ולהתמיד במשימות ארוכות יותר" },
            { "initiative", "להוביל תרגילים חדשים ולהציע רעיונות לפעילויות" },
            { "focus", "לשמור על ריכוז במשימות מורכבות ורב-שלביות" },
            { "instructions", "לסייע בהדגמת תרגילים ולשמש דוגמה לילדים אחרים" },
            { "independence", "לבצע רצפי תרגילים עצמאיים ולנהל אימון אישי" },
        };

        public ChildrenController(DBServices db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetChildren([FromQuery] bool includeInactive = false)
        {
            List<ChildRecord> children = _db.GetChildren(includeInactive);
            return Ok(children);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetChildById(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid child id." });
            }

            ChildRecord? child = _db.GetChildById(id);
            if (child == null)
            {
                return NotFound(new { message = "Child was not found." });
            }

            return Ok(child);
        }

        [HttpPost]
        public IActionResult CreateChild([FromBody] CreateChildRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ParentId <= 0 ||
                string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName))
            {
                return BadRequest(new { message = "ParentId, first name and last name are required." });
            }

            if (!_db.GetParentExists(request.ParentId))
            {
                return BadRequest(new { message = "Parent does not exist or is inactive." });
            }

            string? strengthsJson = SerializeSkillList(request.Strengths);
            string? weaknessesJson = SerializeSkillList(request.Weaknesses);
            string personalGoalsJson = GeneratePersonalGoals(request.Strengths, request.Weaknesses);

            try
            {
                int newId = _db.PostCreateChild(
                    request.ParentId,
                    request.FirstName.Trim(),
                    request.LastName.Trim(),
                    request.BirthDate,
                    request.ChildDescription,
                    strengthsJson,
                    weaknessesJson,
                    personalGoalsJson);

                ChildRecord? created = _db.GetChildById(newId);
                return CreatedAtAction(nameof(GetChildById), new { id = newId }, created);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateChild(int id, [FromBody] UpdateChildRequest request)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid child id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ParentId <= 0 ||
                string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName))
            {
                return BadRequest(new { message = "ParentId, first name and last name are required." });
            }

            if (!_db.GetParentExists(request.ParentId))
            {
                return BadRequest(new { message = "Parent does not exist or is inactive." });
            }

            string? strengthsJson = SerializeSkillList(request.Strengths);
            string? weaknessesJson = SerializeSkillList(request.Weaknesses);
            string personalGoalsJson = GeneratePersonalGoals(request.Strengths, request.Weaknesses);

            bool updated = _db.PutUpdateChild(
                id,
                request.ParentId,
                request.FirstName.Trim(),
                request.LastName.Trim(),
                request.BirthDate,
                request.ChildDescription,
                request.IsActive,
                strengthsJson,
                weaknessesJson,
                personalGoalsJson);

            if (!updated)
            {
                return NotFound(new { message = "Child was not found." });
            }

            ChildRecord? child = _db.GetChildById(id);
            return Ok(child);
        }

        [HttpDelete("{id:int}")]
        public IActionResult DeactivateChild(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "Invalid child id." });
            }

            bool deleted = _db.DeleteDeactivateChild(id);
            if (!deleted)
            {
                return NotFound(new { message = "Child was not found." });
            }

            return NoContent();
        }

        public class CreateChildRequest
        {
            public int ParentId { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public DateTime? BirthDate { get; set; }
            public string? ChildDescription { get; set; }
            public List<string>? Strengths { get; set; }
            public List<string>? Weaknesses { get; set; }
        }

        public class UpdateChildRequest
        {
            public int ParentId { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public DateTime? BirthDate { get; set; }
            public string? ChildDescription { get; set; }
            public bool IsActive { get; set; } = true;
            public List<string>? Strengths { get; set; }
            public List<string>? Weaknesses { get; set; }
        }

        private static string? SerializeSkillList(List<string>? skills)
        {
            if (skills == null || skills.Count == 0)
            {
                return null;
            }

            var validSkills = skills.Where(s => SkillAreaLabels.ContainsKey(s)).ToList();
            return validSkills.Count > 0 ? JsonSerializer.Serialize(validSkills) : null;
        }

        private static string GeneratePersonalGoals(List<string>? strengths, List<string>? weaknesses)
        {
            var goals = new List<object>();

            if (weaknesses != null)
            {
                foreach (var key in weaknesses.Where(k => WeaknessGoals.ContainsKey(k)))
                {
                    goals.Add(new
                    {
                        area = key,
                        areaLabel = SkillAreaLabels[key],
                        type = "weakness",
                        typeLabel = "חולשה - יעד לשיפור",
                        goal = WeaknessGoals[key]
                    });
                }
            }

            if (strengths != null)
            {
                foreach (var key in strengths.Where(k => StrengthGoals.ContainsKey(k)))
                {
                    goals.Add(new
                    {
                        area = key,
                        areaLabel = SkillAreaLabels[key],
                        type = "strength",
                        typeLabel = "חוזקה - יעד להתקדמות",
                        goal = StrengthGoals[key]
                    });
                }
            }

            return JsonSerializer.Serialize(goals);
        }
    }
}
