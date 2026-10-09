using System.Text.RegularExpressions;
using Chitra.Application.Abstractions.Prompting;

namespace Chitra.Application.Stories;

/// <summary>
/// Free, offline story planner. It splits the synopsis into narrative beats, adds an establishing opening and a
/// closing shot, and derives location, mood, lighting and camera work from keywords. The result is a draft that
/// the user edits; an LLM-backed <see cref="IStoryPlanner"/> can replace it later.
/// </summary>
public sealed partial class RuleBasedStoryPlanner : IStoryPlanner
{
    public string Name => "RuleBased";

    private enum BeatKind { Opening, Action, Reaction, Closing }

    private sealed record Beat(BeatKind Kind, string Text);

    private sealed record MoodInfo(string Mood, string Lighting, string Motion, string Camera);

    private static readonly MoodInfo Calm = new("calm and contemplative", "soft morning light", "gentle natural movement, people passing in the background", "push-in");
    private static readonly MoodInfo Wonder = new("wonder and excitement", "warm golden light", "slow deliberate movements, eyes widening with surprise", "push-in");
    private static readonly MoodInfo Tense = new("tense and dramatic", "harsh contrasty light with deep shadows", "sharp sudden gestures, restless movement", "handheld");
    private static readonly MoodInfo Somber = new("melancholic and somber", "cold overcast light, muted colours", "slow heavy movements, head lowered", "pull-back");
    private static readonly MoodInfo Joyful = new("joyful and warm", "bright cheerful sunlight", "lively animated movement, smiling", "orbit");

    private static readonly (string[] Words, MoodInfo Mood)[] MoodRules =
    [
        (["loses", "lost", "dies", "died", "death", "sad", "alone", "cries", "weeps", "regret", "ruin", "everything", "poverty", "destroyed", "fails"], Somber),
        (["arrogant", "greedy", "greed", "angry", "fight", "fights", "argues", "proud", "cruel", "steals", "betrays", "storm", "attack", "chase", "danger", "threat"], Tense),
        (["finds", "found", "discovers", "treasure", "gold", "magic", "wins", "surprise", "secret", "mysterious"], Wonder),
        (["happy", "celebrates", "celebration", "love", "friends", "together", "wedding", "festival", "laughs", "reunites"], Joyful),
    ];

    private static readonly (string Keyword, string Location)[] Locations =
    [
        ("temple", "an old stone temple courtyard"),
        ("palace", "a grand royal palace hall"),
        ("castle", "an ancient castle"),
        ("market", "a busy village market"),
        ("forest", "a dense ancient forest"),
        ("jungle", "a lush green jungle"),
        ("river", "the bank of a slow-flowing river"),
        ("beach", "a quiet sandy beach"),
        ("sea", "the open sea"),
        ("ocean", "the open ocean"),
        ("mountain", "a misty mountain path"),
        ("desert", "a vast golden desert"),
        ("farm", "golden farm fields"),
        ("field", "golden farm fields"),
        ("school", "a small village school"),
        ("city", "a crowded city street"),
        ("street", "a narrow busy street"),
        ("shop", "a small cluttered shop"),
        ("house", "a humble home"),
        ("home", "a humble home"),
        ("hut", "a simple mud hut"),
        ("kitchen", "a warm homely kitchen"),
        ("garden", "a blooming garden"),
        ("road", "a dusty country road"),
        ("village", "a quiet village"),
        ("space", "deep outer space"),
        ("office", "a modern office"),
    ];

    private static readonly string[] Connectors =
    [
        @",?\s+but\s+", @",?\s+and then\s+", @",?\s+then\s+", @",?\s+and eventually\s+", @",?\s+eventually\s+",
        @",?\s+and finally\s+", @",?\s+finally\s+", @",?\s+however,?\s+", @",?\s+until\s+", @",?\s+after that,?\s+",
        @",?\s+later,?\s+", @";\s*",
    ];

