using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;
using GeminiMod.Models;

namespace GeminiMod.Services
{
    public class NpcInteractionService
    {
        private readonly ModConfig Config;
        private readonly IMonitor Monitor;
        private readonly AiService AiService;
        private readonly MemoryManager MemoryManager;
        private readonly ConcurrentQueue<Action> MainThreadQueue;
        private readonly IModHelper Helper;

        public NpcInteractionService(ModConfig config, IMonitor monitor, AiService aiService, MemoryManager memoryManager, ConcurrentQueue<Action> mainThreadQueue, IModHelper helper)
        {
            this.Config = config;
            this.Monitor = monitor;
            this.AiService = aiService;
            this.MemoryManager = memoryManager;
            this.MainThreadQueue = mainThreadQueue;
            this.Helper = helper;
        }

        /// <summary>Processa a interação direta com um NPC (Diálogo com retrato).</summary>
        public async Task HandleNpcDialogue(NPC npc, string playerText)
        {
            string gameContext = this.GetGameContext(npc);
            string profileContent = this.MemoryManager.GetNpcProfile(npc.Name);
            string portraitMapping = this.MemoryManager.GetNpcPortraitMapping(npc.Name);
            var memoryList = this.MemoryManager.GetNpcMemory(npc.Name);
            string memoryHistory = string.Join("\n", memoryList.TakeLast(10).Select(m => $"{m.Role}: {m.Content}"));

            this.MainThreadQueue.Enqueue(() => Game1.drawObjectDialogue(this.Helper.Translation.Get("prompt.thinking", new { name = npc.Name })));

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(this.Helper.Translation.Get("prompt.role", new { name = npc.Name }));
                sb.AppendLine(this.Helper.Translation.Get("prompt.language_instruction", new { language = this.Helper.Translation.Locale }));
                sb.AppendLine($"### {this.Helper.Translation.Get("prompt.profile")}:\n{profileContent}");
                sb.AppendLine($"### {this.Helper.Translation.Get("prompt.portraits")}:\n{portraitMapping}");
                sb.AppendLine($"### {this.Helper.Translation.Get("prompt.history")}:\n{(string.IsNullOrWhiteSpace(memoryHistory) ? this.Helper.Translation.Get("prompt.history.none") : memoryHistory)}");
                sb.AppendLine($"### {this.Helper.Translation.Get("prompt.context")}:\n{gameContext}");
                sb.AppendLine($"\n### {this.Helper.Translation.Get("prompt.player_says")}: \"{playerText}\"");
                
                sb.AppendLine(this.Helper.Translation.Get("prompt.instruction.portrait"));
                sb.AppendLine(this.Helper.Translation.Get("prompt.instruction.json"));
                sb.AppendLine(this.Helper.Translation.Get("prompt.instruction.sentiment"));

                if (!this.Config.AllowNSFW)
                    sb.AppendLine(this.Helper.Translation.Get("prompt.rule.nsfw"));

                string rawResponse = await this.AiService.GetAiResponse(sb.ToString());
                
                var (response, friendshipChange, portraitIndex) = this.ParseStructuredResponse(rawResponse);
                response = Regex.Replace(response, @"\*+", "");

                // Atualiza Memória
                string timeStamp = $"{Game1.dayOfMonth} de {Game1.currentSeason}, {Game1.getTimeOfDayString(Game1.timeOfDay)}";
                memoryList.Add(new MemoryEntry { Role = "Jogador", Content = playerText, GameTime = timeStamp });
                memoryList.Add(new MemoryEntry { Role = npc.Name, Content = response, GameTime = timeStamp });

                this.MainThreadQueue.Enqueue(() =>
                {
                    if (Context.IsWorldReady)
                    {
                        if (friendshipChange != 0)
                        {
                            Game1.player.changeFriendship(friendshipChange, npc);
                            this.Monitor.Log($"Amizade com {npc.Name} alterada: {friendshipChange} pts.", LogLevel.Debug);
                        }

                        Game1.activeClickableMenu = null;
                        npc.CurrentDialogue.Push(new Dialogue(npc, null, $"${portraitIndex}{response}"));
                        Game1.drawDialogue(npc);
                    }
                });
            }
            catch (Exception ex)
            {
                this.HandleError(ex, npc.Name);
                // Fallback para não travar o diálogo
                this.MainThreadQueue.Enqueue(() => {
                    if (Game1.activeClickableMenu == null)
                        npc.CurrentDialogue.Push(new Dialogue(npc, null, this.Helper.Translation.Get("prompt.fallback")));
                });
            }
        }

