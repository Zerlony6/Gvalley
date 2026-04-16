using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StardewModdingAPI;
using GValley.Models;

namespace GValley.Services
{
    public class MemoryManager
    {
        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        public Dictionary<string, List<MemoryEntry>> NpcMemoryCache { get; } = new();
        private readonly Dictionary<string, string> ProfileCache = new();
        private readonly Dictionary<string, string> PortraitMappingCache = new();

        public MemoryManager(IModHelper helper, IMonitor monitor)
        {
            this.Helper = helper;
            this.Monitor = monitor;
        }

        public string GetNpcProfile(string npcName)
        {
            if (this.ProfileCache.TryGetValue(npcName, out string cached))
                return cached;

            string profilePath = Path.Combine(this.Helper.DirectoryPath, "npcs", $"{npcName}.yml");
            if (File.Exists(profilePath))
            {
                string content = File.ReadAllText(profilePath);
                this.ProfileCache[npcName] = content;
                return content;
            }
            
            this.Monitor.Log($"Aviso: Perfil YAML não encontrado para {npcName} em {profilePath}. Usando fallback.", LogLevel.Warn);
            return $"Você é {npcName}, um habitante de Stardew Valley. Responda de forma amigável e imersiva.";
        }

        /// <summary>Retorna as instruções de mapeamento de portrait para um NPC específico.</summary>
        public string GetNpcPortraitMapping(string npcName)
        {
            if (this.PortraitMappingCache.TryGetValue(npcName, out string cached))
                return cached;

            // 1. Tenta carregar o arquivo específico (apenas singular)
            string mappingPath = Path.Combine(this.Helper.DirectoryPath, "npcs", $"{npcName}_portrait.txt");
            
            if (File.Exists(mappingPath))
            {
                string result = File.ReadAllText(mappingPath);
                this.PortraitMappingCache[npcName] = result;
                return result;
            }

            // 2. Tenta extrair a seção do NPC do arquivo de mapeamento central
            string mainMappingPath = Path.Combine(this.Helper.DirectoryPath, "Mapeamento de Portrait.txt");
            if (File.Exists(mainMappingPath))
            {
                try {
                    string content = File.ReadAllText(mainMappingPath);
                    // Regex para encontrar o bloco do NPC (de "NPC: NOME" até o próximo separador "---")
                    string pattern = $@"NPC:\s*{Regex.Escape(npcName)}.*?\n(.*?)(?=\n-{{10,}}|\n===|\z)";
                    var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    
                    if (match.Success)
                    {
                        string result = match.Groups[1].Value.Trim();
                        this.PortraitMappingCache[npcName] = result;
                        return result;
                    }
                } catch (Exception ex) {
                    this.Monitor.Log($"Erro ao ler mapeamento central: {ex.Message}", LogLevel.Debug);
                }
            }

            // Fallback genérico caso o mapeamento específico não exista
            string fallback = @"
            0: Neutro
            1: Feliz
            2: Triste
            3: Bravo/Irritado
            ";
            this.PortraitMappingCache[npcName] = fallback;
            return fallback;
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