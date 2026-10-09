// TypeScript mirrors of the API DTOs (enums are serialized as strings).

export type GenerationType = 'TextToImage' | 'TextToVideo' | 'ImageToVideo';
export type GenerationStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled';
export type MediaType = 'Image' | 'Video' | 'Audio';
export type MediaSource = 'Generated' | 'Uploaded' | 'Narration' | 'Export';

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface MediaAsset {
  id: string;
  type: MediaType;
  source: MediaSource;
  fileName: string;
  originalFileName: string | null;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
  url: string;
  downloadUrl: string;
  thumbnailUrl: string | null;
  projectId: string | null;
  generationId: string | null;
  createdAt: string;
}

export interface Generation {
  id: string;
  type: GenerationType;
  status: GenerationStatus;
  progress: number;
  statusMessage: string | null;
  prompt: string;
  finalPrompt: string;
  negativePrompt: string | null;
  promptEnhanced: boolean;
  modelId: string;
  providerName: string;
  styleId: string | null;
  characterIds: string[];
  width: number;
  height: number;
  steps: number;
  cfgScale: number;
  seed: number;
  batchSize: number;
  sampler: string | null;
  scheduler: string | null;
  durationSeconds: number | null;
  fps: number | null;
  frameCount: number | null;
  motionStrength: number | null;
  cameraMovement: string | null;
  inputAssetId: string | null;
  projectId: string | null;
  sceneId: string | null;
  parentGenerationId: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  warnings: string[];
  outputs: MediaAsset[];
}

export interface CreateImageRequest {
  prompt: string;
  negativePrompt?: string | null;
  modelId?: string | null;
  width?: number;
  height?: number;
  steps?: number;
  cfgScale?: number;
  seed?: number | null;
  batchSize?: number;
  sampler?: string | null;
  scheduler?: string | null;
  styleId?: string | null;
  characterIds?: string[];
  enhancePrompt?: boolean;
  projectId?: string | null;
}

export interface CreateVideoRequest {
  prompt: string;
  negativePrompt?: string | null;
  modelId?: string | null;
  durationSeconds: number;
  width?: number;
  height?: number;
  fps?: number;
  steps?: number;
  cfgScale?: number;
  seed?: number | null;
  motionStrength: number;
  cameraMovement?: string | null;
  styleId?: string | null;
  characterIds?: string[];
  enhancePrompt?: boolean;
  projectId?: string | null;
  inputAssetId?: string | null;
}

export interface ModelFileStatus {
  role: string;
  fileName: string;
  folder: string;
  sizeBytes: number;
  downloadUrl: string | null;
  installed: boolean | null;
  installedAs: string | null;
}

export interface ModelDownloadStatus {
  modelId: string;
  state: 'Pending' | 'Downloading' | 'Completed' | 'Failed' | 'Cancelled';
  currentFile: string | null;
  bytesDownloaded: number;
  totalBytes: number;
  progress: number;
  error: string | null;
}

export interface AiModel {
  id: string;
  name: string;
  family: string;
  provider: string;
  description: string | null;
  capabilities: GenerationType[];
  totalSizeBytes: number;
  minVramGb: number;
  recommendedVramGb: number;
  installState: 'Installed' | 'Partial' | 'NotInstalled' | 'Unknown' | 'OnDemand';
  files: ModelFileStatus[];
  readiness: 'Recommended' | 'Limited' | 'Insufficient' | 'Unknown';
  readinessReason: string;
  requiredCustomNodes: string[];
  notes: string | null;
  license: string | null;
  homepage: string | null;
  defaults: { width: number; height: number; steps: number; cfg: number; nativeFps: number; maxFrames: number };
  download: ModelDownloadStatus | null;
}

export interface ModelDownloadPlan {
  modelId: string;
  modelName: string;
  targetDirectory: string | null;
  canDownload: boolean;
  reason: string | null;
  totalBytesToDownload: number;
  freeDiskBytes: number;
  files: {
    fileName: string;
    folder: string;
    url: string | null;
    sizeBytes: number;
    sizeIsExact: boolean;
    alreadyPresent: boolean;
    targetPath: string | null;
  }[];
}

export interface ComponentStatus {
  name: string;
  isAvailable: boolean;
  message: string;
  detail: string | null;
}

export interface GpuInfo {
  name: string;
  vramTotalMb: number;
  vramFreeMb: number | null;
  driverVersion: string | null;
  source: string;
}

export interface SystemInfo {
  os: string;
  machineName: string;
  cpuName: string;
  cpuCores: number;
  totalRamBytes: number;
  availableRamBytes: number | null;
  gpus: GpuInfo[];
  disk: { path: string; totalBytes: number; freeBytes: number } | null;
  nvidiaSmiAvailable: boolean;
  notes: string[];
}