        /// <summary>Processa perguntas gerais via chat (sem retrato).</summary>
        public async Task HandleChatQuery(string userPrompt)
        {
            string gameContext = this.GetGameContext();
            this.MainThreadQueue.Enqueue(() => Game1.chatBox.addMessage(this.Helper.Translation.Get("prompt.thinking", new { name = "AI" }), Microsoft.Xna.Framework.Color.Gray));

            try
            {
                string response = await this.AiService.GetAiResponse($"{this.Helper.Translation.Get("prompt.language_instruction", new { language = this.Helper.Translation.Locale })}\n{gameContext}\n{userPrompt}");
                response = Regex.Replace(response, @"\*+", "");

                this.MainThreadQueue.Enqueue(() =>
                {
                    if (Context.IsWorldReady)
                        Game1.chatBox.addMessage($"Gemini: {response}", Microsoft.Xna.Framework.Color.LightBlue);
                });
            }
            catch (Exception ex)
            {
                this.MainThreadQueue.Enqueue(() => Game1.chatBox.addMessage($"Erro: {ex.Message}", Microsoft.Xna.Framework.Color.Red));
            }
        }

        private (string fala, int pontos, int portrait) ParseStructuredResponse(string raw)
        {
            try
            {
                var match = Regex.Match(raw, @"\{.*\}", RegexOptions.Singleline);
                string json = match.Success ? match.Value : raw;
                
                // Usamos dynamic para maior flexibilidade caso a IA mude o nome das chaves
                dynamic data = JsonConvert.DeserializeObject(json);
                string fala = (string)(data.fala ?? data.Fala ?? "");
                int pontos = (int)(data.pontos ?? data.Pontos ?? 0);
                int portrait = (int)(data.portraitIndex ?? data.PortraitIndex ?? 0);

                if (!string.IsNullOrWhiteSpace(fala))
                    return (fala, pontos, portrait);
                
                throw new Exception("JSON incompleto");
            }
            catch
            {
                // Fallback: Remove chaves, aspas e especificamente o rótulo "fala:" (case insensitive)
                string cleaned = Regex.Replace(raw, @"[\{\}\[\]""]", "");
                cleaned = Regex.Replace(cleaned, @"(?i)fala\s*:\s*", "").Trim();
                return (cleaned, 0, 0);
            }
        }

        private string GetGameContext(NPC targetNpc = null)
        {
            if (!Context.IsWorldReady) return "[Mundo não carregado]";

            string season = Game1.currentSeason;
            string weatherKey = Game1.isRaining ? "weather.raining" : (Game1.isSnowing ? "weather.snowing" : "weather.sunny");
            string weather = this.Helper.Translation.Get(weatherKey);
            string time = Game1.getTimeOfDayString(Game1.timeOfDay);
            string location = Game1.currentLocation.Name;
            
            string context = this.Helper.Translation.Get("prompt.context.format", new { 
                player = Game1.player.Name, location = location, date = Game1.dayOfMonth, 
                season = season, weather = weather, time = time 
            });

            if (targetNpc != null)
            {
                int friendship = Game1.player.friendshipData.TryGetValue(targetNpc.Name, out var friendshipData) ? friendshipData.Points : 0;
                context += " " + this.Helper.Translation.Get("prompt.context.npc_info", new { name = targetNpc.Name, friendship = friendship });
            }

            return context;
        }

        private void HandleError(Exception ex, string npcName)
        {
            string errorMsg = ex.Message.Contains("429") 
                ? this.Helper.Translation.Get("prompt.error.limit")
                : this.Helper.Translation.Get("prompt.error.generic", new { error = ex.Message });

            this.Monitor.Log($"Erro em {npcName}: {ex.Message}", LogLevel.Error);
            this.MainThreadQueue.Enqueue(() =>
            {
                if (Context.IsWorldReady) Game1.drawObjectDialogue(errorMsg);
            });
        }
    }
}