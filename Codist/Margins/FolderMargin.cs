using System;
using System.Threading;
using System.Windows;
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

	readonly ThemedToggleButton _Button;
	readonly IWpfTextView _View;
	Popup _Popup;
	FileList _FileList;
	CancellationTokenSource _CancellationTokenSource;

	public FrameworkElement VisualElement => _Button;
	public double MarginSize => _Button.RenderSize.Height;
	public bool Enabled => true;

	public FolderMargin(IWpfTextView view) {
		_Button = new ThemedToggleButton(IconIds.Folder, R.CMDT_ClickToViewFolder, OnClick) {
			BorderThickness = WpfHelper.NoMargin,
			Resources = SharedDictionaryManager.ThemedControls,
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = WpfHelper.SmallHorizontalMargin,
			Padding = WpfHelper.TinyMargin,
			MinHeight = 10
		};
		_View = view;
	}

	void OnClick(object sender, RoutedEventArgs args) {
		if (_Button.IsChecked != true) {
			return;
		}
		var path = _View.TextBuffer.GetTextDocument().FilePath;
		var (folder, curFile) = FileHelper.DeconstructPath(path, true);
		if (String.IsNullOrEmpty(folder)) {
			_Button.IsChecked = false;
			return;
		}
		if (_Popup == null) {
			_Popup = new Popup {
				PlacementTarget = _Button,
				Placement = PlacementMode.Top,
				AllowsTransparency = true,
				StaysOpen = false,
				Focusable = true,
				MaxHeight = 600,
				Child = _FileList = new FileList()
			};
			_Popup.Closed += Popup_Closed;
			KeystrokeThief.Bind(_Popup);
		}
		_FileList.CurrentFile = path;
		_FileList.LoadCurrentDirectoryAsync(folder, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_FileList.FileActivated += HandleFileActivation;
		_Popup.IsOpen = true;
	}

	void HandleFileActivation(object sender, EventArgs<FileSystemItem> e) {
		_Popup.IsOpen = false;
		if (e.Data.IsCurrent) {
			_View.VisualElement.Focus();
		}
	}

	void Popup_Closed(object sender, EventArgs e) {
		_Button.IsChecked = false;
	}

	public void Dispose() {
		_CancellationTokenSource.CancelAndDispose();
	}

	public ITextViewMargin GetTextViewMargin(string marginName) {
		return marginName == Name ? this : null;
	}

}
