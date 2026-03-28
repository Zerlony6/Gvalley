using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using GeminiMod.Models;
using GeminiMod.Services;
using GeminiMod.UI;

namespace GeminiMod
{
    public class ModEntry : Mod
    {
        /// <summary>Configurações do mod, como a API Key.</summary>
        private ModConfig Config;

        private readonly ConcurrentQueue<Action> MainThreadActions = new();

        private Texture2D PlayerPortrait;
        private AiService AiService;
        private MemoryManager MemoryManager;

        public override void Entry(IModHelper helper)
        {
            this.Config = this.Helper.ReadConfig<ModConfig>();
            this.AiService = new AiService(this.Config, this.Monitor);
            this.MemoryManager = new MemoryManager(this.Helper, this.Monitor);

            this.InitializeDirectories();
            this.LoadPlayerPortrait();
            
            helper.Events.GameLoop.GameLaunched += (sender, e) => this.RegisterConfigMenu();

            // Registra o comando no console/chat do jogo
            helper.ConsoleCommands.Add("ask_gemini", "Envia uma mensagem para o Gemini. Uso: ask_gemini <mensagem>", this.OnAskGemini);
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.UpdateTicked += (sender, e) =>
            {
                while (this.MainThreadActions.TryDequeue(out Action action))
                    action();
            };
        }

        private void InitializeDirectories()
        {
            Directory.CreateDirectory(Path.Combine(this.Helper.DirectoryPath, "npcs"));
            Directory.CreateDirectory(Path.Combine(this.Helper.DirectoryPath, "portrait"));
        }

        private void LoadPlayerPortrait()
        {
            string playerPngPath = Path.Combine(this.Helper.DirectoryPath, "portrait", "player.png");
            if (File.Exists(playerPngPath))
            {
                try {
                    this.PlayerPortrait = Texture2D.FromFile(Game1.graphics.GraphicsDevice, playerPngPath);
                } catch (Exception ex) {
                    this.Monitor.Log($"Erro ao carregar portrait/player.png: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private void RegisterConfigMenu()
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null) return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Chave da API Gemini",
                getValue: () => this.Config.ApiKey,
                setValue: value => this.Config.ApiKey = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Modelo da IA",
                tooltip: () => "Ex: gemini-3-flash-preview, etc.",
                getValue: () => this.Config.Model,
                setValue: value => this.Config.Model = value,
                allowedValues: new[] { 
                    "gemini-3-flash-preview", 
                    "gemini-3.1-flash-lite-preview", 
                    "gemini-2.5-flash", 
                    "gemini-2.5-flash-lite",
                    "Local Llama (llama.cpp)"
                }
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "URL Local (Llama)",
                tooltip: () => "URL do servidor llama.cpp (ex: http://localhost:8080)",
                getValue: () => this.Config.LocalLlamaUrl,
                setValue: value => this.Config.LocalLlamaUrl = value
            );

            configMenu.AddSectionTitle(this.ModManifest, () => "Personalização da IA");

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Permitir Conteúdo NSFW",
                tooltip: () => "Habilita ou desabilita interações de cunho adulto/explícito.",
                getValue: () => this.Config.AllowNSFW,
                setValue: value => this.Config.AllowNSFW = value
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => "Criatividade (Temperature)",
                tooltip: () => "Valores altos (ex: 0.9) tornam a IA mais criativa, valores baixos (ex: 0.2) mais objetiva.",
                getValue: () => this.Config.Temperature,
                setValue: value => this.Config.Temperature = value,
                min: 0.1f, max: 1.5f, interval: 0.1f
            );
        }

