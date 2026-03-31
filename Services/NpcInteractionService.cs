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

        public NpcInteractionService(ModConfig config, IMonitor monitor, AiService aiService, MemoryManager memoryManager, ConcurrentQueue<Action> mainThreadQueue)
        {
            this.Config = config;
            this.Monitor = monitor;
            this.AiService = aiService;
            this.MemoryManager = memoryManager;
            this.MainThreadQueue = mainThreadQueue;
        }

        /// <summary>Processa a interação direta com um NPC (Diálogo com retrato).</summary>
        public async Task HandleNpcDialogue(NPC npc, string playerText)
        {
            string gameContext = this.GetGameContext(npc);
            string profileContent = this.MemoryManager.GetNpcProfile(npc.Name);
            string portraitMapping = this.MemoryManager.GetNpcPortraitMapping(npc.Name);
            var memoryList = this.MemoryManager.GetNpcMemory(npc.Name);
            string memoryHistory = string.Join("\n", memoryList.TakeLast(10).Select(m => $"{m.Role}: {m.Content}"));

            this.MainThreadQueue.Enqueue(() => Game1.drawObjectDialogue($"{npc.Name} está pensando..."));

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Você é o personagem {npc.Name} de Stardew Valley.");
                sb.AppendLine($"### PERFIL (YAML):\n{profileContent}");
                sb.AppendLine($"### MAPEAMENTO DE EXPRESSÕES (Portraits):\n{portraitMapping}");
                sb.AppendLine($"### HISTÓRICO RECENTE:\n{(string.IsNullOrWhiteSpace(memoryHistory) ? "Nenhuma conversa anterior." : memoryHistory)}");
                sb.AppendLine($"### CONTEXTO ATUAL:\n{gameContext}");
                sb.AppendLine($"\n### O JOGADOR DIZ: \"{playerText}\"");
                sb.AppendLine("\nINSTRUÇÃO DE PORTRAIT:");
                sb.AppendLine("- Escolha o 'portraitIndex' que MELHOR represente sua emoção na fala, baseando-se estritamente no MAPEAMENTO fornecido.");
                sb.AppendLine("- Se você estiver bravo ou insultado e o mapeamento tiver um índice para 'Bravo' ou 'Sério', use-o.");
                sb.AppendLine("- Não use índices que não existam na lista acima.");
                sb.AppendLine("Responda OBRIGATORIAMENTE seguindo este formato JSON:");
                sb.AppendLine("{");
                sb.AppendLine("  \"fala\": \"sua resposta aqui\",");
                sb.AppendLine("  \"pontos\": valor_inteiro (ex: -20 para insultos, 20 para elogios, 0 para neutro),");
                sb.AppendLine("  \"portraitIndex\": índice_numérico_da_expressão_conforme_mapeamento");
                sb.AppendLine("}");
                sb.AppendLine("\nAnalise o sentimento da fala do jogador:");
                sb.AppendLine("- Elogio/Agradável: pontos positivos (10 a 20).");
                sb.AppendLine("- Insulto/Grosseiro: pontos negativos (-10 a -20).");
                sb.AppendLine("- Neutro: 0.");

                if (!this.Config.AllowNSFW)
                    sb.AppendLine("REGRA CRÍTICA: Não gere conteúdo NSFW.");

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
                        npc.CurrentDialogue.Push(new Dialogue(npc, null, "... (estou sem palavras)"));
                });
            }
        }

        /// <summary>Processa perguntas gerais via chat (sem retrato).</summary>
        public async Task HandleChatQuery(string userPrompt)
        {
            string gameContext = this.GetGameContext();
            this.MainThreadQueue.Enqueue(() => Game1.chatBox.addMessage("Gemini está pensando...", Microsoft.Xna.Framework.Color.Gray));

            try
            {
                string response = await this.AiService.GetAiResponse(gameContext + "\nPergunta do Jogador: " + userPrompt);
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
                var data = JsonConvert.DeserializeAnonymousType(json, new { fala = "", pontos = 0, portraitIndex = 0 });
                return (data.fala, data.pontos, data.portraitIndex);
            }
            catch
            {
                return (raw, 0, 0);
            }
        }

        private string GetGameContext(NPC targetNpc = null)
        {
            if (!Context.IsWorldReady) return "[Mundo não carregado]";

            string season = Game1.currentSeason;
            string weather = Game1.isRaining ? "Chuvoso" : (Game1.isSnowing ? "Nevando" : "Ensolarado");
            string time = Game1.getTimeOfDayString(Game1.timeOfDay);
            string location = Game1.currentLocation.Name;
            
            string context = $"[Contexto: Jogador={Game1.player.Name}, Local={location}, Data={Game1.dayOfMonth} de {season}, Clima={weather}, Hora={time}]";

            if (targetNpc != null)
            {
                int friendship = Game1.player.friendshipData.TryGetValue(targetNpc.Name, out var friendshipData) ? friendshipData.Points : 0;
                context += $" [Interagindo com NPC: Nome={targetNpc.Name}, Amizade={friendship} pts]";
            }

            return context;
        }

        private void HandleError(Exception ex, string npcName)
        {
            string errorMsg = ex.Message.Contains("429") 
                ? "Limite de API atingido. Aguarde um pouco." 
                : $"Erro na IA: {ex.Message}";

            this.Monitor.Log($"Erro em {npcName}: {ex.Message}", LogLevel.Error);
            this.MainThreadQueue.Enqueue(() =>
            {
                if (Context.IsWorldReady) Game1.drawObjectDialogue(errorMsg);
            });
        }
    }
}