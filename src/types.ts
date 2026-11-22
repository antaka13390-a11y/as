export type LaunchType = 'DirectExecutable' | 'Steam' | 'RiotClient' | 'ExternalLauncher';
export type NetworkMode = 'Online' | 'Offline';
export type CategoryFilterType = 'Custom' | 'AllGames' | 'OnlineGames' | 'OfflineGames';
export type LanguageCode = 'ar' | 'en';

export type ThemeBackgroundType = 'Solid' | 'Gradient' | 'Image' | 'Video';
export type CardLayoutStyle = 'Standard' | 'LargeArtwork' | 'Compact';
export type CardShapeStyle = 'Rounded' | 'Sharp' | 'CutCorner';
export type CardHoverAnimation = 'ScaleUp' | 'Glow' | 'Lift' | 'None';
export type CardBorderEffect = 'Subtle' | 'NeonGlow' | 'None';
export type PlayButtonStyle = 'Gradient' | 'Solid' | 'Outline';
export type SidebarPosition = 'Start' | 'Left' | 'Right';
export type NavigationStyle = 'VerticalRail' | 'CompactIcons' | 'TopTabs';
export type LogoPosition = 'SidebarTop' | 'TopBarStart' | 'TopBarCenter';
export type BackgroundImageScaling = 'Cover' | 'Contain';

export interface ThemeConfig {
  id: string;
  name: string;
  presetKey: string;
  isDefault: boolean;
  isPublished: boolean;
  version: number;
  updatedAtUtc: string;

  backgroundType: ThemeBackgroundType;
  backgroundImageAssetPath: string;
  backgroundVideoAssetPath: string;
  staticFallbackImagePath: string;
  videoBackgroundEnabled: boolean;
  performanceFriendlyMode: boolean;
  solidBackgroundColor: string;
  gradientStartColor: string;
  gradientEndColor: string;
  gradientAngleDegrees: number;
  backgroundBrightness: number;
  backgroundOpacity: number;
  overlayOpacity: number;
  blurIntensity: number;
  imagePosition: string;
  imageScaling: BackgroundImageScaling;

  primaryAccentColor: string;
  secondaryAccentColor: string;
  mainBackgroundColor: string;
  sidebarBackgroundColor: string;
  gameCardBackgroundColor: string;
  primaryTextColor: string;
  secondaryTextColor: string;
  buttonBackgroundColor: string;
  buttonTextColor: string;
  hoverStateColor: string;
  borderColor: string;
  activeNavItemColor: string;

  cardLayoutStyle: CardLayoutStyle;
  cardShape: CardShapeStyle;
  cardBorderRadius: number;
  cardWidth: number;
  cardHeight: number;
  coverImageHeight: number;
  cardSpacing: number;
  cardShadowIntensity: number;
  hoverAnimation: CardHoverAnimation;
  borderEffect: CardBorderEffect;
  titleFontSize: number;
  showDescriptionOnCard: boolean;
  playButtonStyle: PlayButtonStyle;
  gridColumns: number;

  sidebarPosition: SidebarPosition;
  sidebarWidth: number;
  navigationStyle: NavigationStyle;
  topBarAppearance: string;
  searchBarStyle: string;
  gameGridLayout: string;
  categoryNavLayout: string;
  headerSpacing: number;
  mainContentSpacing: number;

  applicationNameEn: string;
  applicationNameAr: string;
  cyberCafeNameEn: string;
  cyberCafeNameAr: string;
  welcomeTitleEn: string;
  welcomeTitleAr: string;
  loadingScreenTitleEn: string;
  loadingScreenTitleAr: string;
  windowTitleEn: string;
  windowTitleAr: string;
  logoAssetPath: string;
  appIconAssetPath: string;
  loadingLogoAssetPath: string;
  logoSizePx: number;
  logoPosition: LogoPosition;
}

export interface ThemeAssetMetadata {
  id: string;
  fileName: string;
  originalFileName: string;
  mediaKind: string;
  contentType: string;
  sizeBytes: number;
  relativeUrl: string;
  uploadedAtUtc: string;
}

export interface PublishedThemePayload {
  publishedVersion: number;
  publishedAtUtc: string;
  theme: ThemeConfig;
  assets: ThemeAssetMetadata[];
}

export interface CategoryEntry {
  id: string;
  slug: string;
  nameEn: string;
  nameAr: string;
  iconKey: string;
  bannerImagePath: string;
  displayOrder: number;
  isSystem: boolean;
  filterType: CategoryFilterType;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface StorageLocation {
  id: string;
  displayName: string;
  uncRootPath: string;
  description: string;
  isReadOnlyForClients: boolean;
}

export interface GameEntry {
  id: string;
  name: string;
  category: string;
  categoryIds: string[];
  networkMode: NetworkMode;
  description: string;
  developer: string;
  coverImagePath: string;
  heroImagePath: string;
  storageLocationId: string;
  executablePath: string;
  launchArguments: string;
  launchType: LaunchType;
  externalAppId: string;
  processNameToMonitor: string;
  isVisible: boolean;
  isFeatured: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CatalogSnapshot {
  catalogVersion: number;
  publishedAtUtc: string;
  serverHostname: string;
  storageLocations: StorageLocation[];
  categories: CategoryEntry[];
  games: GameEntry[];
  publishedTheme?: PublishedThemePayload;
}

export interface AdminAuditLog {
  id: number;
  timestampUtc: string;
  action: string;
  targetGameId: string;
  targetGameName: string;
  workstationName: string;
  sourceIp: string;
  details: string;
}

export interface AdminStatus {
  isConfigured: boolean;
  usesEnvOverride: boolean;
  workstationAuthorized: boolean;
  detectedWorkstation: string;
  detectedClientIp: string;
  totalGames: number;
  visibleGames: number;
  totalCategories: number;
  lastBackupUtc: string;
}

export interface SolutionFileItem {
  path: string;
  sizeBytes: number;
}
