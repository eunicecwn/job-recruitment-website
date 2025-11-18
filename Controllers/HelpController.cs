using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization; 
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;

namespace HireRightPro.Controllers
{
    [Route("[controller]")]
    public class HelpController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HelpController> _logger;

        public HelpController(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<HelpController> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }
        [HttpGet]
        public IActionResult TestConnection()
        {
            var apiKey = _configuration["OpenAI:ApiKey"];
            return Json(new
            {
                message = "Controller is working",
                hasApiKey = !string.IsNullOrEmpty(apiKey),
                keyLength = apiKey?.Length ?? 0
            });
        }

        [HttpGet]
        public IActionResult GetHelpModal()
        {
            return PartialView("_HelpModal");
        }

        [HttpPost("[action]")]
       // [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendChatMessage([FromBody] ChatRequest request)
        {
            try
            {
                // Validate input
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return Json(new { success = false, error = "Message cannot be empty" });
                }

                if (request.Message.Length > 1000)
                {
                    return Json(new { success = false, error = "Message too long. Please keep it under 1000 characters." });
                }

                var apiKey = _configuration["OpenAI:ApiKey"];

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("OpenAI API key not configured");
                    return Json(new { success = false, error = "Chat service temporarily unavailable" });
                }

                var response = await CallOpenAIAPI(request.Message, apiKey);
                return Json(new { success = true, response = response });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error when calling OpenAI API");
                return Json(new { success = false, error = "Unable to connect to chat service. Please try again." });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout when calling OpenAI API");
                return Json(new { success = false, error = "Request timed out. Please try again." });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error from OpenAI API");
                return Json(new { success = false, error = "Invalid response from chat service." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SendChatMessage");
                return Json(new { success = false, error = "An unexpected error occurred. Please try again." });
            }
        }

        // NEW: Admin AI Assistant Endpoint
        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        //[Authorize(Roles = "Admin")] // Only admins can access this
        public async Task<IActionResult> SendAdminChatMessage([FromBody] ChatRequest request)
        {
            try
            {
                // Validate input
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return Json(new { success = false, error = "Message cannot be empty" });
                }

                if (request.Message.Length > 1000)
                {
                    return Json(new { success = false, error = "Message too long. Please keep it under 1000 characters." });
                }

                var apiKey = _configuration["OpenAI:ApiKey"];

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("OpenAI API key not configured");
                    return Json(new { success = false, error = "Chat service temporarily unavailable" });
                }

                var response = await CallAdminOpenAIAPI(request.Message, apiKey);
                return Json(new { success = true, response = response });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error when calling OpenAI API for admin");
                return Json(new { success = false, error = "Unable to connect to chat service. Please try again." });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout when calling OpenAI API for admin");
                return Json(new { success = false, error = "Request timed out. Please try again." });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error from OpenAI API for admin");
                return Json(new { success = false, error = "Invalid response from chat service." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SendAdminChatMessage");
                return Json(new { success = false, error = "An unexpected error occurred. Please try again." });
            }
        }

        private async Task<string> CallOpenAIAPI(string message, string apiKey)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("User-Agent", "HireRightPro/1.0");

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "system", content = GetSystemPrompt() },
                    new { role = "user", content = message }
                },
                max_tokens = 300,
                temperature = 0.7,
                top_p = 1.0,
                frequency_penalty = 0.0,
                presence_penalty = 0.0
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(requestBody, options);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"OpenAI API error: {response.StatusCode} - {errorContent}");

                throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(responseJson))
            {
                throw new InvalidOperationException("Empty response from OpenAI API");
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, options);

            if (result?.Choices == null || result.Choices.Length == 0)
            {
                throw new InvalidOperationException("No choices returned from OpenAI API");
            }

            if (result.Choices[0]?.Message?.Content == null)
            {
                throw new InvalidOperationException("Invalid message structure from OpenAI API");
            }

            return result.Choices[0].Message.Content.Trim();
        }

        // NEW: Admin-specific OpenAI API call
        private async Task<string> CallAdminOpenAIAPI(string message, string apiKey)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("User-Agent", "HireRightPro-Admin/1.0");

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "system", content = GetAdminSystemPrompt() },
                    new { role = "system", content = "Current user role: Administrator" },
                    new { role = "user", content = message }
                },
                max_tokens = 300,
                temperature = 0.7,
                top_p = 1.0,
                frequency_penalty = 0.0,
                presence_penalty = 0.0
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(requestBody, options);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"OpenAI API error (admin): {response.StatusCode} - {errorContent}");
                throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(responseJson))
            {
                throw new InvalidOperationException("Empty response from OpenAI API");
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, options);

            if (result?.Choices == null || result.Choices.Length == 0)
            {
                throw new InvalidOperationException("No choices returned from OpenAI API");
            }

            if (result.Choices[0]?.Message?.Content == null)
            {
                throw new InvalidOperationException("Invalid message structure from OpenAI API");
            }

            return result.Choices[0].Message.Content.Trim();
        }

        private string GetSystemPrompt()
        {
            return @"You are a helpful AI assistant for HireRightPro, a job portal platform. 

            Your role is to help users with:
            - Platform navigation and features
            - Job searching and application processes
            - Profile management and optimization
            - Job posting and candidate management (for employers)
            - Account settings and troubleshooting
            - General career advice and best practices
            - Privacy policies and platform rules

            Guidelines:
            - Keep responses concise (under 200 words)
            - Be professional and friendly
            - Provide specific, actionable advice
            - If you don't know something specific about the platform, be honest
            - For technical issues or account-specific problems, recommend contacting support at support@hirerightpro.com
            - Always prioritize user privacy and security

            Remember: You are representing HireRightPro, so maintain a professional tone while being helpful and approachable.";
        }

        // NEW: Admin-specific system prompt
        private string GetAdminSystemPrompt()
        {
            return @"You are an AI assistant for HireRightPro administrators. The person you are speaking with is a verified administrator with elevated privileges.

            When addressing the user:
            - Recognize them as an administrator/admin user
            - Use appropriate admin-focused language
            - Provide guidance suitable for their administrative role

            Your role is to help this administrator with:
            - System administration and management tasks
            - User account management and moderation
            - Job posting review and content moderation
            - Platform analytics and reporting guidance
            - Database and system performance monitoring
            - Security and compliance matters
            - Administrative policy interpretation
            - Troubleshooting technical issues
            - Platform configuration and settings

            Administrative Guidelines:
            - Address them as an administrator when relevant
            - Provide technical, actionable advice for admin tasks
            - Prioritize security and compliance in all recommendations  
            - Be direct and professional in tone
            - Focus on efficiency and best practices
            - Include relevant admin panel navigation when helpful
            - For complex technical issues, recommend escalation to development team
            - Always consider the impact on platform users and performance
            - Maintain confidentiality of sensitive admin information

            Remember: You are speaking directly with a platform administrator who has elevated privileges and responsibilities for maintaining the HireRightPro system.";
        }
    }

    public class ChatRequest
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class OpenAIResponse
    {
        [JsonPropertyName("choices")]
        public Choice[] Choices { get; set; } = Array.Empty<Choice>();

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    public class Choice
    {
        [JsonPropertyName("message")]
        public Message Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

}