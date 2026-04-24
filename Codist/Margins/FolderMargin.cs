using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Codist.Controls;
using Codist.FileBrowser;
using Microsoft.VisualStudio.Text.Editor;
using R = Codist.Properties.Resources;

namespace Codist.Margins;

sealed partial class FolderMargin : IWpfTextViewMargin
{
	internal const string Name = nameof(FolderMargin);

	readonly StackPanel _Container;
	readonly ThemedToggleButton _FileButton;
	readonly ThemedToggleButton _SolutionButton;
	ThemedToggleButton _ProjectButton;
	readonly IWpfTextView _View;
	Popup _FilePopup;
	FileList _FileList;
	CancellationTokenSource _CancellationTokenSource;

	public FrameworkElement VisualElement => _Container;
	public double MarginSize => _FileButton.RenderSize.Height;
	public bool Enabled => true;

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	public FolderMargin(IWpfTextView view) {
		_Container = new StackPanel {
			Orientation = Orientation.Horizontal,
			Resources = SharedDictionaryManager.ThemedControls,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = WpfHelper.SmallHorizontalMargin,
		};
		if (!String.IsNullOrEmpty(ServicesHelper.Instance.DTE.Solution.FullName)) {
			_SolutionButton = new ThemedToggleButton(IconIds.GoToSolutionFolder, R.CMD_GoToSolutionFolder, OnSolutionClick) {
				BorderThickness = WpfHelper.NoMargin,
				Background = Brushes.Transparent,
				Padding = WpfHelper.TinyMargin,
				MinHeight = 10,
			};
			_Container.Add(_SolutionButton);
			_Container.Loaded += AddProjectButtonOnLoaded;
		}
		_FileButton = new ThemedToggleButton(IconIds.Folder, R.CMDT_ClickToViewFolder, OnFolderClick) {
			BorderThickness = WpfHelper.NoMargin,
			Background = Brushes.Transparent,
			Padding = WpfHelper.TinyMargin,
			MinHeight = 10
		};
		_Container.Add(_FileButton);
		_View = view;
	}

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.EventHandler)]
	void AddProjectButtonOnLoaded(object sender, RoutedEventArgs e) {
		_Container.Loaded -= AddProjectButtonOnLoaded;
		var project = ServicesHelper.Instance.DTE.ActiveDocument?.ProjectItem?.ContainingProject;
		if (project?.IsMiscOrProjectFolder() != false) {
			return;
		}
		_ProjectButton = new ThemedToggleButton(FileSystemItem.GetFileIconId(System.IO.Path.GetExtension(project.UniqueName)), R.CMD_GoToProjectFolder, OnProjectClick) {
			BorderThickness = WpfHelper.NoMargin,
			Background = Brushes.Transparent,
			Padding = WpfHelper.TinyMargin,
			MinHeight = 10,
		};
		_Container.Children.Insert(_Container.Children.Count - 1, _ProjectButton);
	}

	void OnSolutionClick(object sender, RoutedEventArgs e) {
		if (_SolutionButton.IsChecked != true) {
			return;
		}
		_FileButton.IsChecked = false;
		_ProjectButton?.IsChecked = false;
		CreateFilePopup();
		_FileList.InitCurrentFile();
		_FileList.LoadSolutionDirectoryAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_FilePopup.IsOpen = true;
	}

	void OnProjectClick(object sender, RoutedEventArgs e) {
		if (_ProjectButton.IsChecked != true) {
			return;
		}
		_SolutionButton?.IsChecked = false;
		_FileButton?.IsChecked = false;
		CreateFilePopup();
		_FileList.InitCurrentFile();
		_FileList.LoadCurrentProjectDirectoryAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_FilePopup.IsOpen = true;
	}

	void OnFolderClick(object sender, RoutedEventArgs args) {
		if (_FileButton.IsChecked != true) {
			return;
		}
		_SolutionButton?.IsChecked = false;
		_ProjectButton?.IsChecked = false;
		var path = _View.TextBuffer.GetTextDocument().FilePath;
		var (folder, _) = FileHelper.DeconstructPath(path, true);
		if (String.IsNullOrEmpty(folder)) {
			_FileButton.IsChecked = false;
			return;
		}
		CreateFilePopup();
		_FileList.CurrentFile = path;
		_FileList.LoadCurrentDirectoryAsync(folder, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_FilePopup.IsOpen = true;
	}

	void CreateFilePopup() {
		if (_FilePopup != null) {
			return;
		}
		_FilePopup = new Popup {
			PlacementTarget = _Container,
			Placement = PlacementMode.Top,
			AllowsTransparency = true,
			StaysOpen = false,
			Focusable = true,
			MaxHeight = 600,
			Child = _FileList = new FileList()
		};
		_FilePopup.Closed += Popup_Closed;
		KeystrokeThief.Bind(_FilePopup);
		_FileList.FileActivated += HandleFileActivation;
	}

	void HandleFileActivation(object sender, EventArgs<FileSystemItem> e) {
		_FilePopup.IsOpen = false;
		if (e.Data.IsCurrent) {
			_View.VisualElement.Focus();
		}
	}

	void Popup_Closed(object sender, EventArgs e) {
		_FileButton.IsChecked = false;
		_SolutionButton?.IsChecked = false;
		_ProjectButton?.IsChecked = false;
	}

	public void Dispose() {
		_CancellationTokenSource.CancelAndDispose();
	}

	ITextViewMargin ITextViewMargin.GetTextViewMargin(string marginName) {
		return marginName == Name ? this : null;
	}

}
