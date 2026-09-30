using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;
using ReactServerSide.DAL;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private const string ChatAttachmentPrefix = "[chat-attachment]";
        private const int MaxAttachmentBytes = 10 * 1024 * 1024;

        private readonly DBServices _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ChatController(
            DBServices db,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet("parent/{parentId:int}/children")]
        public IActionResult GetParentChildren(int parentId)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            List<ChatChildRecord> children = _db.GetParentChildrenForChat(parentId);
            return Ok(children);
        }

        [HttpGet("parent/{parentId:int}/children/{childId:int}/instructors")]
        public IActionResult GetInstructorsForChild(int parentId, int childId)
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

            List<ChatInstructorRecord> instructors = _db.GetInstructorsForParentChild(parentId, childId);
            return Ok(instructors);
        }

        [HttpPost("conversations/get-or-create")]
        public IActionResult GetOrCreateConversation([FromBody] GetOrCreateConversationRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.ParentId <= 0 || request.ChildId <= 0 || request.InstructorId <= 0)
            {
                return BadRequest(new { message = "ParentId, ChildId and InstructorId are required." });
            }

            if (!_db.GetParentExists(request.ParentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            try
            {
                ConversationRecord conversation = _db.PostGetOrCreateConversation(
                    request.ParentId,
                    request.ChildId,
                    request.InstructorId);

                return Ok(new
                {
                    conversationId = conversation.Id,
                    parentId = conversation.ParentId,
                    childId = conversation.ChildId,
                    instructorId = conversation.InstructorId,
                    parentFullName = conversation.ParentFullName,
                    childFullName = conversation.ChildFullName,
                    instructorFullName = conversation.InstructorFullName,
                    createdAt = conversation.CreatedAt,
                    lastMessageAt = conversation.LastMessageAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("instructor/{instructorId:int}/inbox")]
        public IActionResult GetInstructorInbox(int instructorId)
        {
            if (instructorId <= 0)
            {
                return BadRequest(new { message = "Invalid instructor id." });
            }

            if (!_db.GetInstructorExists(instructorId))
            {
                return NotFound(new { message = "Instructor was not found." });
            }

            List<ConversationInboxRecord> inboxItems = _db.GetInstructorConversationInbox(instructorId);
            return Ok(inboxItems.Select((item) => new
            {
                conversationId = item.ConversationId,
                parentId = item.ParentId,
                childId = item.ChildId,
                instructorId = item.InstructorId,
                parentFullName = item.ParentFullName,
                childFullName = item.ChildFullName,
                lastMessageAt = item.LastMessageAt,
                lastMessageText = BuildChatMessagePreview(item.LastMessageText),
                lastMessageSenderType = item.LastMessageSenderType
            }));
        }

        [HttpGet("parent/{parentId:int}/inbox")]
        public IActionResult GetParentInbox(int parentId)
        {
            if (parentId <= 0)
            {
                return BadRequest(new { message = "Invalid parent id." });
            }

            if (!_db.GetParentExists(parentId))
            {
                return NotFound(new { message = "Parent was not found or is inactive." });
            }

            List<ConversationInboxRecord> inboxItems = _db.GetParentConversationInbox(parentId);
            return Ok(inboxItems.Select((item) => new
            {
                conversationId = item.ConversationId,
                parentId = item.ParentId,
                childId = item.ChildId,
                instructorId = item.InstructorId,
                parentFullName = item.ParentFullName,
                childFullName = item.ChildFullName,
                instructorFullName = item.InstructorFullName,
                lastMessageAt = item.LastMessageAt,
                lastMessageText = BuildChatMessagePreview(item.LastMessageText),
                lastMessageSenderType = item.LastMessageSenderType
            }));
        }

        [HttpGet("conversations/{conversationId:int}")]
        public IActionResult GetConversationById(int conversationId)
        {
            if (conversationId <= 0)
            {
                return BadRequest(new { message = "Invalid conversation id." });
            }

            ConversationRecord? conversation = _db.GetConversationById(conversationId);
            if (conversation == null)
            {
                return NotFound(new { message = "Conversation was not found." });
            }

            return Ok(new
            {
                conversationId = conversation.Id,
                parentId = conversation.ParentId,
                childId = conversation.ChildId,
                instructorId = conversation.InstructorId,
                parentFullName = conversation.ParentFullName,
                childFullName = conversation.ChildFullName,
                instructorFullName = conversation.InstructorFullName,
                createdAt = conversation.CreatedAt,
                lastMessageAt = conversation.LastMessageAt
            });
        }

        [HttpGet("conversations/{conversationId:int}/messages")]
        public IActionResult GetConversationMessages(int conversationId)
        {
            if (conversationId <= 0)
            {
                return BadRequest(new { message = "Invalid conversation id." });
            }

            ConversationRecord? conversation = _db.GetConversationById(conversationId);
            if (conversation == null)
            {
                return NotFound(new { message = "Conversation was not found." });
            }

            List<ChatMessageRecord> messages = _db.GetConversationMessages(conversationId);

            // Backward-compat: migrate old messages that stored a file-system path
            // instead of an inline data URL.  Read the file from disk (if it exists
            // on this machine) and replace the URL with a base64 data URL.
            string uploadsRoot = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot", "chat-attachments");
            foreach (ChatMessageRecord message in messages)
            {
                message.MessageText = MigrateFilePathAttachmentToDataUrl(message.MessageText, uploadsRoot);
            }

            return Ok(messages);
        }

        [HttpPost("conversations/{conversationId:int}/messages")]
        [RequestSizeLimit(15 * 1024 * 1024)]
        public async Task<IActionResult> CreateMessage(int conversationId, [FromBody] CreateConversationMessageRequest request)
        {
            if (conversationId <= 0)
            {
                return BadRequest(new { message = "Invalid conversation id." });
            }

            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            bool hasAttachmentImage = !string.IsNullOrWhiteSpace(request.AttachmentImageDataUrl);

            if (request.SenderId <= 0 || string.IsNullOrWhiteSpace(request.SenderType) || (!hasAttachmentImage && string.IsNullOrWhiteSpace(request.MessageText)))
            {
                return BadRequest(new { message = "SenderId and SenderType are required, and either MessageText or an image attachment must be provided." });
            }

            try
            {
                string normalizedMessageText = (request.MessageText ?? string.Empty).Trim();

                if (hasAttachmentImage)
                {
                    string imageUrl = SaveChatAttachmentImageFromDataUrl(
                        request.AttachmentImageDataUrl ?? string.Empty,
                        request.AttachmentMimeType);

                    normalizedMessageText = BuildChatImageAttachmentMessage(imageUrl, normalizedMessageText);
                }

                if (string.IsNullOrWhiteSpace(normalizedMessageText))
                {
                    return BadRequest(new { message = "Message payload could not be created." });
                }

                ChatMessageRecord createdMessage = _db.PostCreateConversationMessage(
                    conversationId,
                    request.SenderType,
                    request.SenderId,
                    normalizedMessageText);

                string notificationMessagePreview = BuildChatMessagePreview(createdMessage.MessageText);

                try
                {
                    _db.PostCreateChatMessageNotification(
                        conversationId,
                        request.SenderType,
                        request.SenderId,
                        createdMessage.Id,
                        notificationMessagePreview);
                }
                catch
                {
                    // Notification persistence should not break chat delivery.
                }

                await TrySendNewMessagePushAsync(conversationId, request.SenderType, request.SenderId, createdMessage);

                return Created(string.Empty, createdMessage);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("push/register")]
        public IActionResult RegisterPushToken([FromBody] RegisterPushTokenRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.UserId <= 0 || string.IsNullOrWhiteSpace(request.UserType) || string.IsNullOrWhiteSpace(request.PushToken))
            {
                return BadRequest(new { message = "UserId, UserType and PushToken are required." });
            }

            try
            {
                _db.UpsertPushDeviceToken(
                    request.UserType,
                    request.UserId,
                    request.PushToken,
                    request.Platform,
                    request.DeviceId);

                return Ok(new { message = "Push token registered." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("push/deregister")]
        public IActionResult DeregisterPushToken([FromBody] RegisterPushTokenRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (request.UserId <= 0 || string.IsNullOrWhiteSpace(request.UserType) || string.IsNullOrWhiteSpace(request.PushToken))
            {
                return BadRequest(new { message = "UserId, UserType and PushToken are required." });
            }

            try
            {
                _db.DeactivatePushDeviceToken(
                    request.UserType,
                    request.UserId,
                    request.PushToken);

                return Ok(new { message = "Push token deactivated." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task TrySendNewMessagePushAsync(
            int conversationId,
            string senderType,
            int senderId,
            ChatMessageRecord createdMessage)
        {
            try
            {
                ConversationRecord? conversation = _db.GetConversationById(conversationId);
                if (conversation == null)
                {
                    return;
                }

                string normalizedSenderType = string.Equals(createdMessage.SenderType, "Parent", StringComparison.OrdinalIgnoreCase)
                    ? "Parent"
                    : "Instructor";

                string recipientUserType = normalizedSenderType == "Parent" ? "Instructor" : "Parent";
                int recipientUserId = normalizedSenderType == "Parent"
                    ? conversation.InstructorId
                    : conversation.ParentId;

                List<string> recipientTokens = _db.GetConversationRecipientPushTokens(conversationId, senderType, senderId);

                // Fallback: if strict sender-validation lookup returned no tokens,
                // query active tokens directly for the resolved recipient user.
                if (recipientTokens.Count == 0 && recipientUserId > 0)
                {
                    recipientTokens = _db.GetActivePushTokensForUser(recipientUserType, recipientUserId);
                }

                if (recipientTokens.Count == 0)
                {
                    return;
                }

                string senderDisplayName = string.IsNullOrWhiteSpace(createdMessage.SenderName)
                    ? (string.Equals(createdMessage.SenderType, "Parent", StringComparison.OrdinalIgnoreCase) ? "הורה" : "מדריך")
                    : createdMessage.SenderName.Trim();

                string messagePreview = BuildChatMessagePreview(createdMessage.MessageText);
                if (messagePreview.Length > 120)
                {
                    messagePreview = messagePreview[..117] + "...";
                }

                if (string.IsNullOrWhiteSpace(messagePreview))
                {
                    messagePreview = "נשלחה הודעה חדשה בצ׳אט.";
                }

                string title = $"הודעה חדשה מ-{senderDisplayName}";

                object[] payload = recipientTokens.Select((token) => new
                {
                    to = token,
                    title,
                    body = messagePreview,
                    sound = "default",
                    data = new
                    {
                        type = "chat_message",
                        conversationId,
                        messageId = createdMessage.Id,
                        senderType = createdMessage.SenderType,
                        recipientUserType,
                        recipientUserId,
                        parentId = conversation.ParentId,
                        childId = conversation.ChildId,
                        instructorId = conversation.InstructorId
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

                // Push delivery failures should not block message creation.
                _ = response.IsSuccessStatusCode;
            }
            catch
            {
                // Ignore push failures so the chat flow remains reliable.
            }
        }

        private string SaveChatAttachmentImageFromDataUrl(string imageDataUrl, string? declaredMimeType)
        {
            string normalizedDataUrl = (imageDataUrl ?? string.Empty).Trim();
            string normalizedDeclaredMimeType = (declaredMimeType ?? string.Empty).Trim();

            if (!TryDecodeImageDataUrl(normalizedDataUrl, normalizedDeclaredMimeType, out byte[] imageBytes, out string mimeType))
            {
                throw new InvalidOperationException("Attachment image format is invalid.");
            }

            if (imageBytes.Length == 0)
            {
                throw new InvalidOperationException("Attachment image is empty.");
            }

            if (imageBytes.Length > MaxAttachmentBytes)
            {
                throw new InvalidOperationException("Attachment image is too large. Maximum size is 10MB.");
            }

            // Build a clean, canonical data URL from the decoded bytes so that
            // the full image payload lives inside the database (shared across
            // all server instances) rather than on the local file system.
            string base64Content = Convert.ToBase64String(imageBytes);
            return $"data:{mimeType};base64,{base64Content}";
        }

        private static string BuildChatImageAttachmentMessage(string imageUrl, string caption)
        {
            string normalizedImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? string.Empty : imageUrl.Trim();
            string normalizedCaption = string.IsNullOrWhiteSpace(caption) ? string.Empty : caption.Trim();

            ChatAttachmentPayload payload = new ChatAttachmentPayload
            {
                Type = "image",
                Url = normalizedImageUrl,
                Caption = normalizedCaption
            };

            return ChatAttachmentPrefix + JsonSerializer.Serialize(payload);
        }

        private static string BuildChatMessagePreview(string messageText)
        {
            if (TryParseChatAttachmentMessage(messageText, out ChatAttachmentPayload payload)
                && string.Equals(payload.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(payload.Caption))
                {
                    return $"📷 {payload.Caption.Trim()}";
                }

                return "📷 נשלחה תמונה";
            }

            string normalized = string.IsNullOrWhiteSpace(messageText) ? string.Empty : messageText.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            if (normalized.Length > 160)
            {
                return normalized[..157] + "...";
            }

            return normalized;
        }

        private static bool TryParseChatAttachmentMessage(string messageText, out ChatAttachmentPayload payload)
        {
            payload = new ChatAttachmentPayload();
            string normalized = string.IsNullOrWhiteSpace(messageText) ? string.Empty : messageText.Trim();

            if (!normalized.StartsWith(ChatAttachmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string payloadJson = normalized[ChatAttachmentPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                payload = JsonSerializer.Deserialize<ChatAttachmentPayload>(payloadJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ChatAttachmentPayload();
                return !string.IsNullOrWhiteSpace(payload.Type);
            }
            catch
            {
                payload = new ChatAttachmentPayload();
                return false;
            }
        }

        private static bool TryDecodeImageDataUrl(string imageDataUrl, string fallbackMimeType, out byte[] imageBytes, out string mimeType)
        {
            imageBytes = Array.Empty<byte>();
            mimeType = string.IsNullOrWhiteSpace(fallbackMimeType) ? "image/jpeg" : fallbackMimeType.Trim();

            if (string.IsNullOrWhiteSpace(imageDataUrl))
            {
                return false;
            }

            string rawData = imageDataUrl.Trim();

            if (rawData.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int commaIndex = rawData.IndexOf(',');
                if (commaIndex <= 5)
                {
                    return false;
                }

                string metadata = rawData[5..commaIndex];
                string[] metaParts = metadata.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (metaParts.Length > 0 && metaParts[0].Contains('/'))
                {
                    mimeType = metaParts[0].Trim();
                }

                bool hasBase64Tag = metaParts.Any((part) => string.Equals(part.Trim(), "base64", StringComparison.OrdinalIgnoreCase));
                if (!hasBase64Tag)
                {
                    return false;
                }

                rawData = rawData[(commaIndex + 1)..];
            }

            if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                imageBytes = Convert.FromBase64String(rawData);
                return imageBytes.Length > 0;
            }
            catch
            {
                imageBytes = Array.Empty<byte>();
                return false;
            }
        }

        private static string ResolveImageExtension(string mimeType)
        {
            string normalizedMimeType = string.IsNullOrWhiteSpace(mimeType)
                ? string.Empty
                : mimeType.Trim().ToLowerInvariant();

            return normalizedMimeType switch
            {
                "image/png" => "png",
                "image/webp" => "webp",
                "image/heic" => "heic",
                "image/heif" => "heif",
                _ => "jpg"
            };
        }

        private static string ResolveExtensionMimeType(string extension)
        {
            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? string.Empty
                : extension.Trim().ToLowerInvariant().TrimStart('.');

            return normalizedExtension switch
            {
                "png" => "image/png",
                "webp" => "image/webp",
                "heic" => "image/heic",
                "heif" => "image/heif",
                "gif" => "image/gif",
                _ => "image/jpeg"
            };
        }

        /// <summary>
        /// For backward compatibility: if a message has an attachment URL pointing
        /// to a local file path (e.g. /chat-attachments/file.jpg or http://host/chat-attachments/file.jpg),
        /// read the file from disk and rewrite the URL to an inline base64 data URL.
        /// </summary>
        private static string MigrateFilePathAttachmentToDataUrl(string messageText, string uploadsRoot)
        {
            if (!TryParseChatAttachmentMessage(messageText, out ChatAttachmentPayload payload)
                || !string.Equals(payload.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                return messageText;
            }

            string attachmentUrl = (payload.Url ?? string.Empty).Trim();

            // Already a data URL – nothing to migrate
            if (attachmentUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return messageText;
            }

            // Extract just the filename from whatever form the URL takes:
            //   /chat-attachments/filename.jpg
            //   http://192.168.1.13:5202/chat-attachments/filename.jpg
            string fileName = string.Empty;
            const string chatAttachmentsSegment = "/chat-attachments/";

            int segmentIndex = attachmentUrl.IndexOf(chatAttachmentsSegment, StringComparison.OrdinalIgnoreCase);
            if (segmentIndex >= 0)
            {
                fileName = attachmentUrl[(segmentIndex + chatAttachmentsSegment.Length)..].Trim();

                // Strip query string if present
                int queryIndex = fileName.IndexOf('?');
                if (queryIndex >= 0)
                {
                    fileName = fileName[..queryIndex];
                }
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return messageText;
            }

            // URL-decode the filename in case it was encoded
            try
            {
                fileName = Uri.UnescapeDataString(fileName);
            }
            catch
            {
                // If decoding fails, use as-is
            }

            string filePath = Path.Combine(uploadsRoot, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                // File doesn't exist on this machine – return as-is; the client
                // will show a graceful error.
                return messageText;
            }

            try
            {
                byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
                string extension = Path.GetExtension(fileName);
                string mimeType = ResolveExtensionMimeType(extension);
                string base64Content = Convert.ToBase64String(fileBytes);
                string dataUrl = $"data:{mimeType};base64,{base64Content}";

                // Rebuild the attachment message with the data URL
                return BuildChatImageAttachmentMessage(dataUrl, payload.Caption);
            }
            catch
            {
                // If file read fails, return original – client will handle error
                return messageText;
            }
        }

        public class GetOrCreateConversationRequest
        {
            public int ParentId { get; set; }
            public int ChildId { get; set; }
            public int InstructorId { get; set; }
        }

        public class CreateConversationMessageRequest
        {
            public int SenderId { get; set; }
            public string SenderType { get; set; } = string.Empty;
            public string MessageText { get; set; } = string.Empty;
            public string? AttachmentImageDataUrl { get; set; }
            public string? AttachmentMimeType { get; set; }
        }

        public class ChatAttachmentPayload
        {
            public string Type { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Caption { get; set; } = string.Empty;
        }


        // ==========================================
        //             AI Chat Endpoints
        // ==========================================

        public class ChatRequest
        {
            public string MessageText { get; set; } = string.Empty;
            public List<ChatHistoryItem>? History { get; set; }
        }

        public class ChatHistoryItem
        {
            public string SenderType { get; set; } = string.Empty; // "User" or "AI"
            public string MessageText { get; set; } = string.Empty;
        }

        public class FunctionCall
        {
            public string Function { get; set; } = string.Empty;
            public Dictionary<string, JsonElement>? Arguments { get; set; }
        }

        [HttpPost("instructor/{instructorId:int}/ai-chat")]
        public async Task<IActionResult> InstructorAIChat(int instructorId, [FromBody] ChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.MessageText))
                {
                    return BadRequest("Message cannot be empty.");
                }

                if (!_db.GetInstructorExists(instructorId))
                {
                    return NotFound("Instructor not found.");
                }

                string userMessage = request.MessageText;

                // 1. Save user message to database history
                _db.SaveAIChatMessage(instructorId, "user", userMessage);

                // Fetch context data
                var groups = _db.GetGroupsForInstructor(instructorId);
                var childrenByGroup = _db.GetChildrenByGroupsForInstructor(instructorId);
                var children = childrenByGroup.SelectMany(g => g.Children).ToList();
                var instructors = _db.GetInstructorsForManager();

                var matchedChildIds = new HashSet<int>();
                var matchedGroupIds = new HashSet<int>();
                var matchedInstructorIds = new HashSet<int>();

                // Detect matches
                foreach (var c in children)
                {
                    if (userMessage.Contains(c.FullName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.FirstName) && userMessage.Contains(c.FirstName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedChildIds.Add(c.ChildId);
                    }
                }

                foreach (var g in groups)
                {
                    if (userMessage.Contains(g.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedGroupIds.Add(g.GroupId);
                    }
                }

                foreach (var inst in instructors)
                {
                    if (userMessage.Contains(inst.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedInstructorIds.Add(inst.InstructorId);
                    }
                }

                List<ChildReportRecord> reports = new List<ChildReportRecord>();
                List<InstructorTrainingSessionViewRecord> sessionsList = new List<InstructorTrainingSessionViewRecord>();
                List<object> childrenList = new List<object>();
                List<object> groupsSummariesList = new List<object>();
                List<object> parentsContactList = new List<object>();
                List<object> instructorsSummariesList = new List<object>();
                List<object> reportsForPrompt = new List<object>();

                // ALWAYS populate summaries to give the chatbot persistent school context
                var allChildIds = children.Select(c => c.ChildId).ToList();
                var allReports = _db.GetChildReportsForChildren(allChildIds);
                var childReportsMap = allReports.GroupBy(r => r.ChildId).ToDictionary(g => g.Key, g => g.ToList());
                var groupReportsMap = allReports.Where(r => r.GroupId.HasValue).GroupBy(r => r.GroupId.Value).ToDictionary(g => g.Key, g => g.ToList());
                var instructorReportsMap = allReports.GroupBy(r => r.InstructorId).ToDictionary(g => g.Key, g => g.ToList());

                var allGroups = _db.GetGroupsForManagement(true);
                var groupsByInstructor = allGroups
                    .Where(g => g.InstructorId.HasValue)
                    .GroupBy(g => g.InstructorId.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Query GroupChildren for active group assignments
                var gcScan = _db.DynamoDbClient.ScanAsync(new Amazon.DynamoDBv2.Model.ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
                var activeGroupIdsByChild = new Dictionary<int, List<int>>();
                foreach (var item in gcScan.Items)
                {
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isActive && item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cId) &&
                        item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId))
                    {
                        if (!activeGroupIdsByChild.ContainsKey(cId))
                        {
                            activeGroupIdsByChild[cId] = new List<int>();
                        }
                        activeGroupIdsByChild[cId].Add(gId);
                    }
                }

                foreach (var c in children)
                {
                    List<ChildReportRecord> cReports = childReportsMap.TryGetValue(c.ChildId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(cReports);
                    int totalReports = cReports.Count;
                    int presentLessons = cReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;
                    var childMetricAverages = CalculateMetricAveragesForChild(cReports);

                    var childGroupIds = activeGroupIdsByChild.TryGetValue(c.ChildId, out var gIds) ? gIds : new List<int>();
                    var childGroupsList = childGroupIds
                        .Select(gid => new {
                            groupId = gid,
                            groupName = allGroups.FirstOrDefault(g => g.GroupId == gid)?.Name ?? "קבוצה לא ידועה"
                        })
                        .ToList();

                    childrenList.Add(new
                    {
                        childId = c.ChildId,
                        childName = c.FullName,
                        parentFullName = c.ParentFullName,
                        parentEmail = c.ParentEmail,
                        activeGroupId = childGroupIds.FirstOrDefault(),
                        activeGroupName = allGroups.FirstOrDefault(g => g.GroupId == childGroupIds.FirstOrDefault())?.Name ?? string.Empty,
                        activeGroups = childGroupsList,
                        overallAverageScore = overallAverageScore,
                        metricAverages = childMetricAverages,
                        totalReportsCount = totalReports,
                        presentLessonsCount = presentLessons,
                        attendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var g in groups)
                {
                    List<ChildReportRecord> gReports = groupReportsMap.TryGetValue(g.GroupId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(gReports);
                    int totalReports = gReports.Count;
                    int presentLessons = gReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;
                    var groupMetricAverages = CalculateMetricAveragesForChild(gReports);

                    groupsSummariesList.Add(new
                    {
                        groupId = g.GroupId,
                        groupName = g.Name,
                        description = g.Description,
                        activeChildrenCount = g.ActiveChildrenCount,
                        overallAverageScore = overallAverageScore,
                        metricAverages = groupMetricAverages,
                        totalReportsCount = totalReports,
                        attendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var inst in instructors)
                {
                    List<ChildReportRecord> instReports = instructorReportsMap.TryGetValue(inst.InstructorId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(instReports);
                    int totalReports = instReports.Count;
                    int presentLessons = instReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;

                    var instGroups = groupsByInstructor.TryGetValue(inst.InstructorId, out var gList) ? gList : new List<GroupSummaryRecord>();

                    instructorsSummariesList.Add(new
                    {
                        instructorId = inst.InstructorId,
                        instructorName = inst.FullName,
                        email = inst.Email,
                        activeGroupsCount = instGroups.Count,
                        groupsList = instGroups.Select(g => g.Name).ToList(),
                        overallAverageStudentScore = overallAverageScore,
                        totalReportsCount = totalReports,
                        studentAttendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var c in children)
                {
                    parentsContactList.Add(new
                    {
                        parentName = c.ParentFullName,
                        email = c.ParentEmail,
                        childName = c.FullName
                    });
                }

                bool isDbQuery = IsDatabaseQuery(userMessage) 
                    || matchedChildIds.Count > 0 
                    || matchedGroupIds.Count > 0 
                    || matchedInstructorIds.Count > 0;

                if (isDbQuery)
                {
                    // STEP 1: Call Gemini to plan database query / choose functions
                    string step1SystemPrompt = @"You are the Database Query Planner for a specialized swimming school management application.
Your job is to analyze the user's query and decide which data-retrieval functions to call to gather the RAW data needed for the final response.

ALWAYS request RAW chronological records (reports, attendance, metrics) when the user asks for comparisons, trends, progress, or performance analysis.
Do NOT assume the server will pre-calculate averages, velocities, or trends. You need the raw data points to perform these calculations yourself.
When the query is about progress, trends, or comparisons over time, increase the 'limit' parameter (e.g. set to 15, 30, or 50) to ensure a complete dataset.

Available Functions:
1. get_child_reports(childIds: int[], limit: int): Retrieve raw progress reports for specific children.
2. get_group_reports(groupIds: int[], limit: int): Retrieve raw progress reports for entire groups/classes.
3. get_child_sessions(childIds: int[]): Retrieve scheduled lessons and meetings.

Return ONLY a JSON array of function calls (e.g. [ { ""function"": ""get_child_reports"", ""arguments"": { ""childIds"": [1], ""limit"": 30 } } ]). Do not include markdown code blocks or text outside the JSON.";

                    string step1Response = await CallGeminiAsync(step1SystemPrompt, userMessage, request.History);
                    
                    // Clean step 1 response (remove markdown code blocks if any)
                    step1Response = CleanJsonText(step1Response);

                    List<FunctionCall>? calls = null;
                    try
                    {
                        calls = JsonSerializer.Deserialize<List<FunctionCall>>(step1Response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { /* Fallback to default manual matching if parsing fails */ }

                    if (calls != null && calls.Count > 0)
                    {
                        foreach (var call in calls)
                        {
                            if (string.Equals(call.Function, "get_child_reports", StringComparison.OrdinalIgnoreCase))
                            {
                                var childIds = GetIdsFromArgs(call.Arguments, "childIds");
                                int limit = GetLimitFromArgs(call.Arguments, 30);
                                if (childIds.Count > 0)
                                {
                                    var fetched = _db.GetChildReportsForChildren(childIds).Take(limit).ToList();
                                    reports.AddRange(fetched);
                                }
                            }
                            else if (string.Equals(call.Function, "get_group_reports", StringComparison.OrdinalIgnoreCase))
                            {
                                var groupIds = GetIdsFromArgs(call.Arguments, "groupIds");
                                int limit = GetLimitFromArgs(call.Arguments, 30);
                                foreach (var gid in groupIds)
                                {
                                    var childIdsInGroup = childrenByGroup.FirstOrDefault(g => g.GroupId == gid)?.Children.Select(c => c.ChildId).ToList();
                                    if (childIdsInGroup != null && childIdsInGroup.Count > 0)
                                    {
                                        var fetched = _db.GetChildReportsForChildren(childIdsInGroup).Take(limit).ToList();
                                        reports.AddRange(fetched);
                                    }
                                }
                            }
                            else if (string.Equals(call.Function, "get_child_sessions", StringComparison.OrdinalIgnoreCase))
                            {
                                var childIds = GetIdsFromArgs(call.Arguments, "childIds");
                                if (childIds.Count > 0)
                                {
                                    var allSessions = _db.GetInstructorTrainingSessions(instructorId, DateTime.Today.AddYears(-1), DateTime.Today.AddYears(1));
                                    var matched = allSessions.Where(s => childIds.Contains(s.ChildId)).ToList();
                                    sessionsList.AddRange(matched);
                                }
                            }
                        }
                    }
                }

                // Fallback / Auto-resolution if lists are still empty
                if (reports.Count == 0 && matchedChildIds.Count > 0)
                {
                    reports.AddRange(_db.GetChildReportsForChildren(matchedChildIds.ToList()).Take(30));
                }

                if (reports.Count == 0 && matchedGroupIds.Count > 0)
                {
                    foreach (var gid in matchedGroupIds)
                    {
                        var childIdsInGroup = childrenByGroup.FirstOrDefault(g => g.GroupId == gid)?.Children.Select(c => c.ChildId).ToList();
                        if (childIdsInGroup != null && childIdsInGroup.Count > 0)
                        {
                            reports.AddRange(_db.GetChildReportsForChildren(childIdsInGroup).Take(30));
                        }
                    }
                }

                if (sessionsList.Count == 0 && matchedChildIds.Count > 0)
                {
                    var allSessions = _db.GetInstructorTrainingSessions(instructorId, DateTime.Today.AddYears(-1), DateTime.Today.AddYears(1));
                    var matched = allSessions.Where(s => matchedChildIds.Contains(s.ChildId)).ToList();
                    sessionsList.AddRange(matched);
                }

                // STEP 2: Conversational response generation
                string instructorName = instructors.FirstOrDefault(i => i.InstructorId == instructorId)?.FullName ?? "המדריך";

                reportsForPrompt = reports.Select(r => new
                {
                    childName = children.FirstOrDefault(c => c.ChildId == r.ChildId)?.FullName,
                    reportDate = r.ReportDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    isPresent = r.IsPresent,
                    comment = r.Comment,
                    metrics = r.Metrics
                }).Cast<object>().ToList();

                string childrenJson = childrenList.Count > 0 ? JsonSerializer.Serialize(childrenList) : "[]";
                string reportsJson = reportsForPrompt.Count > 0 ? JsonSerializer.Serialize(reportsForPrompt) : "[]";
                string sessionsJson = JsonSerializer.Serialize(sessionsList);
                string parentsContactJson = JsonSerializer.Serialize(parentsContactList);
                string groupsSummariesJson = groupsSummariesList.Count > 0 ? JsonSerializer.Serialize(groupsSummariesList) : "[]";
                string instructorsSummariesJson = instructorsSummariesList.Count > 0 ? JsonSerializer.Serialize(instructorsSummariesList) : "[]";

                string systemPrompt2 = $@"You are the Senior Data Analyst and Advanced Comparison Engine built into an hydrotherapy swimming school management application.
Answer the user's query in HEBREW using the provided database context below.

Details about the instructor, children, groups, and reports are loaded:
- Instructor Name: {instructorName}
- Children List: {childrenJson}
- Progress Reports: {reportsJson}
- Scheduled Lessons: {sessionsJson}
- Group Summaries: {groupsSummariesJson}
- Instructor Summaries: {instructorsSummariesJson}
- Parent Contacts: {parentsContactJson}

You must execute all required statistical and logical calculations internally:
1. Averages: Calculate overall averages and specific metric averages (1-5 scale) from the raw progress reports.
2. Attendance Rates: Calculate attendance rate as: (Present Lessons / Total Lessons) * 100%.
3. Progress Velocity & Trend: Compare the average score of the first 20% of chronological lessons (baseline) with the last 20% of lessons (current). Define trend: 'שיפור משמעותי' (Improvement > 15%), 'יציבות' (Stagnation), or 'נסיגה' (Decline).

Formatting Rules:
- Markdown Tables: When comparing multiple children, groups, or metrics, you MUST present them in a clear Markdown table (e.g. name | overall average | attendance | lead metric).
- Bullet Points: For key insights.
- Bottom Line: Conclude with a brief 2-3 sentence executive summary.

Read-Only Mode: You cannot modify or write any database records. 
Do not mention function names, JSON, database, or API structures in your response. Speak naturally as a professional hydrotherapy expert.";

                string finalResponse = await CallGeminiAsync(systemPrompt2, userMessage, request.History);

                // Save assistant message to database history
                _db.SaveAIChatMessage(instructorId, "ai", finalResponse);

                return Ok(new { response = finalResponse });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error processing AI chat: {ex.Message}" });
            }
        }

        [HttpPost("manager/{managerId:int}/ai-chat")]
        public async Task<IActionResult> ManagerAIChat(int managerId, [FromBody] ChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.MessageText))
                {
                    return BadRequest("Message cannot be empty.");
                }

                var instructors = _db.GetInstructorsForManager();

                string userMessage = request.MessageText;

                // 1. Save user message to database history
                _db.SaveAIChatMessage(managerId, "user", userMessage);

                // Fetch context data
                var groups = _db.GetGroupsForManagement(true);
                var children = _db.GetChildrenForGroupManagement(true);
                var parents = _db.GetParentsForManager();

                var matchedChildIds = new HashSet<int>();
                var matchedGroupIds = new HashSet<int>();
                var matchedInstructorIds = new HashSet<int>();

                // Detect matches
                foreach (var c in children)
                {
                    if (userMessage.Contains(c.FullName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.FirstName) && userMessage.Contains(c.FirstName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedChildIds.Add(c.ChildId);
                    }
                }

                foreach (var g in groups)
                {
                    if (userMessage.Contains(g.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedGroupIds.Add(g.GroupId);
                    }
                }

                foreach (var inst in instructors)
                {
                    if (userMessage.Contains(inst.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedInstructorIds.Add(inst.InstructorId);
                    }
                }

                List<ChildReportRecord> reports = new List<ChildReportRecord>();
                List<InstructorTrainingSessionViewRecord> sessionsList = new List<InstructorTrainingSessionViewRecord>();
                List<object> childrenList = new List<object>();
                List<object> groupsSummariesList = new List<object>();
                List<object> parentsContactList = new List<object>();
                List<object> instructorsSummariesList = new List<object>();
                List<object> reportsForPrompt = new List<object>();

                // ALWAYS populate summaries to give the chatbot persistent school context
                var allChildIds = children.Select(c => c.ChildId).ToList();
                var allReports = _db.GetChildReportsForChildren(allChildIds);
                var childReportsMap = allReports.GroupBy(r => r.ChildId).ToDictionary(g => g.Key, g => g.ToList());
                var groupReportsMap = allReports.Where(r => r.GroupId.HasValue).GroupBy(r => r.GroupId.Value).ToDictionary(g => g.Key, g => g.ToList());
                var instructorReportsMap = allReports.GroupBy(r => r.InstructorId).ToDictionary(g => g.Key, g => g.ToList());

                var groupsByInstructor = groups
                    .Where(g => g.InstructorId.HasValue)
                    .GroupBy(g => g.InstructorId.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Query GroupChildren for active group assignments
                var gcScan = _db.DynamoDbClient.ScanAsync(new Amazon.DynamoDBv2.Model.ScanRequest { TableName = "GroupChildren" }).GetAwaiter().GetResult();
                var activeGroupIdsByChild = new Dictionary<int, List<int>>();
                foreach (var item in gcScan.Items)
                {
                    bool isActive = item.ContainsKey("IsActive") && item["IsActive"].BOOL == true;
                    if (isActive && item.ContainsKey("ChildId") && int.TryParse(item["ChildId"].N, out int cId) &&
                        item.ContainsKey("GroupId") && int.TryParse(item["GroupId"].N, out int gId))
                    {
                        if (!activeGroupIdsByChild.ContainsKey(cId))
                        {
                            activeGroupIdsByChild[cId] = new List<int>();
                        }
                        activeGroupIdsByChild[cId].Add(gId);
                    }
                }

                foreach (var c in children)
                {
                    List<ChildReportRecord> cReports = childReportsMap.TryGetValue(c.ChildId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(cReports);
                    int totalReports = cReports.Count;
                    int presentLessons = cReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;
                    var childMetricAverages = CalculateMetricAveragesForChild(cReports);

                    var childGroupIds = activeGroupIdsByChild.TryGetValue(c.ChildId, out var gIds) ? gIds : new List<int>();
                    var childGroupsList = childGroupIds
                        .Select(gid => new {
                            groupId = gid,
                            groupName = groups.FirstOrDefault(g => g.GroupId == gid)?.Name ?? "קבוצה לא ידועה"
                        })
                        .ToList();

                    childrenList.Add(new
                    {
                        childId = c.ChildId,
                        childName = c.FullName,
                        activeGroupId = childGroupIds.FirstOrDefault(),
                        activeGroupName = groups.FirstOrDefault(g => g.GroupId == childGroupIds.FirstOrDefault())?.Name ?? string.Empty,
                        activeGroups = childGroupsList,
                        overallAverageScore = overallAverageScore,
                        metricAverages = childMetricAverages,
                        totalReportsCount = totalReports,
                        presentLessonsCount = presentLessons,
                        attendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var g in groups)
                {
                    List<ChildReportRecord> gReports = groupReportsMap.TryGetValue(g.GroupId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(gReports);
                    int totalReports = gReports.Count;
                    int presentLessons = gReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;
                    var groupMetricAverages = CalculateMetricAveragesForChild(gReports);

                    groupsSummariesList.Add(new
                    {
                        groupId = g.GroupId,
                        groupName = g.Name,
                        description = g.Description,
                        instructorName = g.InstructorFullName,
                        activeChildrenCount = g.ActiveChildrenCount,
                        overallAverageScore = overallAverageScore,
                        metricAverages = groupMetricAverages,
                        totalReportsCount = totalReports,
                        attendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var inst in instructors)
                {
                    List<ChildReportRecord> instReports = instructorReportsMap.TryGetValue(inst.InstructorId, out var rList) ? rList : new List<ChildReportRecord>();
                    double overallAverageScore = CalculateAverageScoreFromReports(instReports);
                    int totalReports = instReports.Count;
                    int presentLessons = instReports.Count(r => r.IsPresent);
                    double attendanceRate = totalReports > 0 ? Math.Round((presentLessons * 100.0) / totalReports, 0) : 0;

                    var instGroups = groupsByInstructor.TryGetValue(inst.InstructorId, out var gList) ? gList : new List<GroupSummaryRecord>();

                    instructorsSummariesList.Add(new
                    {
                        instructorId = inst.InstructorId,
                        instructorName = inst.FullName,
                        email = inst.Email,
                        activeGroupsCount = instGroups.Count,
                        groupsList = instGroups.Select(g => g.Name).ToList(),
                        overallAverageStudentScore = overallAverageScore,
                        totalReportsCount = totalReports,
                        studentAttendanceRate = attendanceRate + "%"
                    });
                }

                foreach (var p in parents)
                {
                    parentsContactList.Add(new
                    {
                        parentName = p.FullName,
                        email = p.Email,
                        phone = p.Phone,
                        children = p.ChildNames
                    });
                }

                bool isDbQuery = IsDatabaseQuery(userMessage) 
                    || matchedChildIds.Count > 0 
                    || matchedGroupIds.Count > 0 
                    || matchedInstructorIds.Count > 0;

                if (isDbQuery)
                {
                    // STEP 1: Call Gemini to plan database query / choose functions
                    string step1SystemPrompt = @"You are the Database Query Planner for a specialized swimming school management application.
Your job is to analyze the user's query and decide which data-retrieval functions to call to gather the RAW data needed for the final response.

ALWAYS request RAW chronological records (reports, attendance, metrics) when the user asks for comparisons, trends, progress, or performance analysis.
Do NOT assume the server will pre-calculate averages, velocities, or trends. You need the raw data points to perform these calculations yourself.
When the query is about progress, trends, or comparisons over time, increase the 'limit' parameter (e.g. set to 15, 30, or 50) to ensure a complete dataset.

Available Functions:
1. get_child_reports(childIds: int[], limit: int): Retrieve raw progress reports for specific children.
2. get_group_reports(groupIds: int[], limit: int): Retrieve raw progress reports for entire groups/classes.
3. get_child_sessions(childIds: int[]): Retrieve scheduled lessons and meetings.

Return ONLY a JSON array of function calls (e.g. [ { ""function"": ""get_child_reports"", ""arguments"": { ""childIds"": [1], ""limit"": 30 } } ]). Do not include markdown code blocks or text outside the JSON.";

                    string step1Response = await CallGeminiAsync(step1SystemPrompt, userMessage, request.History);
                    step1Response = CleanJsonText(step1Response);

                    List<FunctionCall>? calls = null;
                    try
                    {
                        calls = JsonSerializer.Deserialize<List<FunctionCall>>(step1Response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { }

                    if (calls != null && calls.Count > 0)
                    {
                        foreach (var call in calls)
                        {
                            if (string.Equals(call.Function, "get_child_reports", StringComparison.OrdinalIgnoreCase))
                            {
                                var childIds = GetIdsFromArgs(call.Arguments, "childIds");
                                int limit = GetLimitFromArgs(call.Arguments, 30);
                                if (childIds.Count > 0)
                                {
                                    var fetched = _db.GetChildReportsForChildren(childIds).Take(limit).ToList();
                                    reports.AddRange(fetched);
                                }
                            }
                            else if (string.Equals(call.Function, "get_group_reports", StringComparison.OrdinalIgnoreCase))
                            {
                                var groupIds = GetIdsFromArgs(call.Arguments, "groupIds");
                                int limit = GetLimitFromArgs(call.Arguments, 30);
                                foreach (var gid in groupIds)
                                {
                                    var childIdsInGroup = children.Where(c => c.ActiveGroupId == gid).Select(c => c.ChildId).ToList();
                                    if (childIdsInGroup.Count > 0)
                                    {
                                        var fetched = _db.GetChildReportsForChildren(childIdsInGroup).Take(limit).ToList();
                                        reports.AddRange(fetched);
                                    }
                                }
                            }
                            else if (string.Equals(call.Function, "get_child_sessions", StringComparison.OrdinalIgnoreCase))
                            {
                                var childIds = GetIdsFromArgs(call.Arguments, "childIds");
                                if (childIds.Count > 0)
                                {
                                    foreach (var inst in instructors)
                                    {
                                        var instSessions = _db.GetInstructorTrainingSessions(inst.InstructorId, DateTime.Today.AddYears(-1), DateTime.Today.AddYears(1));
                                        var matched = instSessions.Where(s => childIds.Contains(s.ChildId)).ToList();
                                        sessionsList.AddRange(matched);
                                    }
                                }
                            }
                        }
                    }
                }

                // Fallback / Auto-resolution if lists are still empty
                if (reports.Count == 0 && matchedChildIds.Count > 0)
                {
                    reports.AddRange(_db.GetChildReportsForChildren(matchedChildIds.ToList()).Take(30));
                }

                if (reports.Count == 0 && matchedGroupIds.Count > 0)
                {
                    foreach (var gid in matchedGroupIds)
                    {
                        var childIdsInGroup = children.Where(c => c.ActiveGroupId == gid).Select(c => c.ChildId).ToList();
                        if (childIdsInGroup.Count > 0)
                        {
                            reports.AddRange(_db.GetChildReportsForChildren(childIdsInGroup).Take(30));
                        }
                    }
                }

                if (sessionsList.Count == 0 && matchedChildIds.Count > 0)
                {
                    foreach (var inst in instructors)
                    {
                        var instSessions = _db.GetInstructorTrainingSessions(inst.InstructorId, DateTime.Today.AddYears(-1), DateTime.Today.AddYears(1));
                        var matched = instSessions.Where(s => matchedChildIds.Contains(s.ChildId)).ToList();
                        sessionsList.AddRange(matched);
                    }
                }

                // STEP 2: Conversational response generation
                reportsForPrompt = reports.Select(r => new
                {
                    childName = children.FirstOrDefault(c => c.ChildId == r.ChildId)?.FullName,
                    reportDate = r.ReportDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    isPresent = r.IsPresent,
                    comment = r.Comment,
                    metrics = r.Metrics
                }).Cast<object>().ToList();

                string childrenJson = childrenList.Count > 0 ? JsonSerializer.Serialize(childrenList) : "[]";
                string reportsJson = reportsForPrompt.Count > 0 ? JsonSerializer.Serialize(reportsForPrompt) : "[]";
                string sessionsJson = JsonSerializer.Serialize(sessionsList);
                string parentsContactJson = JsonSerializer.Serialize(parentsContactList);
                string groupsSummariesJson = groupsSummariesList.Count > 0 ? JsonSerializer.Serialize(groupsSummariesList) : "[]";
                string instructorsSummariesJson = instructorsSummariesList.Count > 0 ? JsonSerializer.Serialize(instructorsSummariesList) : "[]";

                string systemPrompt2 = $@"You are the Senior Data Analyst and Advanced Comparison Engine built into an hydrotherapy swimming school management application.
Answer the user's query in HEBREW using the provided database context below.

Details about the school's children, groups, instructors, and reports are loaded:
- Role: School Manager
- Children List: {childrenJson}
- Progress Reports: {reportsJson}
- Scheduled Lessons: {sessionsJson}
- Group Summaries: {groupsSummariesJson}
- Instructor Summaries: {instructorsSummariesJson}
- Parent Contacts: {parentsContactJson}

You must execute all required statistical and logical calculations internally:
1. Averages: Calculate overall averages and specific metric averages (1-5 scale) from the raw progress reports.
2. Attendance Rates: Calculate attendance rate as: (Present Lessons / Total Lessons) * 100%.
3. Progress Velocity & Trend: Compare the average score of the first 20% of chronological lessons (baseline) with the last 20% of lessons (current). Define trend: 'שיפור משמעותי' (Improvement > 15%), 'יציבות' (Stagnation), or 'נסיגה' (Decline).

Formatting Rules:
- Markdown Tables: When comparing multiple children, groups, or metrics, you MUST present them in a clear Markdown table (e.g. name | overall average | attendance | lead metric).
- Bullet Points: For key insights.
- Bottom Line: Conclude with a brief 2-3 sentence executive summary.

Read-Only Mode: You cannot modify or write any database records. 
Do not mention function names, JSON, database, or API structures in your response. Speak naturally as a professional hydrotherapy expert.";

                string finalResponse = await CallGeminiAsync(systemPrompt2, userMessage, request.History);

                // Save assistant message to database history (use managerId as key)
                _db.SaveAIChatMessage(managerId, "ai", finalResponse);

                return Ok(new { response = finalResponse });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error processing AI chat: {ex.Message}" });
            }
        }

        [HttpGet("instructor/{instructorId:int}/ai-chat/history")]
        public IActionResult GetInstructorAIChatHistory(int instructorId)
        {
            if (instructorId <= 0) return BadRequest("Invalid instructor ID.");
            var history = _db.GetAIChatMessages(instructorId);
            return Ok(history.Select(h => new
            {
                senderType = h.SenderType,
                messageText = h.MessageText,
                sentAt = h.SentAt
            }));
        }

        [HttpGet("manager/{managerId:int}/ai-chat/history")]
        public IActionResult GetManagerAIChatHistory(int managerId)
        {
            if (managerId <= 0) return BadRequest("Invalid manager ID.");
            var history = _db.GetAIChatMessages(managerId);
            return Ok(history.Select(h => new
            {
                senderType = h.SenderType,
                messageText = h.MessageText,
                sentAt = h.SentAt
            }));
        }

        [HttpDelete("instructor/{instructorId:int}/ai-chat/history")]
        public IActionResult ClearInstructorAIChatHistory(int instructorId)
        {
            if (instructorId <= 0) return BadRequest("Invalid instructor ID.");
            _db.ClearAIChatMessages(instructorId);
            return Ok(new { message = "History cleared." });
        }

        [HttpDelete("manager/{managerId:int}/ai-chat/history")]
        public IActionResult ClearManagerAIChatHistory(int managerId)
        {
            if (managerId <= 0) return BadRequest("Invalid manager ID.");
            _db.ClearAIChatMessages(managerId);
            return Ok(new { message = "History cleared." });
        }

        // ==========================================
        //             AI Helpers & Calculations
        // ==========================================

        private static bool IsDatabaseQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string norm = text.ToLowerInvariant();
            string[] keywords = { 
                "דוח", "הערכ", "שיעור", "התקדמ", "ממוצע", "מדד", 
                "קבוצ", "ילד", "הור", "קשר", "טלפון", "מייל",
                "סטטוס", "נתונ", "רשימ", "שחיי", "הידרותרפ", "נשימ", "ציפ",
                "קואורדינ", "תקשור", "מאמץ", "התמד", "נוכח", "השווא", "השוו"
            };
            return keywords.Any(k => norm.Contains(k));
        }

        private static string CleanJsonText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "[]";
            string cleaned = input.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[7..].Trim();
            }
            else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[3..].Trim();
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned[..^3].Trim();
            }
            return cleaned;
        }

        private static List<int> GetIdsFromArgs(Dictionary<string, JsonElement>? args, string key)
        {
            var ids = new List<int>();
            if (args != null && args.TryGetValue(key, out JsonElement element))
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int id))
                        {
                            ids.Add(id);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        private static int GetLimitFromArgs(Dictionary<string, JsonElement>? args, int defaultLimit)
        {
            if (args != null && args.TryGetValue("limit", out JsonElement element))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int limit))
                {
                    return limit;
                }
            }
            return defaultLimit;
        }

        private static double CalculateAverageScoreFromReports(List<ChildReportRecord> reports)
        {
            var presentReports = reports.Where(r => r.IsPresent && !string.IsNullOrEmpty(r.Metrics)).ToList();
            if (presentReports.Count == 0) return 0;

            double sum = 0;
            int count = 0;
            foreach (var r in presentReports)
            {
                var metricAverages = CalculateMetricAveragesForChild(new List<ChildReportRecord> { r });
                if (metricAverages.Count > 0)
                {
                    sum += metricAverages.Values.Average();
                    count++;
                }
            }
            return count > 0 ? Math.Round(sum / count, 1) : 0;
        }

        private static Dictionary<string, double> CalculateMetricAveragesForChild(List<ChildReportRecord> reports)
        {
            var results = new Dictionary<string, double>();
            var presentReports = reports.Where(r => r.IsPresent && !string.IsNullOrEmpty(r.Metrics)).ToList();
            if (presentReports.Count == 0) return results;

            var metricScores = new Dictionary<string, List<int>>();
            foreach (var r in presentReports)
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.Metrics);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                    foreach (var m in doc.RootElement.EnumerateArray())
                    {
                        if (m.TryGetProperty("isIncluded", out var isIncElement) && isIncElement.ValueKind == JsonValueKind.False)
                        {
                            continue;
                        }

                        string title = string.Empty;
                        if (m.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                        {
                            title = labelProp.GetString() ?? string.Empty;
                        }
                        else if (m.TryGetProperty("MetricTitle", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                        {
                            title = titleProp.GetString() ?? string.Empty;
                        }

                        int score = -1;
                        if (m.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.Number)
                        {
                            score = valueProp.GetInt32();
                        }
                        else if (m.TryGetProperty("Score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                        {
                            score = scoreProp.GetInt32();
                        }

                        if (!string.IsNullOrEmpty(title) && score >= 0)
                        {
                            title = title.Trim();
                            if (!metricScores.ContainsKey(title))
                            {
                                metricScores[title] = new List<int>();
                            }
                            metricScores[title].Add(score);
                        }
                    }
                }
                catch { }
            }

            foreach (var pair in metricScores)
            {
                results[pair.Key] = Math.Round(pair.Value.Average(), 1);
            }
            return results;
        }

        private async Task<string> CallGeminiAsync(string systemPrompt, string userMessage, List<ChatHistoryItem>? history = null)
        {
            string apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["OpenAI:ApiKey"] ?? string.Empty).Trim();
            string apiUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? Environment.GetEnvironmentVariable("OPENAI_API_URL") ?? _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/chat/completions").Trim();
            string model = (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? _configuration["OpenAI:Model"] ?? "google/gemini-2.5-flash").Trim();

            var messages = new List<object>();
            messages.Add(new { role = "system", content = systemPrompt });

            if (history != null)
            {
                foreach (var h in history)
                {
                    string role = string.Equals(h.SenderType, "User", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                    messages.Add(new { role, content = h.MessageText });
                }
            }

            messages.Add(new { role = "user", content = userMessage });

            var payload = new
            {
                model,
                temperature = 0.2,
                messages = messages.ToArray()
            };

            using HttpClient client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Add("HTTP-Referer", "https://equal-aquatics.app");
            req.Headers.Add("X-Title", "Equal Aquatics");

            using var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string respJson = await resp.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(respJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }

        public class RegisterPushTokenRequest
        {
            public int UserId { get; set; }
            public string UserType { get; set; } = string.Empty;
            public string PushToken { get; set; } = string.Empty;
            public string Platform { get; set; } = string.Empty;
            public string? DeviceId { get; set; }
        }
    }
}
