using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Persistence;

/// <summary>Applies migrations and seeds built-in data. Safe to run on every start.</summary>
public sealed class DatabaseInitializer(AppDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        await SeedUserAsync(ct);
        await SeedStylesAsync(ct);
        await SeedTemplatesAsync(ct);
        await SeedProvidersAsync(ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Database ready ({Provider})", db.Database.ProviderName);
    }

    private async Task SeedUserAsync(CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == User.LocalUserId, ct))
        {
            db.Users.Add(new User { Id = User.LocalUserId, DisplayName = "Local User", IsLocal = true });
        }
    }

    private async Task SeedStylesAsync(CancellationToken ct)
    {
        var existing = await db.Styles.Where(s => s.IsBuiltIn).Select(s => s.Id).ToListAsync(ct);
        foreach (var style in BuiltInStyles.Where(s => !existing.Contains(s.Id)))
        {
            db.Styles.Add(style);
        }
    }

    private async Task SeedTemplatesAsync(CancellationToken ct)
    {
        var existing = await db.PromptTemplates.Where(t => t.IsBuiltIn).Select(t => t.Id).ToListAsync(ct);
        foreach (var template in BuiltInTemplates.Where(t => !existing.Contains(t.Id)))
        {
            db.PromptTemplates.Add(template);
        }
    }

    private async Task SeedProvidersAsync(CancellationToken ct)
    {
        var existing = await db.ProviderConfigurations.Select(p => p.Name).ToListAsync(ct);
        foreach (var provider in BuiltInProviders.Where(p => !existing.Contains(p.Name)))
        {
            db.ProviderConfigurations.Add(provider);
        }
    }

    private static Style BuiltIn(string id, string name, string description, string? prefix, string? suffix, string? negative) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Description = description,
        PromptPrefix = prefix,
        PromptSuffix = suffix,
        NegativePrompt = negative,
        IsBuiltIn = true,
    };

    private static readonly Style[] BuiltInStyles =
    [
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000001", "Cinematic", "Film-like lighting and composition",
            "cinematic film still", "dramatic lighting, shallow depth of field, anamorphic lens, film grain, color graded", "cartoon, illustration"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000002", "Photorealistic", "Natural photography look",
            "photograph", "photorealistic, 35mm lens, natural light, sharp focus, high detail", "painting, cartoon, 3d render, illustration"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000003", "Stylized 3D Animation", "Feature-animation inspired 3D look",
            "stylized 3D animation", "expressive characters, soft global illumination, subsurface scattering, highly detailed 3D render", "photo, realistic skin, grainy"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000004", "Anime", "Japanese animation style",
            "anime illustration", "clean line art, cel shading, vibrant colors, detailed background", "photo, realistic, 3d render"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000005", "Watercolor", "Soft painted look",
            "watercolor painting", "soft bleeding edges, textured paper, delicate washes of color", "photo, 3d render, harsh lines"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000006", "Storybook Illustration", "Warm children's-book illustration",
            "storybook illustration", "warm colors, gentle shapes, hand-painted textures, whimsical", "photo, gore, realistic"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000007", "Indian Miniature Painting", "Inspired by traditional miniature art",
            "traditional Indian miniature painting", "intricate patterns, flat perspective, rich mineral pigments, ornate borders", "photo, 3d render"),
        BuiltIn("6f1b6c1e-0001-4c6a-9a51-000000000008", "Cyberpunk", "Neon-lit futuristic",
            "cyberpunk scene", "neon lights, rain-soaked streets, holographic signs, moody atmosphere", null),
    ];

    private static readonly PromptTemplate[] BuiltInTemplates =
    [
        new()
        {
            Id = Guid.Parse("7a2c0d4e-0002-4b1a-8f61-000000000001"), Name = "Character portrait", Category = "Image", IsBuiltIn = true,
            Template = "Portrait of {subject}, looking at the camera, neutral background, soft studio lighting, highly detailed face",
            Description = "Clean reference portrait — useful for a character's reference image.",
        },
        new()
        {
            Id = Guid.Parse("7a2c0d4e-0002-4b1a-8f61-000000000002"), Name = "Establishing shot", Category = "Video", IsBuiltIn = true,
            Template = "Wide establishing shot of {location}, {time of day}, the camera slowly glides forward, atmospheric, cinematic",
            Description = "Opens a scene by showing where it takes place.",
        },
        new()
        {
            Id = Guid.Parse("7a2c0d4e-0002-4b1a-8f61-000000000003"), Name = "Product shot", Category = "Image", IsBuiltIn = true,
            Template = "Studio product photograph of {product} on a seamless background, softbox lighting, crisp reflections, 85mm lens",
            Description = "Clean commercial product imagery.",
        },
        new()
        {
            Id = Guid.Parse("7a2c0d4e-0002-4b1a-8f61-000000000004"), Name = "Character walk cycle", Category = "Video", IsBuiltIn = true,
            Template = "{subject} walks steadily toward the camera through {location}, tracking shot, natural motion, consistent lighting",
            Description = "Simple, reliable motion for image-to-video.",
        },
    ];

    private static readonly ProviderConfiguration[] BuiltInProviders =
    [
        new() { Name = "ComfyUI", Kind = ProviderKind.ImageVideo, BaseUrl = "http://localhost:8188", IsLocal = true, IncursApiCharges = false },
        new() { Name = "Piper", Kind = ProviderKind.TextToSpeech, IsLocal = true, IncursApiCharges = false },
        new() { Name = "Coqui", Kind = ProviderKind.TextToSpeech, BaseUrl = "http://localhost:5002", IsLocal = true, IncursApiCharges = false, IsEnabled = false },
        new() { Name = "RuleBased", Kind = ProviderKind.PromptLlm, IsLocal = true, IncursApiCharges = false },
    ];
}
