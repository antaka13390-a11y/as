import type { LanguageCode } from '../types';

export const TRANSLATIONS = {
  ar: {
    brand: 'سايبر هب',
    subtitle: 'مشغل ألعاب الصالة',
    switchLang: 'EN / عربي',
    navHome: 'الرئيسية',
    navRecent: 'لُعبت مؤخراً',
    navCategories: 'التصنيفات',
    syncCatalog: 'مزامنة المكتبة',
    readOnlyTag: 'قراءة فقط',
    sectionHome: 'الألعاب المميزة والتشغيل السريع',
    sectionRecent: 'لُعبت مؤخراً',
    searchPlaceholder: 'ابحث عن لعبة أو استوديو...',
    online: 'أونلاين',
    offline: 'أوفلاين',
    playNow: 'العب الآن',
    playGame: 'تشغيل اللعبة',
    details: 'التفاصيل',
    close: 'إغلاق',
    dismiss: 'إخفاء',
    gameDetails: 'تفاصيل اللعبة',
    connectionMode: 'نوع الاتصال',
    launchMode: 'طريقة التشغيل',
    categoriesLabel: 'التصنيفات',
    storageVolume: 'مسار التخزين',
    networkUncPath: 'مسار الشبكة',
    gameInProgressTitle: 'اللعبة قيد التشغيل',
    activeGamePrefix: 'اللعبة النشطة:',
    sessionLockNotice: 'تم قفل تشغيل الألعاب على هذا الجهاز حتى انتهاء الجلسة الحالية بالكامل.',
    duplicateBlocked: 'لا يمكن تشغيل لعبة ثانية أثناء تشغيل لعبة أخرى على هذا الجهاز.',
    closeGameReturn: 'إنهاء اللعبة والعودة للمشغل',
    childProcessTracking: 'جارٍ مراقبة عملية اللعبة والعمليات الفرعية المرتبطة بها...',
    offlineWarning: 'تعذر الوصول إلى الخادم المركزي. يتم عرض آخر كتالوج متزامن محلياً.',
    noGamesMatch: 'لا توجد ألعاب مطابقة لهذا التصنيف أو البحث.',
    noGamesHint: 'جرّب مسح نص البحث أو اختيار تصنيف آخر.',
    playedAgo: 'لُعبت مؤخراً',
  },
  en: {
    brand: 'CYBER HUB',
    subtitle: 'Station Launcher',
    switchLang: 'عربي / EN',
    navHome: 'Home',
    navRecent: 'Recently Played',
    navCategories: 'Categories',
    syncCatalog: 'Sync Catalog',
    readOnlyTag: 'Read-Only',
    sectionHome: 'Featured & Quick Launch',
    sectionRecent: 'Recently Played',
    searchPlaceholder: 'Search games, categories, studios...',
    online: 'Online',
    offline: 'Offline',
    playNow: 'Play Now',
    playGame: 'Play Game',
    details: 'Details',
    close: 'Close',
    dismiss: 'Dismiss',
    gameDetails: 'Game Details',
    connectionMode: 'Connection Mode',
    launchMode: 'Launch Mode',
    categoriesLabel: 'Categories',
    storageVolume: 'Storage Volume',
    networkUncPath: 'Network UNC Target',
    gameInProgressTitle: 'GAME IN PROGRESS',
    activeGamePrefix: 'Active Game:',
    sessionLockNotice: 'Game launching is locked on this client PC until the active game session exits cleanly.',
    duplicateBlocked: 'Cannot launch a second game while another game session is active.',
    closeGameReturn: 'Close Game & Return to Launcher',
    childProcessTracking: 'Monitoring root launcher and spawned game child processes...',
    offlineWarning: 'Central ServerService unreachable. Displaying last synchronized station catalog.',
    noGamesMatch: 'No games match this filter',
    noGamesHint: 'Clear your search query or choose another category.',
    playedAgo: 'Played recently',
  },
} as const;

export function getStoredLanguage(): LanguageCode {
  try {
    const saved = window.localStorage.getItem('cyberhub_lang');
    return saved === 'en' ? 'en' : 'ar';
  } catch {
    return 'ar';
  }
}

export function saveLanguage(lang: LanguageCode): void {
  try {
    window.localStorage.setItem('cyberhub_lang', lang);
  } catch {
    // Ignore storage errors
  }
}
