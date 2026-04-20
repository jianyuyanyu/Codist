using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using CLR;

namespace Codist.FileBrowser;

sealed class FileSystemItem : INotifyPropertyChanged
{
	readonly FileSystemInfo _Info;
	readonly FileItemType _Type;
	bool _IsCurrent;
	FrameworkElement _Icon;
	SolutionItemInfo _IsSolutionItem;

	long _FileSize = -1;
	DateTime _CreationTime;
	DateTime _LastWriteTime;

	public event PropertyChangedEventHandler PropertyChanged;

	public string Name => _Info.Name;
	public string FullPath => _Info.FullName;
	public FileItemType Type => _Type;
	public bool IsEmptyFolder => _Type == FileItemType.EmptyFolder;
	public bool IsFolder => _Type.CeqAny(FileItemType.Folder, FileItemType.EmptyFolder);
	public bool IsFile => _Type == FileItemType.File;
	public bool IsCurrent {
		get => _IsCurrent;
		set {
			if (_IsCurrent != value) {
				_IsCurrent = value;
				PropertyChanged?.Invoke(this, new (nameof(IsCurrent)));
			}
		}
	}

	public FrameworkElement Icon => _Icon ??= VsImageHelper.GetImage(_Type switch {
		FileItemType.Folder => IconIds.Folder,
		FileItemType.EmptyFolder => IconIds.EmptyFolder,
		FileItemType.InaccessibleFolder => IconIds.InaccessibleFolder,
		_ => GetFileIconId(_Info.Extension)
	});

	public long FileSize {
		get {
			if (_FileSize < 0) {
				if (IsFile) {
					try {
						_FileSize = _Info is FileInfo info
							? info.Length
							: new FileInfo(_Info.FullName).Length;
					}
					catch {
						_FileSize = 0;
					}
				}
				else {
					_FileSize = 0;
				}
			}
			return _FileSize;
		}
	}

	public DateTime CreationTime {
		get {
			if (_CreationTime == default) {
				try { _CreationTime = _Info.CreationTime; }
				catch { _CreationTime = DateTime.MaxValue; }
			}
			return _CreationTime;
		}
	}

	public DateTime LastWriteTime {
		get {
			if (_LastWriteTime == default) {
				try { _LastWriteTime = _Info.LastWriteTime; }
				catch { _LastWriteTime = DateTime.MaxValue; }
			}
			return _LastWriteTime;
		}
	}

	public string FormattedFileSize => IsFile ? FormatFileSize(FileSize) : null;

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	public bool IsSolutionItem {
		get {
			if (_Type != FileItemType.File) {
				return true;
			}
			if (_IsSolutionItem == 0) {
				_IsSolutionItem = GetIsSolutionItem();
			}
			return _IsSolutionItem == SolutionItemInfo.Yes;
		}
	}

	public FileSystemItem(FileInfo fileInfo, bool isCurrent) {
		(_Info, _Type, _IsCurrent) = (fileInfo, FileItemType.File, isCurrent);
	}

	public FileSystemItem(DirectoryInfo parentDirInfo, bool isEmpty, bool isCurrent) {
		(_Info, _Type, _IsCurrent) = (parentDirInfo, isEmpty ? FileItemType.Folder : FileItemType.EmptyFolder, isCurrent);
	}
	public FileSystemItem(DirectoryInfo parentDirInfo, FileItemType type) {
		(_Info, _Type) = (parentDirInfo, type);
	}

	public static int GetFileIconId(string extName) {
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
			".sln" or ".slnx" => IconIds.VisualStudioFile,
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

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	internal void RefreshIsSolutionItem() {
		if (_Type != FileItemType.File
			|| _IsSolutionItem == 0) { // do not update IsSolutionItem if it is not initialized
			return;
		}
		var i = GetIsSolutionItem();
		if (i != _IsSolutionItem) {
			_IsSolutionItem = i;
			PropertyChanged?.Invoke(this, new(nameof(IsSolutionItem)));
		}
	}

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	SolutionItemInfo GetIsSolutionItem() {
		const string MISC_FILES = "{66A2671F-8FB5-11D2-AA7E-00C04F688DDE}";
		var projItem = ServicesHelper.Instance.DTE.Solution.FindProjectItem(_Info.FullName);
		return projItem != null && projItem.Kind != MISC_FILES ? SolutionItemInfo.Yes : SolutionItemInfo.No;
	}

	internal void ClearIsSolutionItem() {
		if (_Type != FileItemType.File) {
			return;
		}
		if (_IsSolutionItem != 0) {
			_IsSolutionItem = 0;
			PropertyChanged?.Invoke(this, new(nameof(IsSolutionItem)));
		}
	}
	internal void ClearIsCurrent() {
		if (_IsCurrent) {
			_IsCurrent = false;
			PropertyChanged?.Invoke(this, new(nameof(IsCurrent)));
		}
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

	enum SolutionItemInfo : byte
	{
		Unknown,
		Yes,
		No
	}
}
