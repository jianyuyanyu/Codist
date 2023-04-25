﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Codist.Controls;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using R = Codist.Properties.Resources;

namespace Codist.Commands
{
	internal static class OpenOutputFolderCommand
	{
		public static void Initialize() {
			Command.OpenOutputFolder.Register(Execute, (s, args) => ((OleMenuCommand)s).Visible = GetSelectedProject() != null);
			Command.OpenDebugOutputFolder.Register(ExecuteDebug, (s, args) => ((OleMenuCommand)s).Visible = GetSelectedProjectConfigurationExceptActive("Debug") != null);
			Command.OpenReleaseOutputFolder.Register(ExecuteRelease, (s, args) => ((OleMenuCommand)s).Visible = GetSelectedProjectConfigurationExceptActive("Release") != null);
		}

		static void Execute(object sender, EventArgs e) {
			var p = GetSelectedProject();
			if (p != null) {
				OpenOutputFolder(p, null);
			}
		}
		static void ExecuteDebug(object sender, EventArgs e) {
			var p = GetSelectedProject();
			if (p != null) {
				OpenOutputFolder(p, "Debug");
			}
		}
		static void ExecuteRelease(object sender, EventArgs e) {
			var p = GetSelectedProject();
			if (p != null) {
				OpenOutputFolder(p, "Release");
			}
		}
		[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.EventHandler)]
		static Configuration GetSelectedProjectConfigurationExceptActive(string rowName) {
			var cm = GetSelectedProject()?.ConfigurationManager;
			if (cm?.ConfigurationRowNames is object[] rows) {
				if (cm.ActiveConfiguration.ConfigurationName == rowName) {
					return null;
				}
				for (int i = 0; i < rows.Length; i++) {
					if (rows[i] is string s && s == rowName) {
						return cm.Item(i+1);
					}
				}
			}
			return null;
		}

		[SuppressMessage("Usage", Suppression.VSTHRD010, Justification = Suppression.EventHandler)]
		static void OpenOutputFolder(Project p, string rowName) {
			try {
				if (p.Properties.Item("FullPath")?.Value is string projectPath
					&& (rowName == null ? p.ConfigurationManager.ActiveConfiguration : GetSelectedProjectConfigurationExceptActive(rowName))?.Properties.Item("OutputPath")?.Value is string confPath) {
					var outputPath = Path.Combine(projectPath, confPath);
					if (Directory.Exists(outputPath)) {
						FileHelper.TryRun(outputPath);
					}
					else {
						MessageWindow.Error($"{R.T_OutputFolderMissing}{Environment.NewLine}{outputPath}", R.CMD_OpenOutputFolder);
					}
				}
			}
			catch (System.Runtime.InteropServices.COMException ex) {
				ShowError(ex);
			}
			catch (IOException ex) {
				ShowError(ex);
			}
			catch (InvalidOperationException ex) {
				ShowError(ex);
			}
		}

		static void ShowError(Exception ex) {
			MessageWindow.Error($"{R.T_FailedToOpenOutputFolder}{Environment.NewLine}{ex}", R.CMD_OpenOutputFolder);
		}

		static Project GetSelectedProject() {
			ThreadHelper.ThrowIfNotOnUIThread();
			return CodistPackage.DTE.ActiveDocument?.ProjectItem.ContainingProject;
		}
	}
}
