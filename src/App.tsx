import { useEffect, useState } from 'react';
import ClientLauncherView from './components/ClientLauncherView';
import { getStoredLanguage, saveLanguage } from './i18n/translations';
import type { LanguageCode } from './types';

export default function App() {
  const [language, setLanguage] = useState<LanguageCode>(() => getStoredLanguage());

  useEffect(() => {
    document.documentElement.lang = language;
    document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
    saveLanguage(language);
  }, [language]);

  const toggleLanguage = () => {
    setLanguage((prev) => (prev === 'ar' ? 'en' : 'ar'));
  };

  return (
    <ClientLauncherView
      language={language}
      onToggleLanguage={toggleLanguage}
    />
  );
}
