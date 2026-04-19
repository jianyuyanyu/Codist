using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using CLR;
using Codist.FileBrowser;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Text.Editor;
using R = Codist.Properties.Resources;

namespace Codist.Commands;

[Guid(WindowGuidString)]
public class FileExplorerWindow : ToolWindowPane
{
	internal const string WindowGuidString = "f4fff674-6d0e-4cae-8619-8a66bb65c7b5";
	internal static readonly Guid WindowGuid = new(WindowGuidString);

	FileList _FileList;
	bool _SolutionJustLoaded, _SuppressRefresh;
	PendingRefresh _PendingRefresh;

	CancellationTokenSource _CancellationTokenSource = new();

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	public FileExplorerWindow() : base(null) {
		Caption = R.T_FileExplorer;
		var activeFile = CodistPackage.DTE.ActiveDocument?.FullName ?? CodistPackage.DTE.Solution.FullName;
		Content = _FileList = new FileList(true) {
			CurrentFile = activeFile,
			Resources = SharedDictionaryManager.VirtualList
		};
		_FileList.LoadCurrentDirectoryAsync(_CancellationTokenSource.Token).FireAndForget();

		SolutionEvents.OnAfterOpenSolution += HandleAfterOpenSolution;
		SolutionEvents.OnAfterCloseSolution += HandleAfterCloseSolution;
		SolutionEvents.OnAfterLoadProject += HandleAfterLoadProject;
		SolutionEvents.OnAfterLoadProjectBatch += HandleAfterLoadProjects;
		TextEditorHelper.ActiveTextViewChanged += HandleActiveTextViewChanged;
		TextEditorHelper.AllTextViewClosed += HandleAllTextViewClosed;

		_FileList.IsVisibleChanged += HandleVisibilityChange;
	}

	void HandleVisibilityChange(object sender, DependencyPropertyChangedEventArgs e) {
		if (!(bool)e.NewValue) {
			_SuppressRefresh = true;
			return;
		}

		_SuppressRefresh = false;
		if (_PendingRefresh == 0) {
			return;
		}
		var r = _PendingRefresh;
		if (r.MatchFlags(PendingRefresh.ClearFile)) {
			HandleAllTextViewClosed(null, EventArgs.Empty);
		}
		if (r.MatchFlags(PendingRefresh.ClearSolution)) {
			HandleAfterCloseSolution(null, EventArgs.Empty);
		}
		if (r.MatchFlags(PendingRefresh.Solution)) {
			HandleAfterOpenSolution(null, EventArgs.Empty);
		}
		if (r.MatchFlags(PendingRefresh.Project)) {
			HandleAfterLoadProjects(null, new(false));
		}
		if (r.MatchFlags(PendingRefresh.File)) {
			_FileList.RefreshCurrentFileAsync(_SolutionJustLoaded, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
			_SolutionJustLoaded = false;
		}
		_PendingRefresh = 0;
	}

	void HandleAllTextViewClosed(object sender, EventArgs e) {
		if (_SuppressRefresh) {
			_PendingRefresh |= PendingRefresh.ClearFile;
			_PendingRefresh = _PendingRefresh.SetFlags(PendingRefresh.File, false);
			return;
		}
		_FileList.ClearCurrentFile();
		"All text view closed".Log();
	}

	void HandleAfterOpenSolution(object sender, EventArgs e) {
		if (_SuppressRefresh) {
			_PendingRefresh |= PendingRefresh.Solution;
			return;
		}
		_SolutionJustLoaded = true;
		_FileList.RefreshSolutionAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		"Open solution".Log();
	}

	void HandleAfterCloseSolution(object sender, EventArgs e) {
		if (_SuppressRefresh) {
			_PendingRefresh |= PendingRefresh.ClearSolution;
			_PendingRefresh = _PendingRefresh.SetFlags(PendingRefresh.Solution | PendingRefresh.Project | PendingRefresh.File, false);
			return;
		}
		_FileList.RefreshSolutionAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		"Close solution".Log();
	}

	void HandleAfterLoadProject(object sender, LoadProjectEventArgs e) {
		$"Load project".Log();
	}
	void HandleAfterLoadProjects(object sender, LoadProjectBatchEventArgs e) {
		if (_SuppressRefresh) {
			_PendingRefresh |= PendingRefresh.Project;
			return;
		}
		_FileList.RefreshProjectAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		"Load projects".Log();
	}

	void HandleActiveTextViewChanged(object sender, TextViewCreatedEventArgs e) {
		if (_SuppressRefresh) {
			_PendingRefresh |= PendingRefresh.File;
			return;
		}
		_FileList.RefreshCurrentFileAsync(_SolutionJustLoaded, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		$"Active file {e.TextView.TextBuffer.GetTextDocument().FilePath}".Log();
		_SolutionJustLoaded = false;
	}

	protected override void Dispose(bool disposing) {
		base.Dispose(disposing);
		_CancellationTokenSource.CancelAndDispose();
	}

	[Flags]
	enum PendingRefresh
	{
		None,
		Solution = 1,
		Project = 2,
		File = 4,
		ClearFile = 8,
		ClearSolution = 16,
	}
}
