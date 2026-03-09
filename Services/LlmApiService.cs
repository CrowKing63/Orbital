using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orbital
{
    public interface ILlmApiService
    {
        Task<string> CallApiAsync(string prompt);
    }

    public class OpenAiApiService : ILlmApiService, IDisposable
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private readonly string _endpointUrl;
        private readonly string _model;
        private readonly string _authHeader;
        private bool _disposed;

        public OpenAiApiService(string apiKey, string baseUrl = "https://api.openai.com/v1", string model = "gpt-4o-mini")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL cannot be empty", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model name cannot be empty", nameof(model));

            _model = model;
            _endpointUrl = baseUrl.TrimEnd('/') + "/chat/completions";
            _authHeader = $"Bearer {apiKey.Trim()}";
        }

        public async Task<string> CallApiAsync(string prompt)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OpenAiApiService));

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 2048
            };

            string json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl)
            {
                Content = content
            };
            request.Headers.Add("Authorization", _authHeader);
            request.Headers.Add("HTTP-Referer", "https://github.com/orbit-app");
            request.Headers.Add("X-Title", "Orbital");

            HttpResponseMessage response;
            try
            {
                response = await SharedHttpClient.SendAsync(request);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Request timed out after {SharedHttpClient.Timeout.TotalSeconds}s\nEndpoint: {_endpointUrl}", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to {_endpointUrl}\nDetails: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error calling API\nEndpoint: {_endpointUrl}\nDetails: {ex.Message}", ex);
            }

            string responseBody = string.Empty;
            try
            {
                responseBody = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read response body: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorDetail = ExtractErrorMessage(responseBody);
                throw new Exception($"API returned {(int)response.StatusCode} {response.ReasonPhrase}\n{errorDetail}");
            }

            return ParseResponse(responseBody);
        }

        private static string ExtractErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return "No error details provided";

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                {
                    if (errorElement.TryGetProperty("message", out JsonElement messageElement))
                    {
                        string? message = messageElement.GetString();
                        if (!string.IsNullOrWhiteSpace(message))
                            return message;
                    }
                }
            }
            catch
            {
                // If JSON parsing fails, return raw body
            }

            return responseBody.Length > 500 ? responseBody.Substring(0, 500) + "..." : responseBody;
        }

        private static string ParseResponse(string responseJson)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseJson);
                
                if (!doc.RootElement.TryGetProperty("choices", out JsonElement choices))
                    throw new Exception("Response missing 'choices' field");

                if (choices.GetArrayLength() == 0)
                    throw new Exception("Response 'choices' array is empty");

                JsonElement firstChoice = choices[0];
                
                if (!firstChoice.TryGetProperty("message", out JsonElement message))
                    throw new Exception("Response choice missing 'message' field");

                if (!message.TryGetProperty("content", out JsonElement content))
                    throw new Exception("Response message missing 'content' field");

                string? result = content.GetString();
                if (result == null)
                    throw new Exception("Response content is null");

                return result;
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse API response as JSON: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
