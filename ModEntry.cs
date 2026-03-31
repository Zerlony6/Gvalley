using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        private NpcInteractionService InteractionService;

        public override void Entry(IModHelper helper)
        {
            this.Config = this.Helper.ReadConfig<ModConfig>();
            this.AiService = new AiService(this.Config, this.Monitor);
            this.MemoryManager = new MemoryManager(this.Helper, this.Monitor);
            this.InteractionService = new NpcInteractionService(this.Config, this.Monitor, this.AiService, this.MemoryManager, this.MainThreadActions);

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
                name: () => this.Helper.Translation.Get("config.api-key-gemini.name"),
                getValue: () => this.Config.ApiKey,
                setValue: value => this.Config.ApiKey = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.api-key-openai.name"),
                getValue: () => this.Config.OpenAiApiKey,
                setValue: value => this.Config.OpenAiApiKey = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.api-key-openrouter.name"),
                getValue: () => this.Config.OpenRouterApiKey,
                setValue: value => this.Config.OpenRouterApiKey = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.model.name"),
                tooltip: () => this.Helper.Translation.Get("config.model.tooltip"),
                getValue: () => this.Config.Model,
                setValue: value => this.Config.Model = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.local-url.name"),
                tooltip: () => this.Helper.Translation.Get("config.local-url.tooltip"),
                getValue: () => this.Config.LocalLlamaUrl,
                setValue: value => this.Config.LocalLlamaUrl = value
            );

            configMenu.AddSectionTitle(this.ModManifest, () => this.Helper.Translation.Get("config.section-customization.title"));

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.nsfw.name"),
                tooltip: () => this.Helper.Translation.Get("config.nsfw.tooltip"),
                getValue: () => this.Config.AllowNSFW,
                setValue: value => this.Config.AllowNSFW = value
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.temperature.name"),
                tooltip: () => this.Helper.Translation.Get("config.temperature.tooltip"),
                getValue: () => this.Config.Temperature,
                setValue: value => this.Config.Temperature = value,
                min: 0.1f, max: 1.5f, interval: 0.1f
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("config.max-tokens.name"),
                tooltip: () => this.Helper.Translation.Get("config.max-tokens.tooltip"),
                getValue: () => this.Config.MaxTokens,
                setValue: value => this.Config.MaxTokens = value,
                min: 50, max: 1000, interval: 50
            );
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
                            Task.Run(() => this.InteractionService.HandleChatQuery(cleanQuery));
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
                        Task.Run(() => this.InteractionService.HandleNpcDialogue(targetNpc, text));
                });
            }
        }

        private void ProcessGeminiQuery(string userPrompt)
        {
            Task.Run(() => this.InteractionService.HandleChatQuery(userPrompt));
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
