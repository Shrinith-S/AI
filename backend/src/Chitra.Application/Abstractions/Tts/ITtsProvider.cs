namespace Chitra.Application.Abstractions.Tts;

/// <summary>Text-to-speech engine. Implementations write a WAV file.</summary>
public interface ITtsProvider
{
    string Name { get; }
    bool IsLocal { get; }
    bool IncursApiCharges { get; }

    Task<TtsHealth> CheckHealthAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken cancellationToken);
    Task SynthesizeAsync(TtsRequest request, string outputWavPath, CancellationToken cancellationToken);
}

public interface ITtsProviderRegistry
{
    IReadOnlyList<ITtsProvider> All { get; }

    /// <summary>Returns the provider selected in settings, enforcing Local-Only mode.</summary>
    ITtsProvider GetActive();
}

public sealed record TtsHealth(bool IsAvailable, string Message);

public sealed record TtsVoice(string Id, string Name, string? Language);

public sealed record TtsRequest(string Text, string? VoiceId, double Speed = 1.0);
