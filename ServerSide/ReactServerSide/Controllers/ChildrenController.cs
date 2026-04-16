using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;

namespace ReactServerSide.Controllers
{
    [Authorize(Roles = "Manager")]
    [Route("api/[controller]")]
    [ApiController]
    public class ChildrenController : ControllerBase
    {
        private readonly DBServices _db;

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

            try
            {
                int newId = _db.PostCreateChild(
                    request.ParentId,
                    request.FirstName.Trim(),
                    request.LastName.Trim(),
                    request.BirthDate,
                    request.ChildDescription);

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

            bool updated = _db.PutUpdateChild(
                id,
                request.ParentId,
                request.FirstName.Trim(),
                request.LastName.Trim(),
                request.BirthDate,
                request.ChildDescription,
                request.IsActive);

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
        }

        public class UpdateChildRequest
        {
            public int ParentId { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public DateTime? BirthDate { get; set; }
            public string? ChildDescription { get; set; }
            public bool IsActive { get; set; } = true;
        }
    }
}
