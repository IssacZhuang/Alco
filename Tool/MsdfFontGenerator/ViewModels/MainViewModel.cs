using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform.Storage;

namespace MsdfFontGenerator.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedFontPath = string.Empty;
    
    [ObservableProperty]
    private string _selectedOutputPath = string.Empty;
    
    [ObservableProperty]
    private bool _isGenerating = false;

    public IStorageProvider? StorageProvider { get; set; }
    
    public ObservableCollection<LanguageSelectionItem> AvailableLanguages { get; }

    public MainViewModel()
    {
        AvailableLanguages = new ObservableCollection<LanguageSelectionItem>
        {
            new("Basic", 1, "English, numbers and symbols (Basic Latin, Latin-1, Latin Extended-A, Cyrillic)"),
            new("Chinese", 2, "CJK Symbols, CJK Unified Ideographs"),
            new("Japanese", 4, "Hiragana, Katakana"),
            new("Korean", 8, "Hangul Compatibility Jamo, Hangul Syllables"),
            new("Cyrillic", 16, "Cyrillic scripts"),
            new("Greek", 32, "Greek and Coptic"),
            new("Thai", 64, "Thai characters"),
            new("Vietnamese", 128, "Vietnamese diacritics")
        };
        
        // Hook up selection change events
        foreach (var language in AvailableLanguages)
        {
            language.SelectionChanged += () => GenerateMsdfCommand.NotifyCanExecuteChanged();
        }
        
        // Select Basic by default
        AvailableLanguages[0].IsSelected = true;
    }

    [RelayCommand]
    private async Task SelectFontAsync()
    {
        if (StorageProvider == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Font File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TrueType Fonts")
                {
                    Patterns = new[] { "*.ttf" }
                }
            }
        });

        if (files.Count > 0)
        {
            SelectedFontPath = files[0].Path.LocalPath;
            GenerateMsdfCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task SelectOutputPathAsync()
    {
        if (StorageProvider == null) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            SelectedOutputPath = folders[0].Path.LocalPath;
            GenerateMsdfCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateMsdfAsync()
    {
        if (string.IsNullOrEmpty(SelectedFontPath) || string.IsNullOrEmpty(SelectedOutputPath))
            return;

        IsGenerating = true;
        GenerateMsdfCommand.NotifyCanExecuteChanged();
        
        try
        {
            var selectedLanguages = GetSelectedLanguages();
            
            // TODO: Implement MSDF generation logic here
            // Use: SelectedFontPath, SelectedOutputPath, selectedLanguages
            await Task.Delay(2000); // Placeholder for actual generation
        }
        finally
        {
            IsGenerating = false;
            GenerateMsdfCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanGenerate => !string.IsNullOrEmpty(SelectedFontPath) && 
                               !string.IsNullOrEmpty(SelectedOutputPath) && 
                               !IsGenerating &&
                               AvailableLanguages.Any(lang => lang.IsSelected);

    [RelayCommand]
    private void SelectAllLanguages()
    {
        foreach (var language in AvailableLanguages)
        {
            language.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllLanguages()
    {
        foreach (var language in AvailableLanguages)
        {
            language.IsSelected = false;
        }
    }

    public int GetSelectedLanguages()
    {
        return AvailableLanguages
            .Where(lang => lang.IsSelected)
            .Sum(lang => lang.Value);
    }
}
