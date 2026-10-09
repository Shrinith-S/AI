using System.Text.RegularExpressions;
using Chitra.Application.Abstractions.Prompting;
using Chitra.Domain.Enums;

namespace Chitra.Application.Prompting;

/// <summary>
/// Free, offline prompt enhancer. It enriches subjects, settings, lighting and composition from curated phrase lists.
/// It is deterministic for a given <see cref="PromptEnhancementRequest.Variation"/>, so results are reproducible.
/// </summary>
public sealed partial class RuleBasedPromptEnhancer : IPromptEnhancer
{
    public string Name => "RuleBased";
    public bool IsLocal => true;
    public bool IncursApiCharges => false;

    private sealed record SettingRule(string Pattern, string Replacement, string[] Atmosphere, string Lighting);

    private static readonly (string Pattern, string Replacement)[] SubjectRules =
    [
        (@"\b(a|an|the) (little |small )?boy\b", "$1 young boy"),
        (@"\b(a|an|the) (little |small )?girl\b", "$1 young girl"),
        (@"\b(a|an|the) old man\b", "$1 elderly man with a weathered, expressive face"),
        (@"\b(a|an|the) old woman\b", "$1 elderly woman with a kind, wrinkled face"),
        (@"\b(a|an|the) dog\b", "$1 loyal dog with soft fur"),
        (@"\b(a|an|the) cat\b", "$1 curious cat with bright eyes"),
        (@"\b(a|an|the) dragon\b", "$1 majestic dragon with iridescent scales"),
        (@"\b(a|an|the) robot\b", "$1 sleek robot with glowing details"),
        (@"\b(a|an|the) car\b", "$1 gleaming car"),
        (@"\b(a|an|the) warrior\b", "$1 battle-worn warrior"),
        (@"\b(a|an|the) king\b", "$1 regal king"),
        (@"\b(a|an|the) queen\b", "$1 graceful queen"),
    ];

    private static readonly (string Pattern, string Replacement)[] ActionRules =
    [
        (@"\bwalking\b(?! slowly)", "walking slowly"),
        (@"\brunning\b(?! fast)", "running energetically"),
        (@"\bsitting\b(?! quietly)", "sitting quietly"),
        (@"\bdancing\b(?! gracefully)", "dancing gracefully"),
        (@"\bflying\b(?! high)", "flying high"),
    ];

    private static readonly SettingRule[] SettingRules =
    [
        new(@"\b(a|the) forest\b", "a dense ancient forest",
            ["golden morning sunlight filtering through the trees", "soft mist floating above the ground"], "volumetric lighting"),
        new(@"\b(a|the) (city|street)\b", "a bustling city street",
            ["neon reflections on wet pavement", "crowds moving in the background"], "moody cinematic night lighting"),
        new(@"\b(a|the) (beach|shore)\b", "a quiet sandy beach",
            ["gentle waves rolling onto the shore", "sea breeze moving the air"], "warm golden hour light"),
        new(@"\b(the|an|a) (ocean|sea)\b", "the vast open ocean",
            ["sparkling water surface", "sea spray in the air"], "bright natural sunlight"),
        new(@"\b(a|the) mountain(s)?\b", "majestic snow-capped mountains",
            ["drifting clouds around the peaks", "crisp clear air"], "dramatic sunrise lighting"),
        new(@"\b(a|the) desert\b", "a vast golden desert",
            ["rippling sand dunes", "heat haze on the horizon"], "harsh bright sunlight with long shadows"),
        new(@"\b(a|an|the) (old )?temple\b", "an ancient stone temple",
            ["carved pillars and worn steps", "incense smoke drifting in the air"], "warm diffused sunlight"),
        new(@"\b(a|the) village\b", "a rustic village",
            ["dusty paths and small houses", "everyday life in the background"], "soft late-afternoon light"),
        new(@"\b(a|the) river\b", "a slow-flowing river",
            ["reflections shimmering on the water", "reeds swaying at the banks"], "soft diffused daylight"),
        new(@"\b(a|the) market\b", "a busy traditional market",
            ["colourful stalls and fabrics", "people bargaining"], "bright midday light with soft shadows"),
        new(@"\b(a|the) castle\b", "a towering medieval castle",
            ["stone battlements", "banners waving in the wind"], "dramatic overcast lighting"),
        new(@"\b(a|the) garden\b", "a lush blooming garden",
            ["flowers swaying gently", "butterflies drifting"], "soft morning light"),
        new(@"\bspace\b", "deep outer space",
            ["distant nebulae and stars", "a glowing planet on the horizon"], "rim lighting from a distant star"),
        new(@"\b(a|the) kitchen\b", "a warm homely kitchen",
            ["steam rising from pots", "copper utensils on the walls"], "warm practical lighting"),
    ];

