using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
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
	bool _SolutionJustLoaded;

	CancellationTokenSource _CancellationTokenSource = new();

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.CheckedInCaller)]
	public FileExplorerWindow() : base(null) {
		Caption = R.T_FileExplorer;
		Content = _FileList = new FileList(true);
		_FileList.IsVisibleChanged += HandleVisibilityChange;
	}

	[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.EventHandler)]
	void HandleVisibilityChange(object sender, DependencyPropertyChangedEventArgs e) {
		if (!(bool)e.NewValue) {
			SolutionEvents.OnAfterOpenSolution -= HandleAfterOpenSolution;
			SolutionEvents.OnAfterCloseSolution -= HandleAfterCloseSolution;
			SolutionEvents.OnAfterLoadProjectBatch -= HandleAfterLoadProjects;
			TextEditorHelper.ActiveTextViewChanged -= HandleActiveTextViewChanged;
			TextEditorHelper.AllTextViewClosed -= HandleAllTextViewClosed;
			return;
		}

		SolutionEvents.OnAfterOpenSolution += HandleAfterOpenSolution;
		SolutionEvents.OnAfterCloseSolution += HandleAfterCloseSolution;
		SolutionEvents.OnAfterLoadProjectBatch += HandleAfterLoadProjects;
		TextEditorHelper.ActiveTextViewChanged += HandleActiveTextViewChanged;
		TextEditorHelper.AllTextViewClosed += HandleAllTextViewClosed;
		var currentFile = ServicesHelper.Instance.DTE.ActiveDocument?.FullName
			?? ServicesHelper.Instance.DTE.Solution.FullName;
		if (!FileHelper.AreFileNamesEqual(currentFile, _FileList.CurrentFile)) {
			_FileList.CurrentFile = currentFile;
			_FileList.LoadCurrentDirectoryAsync(_CancellationTokenSource.Token).FireAndForget();
		}
	}

	void HandleAllTextViewClosed(object sender, EventArgs e) {
		_FileList.ClearCurrentFileAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
	}

	void HandleAfterOpenSolution(object sender, EventArgs e) {
		_SolutionJustLoaded = true;
		_FileList.RefreshSolutionAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
	}

	void HandleAfterCloseSolution(object sender, EventArgs e) {
		_FileList.RefreshSolutionAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
	}
	void HandleAfterLoadProjects(object sender, LoadProjectBatchEventArgs e) {
		_FileList.RefreshProjectAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
	}

	void HandleActiveTextViewChanged(object sender, TextViewCreatedEventArgs e) {
		if (!e.TextView.Roles.Contains(PredefinedTextViewRoles.PrimaryDocument)) {
			return;
		}
		_FileList.RefreshCurrentFileAsync(_SolutionJustLoaded, SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		_SolutionJustLoaded = false;
	}

	protected override void Dispose(bool disposing) {
		base.Dispose(disposing);
		_CancellationTokenSource.CancelAndDispose();
	}
}
