using Microsoft.AspNetCore.Mvc;
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
