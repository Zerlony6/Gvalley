using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using StardewModdingAPI;
using GeminiMod.Models;

namespace GeminiMod.Services
{
    public class MemoryManager
    {
        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        public Dictionary<string, List<MemoryEntry>> NpcMemoryCache { get; } = new();

        public MemoryManager(IModHelper helper, IMonitor monitor)
        {
            this.Helper = helper;
            this.Monitor = monitor;
        }

        public string GetNpcProfile(string npcName)
        {
            string profilePath = Path.Combine(this.Helper.DirectoryPath, "npcs", $"{npcName}.yml");
            return File.Exists(profilePath) ? File.ReadAllText(profilePath) : "Personalidade padrão.";
        }

        public List<MemoryEntry> GetNpcMemory(string npcName)
        {
            if (this.NpcMemoryCache.TryGetValue(npcName, out var cached))
                return cached;

            string memoryPath = Path.Combine(this.Helper.DirectoryPath, "npcs", $"{npcName}_memoria.json");
            List<MemoryEntry> memory = new();

            if (File.Exists(memoryPath))
            {
                try {
                    memory = JsonConvert.DeserializeObject<List<MemoryEntry>>(File.ReadAllText(memoryPath)) ?? new();
                } catch {
                    this.Monitor.Log($"Falha ao ler memória de {npcName}.", LogLevel.Warn);
                }
            }

            this.NpcMemoryCache[npcName] = memory;
            return memory;
        }

        public void SaveAllToDisk()
        {
            string npcFolderPath = Path.Combine(this.Helper.DirectoryPath, "npcs");
            foreach (var entry in this.NpcMemoryCache)
            {
                try {
                    string memoryPath = Path.Combine(npcFolderPath, $"{entry.Key}_memoria.json");
                    File.WriteAllText(memoryPath, JsonConvert.SerializeObject(entry.Value, Formatting.Indented));
                } catch (Exception ex) {
                    this.Monitor.Log($"Erro ao salvar memória de {entry.Key}: {ex.Message}", LogLevel.Error);
                }
            }
        }
    }
}