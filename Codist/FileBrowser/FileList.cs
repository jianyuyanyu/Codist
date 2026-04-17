using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using CLR;
using Codist.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using R = Codist.Properties.Resources;
using Task = System.Threading.Tasks.Task;

namespace Codist.FileBrowser;

sealed class FileList : VirtualList
{
	readonly TextBlock _PathBlock, _SelectionInfoBlock;
	readonly TextBox _FilterBox;
	readonly ThemedControlGroup _FilterGroup;
	readonly ThemedToggleButton _FolderFilterButton, _FileFilterButton;
	readonly ThemedButton _GoToCurrentFileButton, _GoToSolutionFolderButton, _GoToProjectFolderButton;

	ObservableCollection<FileSystemItem> _Items;
	ICollectionView _ItemsView;
	bool _LockFilter, _MultiSelectionMode;

	string _ActiveFilePath, _ActiveDirPath, _SolutionFolderPath, _ProjectFolderPath;
	int _ProjectIconId;

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	public FileList() {
		MaxWidth = 600;
		BorderThickness = WpfHelper.NoMargin;
		Focusable = true;
		this.ReferenceStyle(typeof(VirtualList))
			.ReferenceProperty(BackgroundProperty, CommonControlsColors.ComboBoxListBackgroundBrushKey)
			.ReferenceProperty(BorderBrushProperty, CommonControlsColors.ComboBoxListBorderBrushKey);

		ContextMenu m;
		ItemTemplate = SharedDictionaryManager.VirtualList.Get<DataTemplate>("FileItemTemplate");
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
		};
		SelectionMode = SelectionMode.Extended;
		MouseUp += HandleMouseUp;
		#region Extra controls
		var pathControl = new Grid {
			ColumnDefinitions = {
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto }
			},
			Children = {
				new Border {
					BorderThickness = WpfHelper.TinyMargin,
					Margin = WpfHelper.SmallMargin,
					CornerRadius = WpfHelper.SmallCorner,
					Child = new ThemedButton(IconIds.OpenFolder, R.CMD_OpenFolder, OpenInExplorer).ClearSpacing().SetProperty(PaddingProperty, WpfHelper.SmallHorizontalMargin)
				}.ReferenceProperty(Border.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey),
				new TextBlock {
					Padding = WpfHelper.SmallMargin,
					VerticalAlignment = VerticalAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
				}.SetValue(Grid.SetColumn, 1)
				.ReferenceProperty(TextBlock.ForegroundProperty, EnvironmentColors.SystemCaptionTextBrushKey)
				.Set(ref _PathBlock)
			}
		};
		var toolbar = new StackPanel {
			Orientation = Orientation.Horizontal,
			Margin = WpfHelper.SmallMargin,
			Children = {
				new ThemedControlGroup(
					_FolderFilterButton = new ThemedToggleButton(IconIds.Folder, R.T_Folder, HandleFolderFileFilterChange).ReferenceProperty(TextBlock.ForegroundProperty, CommonControlsColors.ButtonTextBrushKey),
					_FileFilterButton = new ThemedToggleButton(IconIds.File, R.T_File, HandleFolderFileFilterChange).ReferenceProperty(TextBlock.ForegroundProperty, CommonControlsColors.ButtonTextBrushKey)
				) {
					Margin = WpfHelper.MiddleHorizontalMargin,
					VerticalAlignment = VerticalAlignment.Center,
				}
				.Set(ref _FilterGroup),
				new ThemedTextBox {
					MinWidth = 120,
					ToolTip = new ThemedToolTip(R.T_ResultFilter, R.T_ResultFilterTip),
				}.Set(ref _FilterBox),
				new Border {
					BorderThickness = WpfHelper.TinyMargin,
					CornerRadius = WpfHelper.SmallCorner,
					Child = new StackPanel {
						Orientation = Orientation.Horizontal,
						Children = {
							new ThemedButton(IconIds.ClearFilter, R.CMD_ClearFilter, ClearFilterBox){ MinHeight = 10 }
								.ClearSpacing(),
							new ThemedToggleButton(IconIds.MultiSelection, R.CMDT_ToggleMultiSelectionMode, ToggleMultiSelectionMode).ClearSpacing(),
							new ThemedButton(IconIds.GoToCurrentFile, R.CMD_BackToCurrentFile, GoToCurrentFile) {
								MinHeight = 10
							}.ClearSpacing().Set(ref _GoToCurrentFileButton),
							new ThemedButton(IconIds.GoToSolutionFolder, R.CMD_GoToSolutionFolder, GoToSolutionFolder) {
								MinHeight = 10
							}.ClearSpacing().Set(ref _GoToSolutionFolderButton),
							new ThemedButton(IconIds.GoToProjectFolder, R.CMD_GoToProjectFolder, GoToProjectFolder) {
								MinHeight = 10
							}.ClearSpacing().Set(ref _GoToProjectFolderButton),
						}
					}
				}.ReferenceProperty(Border.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey),
				new TextBlock {
					Margin = WpfHelper.MiddleHorizontalMargin,
					VerticalAlignment = VerticalAlignment.Center,
				}.Set(ref _SelectionInfoBlock)
			}
		};
		Footer = new StackPanel {
			Children = { pathControl, toolbar }
		};
		#endregion
		ContextMenu = m = new() {
			Resources = SharedDictionaryManager.ContextMenu,
			Items = {
				new ListItemContextMenuItem(IconIds.OpenWithVisualStudio, R.CMD_OpenWithVS, ActivationCondition.HasFile, OpenFilesWithVisualStudio),
				new ListItemContextMenuItem(IconIds.LocateInSolutionExplorer, R.CMD_LocateInSolutionExplorer, ActivationCondition.HasSingleSolutionItem, LocateInSolutionExplorer),
				new Separator(),
				new ListItemContextMenuItem(IconIds.Cut, R.CMD_Cut, ActivationCondition.HasFileOrFolder, CutFiles),
				new ListItemContextMenuItem(IconIds.Copy, R.CMD_Copy, ActivationCondition.HasFileOrFolder, CopyFiles),
				new ListItemContextMenuItem(IconIds.Paste, R.CMD_Paste, ActivationCondition.HasClipboardFile, PasteFiles),
				new Separator(),
				new ListItemContextMenuItem(IconIds.Delete, R.CMD_Delete, ActivationCondition.HasFileOrFolder, DeleteFiles),
				new Separator(),
				new ListItemContextMenuItem(IconIds.Rename, R.CMD_Rename, ActivationCondition.HasSingleItem, StartRename),
				new ListItemContextMenuItem(IconIds.Properties, R.CMD_Properties, ActivationCondition.HasFileOrFolder, ShowProperties),
			}
		};
		this.ReferenceCrispImageBackground(CommonControlsColors.ComboBoxListBackgroundColorKey)
			.ReferenceProperty(ForegroundProperty, CommonControlsColors.ComboBoxListItemTextBrushKey);

		m.SetBackgroundForCrispImage(ThemeCache.TitleBackgroundColor);

		_SolutionFolderPath = CodistPackage.DTE.Solution.FullName;
		SetFolderShortcut(_GoToSolutionFolderButton, ref _SolutionFolderPath);

		_GoToCurrentFileButton.Visibility = Visibility.Collapsed;
		_FilterBox.TextChanged += FilterBox_TextChanged;
	}

	public string CurrentFile {
		get => _ActiveFilePath;
		set {
			ThreadHelper.ThrowIfNotOnUIThread();
			_ActiveFilePath = value;
			var hasValue = !string.IsNullOrEmpty(value);
			_GoToCurrentFileButton?.ToggleVisibility(hasValue);
			if (hasValue) {
				var pi = CodistPackage.DTE.Solution.FindProjectItem(value);
				if (pi != null && !String.IsNullOrEmpty(value = pi.ContainingProject?.FullName)) {
					_ProjectFolderPath = value;
					var icon = FileSystemItem.GetFileIconId(Path.GetExtension(value));
					if (icon == IconIds.OtherFile) {
						icon = IconIds.GoToProjectFolder;
					}
					_ProjectIconId = icon;
					_GoToProjectFolderButton.Content = VsImageHelper.GetImage(icon);
					SetFolderShortcut(_GoToProjectFolderButton, ref _ProjectFolderPath);
				}
				else {
					_GoToProjectFolderButton.Visibility = Visibility.Collapsed;
					_ProjectFolderPath = String.Empty;
				}
			}
			else {
				_ProjectFolderPath = String.Empty;
			}
		}
	}

	public IEnumerable<string> SelectedFileNames {
		get {
			foreach (var item in SelectedItems) {
				if (item is FileSystemItem fs && fs.IsFile) {
					yield return fs.Name;
				}
			}
		}
	}
	public IEnumerable<string> SelectedFilePaths {
		get {
			foreach (var item in SelectedItems) {
				if (item is FileSystemItem fs && fs.IsFile) {
					yield return fs.FullPath;
				}
			}
		}
	}
	public IEnumerable<string> SelectedNames {
		get {
			foreach (var item in SelectedItems) {
				if (item is FileSystemItem fs) {
					yield return fs.Name;
				}
			}
		}
	}
	public IEnumerable<string> SelectedPaths {
		get {
			foreach (var item in SelectedItems) {
				if (item is FileSystemItem fs) {
					yield return fs.FullPath;
				}
			}
		}
	}

	public event EventHandler<EventArgs<FileSystemItem>> FileActivated;

	static void SetFolderShortcut(ThemedButton shortcutButton, ref string filePath) {
		if (String.IsNullOrEmpty(filePath)) {
			shortcutButton.Visibility = Visibility.Collapsed;
			filePath ??= String.Empty;
		}
		else {
			shortcutButton.Visibility = Visibility.Visible;
			filePath = Path.GetDirectoryName(filePath);
		}
	}

	#region Event handlers
	protected override void OnContextMenuOpening(ContextMenuEventArgs e) {
		base.OnContextMenuOpening(e);
		ActivationCondition condition = default;
		if (SelectedItem != null) {
			var selected = SelectedItems;
			if (((FileSystemItem)selected[0]).IsFolder) {
				condition |= ActivationCondition.HasFolder;
			}
			if (((FileSystemItem)selected[selected.Count - 1]).IsFile) {
				condition |= ActivationCondition.HasFile;
			}
			if (selected.Count == 1
				&& ((FileSystemItem)SelectedItem).Type != FileItemType.InaccessibleFolder) {
				condition |= ActivationCondition.HasSingleItem;
			}
			if (condition.MatchFlags(ActivationCondition.HasSingleItem | ActivationCondition.HasFile)
				&& ((FileSystemItem)SelectedItem).IsSolutionItem) {
				condition |= ActivationCondition.HasSingleSolutionItem;
			}
		}
		if (Clipboard.ContainsFileDropList()) {
			condition |= ActivationCondition.HasClipboardFile;
		}
		foreach (var item in ContextMenu.Items) {
			if (item is ListItemContextMenuItem menuItem) {
				menuItem.IsEnabled = menuItem.Condition.HasAnyFlag(condition);
			}
		}
	}

	void HandleFolderFileFilterChange(object sender, RoutedEventArgs e) {
		if (_LockFilter) {
			return;
		}
		var s = (ThemedToggleButton)sender;
		if (s.IsChecked == true) {
			_LockFilter = true;
			if (s == _FolderFilterButton) {
				_FileFilterButton.IsChecked = false;
			}
			else {
				_FolderFilterButton.IsChecked = false;
			}
			_LockFilter = false;
		}
		ApplyFilter();
	}

	void FilterBox_TextChanged(object sender, TextChangedEventArgs e) {
		if (!_LockFilter) {
			ApplyFilter();
		}
	}

	void ToggleMultiSelectionMode(object sender, RoutedEventArgs e) {
		if (_MultiSelectionMode = SelectionMode == SelectionMode.Extended) { // we use comparison to switch selection mode
			MouseUp -= HandleMouseUp;
			MouseDoubleClick += HandleMouseDoubleClick;
			SelectionMode = SelectionMode.Multiple;
		}
		else {
			MouseUp += HandleMouseUp;
			MouseDoubleClick -= HandleMouseDoubleClick;
			SelectionMode = SelectionMode.Extended;
		}
	}

	void OpenInExplorer(object sender, RoutedEventArgs args) {
		if (!String.IsNullOrEmpty(_ActiveFilePath)
			&& Path.GetDirectoryName(_ActiveFilePath) == _ActiveDirPath) {
			FileHelper.OpenInExplorer(_ActiveFilePath);
		}
		else {
			FileHelper.OpenFolderInExplorer(_ActiveDirPath);
		}
	}

	void GoToCurrentFile(object sender, RoutedEventArgs args) {
		ClearFilter();
		var (directory, _) = FileHelper.DeconstructPath(_ActiveFilePath, true);
		if (String.IsNullOrEmpty(directory)) {
			return;
		}
		LoadCurrentDirectoryAsync(directory, default).FireAndForget();
	}

	void GoToSolutionFolder(object sender, RoutedEventArgs args) {
		if (!String.IsNullOrEmpty(_SolutionFolderPath)) {
			NavigateToDirectoryAsync(_SolutionFolderPath).FireAndForget();
		}
	}

	void GoToProjectFolder(object sender, RoutedEventArgs args) {
		if (!String.IsNullOrEmpty(_ProjectFolderPath)) {
			NavigateToDirectoryAsync(_ProjectFolderPath).FireAndForget();
		}
	}

	protected override void OnKeyUp(KeyEventArgs e) {
		if (e.Key == Key.Enter) {
			ActivateSelectedItem();
			e.Handled = true;
		}
		else {
			base.OnKeyUp(e);
		}
	}

	void HandleMouseUp(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton == MouseButton.Left
			&& !UIHelper.IsCtrlDown
			&& !UIHelper.IsShiftDown
			&& e.OriginalSource is UIElement u
			&& u.FindAncestor<ListBoxItem>() != null) {
			ActivateSelectedItem();
			e.Handled = true;
		}
	}

	void HandleMouseDoubleClick(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton == MouseButton.Left
			&& e.OriginalSource is UIElement u
			&& u.FindAncestor<ListBoxItem>() != null) {
			ActivateSelectedItem();
			e.Handled = true;
		}
	}

	protected override void OnSelectionChanged(SelectionChangedEventArgs e) {
		base.OnSelectionChanged(e);
		_SelectionInfoBlock.Inlines.Clear();
		var c = SelectedItems.Count;
		if (c > 1) {
			_SelectionInfoBlock.AddImage(IconIds.FileLocations).Append(c);
		}
	}
	#endregion

	void ClearFilterBox() {
		ClearFilter();
		ApplyFilter();
	}

	void ClearFilter() {
		_LockFilter = true;
		_FolderFilterButton.IsChecked = _FileFilterButton.IsChecked = false;
		_FilterBox.Clear();
		_LockFilter = false;
	}

	void ActivateSelectedItem() {
		if (SelectedItem is FileSystemItem item) {
			switch (item.Type) {
				case FileItemType.File:
					TextEditorHelper.OpenFile(item.FullPath);
					FileActivated?.Invoke(this, new(item));
					break;
				case FileItemType.Folder:
				case FileItemType.EmptyFolder:
					NavigateToDirectoryAsync(item.FullPath).FireAndForget();
					break;
			}
		}
	}

	async Task NavigateToDirectoryAsync(string directoryPath) {
		SetCurrentDir(directoryPath);

		await LoadDirectoryAsync(_ActiveDirPath, default);
		FileSystemItem fs;
		_GoToCurrentFileButton.ToggleVisibility(_ActiveFilePath != null
			&& ((fs = SelectedItem as FileSystemItem) is null || !fs.IsFile || !fs.IsCurrent));
		ToggleFolderButton(_GoToSolutionFolderButton, _SolutionFolderPath, directoryPath);
		ToggleFolderButton(_GoToProjectFolderButton, _ProjectFolderPath, directoryPath);
		if (_Items.Count != 0 && SelectedIndex < 0) {
			SelectedIndex = 0;
		}
		Focus();
	}

	void SetCurrentDir(string directoryPath) {
		_ActiveDirPath = directoryPath;
		BuildPathNavigator(directoryPath);
	}

	public async Task LoadCurrentDirectoryAsync(string directory, CancellationToken cancellationToken = default) {
		SetCurrentDir(directory);
		await LoadDirectoryAsync(_ActiveDirPath, cancellationToken);

		var highlightItem = _Items.FirstOrDefault(i => i.IsCurrent);
		if (highlightItem != null) {
			SelectedItem = highlightItem;
			this.ScrollToSelectedItem();
		}
		_GoToCurrentFileButton.ToggleVisibility(false);
		ToggleFolderButton(_GoToSolutionFolderButton, _SolutionFolderPath, directory);
		Focus();
	}

	static void ToggleFolderButton(ThemedButton folderButton, string folderPath, string directory) {
		folderButton.ToggleVisibility(folderPath.Length != 0 && !directory.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
	}

	async Task LoadDirectoryAsync(string directoryPath, CancellationToken cancellationToken) {
		_Items?.Clear();
		SelectedIndex = -1;
		try {
			if (!Directory.Exists(directoryPath)) {
				await SyncHelper.SwitchToMainThreadAsync(cancellationToken);
				MessageWindow.Error(R.T_ErrorInexistentDirectory + Environment.NewLine + directoryPath, R.T_FailedToOpenFolder);
				return;
			}
			var (newItems, folders, files) = GetFileSystemItems(directoryPath, _ActiveFilePath, cancellationToken);
			_FolderFilterButton.SetText(folders.ToText());
			_FileFilterButton.SetText(files.ToText());
			_Items = new ObservableCollection<FileSystemItem>(newItems);
			ItemsSource = _ItemsView = CollectionViewSource.GetDefaultView(_Items);
			var highlightItem = newItems.FirstOrDefault(i => i.IsCurrent);
			ApplyFilter();
			if (highlightItem != null) {
				SelectedItem = highlightItem;
				this.ScrollToSelectedItem();
			}
			Focus();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			await SyncHelper.SwitchToMainThreadAsync(cancellationToken);
			MessageWindow.Error(ex, R.T_FailedToOpenFolder, source: this);
		}
	}

	static (List<FileSystemItem> items, int folders, int files) GetFileSystemItems(string directory, string highlightFilePath, CancellationToken token) {
		if (directory[directory.Length - 1] == ':') {
			directory += "\\";
		}
		var dirInfo = new DirectoryInfo(directory);
		var dirs = dirInfo.GetDirectories();
		var files = dirInfo.GetFiles();
		var items = new List<FileSystemItem>(dirs.Length + files.Length);
		string highlightName = null;
		bool highlightIsFile = false;
		if (!String.IsNullOrEmpty(highlightFilePath) &&
			highlightFilePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) {
			var relativePath = highlightFilePath.Substring(highlightFilePath[directory.Length] == '\\' ? directory.Length + 1 : directory.Length);
			if (!String.IsNullOrEmpty(relativePath)) {
				int slashIndex = relativePath.IndexOf('\\');
				if (slashIndex == -1) {
					highlightName = relativePath;
					highlightIsFile = true;
				}
				else {
					highlightName = relativePath.Substring(0, slashIndex);
					//highlightIsFile = false;
				}
			}
		}

		bool isCurrent;
		foreach (var dir in dirs) {
			if (token.IsCancellationRequested) break;
			isCurrent = !highlightIsFile
				&& highlightName != null
				&& String.Equals(dir.Name, highlightName, StringComparison.OrdinalIgnoreCase);

			try { items.Add(new FileSystemItem(dir, dir.EnumerateFileSystemInfos().Any(), isCurrent)); }
			catch (UnauthorizedAccessException) { items.Add(new FileSystemItem(dir, FileItemType.InaccessibleFolder)); }
			catch (SecurityException) { items.Add(new FileSystemItem(dir, FileItemType.InaccessibleFolder)); }
		}

		foreach (var file in files) {
			if (token.IsCancellationRequested) break;
			isCurrent = highlightIsFile
				&& highlightName != null
				&& String.Equals(file.Name, highlightName, StringComparison.OrdinalIgnoreCase);
			items.Add(new FileSystemItem(file, isCurrent));
		}

		return (items, dirs.Length, files.Length);
	}

	void BuildPathNavigator(string path) {
		var pathInlines = _PathBlock.Inlines;
		foreach (var item in pathInlines) {
			if (item is Hyperlink link) {
				link.Click -= OpenFolderLink;
			}
		}
		pathInlines.Clear();

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
				pathInlines.Add(new Run(segment) { FontWeight = FontWeights.Bold });
				break;
			}

			#region add icons for solution and project folder
			if (_SolutionFolderPath?.Length == separatorIndex && path.StartsWith(_SolutionFolderPath, StringComparison.OrdinalIgnoreCase)) {
				pathInlines.Add(new InlineUIContainer(VsImageHelper.GetImage(IconIds.GoToSolutionFolder)) { BaselineAlignment = BaselineAlignment.Center });
			}
			if (_ProjectFolderPath?.Length == separatorIndex && path.StartsWith(_ProjectFolderPath, StringComparison.OrdinalIgnoreCase)) {
				pathInlines.Add(new InlineUIContainer(VsImageHelper.GetImage(_ProjectIconId)) { BaselineAlignment = BaselineAlignment.Center });
			}
			#endregion

			var link = new Hyperlink(new Run(segment)) {
				CommandParameter = new PathSegment(path, separatorIndex)
			}.SetContentLazyToolTip(l => new CommandToolTip(IconIds.Folder, R.CMD_GoToFolder + "\n" + l.CommandParameter));
			link.SetResourceReference(TextElement.ForegroundProperty, CommonDocumentColors.HyperlinkBrushKey);
			link.Click += OpenFolderLink;
			link.Unloaded += UnloadLink;
			pathInlines.Add(link);

			// Add the separator character "\"
			if (separatorIndex == -1) {
				// No more separators, we've reached the end of the path
				break;
			}
			pathInlines.Add(new Run("\\"));
			startIndex = separatorIndex + 1;
		}
	}

	void OpenFolderLink(object s, EventArgs e) {
		var link = (Hyperlink)s;
		((TextBlock)link.Parent)
			.FindAncestor<FileList>()
			?.NavigateToDirectoryAsync(((PathSegment)link.CommandParameter).Text)
			.FireAndForget();
	}

	void UnloadLink(object sender, RoutedEventArgs e) {
		var link = (Hyperlink)sender;
		link.Click -= OpenFolderLink;
		link.Unloaded -= UnloadLink;
	}

	void ApplyFilter() {
		const int ALL = 0, FILES = 1, FOLDERS = 2;
		var keywords = _FilterBox.Text.Split([' '], StringSplitOptions.RemoveEmptyEntries);
		var fs = _FolderFilterButton.IsChecked == true ? FOLDERS : _FileFilterButton.IsChecked == true ? FILES : ALL;

		if (fs == ALL && keywords.Length == 0) {
			_ItemsView.Filter = null;
			return;
		}

		_ItemsView.Filter = item => {
			if (item is not FileSystemItem fsi) return false;

			if (fs == FILES && !fsi.IsFile) return false;
			if (fs == FOLDERS && fsi.IsFile) return false;

			if (keywords.Length > 0) {
				foreach (var keyword in keywords) {
					if (fsi.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) {
						return false;
					}
				}
			}

			return true;
		};
	}

	void OpenFilesWithVisualStudio(object sender, RoutedEventArgs args) {
		FileActivated?.Invoke(this, new((FileSystemItem)SelectedItem));
		var newWindow = false; // preview the first; open others
		foreach (var path in SelectedFilePaths) {
			TextEditorHelper.OpenFile(path, newWindow);
			newWindow = true;
		}
	}

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.EventHandler)]
	void LocateInSolutionExplorer(object sender, RoutedEventArgs args) {
		if (SelectedItem is FileSystemItem fsi && fsi.IsFile) {
			var dte = CodistPackage.DTE;
			var projectItem = dte.Solution.FindProjectItem(fsi.FullPath);
			var parents = new Stack<string>();
			var current = projectItem;
			while (current != null) {
				parents.Push(current.Name);
				current = current.Collection?.Parent as EnvDTE.ProjectItem;
				if (current == null && projectItem.ContainingProject != null) {
					parents.Push(projectItem.ContainingProject.Name);
					parents.Push(Path.GetFileNameWithoutExtension(dte.Solution.FileName));
					break;
				}
			}
			dte.ToolWindows.SolutionExplorer.GetItem(String.Join(@"\", parents)).Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
			FileActivated?.Invoke(this, new(fsi));
		}
	}

	void CutFiles(object sender, RoutedEventArgs args) {
		CopyOrCutFiles(true);
	}

	void CopyFiles(object sender, RoutedEventArgs args) {
		CopyOrCutFiles(false);
	}

	void CopyOrCutFiles(bool isCut) {
		var paths = SelectedPaths.ToArray();
		if (paths.Length == 0) {
			return;
		}
		try {
			var dataObject = new DataObject(DataFormats.FileDrop, paths);
			dataObject.SetData("Preferred DropEffect", new MemoryStream([(byte)(isCut ? 2 : 1), 0, 0, 0]));
			Clipboard.SetDataObject(dataObject, true);
		}
		catch (Exception ex) {
			MessageWindow.Error(ex, source: this);
		}
	}

	void PasteFiles(object sender, RoutedEventArgs args) {
		if (!Clipboard.ContainsFileDropList()) {
			return;
		}

		var files = Clipboard.GetFileDropList();
		if (files.Count == 0) {
			return;
		}

		bool isCut = false;
		if (Clipboard.ContainsData("Preferred DropEffect")) {
			using var stream = Clipboard.GetData("Preferred DropEffect") as MemoryStream;
			if (stream?.Length >= 4) {
				var bytes = new byte[4];
				stream.Read(bytes, 0, 4);
				isCut = (bytes[0] == 2); // Move=2
			}
		}

		try {
			NativeMethods.ShellCopyOrMove(files.Cast<string>(), _ActiveDirPath, isCut);
		}
		catch { }
		LoadDirectoryAsync(_ActiveDirPath, default).FireAndForget();
		FileActivated?.Invoke(this, new((FileSystemItem)SelectedItem));
	}

	void DeleteFiles(object sender, RoutedEventArgs args) {
		var selection = SelectedItems;
		int c = selection.Count;
		var pathsToDelete = new string[c];
		for (int i = 0; i < c; i++) {
			var item = (FileSystemItem)selection[i];
			pathsToDelete[i] = item.FullPath;
			if (_ActiveFilePath != null && item.IsCurrent) {
				_ActiveFilePath = null;
			}
		}

		try {
			var toRecycleBin = !UIHelper.IsShiftDown;
			NativeMethods.DeleteFile(pathsToDelete, toRecycleBin);
		}
		catch { }

		LoadDirectoryAsync(_ActiveDirPath, default).FireAndForget();
	}

	void StartRename(object sender, RoutedEventArgs args) {
		if (SelectedItem is not FileSystemItem fsi) {
			return;
		}

		var itemContainer = (ListBoxItem)ItemContainerGenerator.ContainerFromItem(SelectedItem);
		if (itemContainer == null) {
			return;
		}

		// retrieve StackPanel and TextBlock created in CreateItemTemplate 
		// path：ListBoxItem -> ContentPresenter -> StackPanel -> [ContentPresenter(icon), TextBlock(name)]
		var sp = itemContainer.GetFirstVisualChild<StackPanel>();
		if (sp is null) {
			return;
		}
		var originalTextBlock = sp.Children[1] as TextBlock;
		if (originalTextBlock is null) {
			return;
		}

		originalTextBlock.Visibility = Visibility.Collapsed;

		// create in-place TextBox over originalTextBlock
		var editBox = new ThemedTextBox {
			Text = fsi.Name,
			Padding = originalTextBlock.Padding,
			FontFamily = originalTextBlock.FontFamily,
			FontSize = originalTextBlock.FontSize,
			FontWeight = originalTextBlock.FontWeight,
			VerticalAlignment = VerticalAlignment.Center
		};

		sp.Children.Insert(1, editBox);
		editBox.Focus();

		// select file name without extension
		int selectLength = fsi.Name.Length;
		if (fsi.IsFile) {
			int dotIndex = fsi.Name.LastIndexOf('.');
			if (dotIndex > 0) {
				selectLength = dotIndex;
			}
		}
		editBox.Select(0, selectLength);

		bool isCanceled = false;

		void EditBox_LostFocus(object s, RoutedEventArgs e) {
			if (!isCanceled) {
				editBox.LostFocus -= EditBox_LostFocus;
				CommitRename(fsi, editBox.Text, sp, originalTextBlock, editBox);
			}
		}

		editBox.KeyUp += (s, e) => {
			if (e.Key == Key.Enter) {
				e.Handled = true;
				editBox.LostFocus -= EditBox_LostFocus;
				CommitRename(fsi, editBox.Text, sp, originalTextBlock, editBox);
			}
			else if (e.Key == Key.Escape) {
				e.Handled = true;
				isCanceled = true;
				editBox.LostFocus -= EditBox_LostFocus;
				CancelRename(sp, originalTextBlock, editBox);
			}
		};

		editBox.LostFocus += EditBox_LostFocus;
	}

	void CommitRename(FileSystemItem fsi, string newText, StackPanel panel, TextBlock originalTb, TextBox editBox) {
		panel.Children.Remove(editBox);
		originalTb.Visibility = Visibility.Visible;

		newText = newText.Trim();

		if (string.IsNullOrWhiteSpace(newText) || newText.Equals(fsi.Name, StringComparison.OrdinalIgnoreCase)) {
			return;
		}
		if (newText.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
			MessageWindow.Error(R.T_InvalidFileName, R.T_FailedToRename);
			return;
		}

		var newPath = Path.Combine(_ActiveDirPath, newText);
		try {
			NativeMethods.ShellRename(fsi.FullPath, newPath);
		}
		catch (Exception ex) {
			MessageWindow.Error(ex, R.T_FailedToRename, source: this);
			return;
		}

		if (!String.IsNullOrEmpty(_ActiveFilePath)) {
			var oldPath = fsi.FullPath;
			if (String.Equals(_ActiveFilePath, oldPath, StringComparison.OrdinalIgnoreCase)) {
				// current file renamed
				_ActiveFilePath = newPath;
			}
			else if (_ActiveFilePath.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase)
				&& _ActiveFilePath[oldPath.Length] == '\\') {
				// parent folder of current file renamed
				_ActiveFilePath = newPath + _ActiveFilePath.Substring(oldPath.Length);
			}
		}
		LoadDirectoryAsync(_ActiveDirPath, default).FireAndForget();
	}

	static void CancelRename(StackPanel panel, TextBlock originalTb, TextBox editBox) {
		panel.Children.Remove(editBox);
		originalTb.Visibility = Visibility.Visible;
	}

	void ShowProperties(object sender, RoutedEventArgs args) {
		if (SelectedItem is FileSystemItem fsi) {
			NativeMethods.ShowFileProperties(fsi.FullPath);
		}
	}

	sealed class ListItemContextMenuItem(int iconId, string name, ActivationCondition condition, RoutedEventHandler clickHandler) : ThemedMenuItem(iconId, name, clickHandler)
	{
		public ActivationCondition Condition { get; } = condition;
	}

	[Flags]
	enum ActivationCondition
	{
		None,
		HasFile,
		HasFolder = 1 << 1,
		HasFileOrFolder = HasFile | HasFolder,
		HasClipboardFile = 1 << 2,
		HasSingleItem = 1 << 3,
		HasSingleSolutionItem = 1 << 4,
	}

	sealed class PathSegment(string path, int index)
	{
		string _text;

		public string Text => _text ??= path.Substring(0, index);

		public override string ToString() {
			return Text;
		}
	}

	static class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		public struct SHFILEOPSTRUCT
		{
			public IntPtr hwnd;
			public uint wFunc;
			public string pFrom;
			public string pTo;
			public ushort fFlags;
			public bool fAnyOperationsAborted;
			public IntPtr hNameMappings;
			public string lpszProgressTitle;
		}

		const uint FO_CUT = 1;
		const uint FO_COPY = 2;
		const uint FO_DELETE = 3;
		const uint FO_MOVE = 4;
		const ushort FOF_NONE = 0;
		const ushort FOF_SILENT = 0x04; // no progress
		const ushort FOF_ALLOWUNDO = 0x40; // move to recycle bin (allow redo)
		const ushort FOF_NOCONFIRMATION = 0x10;
		const ushort FOF_NOCONFIRMMKDIR = 0x200; // create DIR if not exist

		const int MaxSilentItemCount = 100;
		const long MaxSilentCopyBytes = 50 * 1024 * 1024;

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

		public static void DeleteFile(IEnumerable<string> paths, bool toRecycleBin) {
			var showProgress = ShouldShowProgressDialog(paths, checkBytes: false);

			var op = new SHFILEOPSTRUCT {
				hwnd = CodistPackage.WindowHandle,
				wFunc = FO_DELETE,
				pFrom = JoinPaths(paths),
				fFlags = (ushort)((toRecycleBin ? FOF_ALLOWUNDO : FOF_NONE) | (showProgress ? FOF_NONE : FOF_SILENT))
			};
			SHFileOperation(ref op);
		}

		static string JoinPaths(IEnumerable<string> paths) {
			return String.Join("\0", paths) + "\0\0";
		}

		public static void ShellCopyOrMove(IEnumerable<string> sourcePaths, string destDir, bool isMove) {
			var showProgress = ShouldShowProgressDialog(sourcePaths, checkBytes: false);

			var op = new SHFILEOPSTRUCT {
				hwnd = CodistPackage.WindowHandle,
				wFunc = isMove ? FO_CUT : FO_COPY,
				pFrom = JoinPaths(sourcePaths),
				pTo = destDir + "\0\0",
				fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | (showProgress ? FOF_NONE : FOF_SILENT))
			};
			SHFileOperation(ref op);
		}

		public static void ShellRename(string oldPath, string newPath) {
			var op = new SHFILEOPSTRUCT {
				wFunc = FO_MOVE,
				pFrom = oldPath + "\0\0",
				pTo = newPath + "\0\0",
				fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION
			};
			SHFileOperation(ref op);
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct SHELLEXECUTEINFO
		{
			public int cbSize;
			public uint fMask;
			public IntPtr hwnd;
			public string lpVerb;
			public string lpFile;
			public string lpParameters;
			public string lpDirectory;
			public int nShow;
			public IntPtr hInstApp;
			public IntPtr lpIDList;
			public string lpClass;
			public IntPtr hkeyClass;
			public uint dwHotKey;
			public IntPtr hIcon;
			public IntPtr hProcess;
		}

		public static void ShowFileProperties(string filePath) {
			var info = new SHELLEXECUTEINFO {
				lpVerb = "properties", // 关键动词
				lpFile = filePath,
				nShow = 5, // SW_SHOW
				fMask = 0x0000000C
			};
			info.cbSize = Marshal.SizeOf(info);
			ShellExecuteEx(ref info);
		}

		static bool ShouldShowProgressDialog(IEnumerable<string> paths, bool checkBytes) {
			int count = 0;
			long totalBytes = 0;

			foreach (var path in paths) {
				var attr = File.GetAttributes(path);
				if ((attr & FileAttributes.Directory) == FileAttributes.Directory) {
					if (EnumerateForThresholdCheck(path, ref count, ref totalBytes, checkBytes)) {
						return true;
					}
					continue;
				}
				if (++count > MaxSilentItemCount) {
					return true;
				}
				if (checkBytes) {
					try { totalBytes += new FileInfo(path).Length; }
					catch {
						continue;
					}
					if (totalBytes > MaxSilentCopyBytes) {
						return true;
					}
				}
			}
			return false;
		}

		static bool EnumerateForThresholdCheck(string dirPath, ref int count, ref long totalBytes, bool checkBytes) {
			try {
				foreach (var entry in new DirectoryInfo(dirPath).EnumerateFileSystemInfos()) {
					if (++count > MaxSilentItemCount) {
						return true;
					}

					if (entry is FileInfo file) {
						if (checkBytes) {
							try { totalBytes += file.Length; } catch { continue; }
							if (totalBytes > MaxSilentCopyBytes) return true;
						}
					}
					else if (entry is DirectoryInfo dir) {
						if (EnumerateForThresholdCheck(dir.FullName, ref count, ref totalBytes, checkBytes)) {
							return true;
						}
					}
				}
			}
			catch (UnauthorizedAccessException) { }
			catch (PathTooLongException) { }
			catch (IOException) { }

			return false;
		}
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
			return value is bool b && b ? 1.0d : WpfHelper.DimmedOpacity;
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
					FileItemType.Folder => R.T_Folder,
					FileItemType.EmptyFolder => R.T_EmptyFolder,
					FileItemType.InaccessibleFolder => R.T_UnauthorizedFolder,
					_ => R.T_File
				} + item.Name,
				FontWeight = FontWeights.Bold,
				Margin = WpfHelper.MiddleBottomMargin,
				TextWrapping = TextWrapping.Wrap
			});

			if (item.Type == FileItemType.InaccessibleFolder) {
				return panel;
			}

			panel.Children.Add(new TextBlock { Text = R.T_CreateTime + item.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") });
			panel.Children.Add(new TextBlock { Text = R.T_UpdateTime + item.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") });

			if (item.IsFile) {
				panel.Children.Add(new TextBlock { Text = R.T_FileSize + item.FormattedFileSize });
			}

			return panel;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}

	#endregion
}
