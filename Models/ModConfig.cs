using System;
using StardewModdingAPI;

namespace GeminiMod.Models
{
    public class ModConfig
    {
        public string ApiKey { get; set; } = "COLOQUE_SUA_CHAVE_AQUI";
        public string Model { get; set; } = "gemini-3-flash-preview";
        public string LocalLlamaUrl { get; set; } = "http://localhost:8080";
        public bool AllowNSFW { get; set; } = false;
        public float Temperature { get; set; } = 0.7f;
        public int MaxTokens { get; set; } = 200;
    }

    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string> tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string> formatValue = null, string fieldId = null);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
    }
}