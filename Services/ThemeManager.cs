using System;
using System.Windows;
using Application = System.Windows.Application;
using Microsoft.Win32;

namespace ClipManagerForWindows.Services;

public static class ThemeManager
{
    private const string DarkColorsUri = "Themes/DarkColors.xaml";
    private const string LightColorsUri = "Themes/LightColors.xaml";

    public static void ApplyTheme(string theme)
    {
        var isLight = theme switch
        {
            "Light" => true,
            "Dark" => false,
            _ => IsSystemLightTheme()
        };

        var uri = isLight ? LightColorsUri : DarkColorsUri;
        var newColors = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        // Remove any existing color dictionaries by Source URI match
        for (int i = mergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = mergedDictionaries[i].Source;
            if (src != null && (src.OriginalString.Contains("DarkColors") || src.OriginalString.Contains("LightColors")))
                mergedDictionaries.RemoveAt(i);
        }

        // Add at end for highest priority in WPF resource lookup
        mergedDictionaries.Add(newColors);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }
}
