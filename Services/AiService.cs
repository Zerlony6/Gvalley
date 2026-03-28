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
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public AiService(ModConfig config, IMonitor monitor)
        {
            this.Config = config;
            this.Monitor = monitor;
        }

        public async Task<string> GetAiResponse(string prompt)
        {
            if (this.Config.Model == "Local Llama (llama.cpp)")
            {
                var url = $"{this.Config.LocalLlamaUrl.TrimEnd('/')}/v1/chat/completions";

                var requestBody = new
                {
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = this.Config.Temperature,
                    max_tokens = this.Config.MaxTokens
                };

                string json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                string responseJson = await response.Content.ReadAsStringAsync();
                
                this.Monitor.Log($"Resposta bruta (Local Llama): {responseJson}", LogLevel.Trace);
                response.EnsureSuccessStatusCode();

                var data = JsonConvert.DeserializeObject<LocalLlamaResponse>(responseJson);
                return data?.Choices?[0]?.Message?.Content ?? "O Llama local não retornou texto.";
            }
            else
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{this.Config.Model}:generateContent?key={this.Config.ApiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    },
                    generationConfig = new
                    {
                        temperature = this.Config.Temperature,
                        maxOutputTokens = this.Config.MaxTokens
                    }
                };

                string json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                string responseJson = await response.Content.ReadAsStringAsync();

                this.Monitor.Log($"Resposta bruta da API: {responseJson}", LogLevel.Trace);
                response.EnsureSuccessStatusCode();

                var data = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);
                return data?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "O Gemini não retornou texto.";
            }
        }
    }
}