    private static readonly HashSet<string> CommonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "is", "was", "has", "had", "gets", "got", "goes", "went", "becomes", "became", "finds", "found", "loses", "lost", "lives", "lived",
        "sees", "saw", "meets", "met", "decides", "decided", "tries", "tried", "wants", "wanted", "takes", "took", "gives", "gave",
        "runs", "ran", "walks", "walked", "comes", "came", "leaves", "left", "returns", "returned", "learns", "learned", "realizes",
        "realises", "dreams", "works", "worked", "buys", "bought", "sells", "sold", "builds", "built", "helps", "helped", "falls", "fell",
    };

    private static readonly string[] Shots = ["medium shot", "wide shot", "medium close-up", "low-angle shot", "over-the-shoulder shot"];
    private static readonly string[] ActionCameras = ["tracking", "push-in", "pan-right", "orbit", "tilt-up"];

    public Task<StoryPlan> PlanAsync(StoryPlanRequest request, CancellationToken cancellationToken)
    {
        var description = Normalize(request.Description);
        var sceneCount = Math.Clamp(request.SceneCount, 3, 12);
        var protagonist = DetectProtagonist(description, request.Characters);

        var clauses = SplitClauses(description)
            .Select(c => FillSubject(c, protagonist))
            .Select(c => protagonist.Replace(c))
            .Where(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
            .ToList();
        if (clauses.Count == 0) clauses.Add($"{protagonist.Reference} {description}");

        var beats = BuildBeats(clauses, sceneCount);
        var storyLocation = FindLocation(description) ?? DefaultLocation(description);
        var knownNames = request.Characters.Select(c => c.Name).ToList();

        var scenes = new List<ScenePlan>();
        var location = storyLocation;
        var actionIndex = 0;
        var lastMood = Calm;

        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            location = FindLocation(beat.Text) ?? location;
            var mood = beat.Kind switch
            {
                BeatKind.Opening => Calm,
                BeatKind.Closing => lastMood == Wonder || lastMood == Joyful ? Joyful : Somber,
                _ => DetectMood(beat.Text) ?? lastMood,
            };
            if (beat.Kind is BeatKind.Action or BeatKind.Reaction) lastMood = mood;

            var subject = protagonist.Reference;
            var (title, action, shot, camera, narration) = beat.Kind switch
            {
                BeatKind.Opening => (
                    "Introduction",
                    $"{subject} going about an ordinary day in {location}",
                    "wide establishing shot",
                    "push-in",
                    $"In {location}, there lived {protagonist.Introduction}."),
                BeatKind.Closing => (
                    "Ending",
                    mood == Joyful ? $"{subject} at peace, smiling as the day comes to an end" : $"{subject} alone, reflecting on everything that has happened",
                    "wide shot",
                    mood == Joyful ? "crane-up" : "pull-back",
                    mood == Joyful ? "And so the story found its happy ending." : $"In the end, {LowerFirst(subject)} was left to reflect on all that had happened."),
                BeatKind.Reaction => (
                    $"{Possessive(subject)} reaction",
                    $"{Possessive(subject)} face, an expressive reaction moments after {LowerFirst(beat.Text)}",
                    "close-up",
                    "zoom-in",
                    string.Empty),
                _ => (
                    TitleFrom(beat.Text),
                    beat.Text,
                    Shots[actionIndex % Shots.Length],
                    mood == Tense ? "handheld" : mood == Somber ? "pull-back" : ActionCameras[actionIndex++ % ActionCameras.Length],
                    Sentence(beat.Text)),
            };

            var names = knownNames.Where(n => Regex.IsMatch(action, $@"\b{Regex.Escape(n)}\b", RegexOptions.IgnoreCase)).ToList();
            if (protagonist.CharacterName is { } pn && !names.Contains(pn)) names.Insert(0, pn);

            var where = action.Contains(location, StringComparison.OrdinalIgnoreCase) ? string.Empty : $", in {location}";
            var imagePrompt = $"{Capitalize(shot)}, {action}{where}, {mood.Mood} atmosphere, {mood.Lighting}, cinematic composition, highly detailed";
            var videoPrompt = $"{Capitalize(action)}, {mood.Motion}, {CameraPhrase(camera)}, {mood.Mood} atmosphere, {mood.Lighting}";

            scenes.Add(new ScenePlan(
                $"Scene {i + 1}: {title}",
                Sentence(action),
                location,
                Sentence(action),
                mood.Mood,
                names,
                imagePrompt,
                videoPrompt,
                camera,
                request.SceneDurationSeconds,
                narration));
        }

        return Task.FromResult(new StoryPlan(request.Title.Trim(), protagonist.Reference, scenes, Name));
    }

    private static List<Beat> BuildBeats(List<string> clauses, int sceneCount)
    {
        var actions = clauses.ToList();

        // Too many clauses: merge the shortest neighbours.
        while (actions.Count > Math.Max(1, sceneCount - (sceneCount >= 4 ? 2 : 0)) && actions.Count > 1)
        {
            var best = 0;
            for (var i = 1; i < actions.Count - 1; i++)
            {
                if (actions[i].Length + actions[i + 1].Length < actions[best].Length + actions[best + 1].Length) best = i;
            }

            actions[best] = $"{actions[best]}, then {LowerFirst(actions[best + 1])}";
            actions.RemoveAt(best + 1);
        }

        var beats = actions.Select(a => new Beat(BeatKind.Action, a)).ToList();
        if (beats.Count < sceneCount) beats.Insert(0, new Beat(BeatKind.Opening, string.Empty));
        if (beats.Count < sceneCount) beats.Add(new Beat(BeatKind.Closing, string.Empty));

        // Still short: add close-up reaction shots after the most dramatic (middle) actions.
        var actionBeats = beats.Where(b => b.Kind == BeatKind.Action).ToList();
        var round = 0;
        while (beats.Count < sceneCount && actionBeats.Count > 0)
        {
            var target = actionBeats[(actionBeats.Count / 2 + round) % actionBeats.Count];
            beats.Insert(beats.IndexOf(target) + 1, new Beat(BeatKind.Reaction, target.Text));
            round++;
        }

        return beats.Take(sceneCount).ToList();
    }

    private sealed class Protagonist
    {
        /// <summary>How later sentences refer to them: a character name or e.g. "The old man".</summary>
        public required string Reference { get; init; }

        /// <summary>Used in the opening narration, e.g. "a poor old man named Ramu".</summary>
        public required string Introduction { get; init; }
        public string? CharacterName { get; init; }
        public string? Phrase { get; init; }

        /// <summary>Replaces "a poor old man", "the old man", "he"/"she" with the character name when one is chosen.</summary>
        public string Replace(string clause)
        {
            if (CharacterName is null || Phrase is null) return clause;
            var words = Phrase.Split(' ');
            var tail = string.Join(' ', words.TakeLast(Math.Min(2, words.Length)));
            clause = Regex.Replace(clause, $@"\b(a|an|the)\s+{Regex.Escape(Phrase)}\b", CharacterName, RegexOptions.IgnoreCase);
            clause = Regex.Replace(clause, $@"\bthe\s+{Regex.Escape(tail)}\b", CharacterName, RegexOptions.IgnoreCase);
            return Regex.Replace(clause, @"^(he|she|they)\b", CharacterName, RegexOptions.IgnoreCase);
        }
    }

    private static Protagonist DetectProtagonist(string description, IReadOnlyList<StoryCharacterInfo> characters)
    {
        var firstSentence = SentenceRegex().Split(description).FirstOrDefault() ?? description;
        var cleaned = OpeningRegex().Replace(firstSentence, string.Empty).Trim();
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? phrase = null;
        if (words.Length > 1 && words[0].ToLowerInvariant() is "a" or "an" or "the")
        {
            var noun = words.Skip(1).TakeWhile(w => !LooksLikeVerb(w) && w.ToLowerInvariant() is not "who" and not "that" and not "with").Take(4).ToList();
            if (noun.Count > 0) phrase = string.Join(' ', noun).Trim(',', '.');
        }

        var character = characters.FirstOrDefault();
        if (character is not null)
        {
            return new Protagonist
            {
                Reference = character.Name,
                CharacterName = character.Name,
                Phrase = phrase,
                Introduction = phrase is null ? character.Name : $"{Article(phrase)} {phrase} named {character.Name}",
            };
        }

        if (phrase is not null)
        {
            var tail = string.Join(' ', phrase.Split(' ').TakeLast(2));
            return new Protagonist { Reference = $"The {tail}", Introduction = $"{Article(phrase)} {phrase}", Phrase = phrase };
        }

        if (words.Length > 0 && char.IsUpper(words[0][0]) && words.Length > 1 && LooksLikeVerb(words[1]))
        {
            return new Protagonist { Reference = words[0], Introduction = words[0], CharacterName = null };
        }

        return new Protagonist { Reference = "The hero", Introduction = "a hero" };
    }

    private static IEnumerable<string> SplitClauses(string description)
    {
        foreach (var sentence in SentenceRegex().Split(description))
        {
            var parts = new List<string> { sentence };
            foreach (var connector in Connectors)
            {
                parts = parts.SelectMany(p => Regex.Split(p, connector, RegexOptions.IgnoreCase)).ToList();
            }

            // Split on "and" only when it joins two actions ("finds gold and becomes proud").
            parts = parts.SelectMany(p => Regex.Split(p, @"\s+and\s+(?=(?:\w+s|\w+ed)\b)", RegexOptions.IgnoreCase)).ToList();

            foreach (var part in parts)
            {
                var clause = part.Trim().Trim('.', ',', '!', '?', ';', ' ');
                if (clause.Length > 0) yield return clause;
            }
        }
    }

    private static string FillSubject(string clause, Protagonist protagonist)
    {
        var first = clause.Split(' ', 2)[0];
        return char.IsLower(first[0]) && LooksLikeVerb(first) ? $"{protagonist.Reference} {clause}" : Capitalize(clause);
    }

    private static bool LooksLikeVerb(string word)
    {
        var w = word.Trim(',', '.').ToLowerInvariant();
        if (CommonVerbs.Contains(w)) return true;
        if (w.Length < 4) return false;
        return (w.EndsWith('s') && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is")) || w.EndsWith("ed");
    }

    private static MoodInfo? DetectMood(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var (words, mood) in MoodRules)
        {
            if (words.Any(w => Regex.IsMatch(lower, $@"\b{w}\b"))) return mood;
        }

        return null;
    }

    private static string? FindLocation(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var (keyword, location) in Locations)
        {
            if (Regex.IsMatch(lower, $@"\b{keyword}s?\b")) return location;
        }

        return null;
    }

    private static string DefaultLocation(string description) =>
        Regex.IsMatch(description, @"\b(poor|king|queen|farmer|merchant|gold|old|beggar|villager)\b", RegexOptions.IgnoreCase)
            ? "a quiet village"
            : "a lively town";

    private static string CameraPhrase(string camera) => camera switch
    {
        "push-in" => "the camera slowly pushes in",
        "pull-back" => "the camera slowly pulls back",
        "handheld" => "handheld camera with subtle shake",
        "orbit" => "the camera orbits around the subject",
        "tracking" => "tracking shot following the subject",
        "pan-right" => "the camera pans slowly to the right",
        "tilt-up" => "the camera tilts up",
        "crane-up" => "the camera cranes up to a wide view",
        "zoom-in" => "slow zoom in on the face",
        _ => "smooth camera movement",
    };

    private static string TitleFrom(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(' ', words.Take(7));
        return words.Length > 7 ? title + "…" : title;
    }

    private static string Possessive(string subject) => subject.EndsWith('s') ? $"{subject}'" : $"{subject}'s";

    private static string Article(string phrase) => "aeiou".Contains(char.ToLowerInvariant(phrase[0])) ? "an" : "a";

    private static string Normalize(string text) => WhitespaceRegex().Replace(text.Trim(), " ");

    private static string Sentence(string text) => Capitalize(text.TrimEnd('.', ' ')) + ".";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Lower-cases a leading article like "The" but keeps names capitalised.</summary>
    private static string LowerFirst(string text) =>
        text.StartsWith("The ", StringComparison.Ordinal) || text.StartsWith("A ", StringComparison.Ordinal) || text.StartsWith("An ", StringComparison.Ordinal)
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"^(once upon a time,?\s*|long ago,?\s*)?(there (was|lived|is)\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex OpeningRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
