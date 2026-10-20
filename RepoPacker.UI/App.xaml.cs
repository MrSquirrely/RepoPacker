using System;
using System.Windows;

namespace RepoPacker.UI;

public partial class App : Application {
	public static void SetTheme(bool isDark) {
		string themeFileName = isDark ? "DarkTheme.xaml" : "LightTheme.xaml";
		Uri themeUri = new Uri($"pack://application:,,,/Themes/{themeFileName}", UriKind.Absolute);

		ResourceDictionary newThemeDictionary = new ResourceDictionary {
			Source = themeUri
		};

		ResourceDictionary applicationResources = Current.Resources;
		applicationResources.MergedDictionaries.Clear();
		applicationResources.MergedDictionaries.Add(newThemeDictionary);
	}
}