# G-Valley: Dynamic Dialogues

**G-Valley** é uma expansão imersiva para *Stardew Valley* que utiliza o poder da Inteligência Artificial para dar vida aos habitantes da Vila Pelicanos. Esqueça os diálogos repetitivos: agora, cada conversa é única, consciente do contexto e fiel à personalidade de cada NPC.

## 🌟 Destaques

*   **Diálogos Dinâmicos:** Converse naturalmente com os NPCs através de uma caixa de texto personalizada.
*   **Multi-Modelo:** Suporte para **Google Gemini**, **OpenAI (GPT-4o)**, **OpenRouter** (Claude, Llama 3, etc.) ou modelos locais via **llama.cpp/Ollama**.
*   **Consciência Contextual:** Os NPCs sabem o seu nome, o clima atual, a estação, onde você está e o nível de amizade entre vocês. Eles podem comentar sobre a chuva verde no verão ou sobre um festival que está acontecendo.
*   **Memória de Curto Prazo:** O mod mantém um histórico das últimas interações, permitindo que os NPCs se lembrem do fluxo da conversa atual.
*   **Sistema de Expressões (Portraits):** A IA escolhe a expressão facial correta do NPC (feliz, triste, bravo, etc.) com base no tom da resposta.
*   **Personalidades Autênticas:** Utiliza perfis detalhados em YAML para garantir que o Sr. Qi permaneça enigmático, a Jodi soe como uma mãe dedicada e o Clint mantenha sua timidez característica.
*   **Influência na Amizade:** Suas palavras têm peso! Dependendo do que você disser, sua pontuação de amizade com o NPC pode aumentar ou diminuir.

## 🛠️ Como Funciona

O mod intercepta as interações e envia um "pacote de contexto" para a IA selecionada, incluindo:
1.  **Perfil do NPC:** História, motivações e segredos.
2.  **Estado do Mundo:** Data, hora, clima e localização.
3.  **Histórico:** O que foi dito anteriormente.

A IA então responde em formato JSON, que o mod processa para exibir o diálogo e atualizar o estado do jogo em tempo real.

## ⚙️ Configuração

O arquivo `prompt_settings.json` na raiz do mod permite traduzir as instruções do sistema e ajustar como a IA deve se comportar.

```json
{
    "prompts": {
        "thinking": "{{name}} está pensando...",
        "language_instruction": "IMPORTANTE: Você DEVE responder no idioma: {{language}}"
    }
}
```

## 🔑 Como obter sua API Key

Para que o mod funcione, você precisa de uma chave, se procura um modelo gratiuto recomendo usar o modelodo Google Gemini:
1. Acesse o [Google AI Studio](https://aistudio.google.com/) ou [OpenAI](https://platform.openai.com/), [OpenRouter](https://openrouter.ai/).
2. Faça login com sua conta.
3. Clique em **"Get API key"**.
4. Copie a chave e cole no seu arquivo `config.json` (gerado após abrir o jogo pela primeira vez com o mod).

##  Requisitos

*   Stardew Valley 1.6+
*   SMAPI 4.0.0+

## 🚀 Instalação

1.  Instale o SMAPI.
2.  Extraia o conteúdo do mod para a pasta `Mods` do seu jogo.
3.  Execute o jogo uma vez para gerar o arquivo `config.json`.
4.  Abra o `config.json` e insira sua `ApiKey`.
5.  Divirta-se conversando!

## 🛠️ Solução de Problemas (FAQ)

*   **O NPC não responde:** Verifique se sua chave de API está correta no `config.json` e se você tem conexão com a internet.
*   **Erro de Limite (Quota):** O plano gratuito do Gemini tem limites de requisições por minuto. Se muitas mensagens forem enviadas seguidas, o NPC pode ficar "sem palavras" por um momento.
*   **O jogo trava ao abrir o menu:** Certifique-se de que o arquivo `prompt_settings.json` está na raiz da pasta do mod, e não dentro de `i18n`.

---
*Desenvolvido por **Zerlony**. Torne sua vida no campo muito mais viva com o poder da IA.*
