using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orbit
{
    public interface ILlmApiService
    {
        Task<string> CallApiAsync(string prompt);
    }

    public class OpenAiApiService : ILlmApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpointUrl;
        private readonly string _model;

        public OpenAiApiService(string apiKey, string baseUrl = "https://api.openai.com/v1", string model = "gpt-4o-mini")
        {
            _model = model;
            // BaseAddress 방식 대신 전체 URL을 직접 조립 (경로 해석 오류 방지)
            _endpointUrl = baseUrl.TrimEnd('/') + "/chat/completions";

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");

            // OpenRouter 사용 시 필수 헤더
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/orbit-app");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Orbit");
        }

        public async Task<string> CallApiAsync(string prompt)
        {
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

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(_endpointUrl, content);
            }
            catch (Exception ex)
            {
                throw new Exception($"Network error (URL: {_endpointUrl})\n{ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new Exception($"API error {(int)response.StatusCode}: {body}");
            }

            string responseJson = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
    }
}