        /// <summary>Captura o estado atual do jogo para dar contexto à IA.</summary>
        private string GetGameContext(NPC targetNpc = null)
        {
            if (!Context.IsWorldReady) return "[Mundo não carregado]";

            string season = Game1.currentSeason;
            string weather = "Ensolarado";
            if (Game1.isRaining) weather = "Chuvoso";
            if (Game1.isSnowing) weather = "Nevando";
            if (Game1.isLightning) weather = "Tempestade";
            if (Game1.isDebrisWeather) weather = "Ventando";

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

        private void OnAskGemini(string command, string[] args)
        {
            string query = string.Join(" ", args);
            this.ProcessGeminiQuery(query);
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Se o menu de diálogo já estiver aberto, ignore novos botões para evitar reiniciar a conversa
            if (Game1.activeClickableMenu is PlayerDialogueMenu)
                return;

            // Ferramenta de Captura de Texto (Monitor de Chat)
            if (e.Button == SButton.Enter && Game1.activeClickableMenu is StardewValley.Menus.ChatBox chatMenu)
            {
                try 
                {
                    // MAPEAMENTO DO CAMINHO:
                    // Game1.activeClickableMenu (Menu de Chat) 
                    //  -> Campo Privado 'chatBox' (Componente TextBox)
                    //    -> Propriedade 'Text' (Conteúdo digitado)
                    var textBoxField = this.Helper.Reflection.GetField<StardewValley.Menus.TextBox>(chatMenu, "chatBox");
                    string capturedText = textBoxField.GetValue()?.Text;

                    if (!string.IsNullOrWhiteSpace(capturedText))
                    {
                        this.Monitor.Log($"[Captura] Texto detectado: \"{capturedText}\"", LogLevel.Info);
                        this.Monitor.Log($"[Mapeamento] Path: ChatBox -> field:chatBox -> property:Text", LogLevel.Debug);
                        
                        // Processamento livre: Se não for um comando do sistema (/), o Gemini assume
                        if (!capturedText.StartsWith("/") || capturedText.StartsWith("/ask_gemini"))
                        {
                            string cleanQuery = capturedText.StartsWith("/") ? capturedText.Substring(capturedText.IndexOf(" ")).Trim() : capturedText;
                            this.ProcessGeminiQuery(cleanQuery);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Erro na captura de texto: {ex.Message}", LogLevel.Error);
                }
                return;
            }

            if (!e.Button.IsActionButton()) return;

            // Lógica de detecção de NPC (já existente)
            Vector2 tile = e.Cursor.GrabTile;
            NPC targetNpc = Game1.currentLocation.characters.FirstOrDefault(c => c.Tile == tile);

            if (targetNpc != null)
            {
                this.Monitor.Log($"Interação detectada com {targetNpc.Name}. Estado do jogo capturado.", LogLevel.Debug);
                
                // Suprime o clique original para evitar que o diálogo padrão do jogo abra
                this.Helper.Input.Suppress(e.Button);

                // Abre o novo menu de diálogo customizado sem limites de caracteres do NamingMenu
                Game1.activeClickableMenu = new PlayerDialogueMenu(this.Helper, targetNpc, this.PlayerPortrait, text =>
                {
                    if (!string.IsNullOrWhiteSpace(text))
                        this.ProcessNpcDialogue(targetNpc, text);
                });
            }
        }

        private void ProcessGeminiQuery(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt)) return;

            bool isLocal = this.Config.Model == "Local Llama (llama.cpp)";
            if (!isLocal && (string.IsNullOrWhiteSpace(this.Config.ApiKey) || this.Config.ApiKey == "COLOQUE_SUA_CHAVE_AQUI"))
            {
                this.Monitor.Log("Você precisa configurar sua API Key no arquivo config.json!", LogLevel.Error);
                return;
            }

            string gameContext = this.GetGameContext();
            
            if (Context.IsWorldReady)
            {
                Game1.chatBox.addMessage($"Você: {userPrompt}", Microsoft.Xna.Framework.Color.Yellow);
                Game1.chatBox.addMessage("Gemini está pensando...", Microsoft.Xna.Framework.Color.Gray);
            }
            else {
                this.Monitor.Log($"Mensagem enviada (Mundo não carregado): {userPrompt}", LogLevel.Info);
            }
            string provider = this.Config.Model == "Local Llama (llama.cpp)" ? "Local Llama" : "Gemini";
            this.Monitor.Log($"Prompt enviado ao {provider} (Chat): {gameContext}", LogLevel.Debug);

            // Executa a chamada de API de forma assíncrona para não travar o jogo
            Task.Run(async () =>
            {
                try
                {
                    string response = await this.AiService.GetAiResponse(gameContext + "\nPergunta do Jogador: " + userPrompt);
                    
                    // Remove Markdown (como **texto**) que o chat do jogo não suporta
                    response = Regex.Replace(response, @"\*+", "");

                    this.MainThreadActions.Enqueue(() =>
                    {
                        if (Context.IsWorldReady)
                            Game1.chatBox.addMessage($"Gemini: {response}", Microsoft.Xna.Framework.Color.LightBlue);
                        this.Monitor.Log($"Resposta enviada ao chat: {response}", LogLevel.Info);
                    });
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Erro Gemini: {ex.Message}";
                    this.MainThreadActions.Enqueue(() =>
                    {
                        if (Context.IsWorldReady)
                            Game1.chatBox.addMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                        this.Monitor.Log(errorMsg, LogLevel.Error);
                    });
                }
            });
        }

        private void ProcessNpcDialogue(NPC npc, string playerText)
        {
            string gameContext = this.GetGameContext(npc);
            string profileContent = this.MemoryManager.GetNpcProfile(npc.Name);
            var memoryList = this.MemoryManager.GetNpcMemory(npc.Name);

            string memoryHistory = string.Join("\n", memoryList.TakeLast(10).Select(m => $"{m.Role}: {m.Content}"));

            Game1.drawObjectDialogue($"{npc.Name} está pensando...");

            Task.Run(async () =>
            {
                try
                {
                    // Constrói o prompt com a Persona do NPC
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Você é o personagem {npc.Name} de Stardew Valley.");
                    sb.AppendLine($"### PERFIL (YAML):\n{profileContent}");
                    sb.AppendLine($"### HISTÓRICO RECENTE:\n{(string.IsNullOrWhiteSpace(memoryHistory) ? "Nenhuma conversa anterior." : memoryHistory)}");
                    sb.AppendLine($"### CONTEXTO ATUAL:\n{gameContext}");
                    sb.AppendLine($"\n### O JOGADOR DIZ: \"{playerText}\"");
                    sb.AppendLine("\nResponda ao jogador de forma imersiva e curta.");

                    if (!this.Config.AllowNSFW)
                        sb.AppendLine("REGRA CRÍTICA: Não gere conteúdo sexualmente explícito, pornográfico ou NSFW.");

                    string finalPrompt = sb.ToString();
                    this.Monitor.Log($"Prompt final construído para {npc.Name}:\n{finalPrompt}", LogLevel.Debug);

                    string response = await this.AiService.GetAiResponse(finalPrompt);

                    // Limpa a resposta para o formato do jogo
                    response = Regex.Replace(response, @"\*+", "");

                    // Adiciona a nova fala à memória e salva no arquivo
                    memoryList.Add(new MemoryEntry { 
                        Role = "Jogador", 
                        Content = playerText, 
                        GameTime = $"{Game1.dayOfMonth} de {Game1.currentSeason}, {Game1.getTimeOfDayString(Game1.timeOfDay)}" 
                    });

                    memoryList.Add(new MemoryEntry { 
                        Role = npc.Name, 
                        Content = response, 
                        GameTime = $"{Game1.dayOfMonth} de {Game1.currentSeason}, {Game1.getTimeOfDayString(Game1.timeOfDay)}" 
                    });

                    this.MainThreadActions.Enqueue(() =>
                    {
                        if (Context.IsWorldReady)
                        {
                            // Fecha a caixa de "pensando" e abre o diálogo real com retrato
                            Game1.activeClickableMenu = null;
                            npc.CurrentDialogue.Push(new Dialogue(npc, null, response));
                            Game1.drawDialogue(npc);
                        }
                    });
                }
                catch (Exception ex)
                {
                    string errorMsg = ex.Message.Contains("429") 
                        ? "Limite de uso da API atingido. Aguarde um pouco ou troque o modelo no menu." 
                        : $"Erro na IA: {ex.Message}";

                    this.Monitor.Log($"Erro ao gerar diálogo para {npc.Name}: {ex.Message}", LogLevel.Error);
                    this.MainThreadActions.Enqueue(() =>
                    {
                        if (Context.IsWorldReady)
                            Game1.drawObjectDialogue(errorMsg);
                    });
                }
            });
        }

        /// <summary>Evento disparado quando o jogo começa a salvar (ao dormir).</summary>
        private void OnSaving(object sender, SavingEventArgs e)
        {
            if (this.MemoryManager.NpcMemoryCache.Count == 0) return;

            this.Monitor.Log("Sincronizando memórias dos NPCs com o arquivo de salvamento...", LogLevel.Info);
            this.MemoryManager.SaveAllToDisk();
            this.MemoryManager.NpcMemoryCache.Clear();
        }
    }
}
