using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Application.Library;

public sealed class CharacterService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<CharacterDto>> ListAsync(CancellationToken ct) =>
        (await db.Characters.AsNoTracking().Include(c => c.ReferenceAsset)
            .Where(c => c.UserId == currentUser.UserId).OrderBy(c => c.Name).ToListAsync(ct))
        .Select(c => c.ToDto()).ToList();

    public async Task<CharacterDto> GetAsync(Guid id, CancellationToken ct) => (await FindAsync(id, ct)).ToDto();

    public async Task<CharacterDto> CreateAsync(UpsertCharacterRequest r, CancellationToken ct)
    {
        var character = new Character { UserId = currentUser.UserId };
        await ApplyAsync(character, r, ct);
        db.Characters.Add(character);
        await db.SaveChangesAsync(ct);
        return character.ToDto();
    }

    public async Task<CharacterDto> UpdateAsync(Guid id, UpsertCharacterRequest r, CancellationToken ct)
    {
        var character = await FindAsync(id, ct, tracking: true);
        await ApplyAsync(character, r, ct);
        character.Touch();
        await db.SaveChangesAsync(ct);
        return character.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var character = await FindAsync(id, ct, tracking: true);
        db.Characters.Remove(character);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Character c, UpsertCharacterRequest r, CancellationToken ct)
    {
        new Validator()
            .NotEmpty(r.Name, "name", 100)
            .Range(r.Age, 0, 150, "age")
            .Require((r.Description?.Length ?? 0) <= 2000, "description", "Description must be at most 2000 characters.")
            .Require((r.PromptOverride?.Length ?? 0) <= 2000, "promptOverride", "Prompt must be at most 2000 characters.")
            .ThrowIfInvalid();

        var duplicate = await db.Characters.AnyAsync(x => x.UserId == currentUser.UserId && x.Id != c.Id && x.Name.ToLower() == r.Name.Trim().ToLower(), ct);
        if (duplicate) throw new ValidationException("name", $"You already have a character named '{r.Name.Trim()}'.");

        if (r.ReferenceAssetId is { } assetId)
        {
            var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId && a.UserId == currentUser.UserId, ct)
                        ?? throw new NotFoundException("Reference image", assetId);
            if (asset.Type != MediaType.Image) throw new ValidationException("referenceAssetId", "The reference must be an image.");
            c.ReferenceAsset = asset;
        }
        else
        {
            c.ReferenceAsset = null;
        }

        c.Name = r.Name.Trim();
        c.Description = Clean(r.Description);
        c.Age = r.Age;
        c.Gender = Clean(r.Gender);
        c.Clothing = Clean(r.Clothing);
        c.Appearance = Clean(r.Appearance);
        c.Personality = Clean(r.Personality);
        c.PromptOverride = Clean(r.PromptOverride);
        c.PreferredSeed = r.PreferredSeed is < 0 ? null : r.PreferredSeed;
        c.ReferenceAssetId = r.ReferenceAssetId;
    }

    private async Task<Character> FindAsync(Guid id, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.Characters : db.Characters.AsNoTracking();
        return await query.Include(c => c.ReferenceAsset).FirstOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, ct)
               ?? throw new NotFoundException("Character", id);
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class StyleService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<StyleDto>> ListAsync(CancellationToken ct) =>
        (await db.Styles.AsNoTracking().Where(s => s.UserId == null || s.UserId == currentUser.UserId)
            .OrderByDescending(s => s.IsBuiltIn).ThenBy(s => s.Name).ToListAsync(ct))
        .Select(s => s.ToDto()).ToList();

    public async Task<StyleDto> CreateAsync(UpsertStyleRequest r, CancellationToken ct)
    {
        Validate(r);
        var style = new Style { UserId = currentUser.UserId };
        Apply(style, r);
        db.Styles.Add(style);
        await db.SaveChangesAsync(ct);
        return style.ToDto();
    }

    public async Task<StyleDto> UpdateAsync(Guid id, UpsertStyleRequest r, CancellationToken ct)
    {
        Validate(r);
        var style = await FindEditableAsync(id, ct);
        Apply(style, r);
        style.Touch();
        await db.SaveChangesAsync(ct);
        return style.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var style = await FindEditableAsync(id, ct);
        await db.Projects.Where(p => p.StyleId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.StyleId, (Guid?)null), ct);
        db.Styles.Remove(style);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Style> FindEditableAsync(Guid id, CancellationToken ct)
    {
        var style = await db.Styles.FirstOrDefaultAsync(s => s.Id == id && (s.UserId == null || s.UserId == currentUser.UserId), ct)
                    ?? throw new NotFoundException("Style", id);
        if (style.IsBuiltIn) throw new AppException(ErrorCode.Conflict, "Built-in styles cannot be changed. Create your own style instead.");
        return style;
    }

    private static void Validate(UpsertStyleRequest r) => new Validator()
        .NotEmpty(r.Name, "name", 100)
        .Require(!string.IsNullOrWhiteSpace(r.PromptPrefix) || !string.IsNullOrWhiteSpace(r.PromptSuffix), "promptSuffix",
            "Add a prompt prefix or suffix so the style changes something.")
        .ThrowIfInvalid();

    private static void Apply(Style s, UpsertStyleRequest r)
    {
        s.Name = r.Name.Trim();
        s.Description = CharacterService.Clean(r.Description);
        s.PromptPrefix = CharacterService.Clean(r.PromptPrefix);
        s.PromptSuffix = CharacterService.Clean(r.PromptSuffix);
        s.NegativePrompt = CharacterService.Clean(r.NegativePrompt);
    }
}

public sealed class PromptTemplateService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<PromptTemplateDto>> ListAsync(CancellationToken ct) =>
        (await db.PromptTemplates.AsNoTracking().Where(t => t.UserId == null || t.UserId == currentUser.UserId)
            .OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync(ct))
        .Select(t => t.ToDto()).ToList();

    public async Task<PromptTemplateDto> CreateAsync(UpsertPromptTemplateRequest r, CancellationToken ct)
    {
        new Validator().NotEmpty(r.Name, "name", 100).NotEmpty(r.Template, "template", 4000)
            .Require(r.Category is "Image" or "Video" or "Story" or "Character", "category", "Category must be Image, Video, Story or Character.")
            .ThrowIfInvalid();
        var template = new PromptTemplate
        {
            UserId = currentUser.UserId,
            Name = r.Name.Trim(),
            Category = r.Category,
            Template = r.Template.Trim(),
            NegativePrompt = CharacterService.Clean(r.NegativePrompt),
            Description = CharacterService.Clean(r.Description),
        };
        db.PromptTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var template = await db.PromptTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == currentUser.UserId, ct)
                       ?? throw new NotFoundException("Prompt template", id);
        db.PromptTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }
}
