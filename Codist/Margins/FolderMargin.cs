using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CLR;
using Codist.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using R = Codist.Properties.Resources;
using Task = System.Threading.Tasks.Task;

namespace Codist.Margins;

sealed class FolderMargin : IWpfTextViewMargin
{
	internal const string Name = nameof(FolderMargin);

	readonly ThemedToggleButton _Button;
	readonly IWpfTextView _View;
	Popup _Popup;
	FileListControl _FileList;
	CancellationTokenSource _CancellationTokenSource;

	public FrameworkElement VisualElement => _Button;
	public double MarginSize => _Button.RenderSize.Height;
	public bool Enabled => true;

	public FolderMargin(IWpfTextView view) {
		_Button = new ThemedToggleButton(IconIds.Folder, R.CMDT_ClickToViewFolder, OnClick) {
			BorderThickness = WpfHelper.NoMargin,
			Margin = WpfHelper.SmallMargin,
			Background = Brushes.Transparent,
			Resources = SharedDictionaryManager.ThemedControls
		};
		_View = view;
	}

	void OnClick(object sender, RoutedEventArgs args) {
		if (_Button.IsChecked != true) {
			return;
		}
		var path = _View.TextBuffer.GetTextDocument().FilePath;
		var (folder, curFile) = FileHelper.DeconstructPath(path);
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
				Resources = SharedDictionaryManager.VirtualList,
				Child = _FileList = new FileListControl(this)
			};
			_Popup.Closed += Popup_Closed;
			KeystrokeThief.Bind(_Popup);
		}
		_FileList.LoadFileDirectoryAsync(path, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_Popup.IsOpen = true;
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

	sealed class FileSystemItem : INotifyPropertyChanged
	{
		readonly FileSystemInfo _info;
		readonly ItemType _type;
		FrameworkElement _icon;

		long _fileSize = -1;
		DateTime _creationTime;
		DateTime _lastWriteTime;

		public string Name => _info.Name;
		public string FullPath => _info.FullName;
		public ItemType Type => _type;
		public bool IsEmptyFolder => _type == ItemType.EmptyFolder;
		public bool IsFolder => _type.CeqAny(ItemType.Folder, ItemType.EmptyFolder);
		public bool IsFile => _type.CeqAny(ItemType.File, ItemType.CurrentFile);
		public bool IsCurrentFile => _type == ItemType.CurrentFile;

		public FrameworkElement Icon => _icon ??= VsImageHelper.GetImage(_type switch {
			ItemType.Folder => IconIds.Folder,
			ItemType.EmptyFolder => IconIds.EmptyFolder,
			ItemType.InaccessibleFolder => IconIds.InaccessibleFolder,
			_ => GetFileIconId(_info.Extension)
		});

		static int GetFileIconId(string extName) {
			return extName?.ToLowerInvariant() switch {
				#region Source Code Files
				".cs" => IconIds.CSFileNode,
				".vb" => IconIds.VBFileNode,
				".cpp" or ".cc" or ".cxx" or ".cp" => IconIds.CPPFileNode,
				".h" or ".hpp" or ".hxx" or ".hh" => IconIds.CPPHeaderFile,
				".c" => IconIds.CFile,
				".fs" or ".fsi" or ".fsx" => IconIds.FSFileNode,
				".py" or ".pyw" => IconIds.PYFileNode,
				".ts" or ".tsx" => IconIds.TSFileNode,
				".js" => IconIds.JSFile,
				".jsx" => IconIds.JSXFile,
				".php" => IconIds.PHPFile,
				".asm" or ".s" => IconIds.ASMFile,
				#endregion

				#region Web & UI Files
				".html" or ".htm" or ".xhtml" => IconIds.HTMLFile,
				".css" or ".scss" or ".less" => IconIds.CSS,
				".xaml" => IconIds.WPFFile,
				".razor" or ".aspx" or ".ashx" or ".asmx" or ".svc" => IconIds.WebFile,
				#endregion

				#region Data & Configuration Files
				".json" or ".yaml" or ".yml" or ".targets" => IconIds.SettingsFile,
				".xml" or ".resx" or ".xsd" or ".xsl" or ".xslt" => IconIds.XMLFile,
				".config" or ".conf" or ".ini" or ".props" => IconIds.ConfigurationFile,
				".dtd" => IconIds.XMLDTDFile,
				".md" or ".markdown" => IconIds.MarkdownFile,
				".txt" or ".log" => IconIds.TextFile,
				".db" or ".sqlite" or ".mdf" or ".ldf" => IconIds.DatabaseFile,
				#endregion

				#region Project & Solution Files
				".csproj" => IconIds.CSProjectNode,
				".vbproj" => IconIds.VBProjectNode,
				".fsproj" => IconIds.FSProjectNode,
				".vcxproj" or ".vcproj" => IconIds.CPPProjectNode,
				".pyproj" => IconIds.PYProjectNode,
				".tsproj" or ".esproj" or ".njsproj" => IconIds.TSProjectNode,
				".sln" => IconIds.VisualStudioFile,
				#endregion

				#region Image & Resource Files
				".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => IconIds.ImageFile,
				".ico" => IconIds.IconFile,
				".cur" or ".ani" => IconIds.CursorFile,
				".tif" or ".tiff" => IconIds.TifFile,
				#endregion

				#region Build Artifacts & Binary Files
				".dll" or ".sys" or ".bin" or ".dat" => IconIds.BinaryFile,
				".exe" or ".com" => IconIds.ExecutableFile,
				".cmd" or ".bat" or ".wsf" or ".ps" or ".ps1" or ".bash" or ".sh" or ".zsh" or ".ksh" => IconIds.ConsoleFile,
				".tmp" or ".temp" or ".obj" => IconIds.IntermediateFile,
				".pdb" => IconIds.PDBFile,
				".dmp" => IconIds.CrashDumpFile,
				#endregion

				#region Office Files
				".doc" or ".docx" or ".rtf" => IconIds.WordFile,
				".xls" or ".xlsx" or ".xlsm" or ".csv" => IconIds.ExcelFile,
				".ppt" or ".pptx" => IconIds.PowerPointFile,
				".accdb" or ".mdb" => IconIds.AccessFile,
				".msg" or ".pst" or ".ost" => IconIds.OutlookFile,
				".vsdx" or ".vsd" => IconIds.VisioFile,
				".mpp" => IconIds.ProjectFile,
				#endregion

				#region Miscellaneous Files
				".jar" => IconIds.JARFile,
				".zip" or ".rar" or ".7z" or ".cab" or ".gz" or ".tar" => IconIds.CompressedFile,
				".pfx" or ".snk" or ".cer" => IconIds.SignatureFile,
				".manifest" => IconIds.ManifestFile,
				".suo" or ".user" or ".vssettings" or ".vsct" or ".vsixmanifest" or ".vsixlangpack" => IconIds.VisualStudioFile,
				".lnk" => IconIds.SymlinkFile,
				".chm" or ".hlp" => IconIds.CompiledHelpFile,
				".pdf" or ".epub" or ".mobi" or ".djvu" => IconIds.EBookFile,
				".mp4" or ".avi" or ".wmv" or ".flv" or ".mts" or ".mpg" or ".mpeg" or ".wav" or ".mp3" or ".ogg" or ".flac" or ".ape" or ".m4a" or ".aac" or ".m3u" or ".m3u8" or ".cue" => IconIds.MediaFile,
				".bak" => IconIds.BackupFile,
				#endregion

				_ => IconIds.OtherFile
			};
		}

		public long FileSize {
			get {
				if (_fileSize < 0) {
					if (IsFile) {
						try {
							_fileSize = _info is FileInfo fileInfo
								? fileInfo.Length
								: new FileInfo(_info.FullName).Length;
						}
						catch {
							_fileSize = 0;
						}
					}
					else {
						_fileSize = 0;
					}
				}
				return _fileSize;
			}
		}

		public DateTime CreationTime {
			get {
				if (_creationTime == default) {
					try { _creationTime = _info.CreationTime; }
					catch { _creationTime = DateTime.MaxValue; }
				}
				return _creationTime;
			}
		}

		public DateTime LastWriteTime {
			get {
				if (_lastWriteTime == default) {
					try { _lastWriteTime = _info.LastWriteTime; }
					catch { _lastWriteTime = DateTime.MaxValue; }
				}
				return _lastWriteTime;
			}
		}

		public string FormattedFileSize => IsFile ? FormatFileSize(FileSize) : null;

		public FileSystemItem(FileInfo fileInfo, bool isCurrentFile) {
			(_info, _type) = (fileInfo, isCurrentFile ? ItemType.CurrentFile : ItemType.File);
		}

		public FileSystemItem(DirectoryInfo parentDirInfo, bool isEmpty) {
			(_info, _type) = (parentDirInfo, isEmpty ? ItemType.Folder : ItemType.EmptyFolder);
		}
		public FileSystemItem(DirectoryInfo parentDirInfo, ItemType type) {
			(_info, _type) = (parentDirInfo, type);
		}

		static string FormatFileSize(long bytes) {
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			int order = 0;
			double size = bytes;
			while (size >= 1024 && order < sizes.Length - 1) {
				order++;
				size /= 1024;
			}
			return $"{size:0.##} {sizes[order]}";
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}

	sealed class FileListControl : UserControl
	{
		readonly FolderMargin _Owner;
		VirtualList _listBox;
		ObservableCollection<FileSystemItem> _items;
		ICollectionView _view;
		TextBlock _PathBlock, _CounterBlock;
		TextBox _filterBox;
		string _currentFilePath;
		string _currentDirPath;

		public FileListControl(FolderMargin owner) {
			_Owner = owner;
			MaxWidth = 600;
			BorderThickness = WpfHelper.NoMargin;
			Focusable = true;
			this.ReferenceProperty(BackgroundProperty, CommonControlsColors.ComboBoxListBackgroundBrushKey)
				.ReferenceProperty(BorderBrushProperty, CommonControlsColors.ComboBoxListBorderBrushKey);

			_listBox = new VirtualList {
				Header = new Grid {
					ColumnDefinitions = {
						new ColumnDefinition { Width = GridLength.Auto },
						new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
						new ColumnDefinition { Width = GridLength.Auto },
					},
					Children = {
						new Border {
							BorderThickness = WpfHelper.TinyMargin,
							Margin = WpfHelper.SmallMargin,
							CornerRadius = WpfHelper.SmallCorner,
							Child = new ThemedButton(IconIds.OpenFolder, R.CMD_OpenFolder, OnOpenInExplorer).ClearSpacing().SetProperty(PaddingProperty, WpfHelper.SmallHorizontalMargin)
						}.ReferenceProperty(Border.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey),
						new TextBlock {
							Padding = WpfHelper.SmallMargin,
							VerticalAlignment = VerticalAlignment.Center,
							TextWrapping = TextWrapping.Wrap,
						}.SetValue(Grid.SetColumn, 1)
						.ReferenceProperty(TextBlock.ForegroundProperty, EnvironmentColors.SystemCaptionTextBrushKey)
						.Set(ref _PathBlock),
						new TextBlock {
							Padding = WpfHelper.SmallMargin,
							VerticalAlignment = VerticalAlignment.Center,
						}.SetValue(Grid.SetColumn, 2)
						.ReferenceProperty(TextBlock.ForegroundProperty, EnvironmentColors.SystemCaptionTextBrushKey)
						.Set(ref _CounterBlock)
					}
				},
				ItemsSource = _items = [],
				ItemTemplate = CreateItemTemplate(),
				ItemContainerStyle = new Style(typeof(ListBoxItem)) {
					Setters = {
						new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
						new Setter(PaddingProperty, WpfHelper.TinyMargin),
						new Setter(MaxWidthProperty, 550d),
						new Setter(ToolTipService.ToolTipProperty, new Binding {
							Converter = new FileSystemItemToTooltipConverter()
						}),
						new Setter(ToolTipService.PlacementProperty, PlacementMode.Right),
						new Setter(ToolTipService.ShowDurationProperty, 30000),
						new Setter(ToolTipService.InitialShowDelayProperty, 500),
					},
				},
				Footer = new StackPanel {
					Orientation = Orientation.Horizontal,
					Margin = WpfHelper.SmallMargin,
					Children = {
						VsImageHelper.GetImage(IconIds.Filter).WrapMargin(WpfHelper.GlyphMargin),
						new ThemedTextBox {
							MinWidth = 120,
							Margin = WpfHelper.GlyphMargin,
							ToolTip = new ThemedToolTip(R.T_ResultFilter, R.T_ResultFilterTip)
						}.Set(ref _filterBox),
						new Border {
							BorderThickness = WpfHelper.TinyMargin,
							CornerRadius = WpfHelper.SmallCorner,
							Child = new StackPanel {
								Orientation = Orientation.Horizontal,
								Children = {
									new ThemedButton(IconIds.ClearFilter, R.CMD_ClearFilter, ClearFilterBox){ MinHeight = 10 }
										.ClearSpacing()
								}
							}
						}.ReferenceProperty(Border.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey),
					}
				}
			}
			.ReferenceCrispImageBackground(CommonControlsColors.ComboBoxListBackgroundColorKey)
			.ReferenceProperty(ForegroundProperty, CommonControlsColors.ComboBoxListItemTextBrushKey);

			_listBox.KeyUp += List_KeyUp;
			_listBox.MouseDoubleClick += List_MouseDoubleClick;
			_filterBox.TextChanged += FilterBox_TextChanged;
			Content = _listBox;
		}

		void FilterBox_TextChanged(object sender, TextChangedEventArgs e) {
			ApplyFilter();
		}

		void ClearFilterBox() {
			_filterBox.Clear();
		}

		void OnOpenInExplorer(object sender, RoutedEventArgs args) {
			FileHelper.OpenInExplorer(_currentFilePath ?? _currentDirPath);
		}

		void List_KeyUp(object sender, KeyEventArgs e) {
			if (e.Key == Key.Enter) {
				ActivateSelectedItem();
			}
		}

		void List_MouseDoubleClick(object sender, RoutedEventArgs e) {
			ActivateSelectedItem();
		}

		void ActivateSelectedItem() {
			if (_listBox.SelectedItem is FileSystemItem item) {
				switch (item.Type) {
					case ItemType.File:
						TextEditorHelper.OpenFile(item.FullPath);
						_Owner._Popup.IsOpen = false;
						break;
					case ItemType.CurrentFile:
						_Owner._Popup.IsOpen = false;
						_Owner._View.VisualElement.Focus();
						break;
					case ItemType.Folder:
						NavigateToDirectoryAsync(item.FullPath).FireAndForget();
						break;
				}
			}
		}

		async Task NavigateToDirectoryAsync(string directoryPath) {
			SetCurrentDir(directoryPath);
			_currentFilePath = null;

			await LoadDirectoryAsync(_currentDirPath, null, SyncHelper.CancelAndRetainToken(ref _Owner._CancellationTokenSource));
			_listBox.Focus();
		}

		void SetCurrentDir(string directoryPath) {
			_currentDirPath = directoryPath;
			BuildPathNavigator(directoryPath);
		}

		DataTemplate CreateItemTemplate() {
			var template = new DataTemplate(typeof(FileSystemItem));

			var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
			stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
			stackPanelFactory.SetValue(MarginProperty, WpfHelper.TinyMargin);
			stackPanelFactory.SetBinding(OpacityProperty, new Binding(nameof(FileSystemItem.IsEmptyFolder)) {
				Converter = new BooleanToOpacityConverter()
			});

			var iconFactory = new FrameworkElementFactory(typeof(ContentPresenter));
			iconFactory.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
			iconFactory.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(FileSystemItem.Icon)));
			stackPanelFactory.AppendChild(iconFactory);

			var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
			textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileSystemItem.Name)));
			var fontWeightBinding = new Binding(nameof(FileSystemItem.IsCurrentFile)) {
				Converter = BooleanToFontWeightConverter.Instance
			};
			textBlockFactory.SetBinding(TextBlock.FontWeightProperty, fontWeightBinding);

			textBlockFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
			stackPanelFactory.AppendChild(textBlockFactory);

			template.VisualTree = stackPanelFactory;
			return template;
		}

		public async Task LoadFileDirectoryAsync(string filePath, CancellationToken cancellationToken) {
			var (directory, currentFileName) = FileHelper.DeconstructPath(filePath);

			if (String.IsNullOrEmpty(directory)) {
				return;
			}

			SetCurrentDir(directory);
			_currentFilePath = filePath;

			await LoadDirectoryAsync(_currentDirPath, currentFileName, cancellationToken);

			_listBox.SelectedItem = _items.FirstOrDefault(i => i.IsCurrentFile);
			_listBox.ScrollToSelectedItem();
		}

		async Task LoadDirectoryAsync(string directoryPath, string currentFileName, CancellationToken cancellationToken) {
			_items.Clear();
			try {
				var (newItems, folders, files) = GetFileSystemItems(directoryPath, currentFileName, cancellationToken);
				_CounterBlock.Inlines.Clear();
				if (folders != 0) {
					_CounterBlock.AddImage(IconIds.Folder).Append(folders.ToText()).Append(" ");
				}
				if (files != 0) {
					_CounterBlock.AddImage(IconIds.OtherFile).Append(files.ToText());
				}
				_items = new ObservableCollection<FileSystemItem>(newItems);
				_listBox.ItemsSource = _view = CollectionViewSource.GetDefaultView(_items);
				if (folders != 0 || files != 0) {
					_listBox.SelectedIndex = 0;
				}
				ApplyFilter();
				_listBox.Focus();
			}
			catch (OperationCanceledException) { }
			catch (Exception ex) {
				await SyncHelper.SwitchToMainThreadAsync(cancellationToken);
				MessageWindow.Error(ex, R.T_FailedToOpenFolder, source: this);
			}
		}

		static (List<FileSystemItem> items, int folders, int files) GetFileSystemItems(string directory, string currentFileNameToHighlight, CancellationToken token) {
			if (directory[directory.Length - 1] == ':') {
				directory += "\\";
			}
			var dirInfo = new DirectoryInfo(directory);

			var dirs = dirInfo.GetDirectories();
			var files = dirInfo.GetFiles();
			var items = new List<FileSystemItem>(dirs.Length + files.Length);
			foreach (var dir in dirs) {
				if (token.IsCancellationRequested) break;
				try { items.Add(new FileSystemItem(dir, dir.EnumerateFileSystemInfos().Any())); }
				catch (UnauthorizedAccessException) { items.Add(new FileSystemItem(dir, ItemType.InaccessibleFolder)); }
				catch (SecurityException) { items.Add(new FileSystemItem(dir, ItemType.InaccessibleFolder)); }
			}

			foreach (var file in files) {
				if (token.IsCancellationRequested) break;
				var isCurrent = currentFileNameToHighlight != null &&
					String.Equals(file.Name, currentFileNameToHighlight, StringComparison.OrdinalIgnoreCase);
				items.Add(new FileSystemItem(file, isCurrent));
			}

			return (items, dirs.Length, files.Length);
		}

		void BuildPathNavigator(string path) {
			foreach (var item in _PathBlock.Inlines) {
				if (item is Hyperlink link) {
					link.Click -= OpenFolderLink;
				}
			}
			_PathBlock.Inlines.Clear();

			int startIndex = 0;
			int length = path.Length;

			if (path[length - 1] == '\\') {
				length--;
			}

			while (startIndex <= length) {
				int separatorIndex = path.IndexOf('\\', startIndex, length - startIndex);
				// If no separator is found (-1), we are at the last segment (e.g., "System32" in "C:\Windows\System32")
				int segmentEndIndex = separatorIndex == -1 ? length : separatorIndex;
				if (segmentEndIndex == startIndex) {
					break;
				}
				var segment = path.Substring(startIndex, segmentEndIndex - startIndex);

				if (separatorIndex < 0) {
					_PathBlock.Inlines.Add(new Run(segment) { FontWeight = FontWeights.Bold });
					break;
				}

				var link = new Hyperlink(new Run(segment)) {
					CommandParameter = new PathSegment(path, separatorIndex)
				}.SetContentLazyToolTip(l => new CommandToolTip(IconIds.Folder, R.CMD_GoToFolder + "\n" + l.CommandParameter));
				link.SetResourceReference(TextElement.ForegroundProperty, CommonDocumentColors.HyperlinkBrushKey);
				link.Click += OpenFolderLink;
				_PathBlock.Inlines.Add(link);

				// Add the separator character "\"
				if (separatorIndex == -1) {
					// No more separators, we've reached the end of the path
					break;
				}
				_PathBlock.Inlines.Add(new Run("\\"));
				startIndex = separatorIndex + 1;
			}
		}

		void OpenFolderLink(object s, EventArgs e) {
			var link = (Hyperlink)s;
			((TextBlock)link.Parent)
				.FindAncestor<FileListControl>()
				?.NavigateToDirectoryAsync(((PathSegment)link.CommandParameter).Text)
				.FireAndForget();
		}

		void ApplyFilter() {
			var keywords = _filterBox.Text.Split([' '], StringSplitOptions.RemoveEmptyEntries);
			if (keywords.Length == 0) {
				_view.Filter = null;
			}
			else {
				_view.Filter = item => item is FileSystemItem fsi && keywords.All(i => fsi.Name.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0);
			}
		}
	}

	sealed class PathSegment(string path, int index) {
		string _text;

		public string Text => _text ??= path.Substring(0, index);

		public override string ToString() {
			return Text;
		}
	}

	enum ItemType
	{
		File,
		Folder,
		CurrentFile,
		EmptyFolder,
		InaccessibleFolder
	}

	#region Converters

	sealed class BooleanToFontWeightConverter : IValueConverter
	{
		public static readonly BooleanToFontWeightConverter Instance = new();
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			return (value is bool b && b) ? FontWeights.Bold : FontWeights.Normal;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}

	sealed class BooleanToOpacityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			return value is bool b && b ? WpfHelper.DimmedOpacity : 1.0d;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}

	sealed class FileSystemItemToTooltipConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			if (value is not FileSystemItem item) return null;

			var panel = new StackPanel { Margin = WpfHelper.MiddleMargin }.LimitSize();

			panel.Children.Add(new TextBlock {
				Text = item.Type switch {
					ItemType.Folder => R.T_Folder,
					ItemType.EmptyFolder => R.T_EmptyFolder,
					ItemType.InaccessibleFolder => R.T_UnauthorizedFolder,
					_ => R.T_File
				} + item.Name,
				FontWeight = FontWeights.Bold,
				Margin = WpfHelper.MiddleBottomMargin,
				TextWrapping = TextWrapping.Wrap
			});

			if (item.Type == ItemType.InaccessibleFolder) {
				return panel;
			}

			if (!item.IsFolder) {
				panel.Children.Add(new TextBlock { Text = R.T_FileSize + item.FormattedFileSize });
			}

			panel.Children.Add(new TextBlock { Text = R.T_CreateTime + item.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") });
			panel.Children.Add(new TextBlock { Text = R.T_UpdateTime + item.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") });

			return panel;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}

	#endregion
}
