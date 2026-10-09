using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Projects;
using Chitra.Application.Settings;
using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Application.Stories;

/// <summary>Turns a story synopsis into a project with editable scenes. Nothing is generated until the user asks.</summary>
public sealed class StoryService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IEnumerable<IStoryPlanner> planners,
    IRuntimeSettings settings,
    ProjectService projects)
{
    public async Task<ProjectDetailDto> GenerateAsync(GenerateStoryRequest r, CancellationToken ct)
    {
        new Validator()
            .NotEmpty(r.Title, "title", 200)
            .NotEmpty(r.Description, "description", 4000)
            .Range(r.SceneCount, 3, 12, "sceneCount")
            .Range(r.SceneDurationSeconds, 2, 20, "sceneDurationSeconds")
            .ThrowIfInvalid();

        var userId = currentUser.UserId;
        var characterIds = r.CharacterIds?.Distinct().ToList() ?? [];
        var characters = await db.Characters.Where(c => characterIds.Contains(c.Id) && c.UserId == userId).ToListAsync(ct);
        if (characters.Count != characterIds.Count)
        {
            throw new NotFoundException("Character", string.Join(", ", characterIds.Except(characters.Select(c => c.Id))));
        }

        // Keep the user's order: the first selected character is the protagonist.
        characters = characterIds.Select(id => characters.First(c => c.Id == id)).ToList();

        var style = r.StyleId is null
            ? null
            : await db.Styles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.StyleId && (s.UserId == null || s.UserId == userId), ct)
              ?? throw new NotFoundException("Style", r.StyleId);

        var planner = planners.First();
        var plan = await planner.PlanAsync(new StoryPlanRequest(
            r.Title,
            r.Description,
            r.SceneCount,
            r.SceneDurationSeconds,
            characters.Select(c => new StoryCharacterInfo(c.Name, c.BuildDescription())).ToList(),
            style?.PromptSuffix), ct);

        Project project;
        if (r.ProjectId is { } projectId)
        {
            project = await db.Projects.Include(p => p.Scenes).Include(p => p.Characters)
                          .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw new NotFoundException("Project", projectId);
            if (r.ReplaceExistingScenes)
            {
                await db.Generations.Where(g => g.ProjectId == projectId && g.SceneId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.SceneId, (Guid?)null), ct);
                foreach (var scene in project.Scenes.ToList()) db.Scenes.Remove(scene);
                project.Scenes.Clear();
            }

            project.StoryText = r.Description.Trim();
            project.StyleId = r.StyleId ?? project.StyleId;
            project.Touch();
        }
        else
        {
            var (width, height) = StudioSettings.ParseResolution(settings.Current.DefaultVideoResolution, (768, 432));
            project = new Project
            {
                UserId = userId,
                Name = r.Title.Trim(),
                Description = $"Story video with {plan.Scenes.Count} scenes",
                StoryText = r.Description.Trim(),
                StyleId = r.StyleId,
                Width = width,
                Height = height,
                Fps = settings.Current.DefaultFps,
            };
            db.Projects.Add(project);
        }

        foreach (var character in characters.Where(c => project.Characters.All(pc => pc.Id != c.Id)))
        {
            project.Characters.Add(character);
        }

        var order = project.Scenes.Count;
        foreach (var planned in plan.Scenes)
        {
            var scene = new Scene
            {
                ProjectId = project.Id,
                Order = order++,
                Title = planned.Title,
                Description = planned.Description,
                Location = planned.Location,
                Action = planned.Action,
                Mood = planned.Mood,
                ImagePrompt = planned.ImagePrompt,
                VideoPrompt = planned.VideoPrompt,
                CameraMovement = planned.CameraMovement,
                DurationSeconds = planned.DurationSeconds,
                Narration = string.IsNullOrWhiteSpace(planned.Narration) ? null : planned.Narration,
            };
            foreach (var character in characters.Where(c => planned.CharacterNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)))
            {
                scene.Characters.Add(character);
            }

            project.Scenes.Add(scene);
        }

        await db.SaveChangesAsync(ct);
        return await projects.BuildDetailAsync(project.Id, ct);
    }
}
