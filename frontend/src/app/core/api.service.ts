import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AiModel,
  Character,
  CreateImageRequest,
  CreateVideoRequest,
  Generation,
  GenerationType,
  MediaAsset,
  MediaType,
  ModelDownloadPlan,
  ModelDownloadStatus,
  PagedResult,
  ProjectDetail,
  ProjectSummary,
  PromptEnhancement,
  PromptTemplate,
  Scene,
  SettingsResponse,
  StudioSettings,
  Style,
  SystemStatus,
  TtsStatus,
  VideoExport,
} from './models';

/** Thin promise-based client for the Chitra REST API. All URLs are relative (proxied in dev, same origin in prod). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  private get<T>(url: string, params?: Record<string, string | number | boolean | null | undefined>) {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params ?? {})) {
      if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
    }
    return firstValueFrom(this.http.get<T>(url, { params: p }));
  }
  private post<T>(url: string, body: unknown = {}) {
    return firstValueFrom(this.http.post<T>(url, body));
  }
  private put<T>(url: string, body: unknown) {
    return firstValueFrom(this.http.put<T>(url, body));
  }
  private delete(url: string) {
    return firstValueFrom(this.http.delete<void>(url));
  }

  // Generations
  createImage = (r: CreateImageRequest) => this.post<Generation>('/api/generations/image', r);
  createVideo = (r: CreateVideoRequest) => this.post<Generation>('/api/generations/video', r);
  createImageToVideo = (r: CreateVideoRequest) => this.post<Generation>('/api/generations/image-to-video', r);
  generation = (id: string) => this.get<Generation>(`/api/generations/${id}`);
  generations = (q: { filter?: string; projectId?: string; search?: string; page?: number; pageSize?: number }) =>
    this.get<PagedResult<Generation>>('/api/generations', q);
  cancelGeneration = (id: string) => this.post<Generation>(`/api/generations/${id}/cancel`);
  regenerate = (id: string) => this.post<Generation>(`/api/generations/${id}/regenerate`, { newSeed: false });
  variation = (id: string) => this.post<Generation>(`/api/generations/${id}/variation`);
  deleteGeneration = (id: string) => this.delete(`/api/generations/${id}`);

  // Media
  upload(file: File, projectId?: string | null) {
    const form = new FormData();
    form.append('file', file, file.name);
    if (projectId) form.append('projectId', projectId);
    return firstValueFrom(this.http.post<MediaAsset>('/api/media/upload', form));
  }
  media = (q: { type?: MediaType; projectId?: string; page?: number; pageSize?: number }) =>
    this.get<PagedResult<MediaAsset>>('/api/media', q);
  asset = (id: string) => this.get<MediaAsset>(`/api/media/${id}`);
  lastFrame = (id: string) => this.post<MediaAsset>(`/api/media/${id}/last-frame`);
  deleteAsset = (id: string) => this.delete(`/api/media/${id}`);

  // Prompts, models, system, settings
  enhancePrompt = (prompt: string, type: GenerationType, styleHint?: string | null, variation = 0) =>
    this.post<PromptEnhancement>('/api/prompts/enhance', { prompt, type, styleHint, variation });
  cameraMovements = () => this.get<string[]>('/api/prompts/camera-movements');
  models = (capability?: GenerationType) => this.get<AiModel[]>('/api/models', { capability });
  downloadPlan = (id: string) => this.get<ModelDownloadPlan>(`/api/models/${id}/download-plan`);
  startDownload = (id: string) => this.post<ModelDownloadStatus>(`/api/models/${id}/download`, { confirm: true });
  cancelDownload = (id: string) => this.post<void>(`/api/models/${id}/download/cancel`);
  status = () => this.get<SystemStatus>('/api/system/status');
  settings = () => this.get<SettingsResponse>('/api/settings');
  saveSettings = (s: StudioSettings) => this.put<SettingsResponse>('/api/settings', s);

  // Styles & templates
  styles = () => this.get<Style[]>('/api/styles');
  createStyle = (s: Partial<Style>) => this.post<Style>('/api/styles', s);
  updateStyle = (id: string, s: Partial<Style>) => this.put<Style>(`/api/styles/${id}`, s);
  deleteStyle = (id: string) => this.delete(`/api/styles/${id}`);
  templates = () => this.get<PromptTemplate[]>('/api/prompt-templates');

  // Characters
  characters = () => this.get<Character[]>('/api/characters');
  character = (id: string) => this.get<Character>(`/api/characters/${id}`);
  createCharacter = (c: Partial<Character> & { referenceAssetId?: string | null }) => this.post<Character>('/api/characters', c);
  updateCharacter = (id: string, c: Partial<Character> & { referenceAssetId?: string | null }) =>
    this.put<Character>(`/api/characters/${id}`, c);
  deleteCharacter = (id: string) => this.delete(`/api/characters/${id}`);

  // Projects & scenes
  projects = () => this.get<ProjectSummary[]>('/api/projects');
  project = (id: string) => this.get<ProjectDetail>(`/api/projects/${id}`);
  createProject = (p: Record<string, unknown>) => this.post<ProjectDetail>('/api/projects', p);
  updateProject = (id: string, p: Record<string, unknown>) => this.put<ProjectDetail>(`/api/projects/${id}`, p);
  deleteProject = (id: string) => this.delete(`/api/projects/${id}`);
  attachToProject = (projectId: string, body: { assetId?: string; generationId?: string }) =>
    this.post<void>(`/api/projects/${projectId}/assets`, body);
  generateStory = (r: Record<string, unknown>) => this.post<ProjectDetail>('/api/stories/generate', r);
  addScene = (projectId: string, s: Partial<Scene>) => this.post<Scene>(`/api/projects/${projectId}/scenes`, s);
  updateScene = (id: string, s: Partial<Scene>) => this.put<Scene>(`/api/scenes/${id}`, s);
  deleteScene = (id: string) => this.delete(`/api/scenes/${id}`);
  duplicateScene = (id: string) => this.post<Scene>(`/api/scenes/${id}/duplicate`);
  reorderScenes = (projectId: string, sceneIds: string[]) =>
    this.post<Scene[]>(`/api/projects/${projectId}/scenes/reorder`, { sceneIds });
  generateScene = (id: string, body: { kind: 'image' | 'video' | 'animate'; modelId?: string | null; motionStrength?: number; enhancePrompt?: boolean }) =>
    this.post<Generation>(`/api/scenes/${id}/generate`, body);
  setSceneMedia = (id: string, body: { imageAssetId?: string | null; videoAssetId?: string | null }) =>
    this.put<Scene>(`/api/scenes/${id}/media`, body);
  sceneNarration = (id: string, voiceId?: string | null) => this.post<Scene>(`/api/scenes/${id}/narration`, { voiceId });

  // Exports & TTS
  createExport = (projectId: string, r: Record<string, unknown>) => this.post<VideoExport>(`/api/projects/${projectId}/exports`, r);
  exports = (projectId: string) => this.get<VideoExport[]>(`/api/projects/${projectId}/exports`);
  cancelExport = (id: string) => this.post<VideoExport>(`/api/exports/${id}/cancel`);
  ttsStatus = () => this.get<TtsStatus>('/api/tts/status');
  synthesize = (text: string, voiceId?: string | null, projectId?: string | null) =>
    this.post<MediaAsset>('/api/tts/synthesize', { text, voiceId, projectId });
}
