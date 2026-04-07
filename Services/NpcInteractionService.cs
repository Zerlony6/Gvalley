using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Input;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using GeminiMod.Models;
using GeminiMod.UI;

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
        private readonly dynamic PromptSettings;

        /// <summary>Portrait do jogador para ser reutilizado no loop do menu.</summary>
        public Texture2D PlayerPortrait { get; set; }

        /// <summary>Cache para as propriedades de reflexão usadas no FormatPrompt.</summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

        public NpcInteractionService(ModConfig config, IMonitor monitor, AiService aiService, MemoryManager memoryManager, ConcurrentQueue<Action> mainThreadQueue, IModHelper helper)
        {
            this.Config = config;
            this.Monitor = monitor;
            this.AiService = aiService;
            this.MemoryManager = memoryManager;
            this.MainThreadQueue = mainThreadQueue;
            this.Helper = helper;

            const string fileName = "prompt_settings.json";
            string fullPath = Path.Combine(helper.DirectoryPath, fileName);

            if (!File.Exists(fullPath))
            {
                this.Monitor.Log($"ERRO CRÍTICO: O arquivo {fileName} deve estar na RAIZ do mod (ao lado do manifest.json). Caminho esperado: {fullPath}", LogLevel.Error);
            }

            this.PromptSettings = helper.Data.ReadJsonFile<dynamic>(fileName);
            
            if (this.PromptSettings == null || this.PromptSettings.prompts == null)
            {
                this.Monitor.Log($"Arquivo {fileName} está CORROMPIDO ou com erro de sintaxe JSON!", LogLevel.Error);
            }
        }

        /// <summary>Processa a interação direta com um NPC (Diálogo com retrato).</summary>
        public async Task HandleNpcDialogue(NPC npc, string playerText)
        {
            string gameContext = this.GetGameContext(npc);
            string profileContent = this.MemoryManager.GetNpcProfile(npc.Name);
            string portraitMapping = this.MemoryManager.GetNpcPortraitMapping(npc.Name);
            var memoryList = this.MemoryManager.GetNpcMemory(npc.Name);
            string memoryHistory = string.Join("\n", memoryList.TakeLast(10).Select(m => $"{m.Role}: {m.Content}"));

            if (this.PromptSettings == null || this.PromptSettings.prompts == null)
            {
                this.Monitor.Log("Não é possível iniciar o diálogo: PromptSettings não carregado.", LogLevel.Error);
                return;
            }

            this.MainThreadQueue.Enqueue(() => Game1.drawObjectDialogue(this.FormatPrompt((string)this.PromptSettings.prompts.thinking, new { name = npc.Name })));

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(this.FormatPrompt((string)this.PromptSettings.prompts.role, new { name = npc.Name }))
                  .AppendLine(this.FormatPrompt((string)this.PromptSettings.prompts.language_instruction, new { language = this.Helper.Translation.Locale }));
                sb.AppendLine($"### {this.PromptSettings.prompts.profile}:\n{profileContent}");
                sb.AppendLine($"### {this.PromptSettings.prompts.portraits}:\n{portraitMapping}");
                sb.AppendLine($"### {this.PromptSettings.prompts.history}:\n{(string.IsNullOrWhiteSpace(memoryHistory) ? this.PromptSettings.prompts.history_none : memoryHistory)}");
                sb.AppendLine($"### {this.PromptSettings.prompts.instructions}");
                sb.AppendLine($"### {this.PromptSettings.prompts.context}: {gameContext}");
                sb.AppendLine($"\n### {this.PromptSettings.prompts.player_says}: \"{playerText}\"");

                if (!this.Config.AllowNSFW)
                    sb.AppendLine((string)this.PromptSettings.prompts.rule_nsfw);

                string rawResponse = await this.AiService.GetAiResponse(sb.ToString());
                
                var (response, friendshipChange, portraitIndex) = this.ParseStructuredResponse(rawResponse);
                response = Regex.Replace(response, @"\*+", "");

                // Atualiza Memória
                string timeStamp = $"{Game1.dayOfMonth} de {Game1.currentSeason}, {Game1.getTimeOfDayString(Game1.timeOfDay)}";
                memoryList.Add(new MemoryEntry { Role = "Jogador", Content = playerText, GameTime = timeStamp });
                memoryList.Add(new MemoryEntry { Role = npc.Name, Content = response, GameTime = timeStamp });

                this.MainThreadQueue.Enqueue(() =>
                {
                    // Verifica se o jogador fechou a caixa de "pensando" ou abriu outro menu enquanto a IA processava
                    // Se o menu atual não for uma DialogueBox, o jogador provavelmente apertou ESC ou saiu de perto.
                    if (!(Game1.activeClickableMenu is StardewValley.Menus.DialogueBox))
                    {
                        this.Monitor.Log($"Interação com {npc.Name} cancelada pelo usuário durante o processamento da IA.", LogLevel.Debug);
                        return;
                    }

                    if (Context.IsWorldReady)
                    {
                        if (friendshipChange != 0)
                        {
                            Game1.player.changeFriendship(friendshipChange, npc);
                            this.Monitor.Log($"Amizade com {npc.Name} alterada: {friendshipChange} pts.", LogLevel.Debug);
                        }
                        
                        var dialogueResponse = new Dialogue(npc, null, $"${portraitIndex}{response}");

                        // Define a ação de retorno: abrir o menu de texto novamente após o diálogo fechar
                        dialogueResponse.onFinish = () =>
                        {
                            // Verifica se o encerramento foi forçado pelo ESC. 
                            // Checamos o estado atual e o frame anterior (oldKBState) para não perder o momento do clique.
                            bool escapePressed = Game1.input.GetKeyboardState().IsKeyDown(Keys.Escape) || 
                                               Game1.oldKBState.IsKeyDown(Keys.Escape);
                            
                            // Se o jogador se afastou muito (mais de 3 tiles), encerramos o loop por movimento.
                            bool playerMovedAway = npc != null && (Game1.player.Tile - npc.Tile).Length() > 3f;

                            if (escapePressed || playerMovedAway)
                                return;

                            this.MainThreadQueue.Enqueue(() =>
                            {
                                // Só reabre se o jogador estiver livre e nenhum outro menu (como inventário) tiver sido aberto.
                                if (!Context.IsPlayerFree || (Game1.activeClickableMenu != null && !(Game1.activeClickableMenu is StardewValley.Menus.DialogueBox)))
                                    return;

                                Game1.activeClickableMenu = new PlayerDialogueMenu(this.Helper, npc, this.PlayerPortrait, nextText =>
                                {
                                    if (!string.IsNullOrWhiteSpace(nextText))
                                        Task.Run(() => this.HandleNpcDialogue(npc, nextText));
                                });
                            });
                        };

                        npc.CurrentDialogue.Push(dialogueResponse);
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
                        npc.CurrentDialogue.Push(new Dialogue(npc, null, (string)this.PromptSettings.prompts.fallback));
                });
            }
        }

        private string FormatPrompt(string template, object tokens)
        {
            if (string.IsNullOrEmpty(template) || tokens == null) return template ?? "";

            string output = template;
            Type type = tokens.GetType();

            // Obtém as propriedades do cache ou as adiciona se não existirem
            if (!PropertyCache.TryGetValue(type, out PropertyInfo[] props))
            {
                props = type.GetProperties();
                PropertyCache[type] = props;
            }

            foreach (var prop in props)
            {
                output = output.Replace("{{" + prop.Name + "}}", prop.GetValue(tokens)?.ToString() ?? "");
            }
            return output;
        }

        /// <summary>Processa perguntas gerais via chat (sem retrato).</summary>
        public async Task HandleChatQuery(string userPrompt)
        {
            if (this.PromptSettings == null) return;
            string gameContext = this.GetGameContext();
            this.MainThreadQueue.Enqueue(() => Game1.chatBox.addMessage(this.FormatPrompt((string)this.PromptSettings.prompts.thinking, new { name = "AI" }), Microsoft.Xna.Framework.Color.Gray));

            try
            {
                string langInstr = this.FormatPrompt((string)this.PromptSettings.prompts.language_instruction, new { language = this.Helper.Translation.Locale });
                string response = await this.AiService.GetAiResponse($"{langInstr}\n{gameContext}\n{userPrompt}");
                response = Regex.Replace(response, @"\*+", "");

                this.MainThreadQueue.Enqueue(() =>
                {
                    if (Context.IsWorldReady)
                        Game1.chatBox.addMessage($"Gemini: {response}", Microsoft.Xna.Framework.Color.LightBlue);
                });
            }
            catch (Exception ex)
            {
                string errorMsg = this.Helper.Translation.Get("error.generic", new { error = ex.Message });
                this.MainThreadQueue.Enqueue(() => Game1.chatBox.addMessage(errorMsg, Microsoft.Xna.Framework.Color.Red));
            }
        }

        private (string fala, int pontos, int portrait) ParseStructuredResponse(string raw)
        {
            try
            {
                string json = Regex.Match(raw, @"\{.*\}", RegexOptions.Singleline).Value;
                var data = JsonConvert.DeserializeObject<dynamic>(!string.IsNullOrEmpty(json) ? json : raw);
                
                return (
                    (string)(data.fala ?? data.Fala ?? raw),
                    (int)(data.pontos ?? data.Pontos ?? 0),
                    (int)(data.portraitIndex ?? data.PortraitIndex ?? 0)
                );
            }
            catch
            {
                return (Regex.Replace(raw, @"[\{\}\[\]""]|(?i)fala\s*:\s*", "").Trim(), 0, 0);
            }
        }

        private string GetGameContext(NPC targetNpc = null)
        {
            if (this.PromptSettings == null) return "[Erro: Configurações de Prompt Ausentes]";
            if (!Context.IsWorldReady) return "[Mundo não carregado]";

            string season = Game1.currentSeason;
            
            // Lógica expandida para detecção de clima
            string weatherKey = "sunny";
            if (Utility.isFestivalDay(Game1.dayOfMonth, Game1.season)) weatherKey = "festival";
            else if (Game1.isGreenRain) weatherKey = "green_rain";
            else if (Game1.isLightning) weatherKey = "stormy";
            else if (Game1.isRaining) weatherKey = "raining";
            else if (Game1.isSnowing) weatherKey = "snowing";
            else if (Game1.isDebrisWeather) weatherKey = season == "spring" ? "windy_spring" : "windy_fall";

            string weather = (string)this.PromptSettings.weather[weatherKey];
            string time = Game1.getTimeOfDayString(Game1.timeOfDay);
            string location = Game1.currentLocation.Name;
            int friendship = targetNpc != null ? (Game1.player.friendshipData.TryGetValue(targetNpc.Name, out var f) ? f.Points : 0) : 0;
            
            return this.FormatPrompt((string)this.PromptSettings.prompts.full_context, new { 
                player = Game1.player.Name, location = location, date = Game1.dayOfMonth, 
                season = season, weather = weather, time = time, name = targetNpc?.Name ?? "N/A", friendship = friendship
            });
        }

        private void HandleError(Exception ex, string npcName)
        {
            string errorKey = ex.Message.Contains("429") ? "error.limit" : "error.generic";
            string errorMsg = this.Helper.Translation.Get(errorKey, new { error = ex.Message });

            this.Monitor.Log($"Erro em {npcName}: {ex.Message}", LogLevel.Error);
            this.MainThreadQueue.Enqueue(() =>
            {
                if (Context.IsWorldReady) Game1.drawObjectDialogue(errorMsg);
            });
        }
    }
}