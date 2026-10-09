using System.Text.RegularExpressions;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;

namespace Chitra.Application.Prompting;

/// <summary>Camera movements offered in the UI. Base video models have no camera control, so they are expressed in the prompt.</summary>
public static class CameraMovements
{
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["static"] = "static camera, locked-off shot",
        ["push-in"] = "the camera slowly pushes in toward the subject",
        ["pull-back"] = "the camera slowly pulls back to reveal the scene",
        ["pan-left"] = "the camera pans slowly to the left",
        ["pan-right"] = "the camera pans slowly to the right",
        ["tilt-up"] = "the camera tilts up",
        ["tilt-down"] = "the camera tilts down",
        ["orbit"] = "the camera orbits smoothly around the subject",
        ["tracking"] = "tracking shot, the camera follows the subject",
        ["crane-up"] = "crane shot rising upward",
        ["handheld"] = "handheld camera with subtle natural shake",
        ["zoom-in"] = "slow zoom in",
        ["zoom-out"] = "slow zoom out",
    };

    public static IReadOnlyCollection<string> All => Phrases.Keys;

    public static string? ToPhrase(string? movement)
    {
        if (string.IsNullOrWhiteSpace(movement) || movement.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return Phrases.TryGetValue(movement.Trim(), out var phrase) ? phrase : movement.Trim();
    }
}

public sealed record ComposedPrompt(string Prompt, string NegativePrompt);

/// <summary>
/// Builds the final prompt sent to a model: characters are injected for consistency, camera and motion are described
/// in words, and the selected style wraps everything.
/// </summary>
public static class PromptComposer
{
    public const string DefaultImageNegative =
        "blurry, low quality, lowres, jpeg artifacts, deformed, disfigured, bad anatomy, extra limbs, watermark, text, signature";

    public const string DefaultVideoNegative =
        "worst quality, low quality, blurry, jittery, distorted, deformed, flickering, static frame, watermark, text";

    public static ComposedPrompt Compose(
        GenerationType type,
        string prompt,
        string? negativePrompt,
        Style? style,
        IReadOnlyList<Character> characters,
        string? cameraMovement = null,
        double? motionStrength = null,
        string? modelNegative = null)
    {
        var text = InjectCharacters(prompt.Trim(), characters);

        if (type.ProducesVideo())
        {
            var extras = new List<string>();
            var motion = DescribeMotion(motionStrength);
            if (motion is not null) extras.Add(motion);
            var camera = CameraMovements.ToPhrase(cameraMovement);
            if (camera is not null) extras.Add(camera);
            if (extras.Count > 0) text = $"{text.TrimEnd('.', ' ')}, {string.Join(", ", extras)}";
        }

        if (style is not null) text = style.Apply(text);

        var negatives = new[]
            {
                negativePrompt,
                style?.NegativePrompt,
                string.IsNullOrWhiteSpace(negativePrompt)
                    ? modelNegative ?? (type.ProducesVideo() ? DefaultVideoNegative : DefaultImageNegative)
                    : null,
            }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => n!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new ComposedPrompt(text, string.Join(", ", negatives));
    }

    /// <summary>
    /// Inlines each character's visual description where the character is mentioned
    /// ("Ramu walks" -> "Ramu (65-year-old man, grey beard, ...) walks"); characters not mentioned are prepended.
    /// </summary>
    public static string InjectCharacters(string prompt, IReadOnlyList<Character> characters)
    {
        var prefix = new List<string>();
        foreach (var character in characters)
        {
            var description = character.BuildDescription();
            if (string.IsNullOrWhiteSpace(description)) continue;

            var pattern = new Regex($@"\b{Regex.Escape(character.Name.Trim())}\b(?!\s*\()", RegexOptions.IgnoreCase);
            var match = pattern.Match(prompt);
            if (match.Success)
            {
                prompt = prompt[..match.Index] + $"{match.Value} ({description})" + prompt[(match.Index + match.Length)..];
            }
            else if (!prompt.Contains(description, StringComparison.OrdinalIgnoreCase))
            {
                prefix.Add(character.BuildPrompt());
            }
        }

        return prefix.Count == 0 ? prompt : $"{string.Join("; ", prefix)}. {prompt}";
    }

    public static string? DescribeMotion(double? strength) => strength switch
    {
        null => null,
        < 0.2 => "very subtle motion, almost still",
        < 0.45 => "gentle slow motion",
        < 0.7 => "natural smooth motion",
        < 0.9 => "dynamic lively motion",
        _ => "fast energetic motion, dramatic movement",
    };
}
