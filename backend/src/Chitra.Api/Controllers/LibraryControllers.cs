using Chitra.Application.Dtos;
using Chitra.Application.Library;
using Microsoft.AspNetCore.Mvc;

namespace Chitra.Api.Controllers;

[ApiController]
[Route("api/characters")]
public sealed class CharactersController(CharacterService characters) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CharacterDto>> List(CancellationToken ct) => characters.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<CharacterDto> Get(Guid id, CancellationToken ct) => characters.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CharacterDto>> Create(UpsertCharacterRequest request, CancellationToken ct)
    {
        var created = await characters.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<CharacterDto> Update(Guid id, UpsertCharacterRequest request, CancellationToken ct) => characters.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await characters.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/styles")]
public sealed class StylesController(StyleService styles) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StyleDto>> List(CancellationToken ct) => styles.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<StyleDto>> Create(UpsertStyleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await styles.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<StyleDto> Update(Guid id, UpsertStyleRequest request, CancellationToken ct) => styles.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await styles.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/prompt-templates")]
public sealed class PromptTemplatesController(PromptTemplateService templates) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PromptTemplateDto>> List(CancellationToken ct) => templates.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<PromptTemplateDto>> Create(UpsertPromptTemplateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await templates.CreateAsync(request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await templates.DeleteAsync(id, ct);
        return NoContent();
    }
}
