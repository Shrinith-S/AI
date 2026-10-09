using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chitra.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Generations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SceneId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentGenerationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    FinalPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    PromptEnhanced = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StyleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CharacterIds = table.Column<string>(type: "TEXT", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Steps = table.Column<int>(type: "INTEGER", nullable: false),
                    CfgScale = table.Column<double>(type: "REAL", nullable: false),
                    Seed = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchSize = table.Column<int>(type: "INTEGER", nullable: false),
                    Sampler = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Scheduler = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Fps = table.Column<int>(type: "INTEGER", nullable: true),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MotionStrength = table.Column<double>(type: "REAL", nullable: true),
                    CameraMovement = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    InputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Warnings = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Generations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Template = table.Column<string>(type: "TEXT", nullable: false),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncursApiCharges = table.Column<bool>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Styles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    PromptPrefix = table.Column<string>(type: "TEXT", nullable: true),
                    PromptSuffix = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviewAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Styles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    IsLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenerationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProviderJobId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenerationJobs_Generations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "Generations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    StoryText = table.Column<string>(type: "TEXT", nullable: true),
                    StyleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImageModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    VideoModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalVideoAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CoverAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Styles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "Styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GenerationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Generations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "Generations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: true),
                    Mood = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    VideoPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    CameraMovement = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", nullable: true),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VideoAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NarrationAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastImageGenerationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastVideoGenerationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    Fps = table.Column<int>(type: "INTEGER", nullable: false),
                    Transition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TransitionSeconds = table.Column<double>(type: "REAL", nullable: false),
                    IncludeNarration = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeSubtitles = table.Column<bool>(type: "INTEGER", nullable: false),
                    MusicAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MusicVolume = table.Column<double>(type: "REAL", nullable: false),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoExports_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Age = table.Column<int>(type: "INTEGER", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Clothing = table.Column<string>(type: "TEXT", nullable: true),
                    Appearance = table.Column<string>(type: "TEXT", nullable: true),
                    Personality = table.Column<string>(type: "TEXT", nullable: true),
                    PromptOverride = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredSeed = table.Column<long>(type: "INTEGER", nullable: true),
                    ReferenceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_MediaAssets_ReferenceAssetId",
                        column: x => x.ReferenceAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCharacters",
                columns: table => new
                {
                    CharactersId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCharacters", x => new { x.CharactersId, x.ProjectsId });
                    table.ForeignKey(
                        name: "FK_ProjectCharacters_Characters_CharactersId",
                        column: x => x.CharactersId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectCharacters_Projects_ProjectsId",
                        column: x => x.ProjectsId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneCharacters",
                columns: table => new
                {
                    CharactersId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenesId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneCharacters", x => new { x.CharactersId, x.ScenesId });
                    table.ForeignKey(
                        name: "FK_SceneCharacters_Characters_CharactersId",
                        column: x => x.CharactersId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SceneCharacters_Scenes_ScenesId",
                        column: x => x.ScenesId,
                        principalTable: "Scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_ReferenceAssetId",
                table: "Characters",
                column: "ReferenceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_UserId_Name",
                table: "Characters",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationJobs_GenerationId",
                table: "GenerationJobs",
                column: "GenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationJobs_Status",
                table: "GenerationJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Generations_SceneId",
                table: "Generations",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_Generations_Status",
                table: "Generations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Generations_UserId_CreatedAt",
                table: "Generations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_GenerationId",
                table: "MediaAssets",
                column: "GenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ProjectId",
                table: "MediaAssets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_UserId_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCharacters_ProjectsId",
                table: "ProjectCharacters",
                column: "ProjectsId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_StyleId",
                table: "Projects",
                column: "StyleId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_UserId_UpdatedAt",
                table: "Projects",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConfigurations_Name_Kind",
                table: "ProviderConfigurations",
                columns: new[] { "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SceneCharacters_ScenesId",
                table: "SceneCharacters",
                column: "ScenesId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ProjectId_Order",
                table: "Scenes",
                columns: new[] { "ProjectId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExternalId",
                table: "Users",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoExports_ProjectId_CreatedAt",
                table: "VideoExports",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "GenerationJobs");

            migrationBuilder.DropTable(
                name: "ProjectCharacters");

            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropTable(
                name: "ProviderConfigurations");

            migrationBuilder.DropTable(
                name: "SceneCharacters");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "VideoExports");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "Generations");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Styles");
        }
    }
}
