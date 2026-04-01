using StardewModdingAPI;

namespace GeminiMod.Models
{
    public class ModConfig
    {
        /// <summary>Chave para a API do Google Gemini.</summary>
        public string ApiKey { get; set; } = "COLOQUE_SUA_CHAVE_AQUI";

        /// <summary>Chave para a API da OpenAI.</summary>
        public string OpenAiApiKey { get; set; } = "";

        /// <summary>Chave para a API do OpenRouter.</summary>
        public string OpenRouterApiKey { get; set; } = "";

        /// <summary>ID do modelo de IA (ex: gemini-1.5-flash, gpt-4o, etc).</summary>
        public string Model { get; set; } = "gemini-1.5-flash";

        public string LocalLlamaUrl { get; set; } = "http://localhost:8080";
        public bool AllowNSFW { get; set; } = false;
        public float Temperature { get; set; } = 0.7f;
        public int MaxTokens { get; set; } = 250;
        public SButton InteractKey { get; set; } = SButton.MouseRight;
    }
}