export interface SystemStatus {
  localOnly: boolean;
  costNotice: string;
  aiProvider: ComponentStatus;
  ffmpeg: ComponentStatus;
  tts: ComponentStatus;
  storage: ComponentStatus;
  system: SystemInfo;
  hardwareRating: 'Recommended' | 'Limited' | 'Insufficient';
  hardwareRatingReason: string;
  queuedGenerations: number;
  processingGenerations: number;
}

export interface StudioSettings {
  localOnly: boolean;
  aiProvider: string;
  comfyUiUrl: string;
  comfyUiModelsPath: string | null;
  defaultImageModel: string;
  defaultVideoModel: string;
  defaultImageToVideoModel: string;
  defaultImageResolution: string;
  defaultVideoResolution: string;
  defaultFps: number;
  ffmpegPath: string;
  maxConcurrentGenerations: number;
  ttsProvider: string;
  piperPath: string;
  piperVoicesPath: string | null;
  piperDefaultVoice: string | null;
  coquiUrl: string;
  promptEnhancer: string;
  theme: 'dark' | 'light' | 'system';
  imageTimeoutMinutes: number;
  videoTimeoutMinutes: number;
  kaggleUsername: string | null;
  kaggleKernelSlug: string;
  kagglePythonPath: string;
  kaggleAccelerator: string;
  kaggleMaxJobsPerRun: number;
  kaggleBatchWindowSeconds: number;
  kaggleRunTimeoutMinutes: number;
}

export interface SettingsResponse {
  settings: StudioSettings;
  storagePath: string;
  databaseProvider: string;
  authMode: string;
}

export interface PromptEnhancement {
  original: string;
  prompt: string;
  negativePrompt: string | null;
  additions: string[];
  enhancer: string;
}

export interface Style {
  id: string;
  name: string;
  description: string | null;
  promptPrefix: string | null;
  promptSuffix: string | null;
  negativePrompt: string | null;
  isBuiltIn: boolean;
}

export interface PromptTemplate {
  id: string;
  name: string;
  category: string;
  template: string;
  negativePrompt: string | null;
  description: string | null;
  isBuiltIn: boolean;
}

export interface Character {
  id: string;
  name: string;
  description: string | null;
  age: number | null;
  gender: string | null;
  clothing: string | null;
  appearance: string | null;
  personality: string | null;
  promptOverride: string | null;
  preferredSeed: number | null;
  referenceAsset: MediaAsset | null;
  generatedPrompt: string;
  createdAt: string;
  updatedAt: string;
}

export interface SceneGenerationState {
  id: string;
  status: GenerationStatus;
  progress: number;
  statusMessage: string | null;
  errorMessage: string | null;
}

export interface Scene {
  id: string;
  projectId: string;
  order: number;
  title: string;
  description: string | null;
  location: string | null;
  action: string | null;
  mood: string | null;
  imagePrompt: string;
  videoPrompt: string;
  negativePrompt: string | null;
  cameraMovement: string | null;
  durationSeconds: number;
  narration: string | null;
  characterIds: string[];
  image: MediaAsset | null;
  video: MediaAsset | null;
  narrationAudio: MediaAsset | null;
  imageGeneration: SceneGenerationState | null;
  videoGeneration: SceneGenerationState | null;
}

export interface ProjectSummary {
  id: string;
  name: string;
  description: string | null;
  sceneCount: number;
  characterCount: number;
  assetCount: number;
  coverUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProjectDetail {
  id: string;
  name: string;
  description: string | null;
  storyText: string | null;
  styleId: string | null;
  imageModelId: string | null;
  videoModelId: string | null;
  width: number;
  height: number;
  fps: number;
  finalVideo: MediaAsset | null;
  scenes: Scene[];
  characters: Character[];
  createdAt: string;
  updatedAt: string;
}

export interface VideoExport {
  id: string;
  projectId: string;
  status: GenerationStatus;
  progress: number;
  statusMessage: string | null;
  resolution: number;
  fps: number;
  transition: string;
  transitionSeconds: number;
  includeNarration: boolean;
  includeSubtitles: boolean;
  musicAssetId: string | null;
  musicVolume: number;
  output: MediaAsset | null;
  errorMessage: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface TtsStatus {
  provider: string;
  isAvailable: boolean;
  message: string;
  voices: { id: string; name: string; language: string | null }[];
}

export const isTerminal = (s: GenerationStatus) => s === 'Completed' || s === 'Failed' || s === 'Cancelled';