    private static readonly Dictionary<string, string[]> StyleTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cinematic"] = ["cinematic composition", "shallow depth of field", "anamorphic lens", "film grain"],
        ["photoreal"] = ["photorealistic", "35mm photograph", "natural skin texture", "sharp focus"],
        ["anime"] = ["anime style", "clean line art", "cel shading", "vibrant colours"],
        ["3d"] = ["highly detailed 3D animation", "stylized characters", "global illumination", "subsurface scattering"],
        ["illustration"] = ["detailed digital illustration", "painterly brushwork", "rich colour palette"],
        ["watercolor"] = ["delicate watercolor painting", "soft bleeding edges", "textured paper"],
    };

    private static readonly string[][] AlternateLighting =
    [
        ["volumetric lighting"],
        ["soft rim lighting"],
        ["dramatic chiaroscuro lighting"],
        ["warm golden hour light"],
    ];

    public Task<PromptEnhancement> EnhanceAsync(PromptEnhancementRequest request, CancellationToken cancellationToken)
    {
        var original = request.Prompt.Trim().TrimEnd('.');
        var additions = new List<string>();
        var text = original;
        var lower = text.ToLowerInvariant();
        var isVideo = request.Type.ProducesVideo();

        // Prompts that are already long only get quality tags appended.
        var alreadyDetailed = WordRegex().Matches(text).Count > 35;

        string? lighting = null;
        if (!alreadyDetailed)
        {
            text = ApplyRules(text, SubjectRules, additions);
            text = ApplyRules(text, ActionRules, additions);

            foreach (var rule in SettingRules)
            {
                var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                if (!regex.IsMatch(text)) continue;

                if (!lower.Contains(rule.Replacement, StringComparison.OrdinalIgnoreCase))
                {
                    text = regex.Replace(text, rule.Replacement, 1);
                    additions.Add(rule.Replacement);
                }

                text += ", " + string.Join(", ", rule.Atmosphere);
                additions.AddRange(rule.Atmosphere);
                lighting = rule.Lighting;
                break;
            }
        }

        var tags = new List<string>();
        var style = ResolveStyle(request.StyleHint, isVideo);
        tags.AddRange(StyleTags[style].Take(style == "cinematic" ? 2 : 4));
        if (ContainsPerson(lower)) tags.Add("realistic facial expressions");

        if (isVideo)
        {
            tags.Add("subtle camera movement");
            tags.Add("smooth natural motion");
        }

        if (style == "cinematic") tags.AddRange(StyleTags["cinematic"].Skip(2).Take(1));

        if (!lower.Contains("light"))
        {
            var variation = Math.Abs(request.Variation) % AlternateLighting.Length;
            tags.AddRange(lighting is not null && variation == 0 ? [lighting] : AlternateLighting[variation]);
        }

        tags.Add("highly detailed");

        foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (text.Contains(tag, StringComparison.OrdinalIgnoreCase)) continue;
            text += ", " + tag;
            additions.Add(tag);
        }

        var negative = isVideo ? PromptComposer.DefaultVideoNegative : PromptComposer.DefaultImageNegative;
        return Task.FromResult(new PromptEnhancement(Capitalize(text), negative, additions, Name));
    }

    private static string ApplyRules(string text, (string Pattern, string Replacement)[] rules, List<string> additions)
    {
        foreach (var (pattern, replacement) in rules)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = regex.Match(text);
            if (!match.Success) continue;

            var replaced = match.Result(replacement);
            text = text[..match.Index] + replaced + text[(match.Index + match.Length)..];
            additions.Add(replaced);
        }

        return text;
    }

    private static string ResolveStyle(string? hint, bool isVideo)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var key = StyleTags.Keys.FirstOrDefault(k => hint.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (key is not null) return key;
        }

        return "cinematic";
    }

    private static bool ContainsPerson(string lower) =>
        PersonRegex().IsMatch(lower);

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex(@"\w+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b(man|woman|boy|girl|child|person|people|king|queen|warrior|farmer|old|lady|he|she|portrait|face)\b")]
    private static partial Regex PersonRegex();
}
