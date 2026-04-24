using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Codist.Commands;

/// <summary>
/// Command handler to display a window pane that list file system contents
/// </summary>
internal sealed class FileExplorerWindowCommand
{
	readonly AsyncPackage _Package;

	/// <summary>
	/// Initializes a new instance of the <see cref="FileExplorerWindowCommand"/> class.
	/// Adds our command handlers for menu (commands must exist in the command table file)
	/// </summary>
	/// <param name="package">Owner package, not null.</param>
	/// <param name="commandService">Command service to add command to, not null.</param>
	FileExplorerWindowCommand(AsyncPackage package, OleMenuCommandService commandService) {
		_Package = package ?? throw new ArgumentNullException(nameof(package));

		Command.FileExplorer.Register(Execute);
	}

	public static FileExplorerWindowCommand Instance { get; private set; }

	/// <summary>
	/// Initializes the singleton instance of the command.
	/// </summary>
	/// <param name="package">Owner package, not null.</param>
	public static async Task InitializeAsync(AsyncPackage package) {
		// Switch to the main thread - the call to AddCommand in FileExplorerWindowCommand's constructor requires
		// the UI thread.
		await SyncHelper.SwitchToMainThreadAsync(package.DisposalToken);

		var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
		Instance = new FileExplorerWindowCommand(package, commandService);
	}

	void Execute(object sender, EventArgs e) {
		ThreadHelper.ThrowIfNotOnUIThread();

		_Package.JoinableTaskFactory.RunAsync(async delegate
		{
			ToolWindowPane window = await _Package.ShowToolWindowAsync(typeof(FileExplorerWindow), 0, true, _Package.DisposalToken);
			if ((null == window) || (null == window.Frame)) {
				throw new NotSupportedException("Cannot create tool window");
			}

			await _Package.JoinableTaskFactory.SwitchToMainThreadAsync(_Package.DisposalToken);
			Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(((IVsWindowFrame)window.Frame).Show());
		}).FileAndForget(nameof(FileExplorerWindowCommand));
	}
}
