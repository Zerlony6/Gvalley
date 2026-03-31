using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StardewModdingAPI;
using GeminiMod.Models;

namespace GeminiMod.Services
{
    public class AiService
    {
        private readonly ModConfig Config;
        private readonly IMonitor Monitor;
        private static readonly HttpClient HttpClient = new HttpClient();

        public AiService(ModConfig config, IMonitor monitor)
        {
            this.Config = config;
            this.Monitor = monitor;
        }

        /// <summary>Envia o prompt para o provedor de IA adequado com base no modelo selecionado.</summary>
        public async Task<string> GetAiResponse(string prompt)
        {
            string model = this.Config.Model?.ToLower() ?? "";

            try
            {
                // 1. Prioridade: OpenRouter (Geralmente modelos com '/' no nome)
                if (!string.IsNullOrWhiteSpace(this.Config.OpenRouterApiKey) && model.Contains("/"))
                {
                    return await this.CallOpenAiCompatibleApi("https://openrouter.ai/api/v1/chat/completions", this.Config.OpenRouterApiKey, prompt, true);
                }

                // 2. OpenAI Nativo
                if (!string.IsNullOrWhiteSpace(this.Config.OpenAiApiKey) && (model.Contains("gpt") || model.Contains("o1")))
                {
                    return await this.CallOpenAiCompatibleApi("https://api.openai.com/v1/chat/completions", this.Config.OpenAiApiKey, prompt);
                }

                // 3. Google Gemini Nativo
                if (!string.IsNullOrWhiteSpace(this.Config.ApiKey) && model.Contains("gemini"))
                {
                    return await this.CallGeminiApi(prompt);
                }

                // 4. Local Llama (Fallback ou URL preenchida)
                if (!string.IsNullOrWhiteSpace(this.Config.LocalLlamaUrl))
                {
                    string endpoint = $"{this.Config.LocalLlamaUrl.TrimEnd('/')}/v1/chat/completions";
                    return await this.CallOpenAiCompatibleApi(endpoint, "no-key", prompt);
                }

                throw new Exception("Nenhum provedor configurado para este modelo. Verifique as chaves de API e o nome do modelo.");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Erro no AiService: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private async Task<string> CallGeminiApi(string prompt)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{this.Config.Model}:generateContent?key={this.Config.ApiKey}";
            
            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { 
                    temperature = this.Config.Temperature,
                    maxOutputTokens = this.Config.MaxTokens
                }
            };

            string json = JsonConvert.SerializeObject(requestBody);
            var response = await HttpClient.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) throw new Exception($"Google API Erro: {response.StatusCode} - {responseJson}");

            dynamic result = JsonConvert.DeserializeObject(responseJson);
            return result.candidates[0].content.parts[0].text;
        }

        private async Task<string> CallOpenAiCompatibleApi(string endpoint, string apiKey, string prompt, bool isOpenRouter = false)
        {
            var requestBody = new
            {
                model = this.Config.Model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = this.Config.Temperature,
                max_tokens = this.Config.MaxTokens
            };

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            if (isOpenRouter)
            {
                // Requisito do OpenRouter para identificação do mod
                request.Headers.Add("HTTP-Referer", "https://github.com/zerlony/Gvalley");
                request.Headers.Add("X-Title", "Stardew Valley Gvalley Mod");
            }

            request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            
            var response = await HttpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) throw new Exception($"API Erro: {response.StatusCode} - {responseJson}");

            dynamic result = JsonConvert.DeserializeObject(responseJson);
            return result.choices[0].message.content;
        }
    }
}