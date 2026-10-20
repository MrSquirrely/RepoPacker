using System;
using System.Windows;
using System.Windows.Media;

namespace RepoPacker.UI;

public partial class MarketplaceWindow : Window {
	public MarketplaceWindow(string personalAccessToken) {
		InitializeComponent();
		DataContext = new MarketplaceViewModel(personalAccessToken);
		StateChanged += MarketplaceWindow_StateChanged;
	}

	private void MarketplaceWindow_StateChanged(object? sender, EventArgs e) {
		if (MaximizeIcon != null) {
			MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
				                                   ? "M2 0.5 H9.5 V8 H8 V2 H2 Z M0.5 2.5 H7.5 V9.5 H0.5 Z"
				                                   : "M0.5 0.5 H9.5 V9.5 H0.5 Z");
		}
	}

	private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

	private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) {
		switch (WindowState) {
			case WindowState.Maximized:
				SystemCommands.RestoreWindow(this);
				break;
			case WindowState.Normal:
			case WindowState.Minimized:
				break;
			default:
				SystemCommands.MaximizeWindow(this);
				break;
		}
	}

	private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}