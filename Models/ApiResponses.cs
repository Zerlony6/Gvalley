namespace GeminiMod.Models
{
    // Classes para Gemini
    public class GeminiResponse
    {
        public Candidate[] Candidates { get; set; }
    }
    public class Candidate
    {
        public Content Content { get; set; }
    }
    public class Content
    {
        public Part[] Parts { get; set; }
    }
    public class Part
    {
        public string Text { get; set; }
    }

    // Classes para Local Llama (OpenAI Standard)
    public class LocalLlamaResponse
    {
        public LlamaChoice[] Choices { get; set; }
    }
    public class LlamaChoice
    {
        public LlamaMessage Message { get; set; }
    }
    public class LlamaMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }
}