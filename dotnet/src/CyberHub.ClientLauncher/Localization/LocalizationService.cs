using System.ComponentModel;
using System.Windows;
using CyberHub.Shared.Localization;

namespace CyberHub.ClientLauncher.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private string _languageCode;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService(string initialLanguage = "ar")
    {
        _languageCode = NormalizeLanguage(initialLanguage);
    }

    public string CurrentLanguage => _languageCode;
    public bool IsArabic => string.Equals(_languageCode, "ar", StringComparison.OrdinalIgnoreCase);
    public FlowDirection CurrentFlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => LocalizationCatalog.Get(_languageCode, key);

    public void SetLanguage(string languageCode)
    {
        string normalized = NormalizeLanguage(languageCode);
        if (string.Equals(_languageCode, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _languageCode = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArabic)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentFlowDirection)));
    }

    public string ToggleLanguage()
    {
        SetLanguage(IsArabic ? "en" : "ar");
        return _languageCode;
    }

    private static string NormalizeLanguage(string? code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
}
