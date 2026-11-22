using System.ComponentModel;
using System.IO;
using System.Windows;
using CyberHub.Shared.Localization;

namespace CyberHub.AdminPanel.Localization;

public sealed class AdminLocalizationService : INotifyPropertyChanged
{
    private readonly string _langFilePath;
    private string _languageCode;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AdminLocalizationService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHub");
        Directory.CreateDirectory(dir);
        _langFilePath = Path.Combine(dir, "admin-language.txt");

        string saved = File.Exists(_langFilePath) ? File.ReadAllText(_langFilePath).Trim() : "ar";
        _languageCode = string.Equals(saved, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
    }

    public string CurrentLanguage => _languageCode;
    public bool IsArabic => string.Equals(_languageCode, "ar", StringComparison.OrdinalIgnoreCase);
    public FlowDirection CurrentFlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => LocalizationCatalog.Get(_languageCode, key);

    public void ToggleLanguage()
    {
        _languageCode = IsArabic ? "en" : "ar";
        try
        {
            File.WriteAllText(_langFilePath, _languageCode);
        }
        catch (Exception)
        {
            // Non-fatal
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArabic)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentFlowDirection)));
    }
}
