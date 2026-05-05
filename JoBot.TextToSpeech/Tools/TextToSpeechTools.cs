using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;

namespace JoBot.TextToSpeech.Tools;

public class TextToSpeechTools : IToolProvider
{
    private readonly ITtsService _ttsService;

    public TextToSpeechTools(ITtsService ttsService)
    {
        _ttsService = ttsService;
    }

    [AiTool("Generate text-to-speech audio")]
    public async Task<string> GenerateTextToSpeechAsync(
        [AiParameter("The text to convert to speech")] string text)
    {
        try
        {
            var audioData = await _ttsService.GenerateAsync(text);
            return ToolResult.Success(audioData);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Failed to generate audio: {ex.Message}");
        }
    }
}