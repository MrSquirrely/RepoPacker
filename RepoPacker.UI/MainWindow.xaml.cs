using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;

namespace RepoPacker.UI;

public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();
		StateChanged += MainWindow_StateChanged;
	}

	private void MainWindow_StateChanged(object? sender, EventArgs e) => 
		MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2 0.5 H9.5 V8 H8 V2 H2 Z M0.5 2.5 H7.5 V9.5 H0.5 Z" : "M0.5 0.5 H9.5 V9.5 H0.5 Z");

	private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
		Uri uri = new($"https://github.com/settings/tokens/new?description=RepoPacker-{Guid.NewGuid()}&scopes=repo");
		ProcessStartInfo startInfo = new ProcessStartInfo(uri.AbsoluteUri) {
			UseShellExecute = true
		};
		Process.Start(startInfo);
		e.Handled = true;
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